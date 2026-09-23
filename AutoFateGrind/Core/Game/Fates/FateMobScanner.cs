using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Numerics;
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
    float DistanceToHitbox,
    bool IsCasting);

internal static unsafe class FateMobScanner
{
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
        distanceToHitbox = float.MaxValue;
        if (Svc.Targets.Target is not IBattleNpc npc)
        {
            return false;
        }
        if (!IsLiveMobOfFate(npc, fateId))
        {
            return false;
        }

        distanceToHitbox = DistanceToHitbox(from, npc);
        return true;
    }

    public static bool TrySurveyTargetedMob(uint fateId, Vector3 from, out FateMobTarget target)
    {
        target = default;
        if (Svc.Targets.Target is not IBattleNpc npc || !IsLiveMobOfFate(npc, fateId))
        {
            return false;
        }

        target = new FateMobTarget(npc.GameObjectId, npc.Position, npc.HitboxRadius, DistanceToHitbox(from, npc), npc.IsCasting);
        return true;
    }

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
        return native->BattleNpcSubKind == BattleNpcSubKind.Combatant;
    }

    private static float DistanceToHitbox(Vector3 from, IBattleNpc npc)
        => MathF.Max(0f, Vector3.Distance(from, npc.Position) - npc.HitboxRadius);
}
