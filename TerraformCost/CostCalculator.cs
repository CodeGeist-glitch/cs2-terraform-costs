namespace TerraformCost
{
    /// <summary>
    /// Shared pricing constants (T-3: measured-volume pricing, see <see cref="StrokeVolume"/> for the
    /// actual formula - <c>cost = movedVolumeM3 * PricePerCubicMeter * OtherToolsFactor(type) * multiplier</c>,
    /// with the lowering refund % applied only to the lowered part of the volume).
    ///
    /// Superseded design note: versions up to 0.9.0 computed cost analytically from the brush's own
    /// parameters (size/strength/dt) instead of measuring the real terrain change - see
    /// testharness/results_T1.md and plans/02_test_results.md "Session 2 - F1" for that history. It was
    /// replaced because (a) real brushes have a soft edge falloff a nominal-disk-area estimate can't see
    /// (F1's ~2.78x overcharge), and (b) TerrainToolSystem applies its own per-type strength shaping
    /// (research/41 S1: up to 0.4-1x for Shift/Soften depending on brush size) before the value ever
    /// reaches ApplyBrush, which the old formula didn't know about (root cause of T-2: Level/Soften/Slope
    /// charged far less than intended). Measuring the real height change sidesteps both problems.
    /// </summary>
    public static class CostCalculator
    {
        /// <summary>
        /// Price in whole city-currency units per cubic metre of REAL measured terrain change, at
        /// multiplier = 1x. Carried over from the F1 real-play grid calibration
        /// (plans/02_test_results.md "Session 2 - F1": 0.16552 / 2.7818 = 0.05950, confirmed within 3% of
        /// the "raise a ~40 m circular plot by 2 m ~= 100 m Small Road" target against a real, mouse-driven
        /// stroke measured with a 9x9 height grid) and re-verified for the 0.9.1 measured-volume rewrite
        /// (testharness/results_T1_v2.md) - since StrokeVolume's own live measurement is that same
        /// grid-sampling technique, generalised and run every frame, the F1 constant is the right starting
        /// point rather than a fresh guess.
        /// </summary>
        public const float PricePerCubicMeter = 0.05950f;

        /// <summary>Level/Soften/Slope cost this fraction of Shift's price for the same measured volume (plan decision 2 default; also a user setting).</summary>
        public const float DefaultModeFactor = 1.0f; // user feedback 2026-09-28: volume is volume, same price per m3 for all four tools
    }
}
