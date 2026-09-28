using System.Collections.Generic;
using Colossal;

namespace TerraformCost
{
    public class LocaleEN : IDictionarySource
    {
        private readonly Settings m_Setting;

        public LocaleEN(Settings setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Terraform Costs" },

                { m_Setting.GetOptionTabLocaleID(Settings.MainTab), "General" },
                { m_Setting.GetOptionGroupLocaleID(Settings.GeneralGroup), "General" },
                { m_Setting.GetOptionGroupLocaleID(Settings.PriceGroup), "Price" },
                { m_Setting.GetOptionGroupLocaleID(Settings.StatsGroup), "Statistics" },
                { m_Setting.GetOptionGroupLocaleID(Settings.CompatGroup), "Compatibility" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.Enabled)), "Enabled" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.Enabled)), "Charge money for terrain-tool brush strokes (raising, lowering, levelling, softening, sloping), priced by the real volume of earth moved. Building, road and water-tool terraforming is never charged." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.Multiplier)), "Price multiplier" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.Multiplier)), "Scales the price per cubic metre moved. 0 = free, 1 = default price, up to 10x." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.OtherToolsFactor)), "Level/Soften/Slope price factor" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.OtherToolsFactor)), "Price factor for Level, Soften and Slope relative to Raise/Lower for the same measured volume. Default 100% (every m³ of moved ground costs the same)." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.RefundPercent)), "Refund when lowering" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.RefundPercent)), "Percentage refunded for the part of a stroke's volume that lowered terrain (raised volume in the same stroke is never refunded). Default 0% (no refund)." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.TotalCharged)), "Total charged this session" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.BlockedStrokes)), "Strokes blocked (not enough money)" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.ResetStats)), "Reset statistics" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.ResetStats)), "Resets the counters above to zero. Does not affect your money." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Settings.ResetStats)), "Reset TerraformCost statistics?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.HardModeContinuedDetected)), "Hard Mode Continued detected" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.HardModeContinuedDetected)), "Hard Mode Continued also charges for terraforming. If both mods are enabled you will be charged by both - consider disabling one of them." },

                { "TerraformCost.STROKE_VOLUME", "Moved" },
                { "TerraformCost.STROKE_COST", "Terraform cost" },
                { "TerraformCost.PRICE_PER_100M3", "Price / 100 m³" },
                { "TerraformCost.NOT_ENOUGH_MONEY", "Not enough money" },
            };
        }

        public void Unload()
        {
        }
    }
}
