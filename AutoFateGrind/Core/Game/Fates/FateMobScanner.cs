using DalamudObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using DalamudStatusFlags = Dalamud.Game.ClientState.Objects.Enums.StatusFlags;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Collections.Generic;
using System.Numerics;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace AutoFateGrind.Core.Game.Fates;

internal readonly record struct FateMobSurvey(
    int LiveCount,
    Vector3 NearestPosition,
    float NearestHitboxRadius,
    float NearestDistanceToHitbox,
    float NearestVerticalDelta)
{
    public bool Any => LiveCount > 0;

    public static readonly FateMobSurvey Empty = new(0, default, 0f, float.MaxValue, 0f);
}

internal readonly record struct FateMobTarget(
    ulong GameObjectId,
    Vector3 Position,
    float HitboxRadius,
    float DistanceToHitbox);

internal readonly record struct FatePullSurvey(int OnPlayer, int Candidates, FateMobTarget Next)
{
    public bool HasCandidate => Candidates > 0;
}

internal enum FatePullTagState { Waiting, OnPlayer, Gone }

internal static unsafe class FateMobScanner
{
    // A pull candidate with others idling this close is worth this many metres of extra walk, so the pull
    // sweeps a pack instead of zig-zagging between strays.
    private const float PullPackMeters = 8f;
    private const float PullPackBonusMeters = 3f;
    private const float PullMaxVerticalMeters = 8f;

    // UIState.Hater holds this many entries; the pull cap in the settings stops at the same number.
    public const int EnmityListCapacity = 32;

    private static readonly List<IBattleNpc> pullCandidates = [];
    private static readonly uint[] enmityIds = new uint[EnmityListCapacity];
    private static int enmityCount;

    public static FateMobSurvey Survey(uint fateId, Vector3 from)
    {
        var liveCount = 0;
        var nearestPosition = default(Vector3);
        var nearestHitbox = 0f;
        var nearestDistance = float.MaxValue;
        var nearestVerticalDelta = 0f;

        var objects = Svc.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            if (objects[objectIndex] is not IBattleNpc npc)
            {
                continue;
            }
            if (!IsLiveMobOfFate(npc, fateId))
            {
                continue;
            }

            liveCount++;
            var candidate = DistanceToHitbox(from, npc);
            if (candidate >= nearestDistance)
            {
                continue;
            }

            nearestDistance = candidate;
            nearestHitbox = npc.HitboxRadius;
            nearestPosition = npc.Position;
            nearestVerticalDelta = npc.Position.Y - from.Y;
        }

        return liveCount == 0
            ? FateMobSurvey.Empty
            : new FateMobSurvey(liveCount, nearestPosition, nearestHitbox, nearestDistance, nearestVerticalDelta);
    }

    public static bool TryGetTargetedMob(uint fateId, Vector3 from, out float distanceToHitbox)
    {
        var found = TryGetTarget(fateId, from, out var target);
        distanceToHitbox = found ? target.DistanceToHitbox : float.MaxValue;
        return found;
    }

    public static bool IsTargetingMobOf(uint fateId)
        => Svc.Targets.Target is IBattleNpc npc && IsLiveMobOfFate(npc, fateId);

    public static bool TryGetTarget(uint fateId, Vector3 from, out FateMobTarget target)
    {
        target = default;
        if (Svc.Targets.Target is not IBattleNpc npc)
        {
            return false;
        }
        if (!IsLiveMobOfFate(npc, fateId))
        {
            return false;
        }

        target = new FateMobTarget(npc.GameObjectId, npc.Position, npc.HitboxRadius, DistanceToHitbox(from, npc));
        return true;
    }

    // BossMod's AutoTarget only waits for a target the player has not pulled yet; one already in a fight stays.
    public static void DropUnpulledTarget(uint fateId)
    {
        if (Svc.Targets.Target is not IBattleNpc npc || !IsLiveMobOfFate(npc, fateId))
        {
            return;
        }
        if ((npc.StatusFlags & DalamudStatusFlags.InCombat) != 0)
        {
            return;
        }
        Svc.Targets.Target = null;
    }

    // OnPlayer counts every live mob on the character's enmity list, FATE or not: it is what the character has to survive.
    // Candidates are this FATE's mobs nobody has on them yet (a mob fighting an NPC still counts), inside the ring.
    public static FatePullSurvey SurveyPull(uint fateId, Vector3 from, Vector3 ringCentre, float ringRadius, float maxMeters, IReadOnlyDictionary<ulong, long> skipped)
    {
        SnapshotEnmityList();
        var onPlayer = 0;
        pullCandidates.Clear();

        var objects = Svc.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            if (objects[objectIndex] is not IBattleNpc npc || npc.CurrentHp == 0)
            {
                continue;
            }
            if (IsOnPlayer(npc.EntityId))
            {
                onPlayer++;
                continue;
            }
            if (!IsLiveMobOfFate(npc, fateId) || skipped.ContainsKey(npc.GameObjectId))
            {
                continue;
            }
            if (npc.TargetObject is IPlayerCharacter)
            {
                continue;
            }
            if (DistanceToHitbox(from, npc) > maxMeters || MathF.Abs(npc.Position.Y - from.Y) > PullMaxVerticalMeters)
            {
                continue;
            }
            if (ringRadius > 0f && FlatDistance(npc.Position, ringCentre) > ringRadius)
            {
                continue;
            }
            pullCandidates.Add(npc);
        }

        if (pullCandidates.Count == 0)
        {
            return new FatePullSurvey(onPlayer, 0, default);
        }

        var bestIndex = 0;
        var bestScore = float.MaxValue;
        var bestDistance = 0f;
        for (var index = 0; index < pullCandidates.Count; index++)
        {
            var candidate = pullCandidates[index];
            var distance = DistanceToHitbox(from, candidate);
            var score = distance - PullPackBonusMeters * PackNeighbours(candidate, index);
            if (score >= bestScore)
            {
                continue;
            }
            bestScore = score;
            bestDistance = distance;
            bestIndex = index;
        }

        var best = pullCandidates[bestIndex];
        var next = new FateMobTarget(best.GameObjectId, best.Position, best.HitboxRadius, bestDistance);
        return new FatePullSurvey(onPlayer, pullCandidates.Count, next);
    }

    public static FatePullTagState PullTagState(uint fateId, ulong gameObjectId)
    {
        if (Svc.Objects.SearchById(gameObjectId) is not IBattleNpc npc || !IsLiveMobOfFate(npc, fateId))
        {
            return FatePullTagState.Gone;
        }
        SnapshotEnmityList();
        if (IsOnPlayer(npc.EntityId))
        {
            return FatePullTagState.OnPlayer;
        }
        // Someone else got to it first; it is theirs to hold.
        return npc.TargetObject is IPlayerCharacter ? FatePullTagState.Gone : FatePullTagState.Waiting;
    }

    public static bool TryGetPullTarget(uint fateId, ulong gameObjectId, out IBattleNpc npc)
    {
        npc = null!;
        if (Svc.Objects.SearchById(gameObjectId) is not IBattleNpc found || !IsLiveMobOfFate(found, fateId))
        {
            return false;
        }
        npc = found;
        return true;
    }

    private static int PackNeighbours(IBattleNpc candidate, int candidateIndex)
    {
        var packSquared = PullPackMeters * PullPackMeters;
        var neighbours = 0;
        for (var index = 0; index < pullCandidates.Count; index++)
        {
            if (index != candidateIndex && Vector3.DistanceSquared(candidate.Position, pullCandidates[index].Position) <= packSquared)
            {
                neighbours++;
            }
        }
        return neighbours;
    }

    // Read once per survey, so the per-object check below is a scan of a small id array.
    private static void SnapshotEnmityList()
    {
        var hater = &UIState.Instance()->Hater;
        enmityCount = Math.Clamp(hater->HaterCount, 0, EnmityListCapacity);
        for (var index = 0; index < enmityCount; index++)
        {
            enmityIds[index] = hater->Haters[index].EntityId;
        }
    }

    private static bool IsOnPlayer(uint entityId)
        => Array.IndexOf(enmityIds, entityId, 0, enmityCount) >= 0;

    private static float FlatDistance(Vector3 a, Vector3 b)
        => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    public static bool HasPickupWithin(uint fateId, Vector3 from, float maxMeters)
    {
        var maxSquared = maxMeters * maxMeters;
        var objects = Svc.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            var candidate = objects[objectIndex];
            if (!IsPickupOfFate(candidate, fateId))
            {
                continue;
            }
            if (Vector3.DistanceSquared(from, candidate!.Position) <= maxSquared)
            {
                return true;
            }
        }
        return false;
    }

    // Same pick as BossMod's FATE helper: the pickup nearest by flat distance to its hitbox.
    public static bool TryFindNearestPickup(uint fateId, Vector3 from, out IGameObject pickup, out float distanceToHitbox)
    {
        pickup = null!;
        distanceToHitbox = float.MaxValue;
        var objects = Svc.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            var candidate = objects[objectIndex];
            if (!IsPickupOfFate(candidate, fateId))
            {
                continue;
            }
            var distance = FlatDistanceToHitbox(from, candidate!);
            if (distance >= distanceToHitbox)
            {
                continue;
            }
            distanceToHitbox = distance;
            pickup = candidate!;
        }
        return distanceToHitbox < float.MaxValue;
    }

    public static bool TryGetPickup(uint fateId, ulong gameObjectId, out IGameObject pickup)
    {
        var candidate = Svc.Objects.SearchById(gameObjectId);
        pickup = candidate!;
        return IsPickupOfFate(candidate, fateId);
    }

    private static bool IsPickupOfFate(IGameObject? candidate, uint fateId)
        => candidate is not null
        && candidate.ObjectKind == DalamudObjectKind.EventObj
        && candidate.IsTargetable
        && ((CSGameObject*)candidate.Address)->FateId == fateId;

    private static float FlatDistanceToHitbox(Vector3 from, IGameObject target)
        => MathF.Max(0f, Vector2.Distance(new Vector2(from.X, from.Z), new Vector2(target.Position.X, target.Position.Z)) - target.HitboxRadius);

    private static bool IsLiveMobOfFate(IBattleNpc npc, uint fateId)
    {
        if (!npc.IsTargetable)
        {
            return false;
        }
        if (npc.CurrentHp == 0)
        {
            return false;
        }

        var native = (CSGameObject*)npc.Address;
        if (native->FateId != fateId)
        {
            return false;
        }
        if (native->BattleNpcSubKind != BattleNpcSubKind.Combatant)
        {
            return false;
        }
        // FATE allies (escorts, the hunters fighting alongside you) carry the FATE id and the Combatant sub-kind too;
        // the game's own target classification is what tells them from the mobs, the same check BossMod uses.
        return ActionManager.ClassifyTarget((CSCharacter*)npc.Address) == ActionManager.TargetCategory.Enemy;
    }

    private static float DistanceToHitbox(Vector3 from, IBattleNpc npc)
        => MathF.Max(0f, Vector3.Distance(from, npc.Position) - npc.HitboxRadius);
}
