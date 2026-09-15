using clib.Utils;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace AutoFateGrind.Core.Game.Fates;

// clib's TimeRemaining and IsOnMap measure server epochs against the PC clock; a PC clock running ahead
// made every Running FATE look expired (issue #65), so remaining time is taken from the game server clock.
internal static class FateClock
{
    private const float UnknownRemainingSeconds = -1f;

    public static long LocalClockOffsetSeconds => DateTimeOffset.UtcNow.ToUnixTimeSeconds() - CSFramework.GetServerTime();

    public static float Remaining(PublicEvent fate)
    {
        var endEpoch = fate.EndTimeEpoch;
        if (endEpoch == 0)
        {
            return UnknownRemainingSeconds;
        }

        return endEpoch - CSFramework.GetServerTime();
    }

    public static bool IsOnMap(PublicEvent fate)
        => (fate.State == FateState.Running && Remaining(fate) > 0f) || fate.IsOnMap;
}
