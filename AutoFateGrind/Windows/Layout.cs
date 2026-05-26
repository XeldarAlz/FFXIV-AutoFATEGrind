namespace AutoFateGrind.Windows;

internal static class Layout
{
    // Top corner icons (plug / info / cog).
    public const float CornerIconStripWidth = 110f;

    // Goal grid (idle state hero).
    public const float GoalCardHeight = 78f;
    public const float GoalCardIconSize = 26f;
    public const float GoalCardGap = 8f;

    // Big primary action button (Start / Stop).
    public const float PrimaryButtonHeight = 44f;

    // Running state.
    public const float StatusCardHeight = 138f;
    public const float QueueRowHeight = 52f;
    public const float QueueBarHeight = 6f;

    // Idle zone picker rows.
    public const float ZoneRowHeight = 22f;

    public const float FooterHeight = 22f;

    // Retained for the legacy ActionButton component still referenced from a few places.
    public const float ActionButtonHeight = 28f;
}
