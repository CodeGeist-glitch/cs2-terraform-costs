using System;
using Game.Prefabs;
using Game.Tools;
using Game.UI.Localization;
using Game.UI.Tooltip;
using UnityEngine;

namespace TerraformCost.Systems
{
    /// <summary>
    /// T-3: while the terrain tool is active, shows the live measured volume/cost of the CURRENT stroke
    /// ("Moved: X m3" / "Terraform cost: Y") plus the effective price per 100 m3 for the active tool, and
    /// a red "Not enough money" warning for a short while after a brush gets blocked (T1.2).
    /// </summary>
    public class TerraformCostTooltipSystem : TooltipSystemBase
    {
        private const int BlockedStickyFrames = 30; // ~0.5s at 60 fps, so the warning doesn't flicker

        private ToolSystem m_ToolSystem;
        private IntTooltip m_MovedVolume;
        private IntTooltip m_StrokeCost;
        private IntTooltip m_PricePer100M3;
        private StringTooltip m_NotEnoughMoney;

        private bool m_HasEverBlocked;
        private int m_LastBlockedFrame;
        private long m_LastBlockedCountSeen;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();

            m_MovedVolume = new IntTooltip
            {
                path = "terraformCostVolume",
                label = LocalizedString.Id("TerraformCost.STROKE_VOLUME"),
                unit = "volume",
            };
            m_StrokeCost = new IntTooltip
            {
                path = "terraformCostStroke",
                label = LocalizedString.Id("TerraformCost.STROKE_COST"),
                unit = "money",
            };
            m_PricePer100M3 = new IntTooltip
            {
                path = "terraformCostPricePer100m3",
                label = LocalizedString.Id("TerraformCost.PRICE_PER_100M3"),
                unit = "money",
            };
            m_NotEnoughMoney = new StringTooltip
            {
                path = "terraformCostNotEnoughMoney",
                value = LocalizedString.Id("TerraformCost.NOT_ENOUGH_MONEY"),
                color = TooltipColor.Error,
            };
        }

        protected override void OnUpdate()
        {
            try
            {
                if (Settings.instance != null && !Settings.instance.Enabled) return;
                if (!(m_ToolSystem.activeTool is TerrainToolSystem terrain)) return;

                // Detect a new block since our last update (Budget.BlockedFrames only ever grows).
                //
                // BUG FIXED (0.9.1, found via real-mouse T-4 testing - testharness/results_T1_v2.md):
                // m_LastBlockedFrame used to be seeded with int.MinValue as an "never blocked yet"
                // sentinel, and the sticky check did `Time.frameCount - m_LastBlockedFrame <= 30`
                // unconditionally. For ANY realistic Time.frameCount (0 and up), frameCount - int.MinValue
                // overflows a signed 32-bit int (wraps to a very negative number, which is always <= 30),
                // so "Not enough money" was shown continuously from the moment the terrain tool was first
                // opened, for the entire session - until the first REAL block finally seeded a sane recent
                // frame number. This is very plausibly what the user actually saw and reported as T-1 ("Not
                // enough money" with 1M in the bank): a cosmetic tooltip bug, not (only) a real block. Fix:
                // gate the sticky check behind an explicit "has a real block ever happened" flag so the
                // frame-count subtraction is only ever done against a real, recent value.
                long blockedNow = Budget.BlockedFrames;
                if (blockedNow != m_LastBlockedCountSeen)
                {
                    m_LastBlockedCountSeen = blockedNow;
                    m_LastBlockedFrame = UnityEngine.Time.frameCount;
                    m_HasEverBlocked = true;
                }
                if (m_HasEverBlocked && UnityEngine.Time.frameCount - m_LastBlockedFrame <= BlockedStickyFrames)
                {
                    AddMouseTooltip(m_NotEnoughMoney);
                }

                TerraformingType type = terrain.prefab != null ? terrain.prefab.m_Type : TerraformingType.Shift;
                float multiplier = Settings.instance?.Multiplier ?? 1f;
                float OtherToolsFactor = (type == TerraformingType.Shift) ? 1f : (Settings.instance?.OtherToolsFactor ?? CostCalculator.DefaultModeFactor);

                // Static price/100m3 for the currently selected mode (informational - the live cost below is what actually gets charged).
                m_PricePer100M3.value = Mathf.RoundToInt(CostCalculator.PricePerCubicMeter * OtherToolsFactor * multiplier * 100f);
                AddMouseTooltip(m_PricePer100M3);

                // Live measured volume/cost while a stroke is active; StrokeVolume keeps the last completed
                // stroke's totals visible (unreset) until a new one begins, matching the pre-0.9.1 tooltip's
                // "shows the last stroke's cost" behaviour.
                m_MovedVolume.value = Mathf.RoundToInt(StrokeVolume.StrokeVolumeM3);
                AddMouseTooltip(m_MovedVolume);

                m_StrokeCost.value = Mathf.RoundToInt(StrokeVolume.StrokeCostSoFar);
                AddMouseTooltip(m_StrokeCost);
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "TerraformCostTooltipSystem.OnUpdate failed; disabling tooltip");
                Enabled = false;
            }
        }
    }
}
