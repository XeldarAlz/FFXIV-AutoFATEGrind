using AutoFateGrind.Core.Game.Fates;
using clib.Utils;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private const int SettleTickMs = 250;

    private long settleUntilMs;
    private readonly List<PublicEvent> varietyPool = new();
    private readonly HashSet<uint> varietyConsideredIds = new();
    private uint? varietyCommittedId;

    private void BeginSettle(string reason)
    {
        var delayMs = Pacing.ReactionDelayMs();
        if (delayMs <= 0)
        {
            return;
        }

        settleUntilMs = Environment.TickCount64 + delayMs;
        Diag($"Pacing: {reason}; waiting {delayMs / 1000.0:F1}s before moving on");
    }

    // Combat always wins: standing still while an add hits the character is the opposite of human.
    private bool IsSettling()
    {
        if (settleUntilMs == 0)
        {
            return false;
        }

        if (Environment.TickCount64 >= settleUntilMs || Svc.Condition[ConditionFlag.InCombat])
        {
            settleUntilMs = 0;
            return false;
        }

        return true;
    }

    private async Task TickSettle()
    {
        var remainingMs = Math.Max(0L, settleUntilMs - Environment.TickCount64);
        Status = $"Pausing before the next FATE ({remainingMs / 1000 + 1}s)";
        await DelayMs((int)Math.Min(remainingMs, SettleTickMs));
    }

    private async Task WaitOutSettle()
    {
        while (IsSettling() && !CancelToken.IsCancellationRequested)
        {
            await TickSettle();
        }
    }

    // A FATE the grind deliberately passed over must not trigger a mid-path retarget back onto it.
    private bool WasPassedOver(uint fateId) => varietyConsideredIds.Contains(fateId);

    private void CommitToFate(uint fateId) => varietyCommittedId = fateId;

    private PublicEvent? PickFate(Vector3 from)
    {
        varietyConsideredIds.Clear();
        if (!Pacing.PickVarietyActive || returnToFateId is not null)
        {
            return FateScanner.PickNext(Plugin.Cfg, from, sessionStuckFateIds, returnToFateId);
        }

        FateScanner.CollectEquivalentPicks(Plugin.Cfg, from, sessionStuckFateIds, varietyPool);
        if (varietyPool.Count == 0)
        {
            return null;
        }

        for (var poolIndex = 0; poolIndex < varietyPool.Count; poolIndex++)
        {
            var candidate = varietyPool[poolIndex];
            varietyConsideredIds.Add(candidate.Id);
            if (candidate.Id == varietyCommittedId)
            {
                return candidate;
            }
        }

        var chosen = varietyPool[Pacing.RollIndex(varietyPool.Count)];
        varietyCommittedId = chosen.Id;
        if (chosen.Id != varietyPool[0].Id)
        {
            Diag($"Pacing: picked FATE {chosen.Id} ({chosen.Name}) over the top-ranked {varietyPool[0].Id} ({varietyPool[0].Name}) from {varietyPool.Count} near-equal choices");
        }
        return chosen;
    }
}
