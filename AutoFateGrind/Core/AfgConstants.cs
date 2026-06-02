namespace AutoFateGrind.Core;

internal static class AfgConstants
{
    public const string PrimaryCommand = "/afg";
    public const string AliasCommand = "/fategrind";

    public const string BundledCombatPresetName = "Auto FATE Grind";

    // Identity AFG presents to TextAdvance external-control; must match on enable/disable to pair the session.
    public const string TextAdvanceCallerName = "AutoFateGrind";

    // Chat/log tag prefixed to AFG's player-facing and diagnostic lines.
    public const string LogPrefix = "[AFG]";

    // Game-imposed Bicolor Gemstone wallet cap.
    public const int BicolorCap = 1500;
    public const int FollowUpWaitMs = 15_000;

    // Shared throttle for game-addon interactions; distinct from SaveThrottleMs even though both equal 500.
    public const int AddonInteractThrottleMs = 500;

    internal static class ThrottleKeys
    {
        public const string Save = "AutoFateGrind.Save";

        // EzThrottler dedup keys — each value is byte-stable (changing it would reset throttle state).
        public const string RepairTrigger = "AFG.Repair.Trigger";
        public const string RepairAll = "AFG.Repair.RepairAll";
        public const string RepairYesno = "AFG.Repair.Yesno";
        public const string RepairIconString = "AFG.Repair.IconString";
        public const string ShopClickIconString = "AFG.ClickSelectIconString";
        public const string ShopClickYesno = "AFG.ClickSelectYesno";
        public const string ShopBuyCurrency = "AFG.BuyFromCurrencyShop";
        public const string ShopBuy = "AFG.BuyFromShop";
        public const string FoodUse = "AFG.Food.Use";
    }

    public const int SaveThrottleMs = 500;

    // Live game-UI addon names matched against the client at runtime; values must stay byte-identical.
    internal static class AddonNames
    {
        public const string SelectYesno = "SelectYesno";
        public const string SelectIconString = "SelectIconString";
        public const string SelectString = "SelectString";
        public const string Shop = "Shop";
        public const string ShopExchangeCurrency = "ShopExchangeCurrency";
        public const string InclusionShop = "InclusionShop";
        public const string Repair = "Repair";
        public const string InputNumeric = "InputNumeric";
        public const string InputString = "InputString";
    }
}
