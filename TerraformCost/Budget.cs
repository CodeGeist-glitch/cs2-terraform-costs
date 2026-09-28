using System.Threading;

namespace TerraformCost
{
    /// <summary>
    /// Money is never written from inside the Harmony patch (plan section 3.2). Instead:
    ///  - <see cref="TerraformChargeSystem"/> publishes <see cref="AvailableMoney"/> once per real frame
    ///    (SystemUpdatePhase.PreTool - runs every frame including while paused, T-1 fix) from the real
    ///    <c>PlayerMoney</c> (or "unlimited" if there is no city / PlayerMoney component, e.g. the map
    ///    editor - see research/40 A4).
    ///  - <see cref="Patches.ApplyBrushPatch"/> (via <see cref="StrokeVolume"/>, T-3) calls
    ///    <see cref="CanAfford"/> to decide whether to block the next brush application (a prediction,
    ///    since real cost is only known after the terrain has actually changed), and
    ///    <see cref="ReserveUpTo"/> to actually charge the measured, already-happened height change -
    ///    this always takes whatever fits rather than failing outright, so a real (unpreventable) charge
    ///    for terrain that already moved can never push money below 0.
    ///  - <see cref="TerraformChargeSystem"/> then drains <see cref="DrainPendingCharge"/> and subtracts
    ///    the real money once per frame.
    /// </summary>
    public static class Budget
    {
        /// <summary>Published once per frame by TerraformChargeSystem. int.MaxValue = unlimited / no city (free).</summary>
        public static int AvailableMoney = int.MaxValue;

        /// <summary>False when there is no CitySystem.City / PlayerMoney (editor, main menu) - charging is then a no-op (always free).</summary>
        public static bool HasPlayerMoney;

        private static int s_PendingCharge;
        private static int s_ReservedThisFrame;
        private static int s_ReservedFrame = int.MinValue;

        // Running totals for Stats(), never reset (only ResetStats()).
        private static long s_TotalCharged;
        private static long s_BlockedFrames;
        private static float s_LastStrokeCost;

        public static long TotalCharged => Interlocked.Read(ref s_TotalCharged);
        public static long BlockedFrames => Interlocked.Read(ref s_BlockedFrames);
        public static float LastStrokeCost { get => s_LastStrokeCost; set => s_LastStrokeCost = value; }

        private static void ResetFrameBucketIfNeeded(int frameCount)
        {
            if (frameCount != s_ReservedFrame)
            {
                s_ReservedThisFrame = 0;
                s_ReservedFrame = frameCount;
            }
        }

        /// <summary>
        /// Read-only affordability check for <paramref name="cost"/> (whole currency units) against this
        /// frame's published balance minus whatever has already been reserved this same frame - no side
        /// effects. Used by StrokeVolume to decide whether to block the NEXT brush application (T-3:
        /// "block when money can't cover the estimated next-frame cost").
        /// </summary>
        public static bool CanAfford(int cost, int frameCount)
        {
            if (cost <= 0) return true;
            if (!HasPlayerMoney) return true;
            ResetFrameBucketIfNeeded(frameCount);
            return AvailableMoney - s_ReservedThisFrame >= cost;
        }

        /// <summary>
        /// Reserves as much of <paramref name="want"/> (whole currency units) as currently fits, never
        /// more - so this can never push money below 0, even when charging for a height change that has
        /// already physically happened (StrokeVolume's retroactive, measured charge) and can't be undone.
        /// Returns the amount actually reserved (0..want). Free (returns want unconditionally, no stats
        /// impact) when there's no PlayerMoney at all (editor/unlimited - matches the old TryReserve).
        /// </summary>
        public static int ReserveUpTo(int want, int frameCount)
        {
            if (want <= 0) return 0;
            if (!HasPlayerMoney) return want;

            ResetFrameBucketIfNeeded(frameCount);
            int available = AvailableMoney - s_ReservedThisFrame;
            int reserve = want < available ? want : available;
            if (reserve <= 0) return 0;

            s_ReservedThisFrame += reserve;
            Interlocked.Add(ref s_PendingCharge, reserve);
            Interlocked.Add(ref s_TotalCharged, reserve);
            return reserve;
        }

        /// <summary>Called once when StrokeVolume's predictive check blocks a brush application (T1.2/T-3).</summary>
        public static void NoteBlocked()
        {
            Interlocked.Increment(ref s_BlockedFrames);
        }

        /// <summary>Called once per frame by TerraformChargeSystem; returns and clears this frame's approved charge.</summary>
        public static int DrainPendingCharge()
        {
            return Interlocked.Exchange(ref s_PendingCharge, 0);
        }

        public static void ResetStats()
        {
            Interlocked.Exchange(ref s_TotalCharged, 0);
            Interlocked.Exchange(ref s_BlockedFrames, 0);
            s_LastStrokeCost = 0f;
        }
    }
}
