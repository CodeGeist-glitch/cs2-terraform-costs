using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace TerraformCost
{
    /// <summary>
    /// Height-readback helper. Two callers:
    ///  - TestApi.HeightAt (calibration-only): a single forced-sync sample, waitForPending=true is fine
    ///    for an occasional test call (research/40 A3).
    ///  - StrokeVolume (T-3, per-frame while a stroke is active): many samples per call, but always via
    ///    <see cref="TryGetHeightData"/> with waitForPending=false, i.e. never a synchronous GPU stall -
    ///    it just reads whatever the AsyncGPUReadback CPU mirror currently holds (can lag the GPU by a
    ///    frame or more - StrokeVolume's settle window and one final forced read at stroke-end are what
    ///    compensate for that lag, not per-frame stalls).
    /// </summary>
    public static class TerrainHeight
    {
        /// <summary>Fetches the CPU heightmap mirror once (cheap unless waitForPending forces a stall); reuse the result for many SampleHeight calls in the same frame instead of calling this once per sample.</summary>
        public static bool TryGetHeightData(bool waitForPending, out TerrainHeightData hd)
        {
            hd = default;
            World world = World.DefaultGameObjectInjectionWorld;
            TerrainSystem terrain = world?.GetExistingSystemManaged<TerrainSystem>();
            if (terrain == null) return false;
            hd = terrain.GetHeightData(waitForPending);
            return hd.isCreated;
        }

        public static float SampleHeight(ref TerrainHeightData hd, float x, float z)
        {
            return TerrainUtils.SampleHeight(ref hd, new float3(x, 0f, z));
        }

        /// <summary>Single-sample convenience wrapper (TestApi.HeightAt only - see class doc for why the hot path never uses this).</summary>
        public static bool TrySample(float x, float z, bool waitForPending, out float height)
        {
            height = 0f;
            if (!TryGetHeightData(waitForPending, out TerrainHeightData hd)) return false;
            height = SampleHeight(ref hd, x, z);
            return true;
        }
    }
}
