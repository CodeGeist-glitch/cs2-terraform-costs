using System.Collections.Generic;
using Colossal;

namespace TerraformCost
{
    public class LocaleDE : IDictionarySource
    {
        private readonly Settings m_Setting;

        public LocaleDE(Settings setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Geländekosten" },

                { m_Setting.GetOptionTabLocaleID(Settings.MainTab), "Allgemein" },
                { m_Setting.GetOptionGroupLocaleID(Settings.GeneralGroup), "Allgemein" },
                { m_Setting.GetOptionGroupLocaleID(Settings.PriceGroup), "Preis" },
                { m_Setting.GetOptionGroupLocaleID(Settings.StatsGroup), "Statistik" },
                { m_Setting.GetOptionGroupLocaleID(Settings.CompatGroup), "Kompatibilität" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.Enabled)), "Aktiviert" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.Enabled)), "Berechnet Kosten für Geländewerkzeug-Striche (Anheben, Absenken, Einebnen, Glätten, Neigen) nach dem tatsächlich bewegten Erdvolumen. Gebäude-, Straßen- und Wasserwerkzeug-Terraforming wird nie berechnet." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.Multiplier)), "Preismultiplikator" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.Multiplier)), "Skaliert den Preis pro Kubikmeter. 0 = kostenlos, 1 = Standardpreis, bis zu 10x." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.OtherToolsFactor)), "Einebnen/Glätten/Neigen-Faktor" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.OtherToolsFactor)), "Preisfaktor für Einebnen, Glätten und Neigen im Vergleich zu Anheben/Absenken beim gleichen gemessenen Volumen. Standard 100% (jeder bewegte m³ kostet gleich viel)." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.RefundPercent)), "Rückerstattung beim Absenken" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.RefundPercent)), "Rückerstattungsanteil für den Teil des Strichvolumens, der Gelände abgesenkt hat (angehobenes Volumen im selben Strich wird nie erstattet). Standard 0% (keine Rückerstattung)." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.TotalCharged)), "Insgesamt berechnet (diese Sitzung)" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.BlockedStrokes)), "Blockierte Striche (nicht genug Geld)" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.ResetStats)), "Statistik zurücksetzen" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.ResetStats)), "Setzt die obigen Zähler auf null zurück. Hat keinen Einfluss auf Ihr Geld." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Settings.ResetStats)), "TerraformCost-Statistik zurücksetzen?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Settings.HardModeContinuedDetected)), "Hard Mode Continued erkannt" },
                { m_Setting.GetOptionDescLocaleID(nameof(Settings.HardModeContinuedDetected)), "Hard Mode Continued berechnet ebenfalls Kosten für Terraforming. Wenn beide Mods aktiv sind, werden Sie von beiden belastet - erwägen Sie, einen der beiden zu deaktivieren." },

                { "TerraformCost.STROKE_VOLUME", "Bewegt" },
                { "TerraformCost.STROKE_COST", "Geländekosten" },
                { "TerraformCost.PRICE_PER_100M3", "Preis / 100 m³" },
                { "TerraformCost.NOT_ENOUGH_MONEY", "Nicht genug Geld" },
            };
        }

        public void Unload()
        {
        }
    }
}
