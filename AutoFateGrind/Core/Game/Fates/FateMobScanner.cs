using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System.Numerics;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace AutoFateGrind.Core.Game.Fates;

internal static unsafe class FateMobScanner
{
    public static bool TryFindNearestMob(uint fateId, Vector3 from, out Vector3 position, out float distance)
    {
        position = default;
        distance = float.MaxValue;

        var objects = Svc.Objects;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not IBattleNpc npc) continue;
            if (!npc.IsTargetable) continue;
            if (npc.CurrentHp == 0) continue;

            var native = (CSGameObject*)npc.Address;
            if (native->FateId != fateId) continue;
            if (native->BattleNpcSubKind != BattleNpcSubKind.Combatant) continue;

            var candidate = Vector3.Distance(from, npc.Position);
            if (candidate >= distance) continue;

            distance = candidate;
            position = npc.Position;
        }

        return distance < float.MaxValue;
    }
}
