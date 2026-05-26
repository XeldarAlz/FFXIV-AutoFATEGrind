using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AutoFateGrind.Core.Game;

internal static unsafe class AchievementProgress
{
    private struct Entry
    {
        public uint Current;
        public uint Max;
        public long LastRequestedTickMs;
        public bool Received;
    }

    private static readonly Dictionary<uint, Entry> cache = new();
    private static readonly object cacheLock = new();
    private static Hook<Achievement.Delegates.ReceiveAchievementProgress>? receiveHook;
    private static long lastListRequestTickMs;

    // Server already throttles; this guards against client-side spam.
    private const int RequestCooldownMs = 5_000;

    public static void Initialize()
    {
        if (receiveHook != null) return;
        try
        {
            receiveHook = Svc.Hook.HookFromAddress<Achievement.Delegates.ReceiveAchievementProgress>(
                Achievement.Addresses.ReceiveAchievementProgress.Value, ReceiveDetour);
            receiveHook.Enable();
        }
        catch (Exception ex)
        {
            Svc.Log.Error($"[AFG] Failed to hook achievement progress: {ex}");
        }
    }

    public static void Shutdown()
    {
        receiveHook?.Disable();
        receiveHook?.Dispose();
        receiveHook = null;
        lock (cacheLock) cache.Clear();
    }

    public static bool TryGet(uint id, out uint current, out uint max)
    {
        lock (cacheLock)
        {
            if (cache.TryGetValue(id, out var entry) && entry.Received)
            {
                current = entry.Current;
                max = entry.Max;
                return true;
            }
        }
        current = max = 0;
        return false;
    }

    public static void Request(uint id, bool force = false)
    {
        if (id == 0) return;
        var now = Environment.TickCount64;

        lock (cacheLock)
        {
            if (cache.TryGetValue(id, out var entry))
            {
                if (!force && entry.Received) return;
                if (!force && now - entry.LastRequestedTickMs < RequestCooldownMs) return;
            }
        }

        try
        {
            var ach = Achievement.Instance();
            if (ach == null) return;

            // The achievement module only populates after its agent has been shown once — opening the
            // Achievements window is what loads the data from the server. Until then IsLoaded stays
            // false and every RequestAchievementProgress is silently dropped, so trigger the load
            // ourselves (Show then immediate Hide so the window doesn't actually linger) and bail; a
            // later retry plus the receive hook pick up the value once it's ready.
            if (!ach->IsLoaded())
            {
                if (now - lastListRequestTickMs >= RequestCooldownMs)
                {
                    lastListRequestTickMs = now;
                    var agent = AgentAchievement.Instance();
                    if (agent != null)
                    {
                        agent->Show();
                        agent->Hide();
                    }
                }
                return;
            }

            ach->RequestAchievementProgress(id);
            lock (cacheLock)
            {
                var entry = cache.TryGetValue(id, out var e) ? e : new Entry();
                entry.LastRequestedTickMs = now;
                cache[id] = entry;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[AFG] Achievement request {id} failed: {ex.Message}");
        }
    }

    private static void ReceiveDetour(Achievement* thisPtr, uint id, uint current, uint max)
    {
        receiveHook!.Original(thisPtr, id, current, max);
        lock (cacheLock)
        {
            cache[id] = new Entry
            {
                Current = current,
                Max = max,
                LastRequestedTickMs = Environment.TickCount64,
                Received = true,
            };
        }
    }
}
