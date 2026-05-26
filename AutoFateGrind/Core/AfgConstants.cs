namespace AutoFateGrind.Core;

internal static class AfgConstants
{
    public const string PrimaryCommand = "/afg";
    public const string AliasCommand = "/fategrind";

    public const int BicolorCap = 1500;
    public const int FollowUpWaitMs = 15_000;
    public const int StuckThresholdMs = 2_000;

    internal static class ThrottleKeys
    {
        public const string Save = "AutoFateGrind.Save";
    }

    public const int SaveThrottleMs = 500;
}
