using System;
using Game;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using HarmonyLib;
using UnityEngine;

namespace TerraformCost.Patches
{
    /// <summary>
    /// Prefix on Game.Simulation.TerrainSystem.ApplyBrush (research/40 A1, /41 S1). Delegates to
    /// <see cref="StrokeVolume"/> (T-3: measured-volume pricing) for the actual charge/block decision -
    /// this patch is now just the gate/enable/editor checks plus the entry point into that tracker. Only
    /// ever charges when PipelineGate.InPipeline is set, i.e. only for calls that came from
    /// ApplyBrushesSystem.ApplyHeight (the vanilla terrain-tool pipeline, all four TerraformingType modes
    /// - T-2) - DisasterDirector's direct ApplyBrush calls for craters are never gated on, so never
    /// charged. Money itself is never written here - see Budget/TerraformChargeSystem.
    /// </summary>
    [HarmonyPatch(typeof(Game.Simulation.TerrainSystem), "ApplyBrush")]
    public static class ApplyBrushPatch
    {
        private static long s_CallCount; // kept for TestApi.Ping() from the P0 skeleton
        private static bool s_LoggedError;

        public static long CallCount => System.Threading.Interlocked.Read(ref s_CallCount);

        [HarmonyPrefix]
        public static bool Prefix(TerraformingType type, Brush brush)
        {
            System.Threading.Interlocked.Increment(ref s_CallCount);
            try
            {
                if (s_LoggedError) return true; // charging broke once already this session; stay out of the way
                if (!PipelineGate.InPipeline) return true; // not the vanilla terrain-tool pipeline (e.g. DisasterDirector craters) - never charged
                if (Settings.instance != null && !Settings.instance.Enabled) return true;

                GameManager gm = GameManager.instance;
                if (gm == null || !gm.gameMode.IsGame()) return true; // editor / main menu / other - always free

                float dt = Time.unscaledDeltaTime;
                if (dt <= 0f) return true; // nothing actually elapsed this real frame - no measurement possible yet

                return StrokeVolume.TryChargeForBrush(type, brush, Time.frameCount);
            }
            catch (Exception e)
            {
                if (!s_LoggedError)
                {
                    s_LoggedError = true;
                    Mod.log.Error(e, "ApplyBrushPatch.Prefix failed; TerraformCost charging disabled for the rest of this session (terrain edits stay free)");
                }
                return true; // never block the brush because OUR code broke
            }
        }
    }
}
