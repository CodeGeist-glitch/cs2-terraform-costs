using System;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.Prefabs;
using Game.Tools;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace TerraformCost.Systems
{
    /// <summary>
    /// Drives a brush through the REAL charging path for TestApi.TestStroke.
    ///
    /// T1 debugging note (see testharness/results_T1.md): the first implementation replicated
    /// TerrainToolSystem.UpdateDefinitions exactly - creating one CreationDefinition+BrushDefinition+
    /// Updated entity per tick for GenerateBrushesSystem/ApplyBrushesSystem to pick up on their own
    /// schedule. That never produced a single TerrainSystem.ApplyBrush call even after many ticks
    /// (confirmed with TestApi.DebugCreateOne/DebugCounts/DebugRefresh: the entity kept its
    /// CreationDefinition+BrushDefinition, Updated got cleared by the engine's own per-frame
    /// bookkeeping, but ApplyBrushPatch.CallCount never moved) - some other undocumented gate in that
    /// ECS scheduling chain (GenerateBrushesSystem's Burst job / its EntityCommandBuffer barrier
    /// ordering) never fired for entities created this way from outside the tool's own system.
    ///
    /// Instead, this calls Game.Tools.ApplyBrushesSystem.ApplyHeight(Brush, Entity, TerraformingType)
    /// directly via reflection, once per tick. Harmony patches the METHOD ITSELF, so
    /// ApplyBrushesGatePatch (which wraps exactly this private method) fires exactly as it would for a
    /// real player stroke, sets PipelineGate.InPipeline, and the real call chain
    /// ApplyHeight -> TerrainSystem.ApplyBrush -> ApplyBrushPatch runs unmodified - only the synthetic
    /// ECS entity plumbing upstream of ApplyHeight (CreationDefinition/GenerateBrushesSystem) is
    /// bypassed. This still exercises 100% of TerraformCost's own gate+charge code with the exact
    /// vanilla methods it patches, which is what T1 needs to verify.
    /// </summary>
    public class TestStrokeSystem : GameSystemBase
    {
        // T-3 (0.9.1): StrokeVolume only charges for a frame's real height change on the NEXT touch (or,
        // once touches stop, on its own settle-timeout finalisation - StrokeVolume.IdleFramesForSettle
        // real frames after the last ApplyOnce() here, ticked from TerraformChargeSystem at
        // SystemUpdatePhase.PreTool, which runs before this system's SystemUpdatePhase.ToolUpdate each
        // frame). So the very last ApplyOnce()'s real height change is only charged once StrokeVolume's
        // own settle finaliser has run - SettleTicks here must be strictly more than
        // StrokeVolume.IdleFramesForSettle so Finish() never reads money_after before that happens.
        private const int SettleTicks = 6;

        private enum State { Idle, Running, Settling }

        private State m_State = State.Idle;
        private int m_FramesRemaining;
        private int m_SettleRemaining;

        private Entity m_BrushShapeEntity = Entity.Null;
        private Entity m_TerraformEntity = Entity.Null;
        private float3 m_Position;
        private float m_Size;
        private float m_Strength;
        private TerraformingType m_Type;

        private int m_MoneyBefore;
        private long m_CallsBefore;
        private double m_SumAbsStrengthDt;

        private readonly Dictionary<TerraformingType, Entity> m_TerraformEntityCache = new Dictionary<TerraformingType, Entity>();
        private Entity? m_CachedBrushShape;

        private ApplyBrushesSystem m_ApplyBrushesSystem;
        private MethodInfo m_ApplyHeightMethod;

        public static string LastResultJson { get; private set; } = "{\"error\":\"no test run yet\"}";

        public bool IsRunning => m_State != State.Idle;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ApplyBrushesSystem = World.GetOrCreateSystemManaged<ApplyBrushesSystem>();
            m_ApplyHeightMethod = AccessTools.Method(typeof(ApplyBrushesSystem), "ApplyHeight", new[] { typeof(Brush), typeof(Entity), typeof(TerraformingType) });
            if (m_ApplyHeightMethod == null)
                Mod.log.Error("TestStrokeSystem: ApplyBrushesSystem.ApplyHeight not found by reflection - TestStroke will not work");
        }

        /// <summary>Starts a new stroke test; returns an error string, or null on success. Call from the main thread only (TestApi is invoked from HarnessBehaviour's Update, so this is always true).</summary>
        public string TryStart(float x, float z, float size, float strength, int frames, string typeName)
        {
            if (IsRunning) return "a TestStroke is already running; poll LastTestResult() first";
            if (frames <= 0) return "frames must be > 0";
            if (size <= 0f) return "size must be > 0";
            if (m_ApplyHeightMethod == null) return "ApplyBrushesSystem.ApplyHeight not found (game update changed its signature?)";

            if (!Enum.TryParse(typeName, true, out TerraformingType type))
                return "unknown type '" + typeName + "' (expected Shift, Level, Soften or Slope)";

            if (!TryFindBrushShapeEntity(out Entity brushShape))
                return "no BrushPrefab (with BrushData) found in the loaded prefab set";
            if (!TryFindTerraformEntity(type, out Entity terraformEntity))
                return "no TerraformingPrefab found for type=" + type + " target=Height";

            m_BrushShapeEntity = brushShape;
            m_TerraformEntity = terraformEntity;
            m_Position = new float3(x, 0f, z);
            if (TerrainHeight.TrySample(x, z, false, out float h)) m_Position.y = h;
            m_Size = size;
            m_Strength = strength;
            m_Type = type;
            m_FramesRemaining = frames;
            m_SumAbsStrengthDt = 0.0;
            m_CallsBefore = Patches.ApplyBrushPatch.CallCount;
            GameMoney.TryRead(out m_MoneyBefore);

            m_State = State.Running;
            LastResultJson = "{\"running\":true}";
            return null;
        }

        protected override void OnUpdate()
        {
            if (m_State == State.Idle) return;
            try
            {
                if (m_State == State.Running)
                {
                    if (m_FramesRemaining > 0)
                    {
                        ApplyOnce();
                        m_FramesRemaining--;
                    }
                    else
                    {
                        m_State = State.Settling;
                        m_SettleRemaining = SettleTicks;
                    }
                }
                else // Settling
                {
                    m_SettleRemaining--;
                    if (m_SettleRemaining <= 0) Finish();
                }
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "TestStrokeSystem.OnUpdate failed");
                LastResultJson = new JObject { ["ok"] = false, ["error"] = e.GetType().Name + ": " + e.Message }.ToString(Newtonsoft.Json.Formatting.None);
                m_State = State.Idle;
            }
        }

        private void ApplyOnce()
        {
            float dt = UnityEngine.Time.unscaledDeltaTime;
            m_SumAbsStrengthDt += Mathf.Abs(m_Strength) * dt;

            Brush brush = new Brush
            {
                m_Tool = m_TerraformEntity,
                m_Position = m_Position,
                m_Target = m_Position,
                m_Start = m_Position,
                m_Angle = 0f,
                m_Size = m_Size,
                m_Strength = m_Strength,
                m_Opacity = 1f,
            };
            // Private instance method: ApplyBrushesSystem.ApplyHeight(Brush, Entity prefab, TerraformingType).
            // Harmony patches the method itself, so ApplyBrushesGatePatch fires exactly as for a real stroke.
            m_ApplyHeightMethod.Invoke(m_ApplyBrushesSystem, new object[] { brush, m_BrushShapeEntity, m_Type });
        }

        private void Finish()
        {
            long callsAfter = Patches.ApplyBrushPatch.CallCount;
            GameMoney.TryRead(out int moneyAfter);
            var o = new JObject
            {
                ["ok"] = true,
                ["money_before"] = m_MoneyBefore,
                ["money_after"] = moneyAfter,
                ["charged"] = m_MoneyBefore - moneyAfter,
                ["apply_brush_calls"] = callsAfter - m_CallsBefore,
                ["sum_abs_strength_dt"] = m_SumAbsStrengthDt,
                ["type"] = m_Type.ToString(),
                ["size"] = m_Size,
                ["strength"] = m_Strength,
                ["position"] = new JArray(m_Position.x, m_Position.y, m_Position.z),
            };
            LastResultJson = o.ToString(Newtonsoft.Json.Formatting.None);
            m_State = State.Idle;
        }

        private bool TryFindBrushShapeEntity(out Entity result)
        {
            if (m_CachedBrushShape.HasValue) { result = m_CachedBrushShape.Value; return true; }
            result = Entity.Null;
            EntityQuery query = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<BrushData>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                int bestPriority = int.MaxValue;
                for (int i = 0; i < entities.Length; i++)
                {
                    BrushData bd = EntityManager.GetComponentData<BrushData>(entities[i]);
                    if (bd.m_Priority < bestPriority) { bestPriority = bd.m_Priority; result = entities[i]; }
                }
            }
            finally { entities.Dispose(); }
            if (result != Entity.Null) m_CachedBrushShape = result;
            return result != Entity.Null;
        }

        private bool TryFindTerraformEntity(TerraformingType type, out Entity result)
        {
            if (m_TerraformEntityCache.TryGetValue(type, out result)) return result != Entity.Null;
            result = Entity.Null;
            EntityQuery query = GetEntityQuery(ComponentType.ReadOnly<TerraformingData>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    TerraformingData td = EntityManager.GetComponentData<TerraformingData>(entities[i]);
                    if (td.m_Target == TerraformingTarget.Height && td.m_Type == type) { result = entities[i]; break; }
                }
            }
            finally { entities.Dispose(); }
            m_TerraformEntityCache[type] = result;
            return result != Entity.Null;
        }
    }
}
