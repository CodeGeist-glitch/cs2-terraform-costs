using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace TerraformCost
{
    [FileLocation(nameof(TerraformCost))]
    [SettingsUISection(MainTab, GeneralGroup)]
    public class Settings : ModSetting
    {
        public const string MainTab = "General";
        public const string GeneralGroup = "General";
        public const string PriceGroup = "Price";
        public const string StatsGroup = "Stats";
        public const string CompatGroup = "Compatibility";

        /// <summary>Set once in Mod.OnLoad so CostCalculator (static, no IMod reference) can read live settings.</summary>
        public static Settings instance;

        public Settings(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(MainTab, GeneralGroup)]
        public bool Enabled { get; set; } = true;

        [SettingsUISlider(min = 0f, max = 10f, step = 0.1f, unit = "floatSingleFraction")]
        [SettingsUISection(MainTab, PriceGroup)]
        public float Multiplier { get; set; } = 1f;

        [SettingsUISlider(min = 0f, max = 1f, step = 0.05f, unit = "floatSingleFraction")]
        [SettingsUISection(MainTab, PriceGroup)]
        public float OtherToolsFactor { get; set; } = CostCalculator.DefaultModeFactor;

        [SettingsUISlider(min = 0f, max = 100f, step = 5f, unit = "integer")]
        [SettingsUISection(MainTab, PriceGroup)]
        public float RefundPercent { get; set; } = 0f;

        [SettingsUISection(MainTab, StatsGroup)]
        public string TotalCharged => Budget.TotalCharged.ToString();

        [SettingsUISection(MainTab, StatsGroup)]
        public string BlockedStrokes => Budget.BlockedFrames.ToString();

        [SettingsUISection(MainTab, StatsGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        public bool ResetStats
        {
            set { Budget.ResetStats(); }
        }

        /// <summary>Read-only warning toggle: true when Hard Mode Continued (which also charges for terraforming) is loaded.</summary>
        [SettingsUISection(MainTab, CompatGroup)]
        public bool HardModeContinuedDetected => Compat.HardModeContinuedLoaded;

        public override void SetDefaults()
        {
            Enabled = true;
            Multiplier = 1f;
            OtherToolsFactor = CostCalculator.DefaultModeFactor;
            RefundPercent = 0f;
        }
    }
}
