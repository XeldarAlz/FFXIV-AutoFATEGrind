using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Numerics;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace AutoFateGrind.Core.Game.Fates;

internal static unsafe class FateMobScanner
{
    // BossMod drops FATE mobs further than this from the player vertically (priority -2), so a mob past
    // it is never something the rotation will approach; measuring reach against it only invents stalls.
    private const float MaxVerticalDeltaMeters = 12f;

    public static bool TryFindNearestMob(uint fateId, Vector3 from, out Vector3 position, out float hitboxRadius, out float distanceToHitbox)
    {
        position = default;
        hitboxRadius = 0f;
        distanceToHitbox = float.MaxValue;

        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not IBattleNpc npc) continue;
            if (!IsLiveMobOfFate(npc, fateId)) continue;
            if (MathF.Abs(npc.Position.Y - from.Y) > MaxVerticalDeltaMeters) continue;

            var candidate = DistanceToHitbox(from, npc);
            if (candidate >= distanceToHitbox) continue;

            distanceToHitbox = candidate;
            hitboxRadius = npc.HitboxRadius;
            position = npc.Position;
        }

        return distanceToHitbox < float.MaxValue;
    }

    public static bool TryGetTargetedMob(uint fateId, Vector3 from, out float distanceToHitbox)
    {
        distanceToHitbox = float.MaxValue;
        if (Svc.Targets.Target is not IBattleNpc npc) return false;
        if (!IsLiveMobOfFate(npc, fateId)) return false;

        distanceToHitbox = DistanceToHitbox(from, npc);
        return true;
    }

    private static bool IsLiveMobOfFate(IBattleNpc npc, uint fateId)
    {
        if (!npc.IsTargetable) return false;
        if (npc.CurrentHp == 0) return false;

        var native = (CSGameObject*)npc.Address;
        if (native->FateId != fateId) return false;
        return native->BattleNpcSubKind == BattleNpcSubKind.Combatant;
    }

    private static float DistanceToHitbox(Vector3 from, IBattleNpc npc)
        => MathF.Max(0f, Vector3.Distance(from, npc.Position) - npc.HitboxRadius);
}
