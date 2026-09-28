using System;
using System.Linq;
using Game.Prefabs;
using Game.Tools;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TerraformCost.Patches;
using TerraformCost.Systems;
using Unity.Collections;
using Unity.Entities;

namespace TerraformCost
{
    /// <summary>Static API surface for ModTestHarness's "invoke" command (reflection-based, no assembly reference needed). All methods are safe to call only from the main thread (true for HarnessBehaviour).</summary>
    public static class TestApi
    {
        /// <summary>Returns {"mod":"TerraformCost","version":..., "patched":true|false, "applyBrushCalls":N, "gateInPipeline":bool}.</summary>
        public static string Ping()
        {
            bool patched = false;
            try
            {
                var target = AccessTools.Method(typeof(Game.Simulation.TerrainSystem), "ApplyBrush");
                var info = target == null ? null : Harmony.GetPatchInfo(target);
                patched = info != null && info.Prefixes.Any(p => p.owner == Mod.HarmonyId);
            }
            catch (Exception e)
            {
                Mod.log.Warn("TestApi.Ping patch check failed: " + e.Message);
            }

            var o = new JObject
            {
                ["mod"] = "TerraformCost",
                ["version"] = Mod.Version,
                ["patched"] = patched,
                ["applyBrushCalls"] = ApplyBrushPatch.CallCount,
                ["gateInPipeline"] = PipelineGate.InPipeline,
                ["hasPlayerMoney"] = Budget.HasPlayerMoney,
                ["hardModeContinuedLoaded"] = Compat.HardModeContinuedLoaded,
            };
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>Returns {"ok":true,"money":N} or {"ok":false,"error":"no city/PlayerMoney (editor?)"}.</summary>
        public static string GetMoney()
        {
            if (GameMoney.TryRead(out int money))
                return new JObject { ["ok"] = true, ["money"] = money }.ToString(Newtonsoft.Json.Formatting.None);
            return new JObject { ["ok"] = false, ["error"] = "no city/PlayerMoney (editor or not in a game)" }.ToString(Newtonsoft.Json.Formatting.None);
        }

#if !RELEASE_PUBLIC
        /// <summary>Test-only helper: directly adds (or removes, if negative) money, bypassing the charging path entirely. Returns {"ok":true,"money":N} (new balance) or {"ok":false,"error":...}. Stripped from public-release builds (ReleasePublic=true) - not something a subscriber's game should expose.</summary>
        public static string AddMoney(int delta)
        {
            if (GameMoney.TryAdd(delta) && GameMoney.TryRead(out int money))
                return new JObject { ["ok"] = true, ["money"] = money }.ToString(Newtonsoft.Json.Formatting.None);
            return new JObject { ["ok"] = false, ["error"] = "no city/PlayerMoney (editor or not in a game)" }.ToString(Newtonsoft.Json.Formatting.None);
        }
#endif

        /// <summary>Returns {"totalCharged":N,"blockedStrokes":N,"lastStrokeCost":N,"applyBrushCalls":N,"strokeActive":bool,"strokeVolumeM3":N,"strokeCostSoFar":N}. The stroke* fields are T-3's live measured-volume tracker (see StrokeVolume) - strokeVolumeM3/strokeCostSoFar hold the CURRENT stroke's totals while strokeActive, else the last completed stroke's totals (unreset until a new one begins).</summary>
        public static string Stats()
        {
            var o = new JObject
            {
                ["totalCharged"] = Budget.TotalCharged,
                ["blockedStrokes"] = Budget.BlockedFrames,
                ["lastStrokeCost"] = Budget.LastStrokeCost,
                ["applyBrushCalls"] = ApplyBrushPatch.CallCount,
                ["availableMoney"] = Budget.AvailableMoney,
                ["hasPlayerMoney"] = Budget.HasPlayerMoney,
                ["strokeActive"] = StrokeVolume.StrokeActive,
                ["strokeVolumeM3"] = StrokeVolume.StrokeVolumeM3,
                ["strokeCostSoFar"] = StrokeVolume.StrokeCostSoFar,
            };
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Drives a brush of the given world-space size (diameter, metres) and strength (sign = raise/
        /// lower, only meaningful for type="Shift") at (x,z) through the REAL vanilla pipeline for
        /// `frames` ticks (see TestStrokeSystem), so ApplyBrushesGatePatch/ApplyBrushPatch are exercised
        /// exactly as for a player stroke. Starts the run and returns immediately - poll LastTestResult().
        /// type is one of Shift, Level, Soften, Slope (TerraformingType).
        /// Returns {"ok":true,"started":true} or {"ok":false,"error":"..."}.
        /// </summary>
        public static string TestStroke(float x, float z, float size, float strength, int frames, string type)
        {
            var system = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<TestStrokeSystem>();
            if (system == null)
                return new JObject { ["ok"] = false, ["error"] = "TestStrokeSystem not in world (not in a game?)" }.ToString(Newtonsoft.Json.Formatting.None);

            string err = system.TryStart(x, z, size, strength, frames, type);
            if (err != null)
                return new JObject { ["ok"] = false, ["error"] = err }.ToString(Newtonsoft.Json.Formatting.None);
            return new JObject { ["ok"] = true, ["started"] = true }.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>Polls the result of the last TestStroke() call. Returns {"running":true} while in progress, or the final result object once done.</summary>
        public static string LastTestResult()
        {
            var system = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<TestStrokeSystem>();
            if (system != null && system.IsRunning) return "{\"running\":true}";
            return TestStrokeSystem.LastResultJson;
        }

        /// <summary>Calibration helper (research/40 A3): forces a synchronous heightmap readback and samples world height at (x,z). Returns {"ok":true,"height":H} or {"ok":false,"error":...}.</summary>
        public static string HeightAt(float x, float z)
        {
            if (TerrainHeight.TrySample(x, z, true, out float h))
                return new JObject { ["ok"] = true, ["height"] = h }.ToString(Newtonsoft.Json.Formatting.None);
            return new JObject { ["ok"] = false, ["error"] = "TerrainSystem/heightmap not available" }.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Task 2 (real terrain-tool stroke): activates the REAL vanilla Game.Tools.TerrainToolSystem with
        /// a real TerraformingPrefab, exactly the way the toolbar/hotkey does (TerrainToolSystem.SetPrefab
        /// + brushSize/brushStrength properties + ToolSystem.activeTool = terrainTool) - no synthetic
        /// entities, no reflection into private methods (unlike TestStroke). After this call, left-mouse
        /// drag in the game window raises terrain (TerrainToolSystem.UpdateActions: for a Shift/Height
        /// prefab, applyActionOverride = "Raise Terrain", secondaryApplyActionOverride = "Lower Terrain" -
        /// so a plain left-button drag raises, matching the task's "left-mouse drag" real-input test) and
        /// is charged/measured by the exact same ApplyBrushesGatePatch/ApplyBrushPatch pipeline as
        /// TestStroke and a real player stroke.
        /// mode is one of Shift, Level, Soften, Slope (TerraformingType; matches TestStroke's `type`).
        /// size/strength &lt;= 0 leave the prefab's own default (or last-used) value unchanged.
        /// Returns {"ok":true,"mode":...,"prefab":...,"brushSize":...,"brushStrength":...} or
        /// {"ok":false,"error":...}.
        /// </summary>
        public static string ActivateTerrainTool(string mode, float size, float strength)
        {
            var o = new JObject();
            try
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world == null) { o["ok"] = false; o["error"] = "world not ready"; return Str(o); }
                if (!Enum.TryParse(mode, true, out TerraformingType type))
                {
                    o["ok"] = false; o["error"] = "unknown mode '" + mode + "' (expected Shift, Level, Soften or Slope)";
                    return Str(o);
                }

                var em = world.EntityManager;
                var prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
                if (prefabSystem == null) { o["ok"] = false; o["error"] = "PrefabSystem not available"; return Str(o); }

                TerraformingPrefab found = null;
                var q = em.CreateEntityQuery(ComponentType.ReadOnly<TerraformingData>());
                using (var ents = q.ToEntityArray(Allocator.Temp))
                {
                    foreach (var e in ents)
                    {
                        var td = em.GetComponentData<TerraformingData>(e);
                        if (td.m_Target != TerraformingTarget.Height || td.m_Type != type) continue;
                        if (prefabSystem.TryGetPrefab<TerraformingPrefab>(e, out var p) && p != null) { found = p; break; }
                    }
                }
                if (found == null)
                {
                    o["ok"] = false; o["error"] = "no TerraformingPrefab found for mode=" + type + " target=Height";
                    return Str(o);
                }

                var terrainTool = world.GetOrCreateSystemManaged<TerrainToolSystem>();
                terrainTool.SetPrefab(found);
                if (size > 0f) terrainTool.brushSize = size;
                if (strength > 0f) terrainTool.brushStrength = strength;

                var toolSystem = world.GetOrCreateSystemManaged<ToolSystem>();
                toolSystem.activeTool = terrainTool;

                o["ok"] = true;
                o["mode"] = type.ToString();
                o["prefab"] = found.name;
                o["brushSize"] = terrainTool.brushSize;
                o["brushStrength"] = terrainTool.brushStrength;
            }
            catch (Exception e) { o["ok"] = false; o["error"] = e.GetType().Name + ": " + e.Message; }
            return Str(o);
        }

        private static string Str(JObject o) => o.ToString(Newtonsoft.Json.Formatting.None);

        // --- T1 debugging aids (temporary, see testharness/results_T1.md) ---
        public static string DebugFind(string type) => Diagnostics.FindEntities(type);
        public static string DebugCreateOne(float x, float z, float size, float strength, string type) => Diagnostics.CreateOne(x, z, size, strength, type);
        public static string DebugCounts() => Diagnostics.Counts();
        public static string DebugRefresh() => Diagnostics.Refresh();
    }
}
