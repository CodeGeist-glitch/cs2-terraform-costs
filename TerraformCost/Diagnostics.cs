using System;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TerraformCost
{
    /// <summary>Temporary T1 debugging aid (not part of the shipped feature set) - see testharness/results_T1.md for why this was needed.</summary>
    public static class Diagnostics
    {
        public static Entity LastDefEntity = Entity.Null;

        public static string FindEntities(string typeName)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            EntityManager em = world.EntityManager;

            if (!Enum.TryParse(typeName, true, out TerraformingType type))
                return new JObject { ["ok"] = false, ["error"] = "bad type" }.ToString(Newtonsoft.Json.Formatting.None);

            Entity brushShape = Entity.Null;
            int bestPriority = int.MaxValue;
            int brushShapeCount = 0;
            {
                EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<BrushData>());
                NativeArray<Entity> arr = q.ToEntityArray(Allocator.Temp);
                brushShapeCount = arr.Length;
                for (int i = 0; i < arr.Length; i++)
                {
                    BrushData bd = em.GetComponentData<BrushData>(arr[i]);
                    if (bd.m_Priority < bestPriority) { bestPriority = bd.m_Priority; brushShape = arr[i]; }
                }
                arr.Dispose();
            }

            Entity terraform = Entity.Null;
            int terraformMatchCount = 0;
            int terraformTotalCount = 0;
            {
                EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<TerraformingData>());
                NativeArray<Entity> arr = q.ToEntityArray(Allocator.Temp);
                terraformTotalCount = arr.Length;
                for (int i = 0; i < arr.Length; i++)
                {
                    TerraformingData td = em.GetComponentData<TerraformingData>(arr[i]);
                    if (td.m_Target == TerraformingTarget.Height && td.m_Type == type) { terraform = arr[i]; terraformMatchCount++; }
                }
                arr.Dispose();
            }

            var o = new JObject
            {
                ["brushShapeCount"] = brushShapeCount,
                ["brushShapeEntity"] = brushShape == Entity.Null ? null : brushShape.Index + ":" + brushShape.Version,
                ["brushShapeHasBrushData"] = brushShape != Entity.Null && em.HasComponent<BrushData>(brushShape),
                ["brushShapePriority"] = bestPriority,
                ["terraformTotalCount"] = terraformTotalCount,
                ["terraformMatchCount"] = terraformMatchCount,
                ["terraformEntity"] = terraform == Entity.Null ? null : terraform.Index + ":" + terraform.Version,
            };
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string CreateOne(float x, float z, float size, float strength, string typeName)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            EntityManager em = world.EntityManager;
            if (!Enum.TryParse(typeName, true, out TerraformingType type))
                return new JObject { ["ok"] = false, ["error"] = "bad type" }.ToString(Newtonsoft.Json.Formatting.None);

            Entity brushShape = Entity.Null; int bestPriority = int.MaxValue;
            {
                EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<BrushData>());
                NativeArray<Entity> arr = q.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < arr.Length; i++)
                {
                    BrushData bd = em.GetComponentData<BrushData>(arr[i]);
                    if (bd.m_Priority < bestPriority) { bestPriority = bd.m_Priority; brushShape = arr[i]; }
                }
                arr.Dispose();
            }
            Entity terraform = Entity.Null;
            {
                EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<TerraformingData>());
                NativeArray<Entity> arr = q.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < arr.Length; i++)
                {
                    TerraformingData td = em.GetComponentData<TerraformingData>(arr[i]);
                    if (td.m_Target == TerraformingTarget.Height && td.m_Type == type) { terraform = arr[i]; break; }
                }
                arr.Dispose();
            }
            if (brushShape == Entity.Null || terraform == Entity.Null)
                return new JObject { ["ok"] = false, ["error"] = "lookup failed", ["brushShape"] = brushShape.Index, ["terraform"] = terraform.Index }.ToString(Newtonsoft.Json.Formatting.None);

            float3 pos = new float3(x, 0f, z);
            TerrainHeight.TrySample(x, z, false, out pos.y);

            if (LastDefEntity != Entity.Null && em.Exists(LastDefEntity)) em.DestroyEntity(LastDefEntity);

            Entity e = em.CreateEntity();
            em.AddComponentData(e, new CreationDefinition { m_Prefab = brushShape });
            em.AddComponentData(e, new BrushDefinition
            {
                m_Tool = terraform,
                m_Line = new Line3.Segment(pos, pos),
                m_Size = size,
                m_Angle = 0f,
                m_Strength = strength,
                m_Time = UnityEngine.Time.deltaTime,
                m_Target = pos,
                m_Start = pos,
            });
            em.AddComponent<Updated>(e);
            LastDefEntity = e;

            return new JObject
            {
                ["ok"] = true,
                ["entity"] = e.Index + ":" + e.Version,
                ["brushShape"] = brushShape.Index + ":" + brushShape.Version,
                ["terraform"] = terraform.Index + ":" + terraform.Version,
                ["hasCreationDefinition"] = em.HasComponent<CreationDefinition>(e),
                ["hasBrushDefinition"] = em.HasComponent<BrushDefinition>(e),
                ["hasUpdated"] = em.HasComponent<Updated>(e),
                ["archetypeComponentCount"] = em.GetChunk(e).Archetype.TypesCount,
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>Re-adds Updated to LastDefEntity (removing it first if present) to test whether a fresh Updated tag on an otherwise-unchanged entity gets picked up.</summary>
        public static string Refresh()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            EntityManager em = world.EntityManager;
            if (LastDefEntity == Entity.Null || !em.Exists(LastDefEntity))
                return new JObject { ["ok"] = false, ["error"] = "no LastDefEntity" }.ToString(Newtonsoft.Json.Formatting.None);
            if (em.HasComponent<Updated>(LastDefEntity)) em.RemoveComponent<Updated>(LastDefEntity);
            em.AddComponent<Updated>(LastDefEntity);
            return new JObject
            {
                ["ok"] = true,
                ["hasUpdated"] = em.HasComponent<Updated>(LastDefEntity),
                ["applyBrushCallsAtRefresh"] = Patches.ApplyBrushPatch.CallCount,
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string Counts()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            EntityManager em = world.EntityManager;
            int defCount = em.CreateEntityQuery(ComponentType.ReadOnly<CreationDefinition>()).CalculateEntityCount();
            int defUpdatedCount = em.CreateEntityQuery(ComponentType.ReadOnly<CreationDefinition>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<BrushDefinition>()).CalculateEntityCount();
            int brushCount = em.CreateEntityQuery(ComponentType.ReadOnly<Brush>()).CalculateEntityCount();
            int brushTempCount = em.CreateEntityQuery(ComponentType.ReadOnly<Brush>(), ComponentType.ReadOnly<Temp>()).CalculateEntityCount();
            bool lastDefExists = LastDefEntity != Entity.Null && em.Exists(LastDefEntity);
            bool lastDefHasCreationDefinition = lastDefExists && em.HasComponent<CreationDefinition>(LastDefEntity);
            bool lastDefHasBrushDefinition = lastDefExists && em.HasComponent<BrushDefinition>(LastDefEntity);
            bool lastDefHasUpdated = lastDefExists && em.HasComponent<Updated>(LastDefEntity);
            return new JObject
            {
                ["defCount"] = defCount,
                ["defUpdatedCount"] = defUpdatedCount,
                ["brushCount"] = brushCount,
                ["brushTempCount"] = brushTempCount,
                ["lastDefExists"] = lastDefExists,
                ["lastDefHasCreationDefinition"] = lastDefHasCreationDefinition,
                ["lastDefHasBrushDefinition"] = lastDefHasBrushDefinition,
                ["lastDefHasUpdated"] = lastDefHasUpdated,
                ["applyBrushCalls"] = Patches.ApplyBrushPatch.CallCount,
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
