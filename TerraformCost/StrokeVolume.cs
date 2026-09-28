using System.Collections.Generic;
using Game.Prefabs;
using Game.Tools;
using UnityEngine;

namespace TerraformCost
{
    /// <summary>
    /// T-3: measured-volume pricing, for all four TerraformingType modes (Shift/Level/Soften/Slope) alike.
    ///
    /// At stroke start, snapshots CPU terrain heights (the AsyncGPUReadback mirror, research/40 A3 -
    /// never a forced sync read except once at stroke-end) inside the brush's footprint. While the same
    /// stroke continues, the tracked region GROWS to cover wherever the brush has since reached (new
    /// cells are snapshotted the moment they first enter the region, before the brush has touched them),
    /// and every touch re-measures the *whole* region so far and charges
    /// <c>Sum(|h_now - h_start| * cellArea) * PricePerCubicMeter - alreadyCharged</c> as the delta since
    /// the last measurement - this is what makes the price track the real moved dirt regardless of any
    /// vanilla per-type strength shaping upstream (T-2: TerrainToolSystem eases Shift/Soften strength by
    /// up to 0.4-1x depending on brush size before it ever reaches ApplyBrush - the old analytic formula
    /// trusted the raw, already-eased brush.m_Strength and Level/Slope's own effective strength/skip
    /// rules in ApplyBrushesSystem.ApplyHeight, so it never charged what the terrain actually did; the
    /// measured-volume approach is blind to all of that and just charges for the real height change).
    ///
    /// Blocking (prefix returns false, T1.2/T-3) is necessarily a PREDICTION: the real cost of the brush
    /// call about to happen isn't knowable until the terrain has actually changed and the CPU mirror has
    /// caught up (a frame or more later), so we block the next call only when money can't cover the most
    /// recently measured per-touch increment (the "recent charge rate").
    ///
    /// Main-thread only - same call chain as PipelineGate/ApplyBrushPatch, no Burst/job scheduling here.
    /// </summary>
    public static class StrokeVolume
    {
        /// <summary>
        /// Sampling grid cell size in metres, chosen per-stroke from the opening brush size (fixed for the
        /// rest of that stroke - the incremental Sum(|delta|*cellArea) measurement needs a stable grid).
        /// Small brushes get a fine grid close to the native heightmap texel size (~3.5 m @ 4096 over
        /// 14336 m, research/40 A3); big brushes get a coarser one so <see cref="MaxCells"/> is reached
        /// only for drags much longer than the brush footprint itself, not by the footprint alone.
        /// </summary>
        private static float CellSizeFor(float brushSize) => Mathf.Clamp(brushSize / 25f, 2f, 20f);

        /// <summary>Extra metres added around the brush's nominal radius so the falloff edge (visible in real strokes, plans/02_test_results.md "Session 2 - F1") is inside the measured region.</summary>
        private const float BoundsMargin = 3f;

        /// <summary>Real frames with no Touch() before a stroke is considered released (mouse up / tool switch) and finalised. Small: the tool only calls us while the button is actually held, so any gap this size means it stopped.</summary>
        private const int IdleFramesForSettle = 4;

        /// <summary>Safety cap on tracked cells for one stroke (a ~570x570 m region at CellSize=4) so an extreme drag can't grow memory/CPU cost unboundedly; touches beyond it just stop growing the region (undercharge at the margin, never a crash).</summary>
        private const int MaxCells = 20000;

        private struct Cell
        {
            public float Baseline;
            public float Last;
        }

        private static readonly Dictionary<long, Cell> s_Cells = new Dictionary<long, Cell>();
        private static readonly List<long> s_CellOrder = new List<long>();

        private static bool s_Active;
        private static TerraformingType s_Type;
        private static float s_CellSize = 4f;
        private static float s_CellArea = 16f;
        private static int s_LastTouchFrame = int.MinValue;
        private static int s_ChargedWhole;
        private static float s_LastIncrementCost;

        public static bool StrokeActive => s_Active;
        public static float StrokeVolumeM3 { get; private set; }
        public static float StrokeCostSoFar { get; private set; }

        private static long CellKey(int ix, int iz) => ((long)ix << 32) ^ (uint)iz;

        /// <summary>Called from ApplyBrushPatch.Prefix, inside the pipeline gate, BEFORE the real ApplyBrush. Returns false to block (skip) this call.</summary>
        public static bool TryChargeForBrush(TerraformingType type, Brush brush, int frameCount)
        {
            if (s_Active && (type != s_Type || frameCount - s_LastTouchFrame > IdleFramesForSettle))
            {
                // Shouldn't normally happen (Tick() settles idle strokes first), but never charge across a
                // stale/mismatched stroke - finalise defensively before starting the new one.
                FinishStroke(frameCount);
            }

            if (!s_Active)
            {
                BeginStroke(type, brush.m_Size);
            }

            GetBounds(brush, out float minX, out float maxX, out float minZ, out float maxZ);

            bool grew = GrowRegion(minX, maxX, minZ, maxZ);
            if (grew || s_Cells.Count > 0)
            {
                Remeasure(frameCount);
            }

            s_LastTouchFrame = frameCount;
            s_Active = true;

            // Predictive block for THIS about-to-happen call, from the most recently measured increment.
            int predicted = Mathf.CeilToInt(s_LastIncrementCost);
            if (predicted > 0 && !Budget.CanAfford(predicted, frameCount))
            {
                Budget.NoteBlocked();
                return false;
            }
            return true;
        }

        /// <summary>Called every real frame (pause-safe phase) by TerraformChargeSystem. Detects stroke release and finalises with one forced-readback remeasurement.</summary>
        public static void Tick(int frameCount)
        {
            if (!s_Active) return;
            if (frameCount - s_LastTouchFrame < IdleFramesForSettle) return;
            FinishStroke(frameCount);
        }

        private static void BeginStroke(TerraformingType type, float brushSize)
        {
            s_Cells.Clear();
            s_CellOrder.Clear();
            s_Type = type;
            s_CellSize = CellSizeFor(brushSize);
            s_CellArea = s_CellSize * s_CellSize;
            s_ChargedWhole = 0;
            s_LastIncrementCost = 0f;
            StrokeVolumeM3 = 0f;
            StrokeCostSoFar = 0f;
        }

        private static void FinishStroke(int frameCount)
        {
            // One forced sync readback so the settle-time measurement (task requirement: "for a short
            // settle time after the mouse is released, the CPU height copy lags the GPU a few frames")
            // is accurate, not stale - acceptable as a single one-off per stroke, not a per-frame stall.
            Remeasure(frameCount, forceSync: true);
            Budget.LastStrokeCost = StrokeCostSoFar;
            s_Active = false;
            s_Cells.Clear();
            s_CellOrder.Clear();
        }

        private static void GetBounds(Brush brush, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            float half = brush.m_Size * 0.5f + BoundsMargin;
            minX = brush.m_Position.x - half;
            maxX = brush.m_Position.x + half;
            minZ = brush.m_Position.z - half;
            maxZ = brush.m_Position.z + half;
        }

        /// <summary>Seeds baseline=current height for any cell inside the bounds not already tracked. Returns true if at least one new cell was added.</summary>
        private static bool GrowRegion(float minX, float maxX, float minZ, float maxZ)
        {
            if (s_Cells.Count >= MaxCells) return false;
            if (!TerrainHeight.TryGetHeightData(false, out var hd)) return false;

            int ixMin = Mathf.FloorToInt(minX / s_CellSize);
            int ixMax = Mathf.FloorToInt(maxX / s_CellSize);
            int izMin = Mathf.FloorToInt(minZ / s_CellSize);
            int izMax = Mathf.FloorToInt(maxZ / s_CellSize);

            bool grew = false;
            for (int ix = ixMin; ix <= ixMax; ix++)
            {
                for (int iz = izMin; iz <= izMax; iz++)
                {
                    long key = CellKey(ix, iz);
                    if (s_Cells.ContainsKey(key)) continue;
                    if (s_Cells.Count >= MaxCells) return grew;

                    float wx = (ix + 0.5f) * s_CellSize;
                    float wz = (iz + 0.5f) * s_CellSize;
                    float h = TerrainHeight.SampleHeight(ref hd, wx, wz);
                    s_Cells[key] = new Cell { Baseline = h, Last = h };
                    s_CellOrder.Add(key);
                    grew = true;
                }
            }
            return grew;
        }

        /// <summary>Re-samples every tracked cell, recomputes cumulative moved volume/cost, and reserves+drains the delta since the last measurement (T-3 incremental charging).</summary>
        private static void Remeasure(int frameCount, bool forceSync = false)
        {
            if (!TerrainHeight.TryGetHeightData(forceSync, out var hd))
            {
                s_LastIncrementCost = 0f;
                return;
            }

            float raisedVol = 0f;
            float loweredVol = 0f;

            for (int i = 0; i < s_CellOrder.Count; i++)
            {
                long key = s_CellOrder[i];
                Cell c = s_Cells[key];
                int ix = (int)(key >> 32);
                int iz = (int)key;
                float wx = (ix + 0.5f) * s_CellSize;
                float wz = (iz + 0.5f) * s_CellSize;
                float h = TerrainHeight.SampleHeight(ref hd, wx, wz);
                c.Last = h;
                s_Cells[key] = c;

                float d = h - c.Baseline;
                if (d > 0f) raisedVol += d * s_CellArea;
                else loweredVol += -d * s_CellArea;
            }

            float OtherToolsFactor = (s_Type == TerraformingType.Shift) ? 1f : Mathf.Max(0f, Settings.instance?.OtherToolsFactor ?? CostCalculator.DefaultModeFactor);
            float refundFrac = Mathf.Clamp01((Settings.instance?.RefundPercent ?? 0f) / 100f);
            float multiplier = Mathf.Max(0f, Settings.instance?.Multiplier ?? 1f);

            float rawCost = (raisedVol + loweredVol * (1f - refundFrac)) * CostCalculator.PricePerCubicMeter * OtherToolsFactor * multiplier;
            int totalWhole = Mathf.RoundToInt(rawCost);

            int increment = totalWhole - s_ChargedWhole;
            if (increment > 0)
            {
                int reserved = Budget.ReserveUpTo(increment, frameCount);
                s_ChargedWhole += reserved;
                s_LastIncrementCost = reserved;
            }
            else
            {
                s_LastIncrementCost = 0f;
            }

            StrokeVolumeM3 = raisedVol + loweredVol;
            StrokeCostSoFar = s_ChargedWhole;
        }
    }
}
