namespace TerraformCost
{
    /// <summary>
    /// Set true only while we are inside <c>ApplyBrushesSystem.ApplyHeight</c> (the vanilla terrain-tool
    /// pipeline), by <see cref="Patches.ApplyBrushesGatePatch"/>. <see cref="Patches.ApplyBrushPatch"/>
    /// only charges when this is true, so DisasterDirector's direct calls to
    /// <c>TerrainSystem.ApplyBrush</c> (meteor craters etc.) are never charged, no matter how it calls it.
    /// Main-thread only (both the gate and the terrain system run on the main thread; no Burst/IJobEntity
    /// is involved for this call chain), so a plain bool is enough - no Interlocked needed.
    /// </summary>
    public static class PipelineGate
    {
        public static bool InPipeline;
    }
}
