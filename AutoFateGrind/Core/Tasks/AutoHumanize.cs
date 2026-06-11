using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Zones;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// Idle-break task: teleports to a city aetheryte and wanders between random reachable points until the
// configured break window elapses. The grind loop calls this through AutoFateController; on return the
// controller resumes the FATE grind in the origin zone, so this task only owns the in-city portion.
public sealed class AutoHumanize(uint cityTerritoryId, int durationMs) : AutoCommon
{
    private readonly uint cityTerritoryId = cityTerritoryId;
    private readonly int durationMs = durationMs;

    // False until we actually reach the city and start the break. Lets the controller distinguish a real
    // break from a teleport-abort, so a skipped break re-triggers on the next FATE instead of being consumed.
    public bool BreakTaken { get; private set; }

    private const int   TeleportWatchdogMs = 60_000;
    private const int   WalkWatchdogMs     = 90_000;
    private const int   DismountWatchdogMs = 30_000;
    private const int   NavmeshReadyWaitMs = 60_000;
    private const float ArrivalTolerance   = 4f;

    private static readonly Random rng = new();

    // User-tunable knobs come straight from config so a /afg config edit mid-break takes effect on the
    // next hop. Snapped at use, not at construction, for the same reason.
    private static (int minMs, int maxMs) PauseRangeMs()
    {
        var cfg = Plugin.Cfg;
        var lo = Math.Max(0, cfg.HumanizerPauseMinSec);
        var hi = Math.Max(lo, cfg.HumanizerPauseMaxSec);
        return (lo * 1000, hi * 1000);
    }

    private static (float min, float max) WanderRange()
    {
        var cfg = Plugin.Cfg;
        var lo = Math.Max(1, cfg.HumanizerWanderMinMeters);
        var hi = Math.Max(lo, cfg.HumanizerWanderMaxMeters);
        return (lo, hi);
    }

    protected override async Task Execute()
    {
        var city = CityCatalog.Find(cityTerritoryId);
        var label = city?.Name ?? $"city {cityTerritoryId}";
        var breakMin = Math.Max(1, durationMs / 60_000);
        Diag($"Humanize start: {label}, break {durationMs / 1000}s");
        Svc.Chat.Print($"[AFG] Humanize: taking a ~{breakMin}m break in {label}.");

        if (Svc.ClientState.TerritoryType != cityTerritoryId)
        {
            var reached = false;
            await RunWithStatusPinned($"Teleporting to {label}",
                async () => reached = await TeleportToTerritory(cityTerritoryId, Vector3.Zero, "humanize-teleport", TeleportWatchdogMs));
            if (!reached)
            {
                Diag($"Humanize aborted: could not reach {label} (still in {Svc.ClientState.TerritoryType}).");
                return;
            }
        }

        BreakTaken = true;
        await WaitForNavmeshReady(NavmeshReadyWaitMs);
        if (CancelToken.IsCancellationRequested) return;

        if (Svc.Condition[ConditionFlag.Mounted])
            await RunCancellable(new MoveOp(o => o.DismountNow()), DismountWatchdogMs, "humanize-dismount");

        var deadline = Environment.TickCount64 + durationMs;
        var hops = 0;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            if (Svc.ClientState.TerritoryType != cityTerritoryId)
            {
                Diag($"Humanize: territory changed to {Svc.ClientState.TerritoryType} (expected {cityTerritoryId}); ending early.");
                return;
            }

            var player = Svc.Objects.LocalPlayer;
            if (player is null) { await NextFrame(120); continue; }

            var dest = PickRandomDestination(player.Position);
            if (dest is null)
            {
                Status = $"Idling in {label}";
                await IdleFor(1_500, deadline);
                continue;
            }

            hops++;
            var remainingSec = Math.Max(0, (deadline - Environment.TickCount64) / 1000);
            Status = $"Wandering in {label} (~{remainingSec}s left)";
            Diag($"Humanize hop {hops}: walking {Vector3.Distance(player.Position, dest.Value):F0}m to {dest.Value}");

            var perHopBudget = (int)Math.Min(WalkWatchdogMs, deadline - Environment.TickCount64);
            if (perHopBudget < 4_000) break;

            var move = new MoveOp(o => o.Move(cityTerritoryId, dest.Value,
                MovementConfig.Everything.WithTolerance(ArrivalTolerance),
                allowTeleportIfFaster: false,
                stopCondition: () => Environment.TickCount64 >= deadline || CancelToken.IsCancellationRequested,
                allowAethernetWithinTerritory: false));
            await RunCancellable(move, perHopBudget, $"humanize-walk-{hops}", StuckDetector.IdleStallAbort(StuckDetector.IdleStallTimeoutMs));

            if (CancelToken.IsCancellationRequested) return;
            if (Environment.TickCount64 >= deadline) break;

            var (pauseLo, pauseHi) = PauseRangeMs();
            var pauseMs = pauseLo == pauseHi ? pauseLo : rng.Next(pauseLo, pauseHi + 1);
            if (pauseMs > 0) await IdleFor(pauseMs, deadline);
        }

        Diag($"Humanize done in {label} after {hops} hop(s).");
    }

    private async Task IdleFor(int pauseMs, long deadline)
    {
        var cappedDeadline = Math.Min(Environment.TickCount64 + pauseMs, deadline);
        while (Environment.TickCount64 < cappedDeadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            await NextFrame(120);
        }
    }

    private Vector3? PickRandomDestination(Vector3 from)
    {
        var (minR, maxR) = WanderRange();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var angle = rng.NextDouble() * Math.PI * 2;
            var radius = minR + (float)rng.NextDouble() * (maxR - minR);
            var candidate = new Vector3(
                from.X + (float)Math.Cos(angle) * radius,
                from.Y,
                from.Z + (float)Math.Sin(angle) * radius);

            var snapped = NavmeshIPC.Instance.NearestPointReachable(candidate, halfExtentXZ: 10f, halfExtentY: 20f);
            if (snapped is not null && Vector3.Distance(snapped.Value, from) >= minR * 0.5f)
                return snapped;
        }
        return null;
    }
}
