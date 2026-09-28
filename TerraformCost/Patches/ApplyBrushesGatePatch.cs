using System;
using Game.Prefabs;
using Game.Tools;
using HarmonyLib;
using Unity.Entities;

namespace TerraformCost.Patches
{
    /// <summary>
    /// Tightly wraps the ONE call site that the vanilla terrain-tool pipeline uses to reach
    /// TerrainSystem.ApplyBrush for height edits - ApplyBrushesSystem.ApplyHeight (private,
    /// research/40 A1 / research/41 S1: Game.Tools/ApplyBrushesSystem.cs:302-314). Setting
    /// PipelineGate.InPipeline only around this exact call (not the whole system OnUpdate) means:
    ///  - DisasterDirector calling TerrainSystem.ApplyBrush directly (meteor craters etc.) never sees
    ///    the gate set, so it is never charged, by construction - no cooperation needed from that mod.
    ///  - Vanilla Ore/Oil/FertileLand/GroundWater/Material brushes (other branches of ApplyBrushesSystem)
    ///    also never set the gate, since they never call ApplyHeight.
    /// A HarmonyFinalizer guarantees the flag is cleared even if ApplyHeight throws.
    /// </summary>
    [HarmonyPatch(typeof(ApplyBrushesSystem), "ApplyHeight", new[] { typeof(Brush), typeof(Entity), typeof(TerraformingType) })]
    public static class ApplyBrushesGatePatch
    {
        private static bool s_LoggedError;

        [HarmonyPrefix]
        public static void Prefix()
        {
            try { PipelineGate.InPipeline = true; }
            catch (Exception e) { LogOnce(e); }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try { PipelineGate.InPipeline = false; }
            catch (Exception e) { LogOnce(e); }
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            PipelineGate.InPipeline = false;
            return __exception;
        }

        private static void LogOnce(Exception e)
        {
            if (s_LoggedError) return;
            s_LoggedError = true;
            Mod.log.Error(e, "ApplyBrushesGatePatch failed");
        }
    }
}
