using Game.Rendering;
using Game;
using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Collections;
using System.Text;
using System.Threading.Tasks;
using Unity.Entities;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Tools;
using Game.Vehicles;
using Unity.Collections;
using Unity.Burst.Intrinsics;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;

namespace Trejak.PropLightsPDXMods.Systems
{
    public partial class PropLightingSystem : GameSystemBase
    {

        EntityQuery propQuery;
        LightingSystem lightingSystem;
        EndFrameBarrier endFrameBarrier;

        protected override void OnCreate()
        {
            EntityQueryBuilder eqb = new EntityQueryBuilder(Allocator.Temp);
            propQuery = eqb.WithAll<StreetLight, LightState, Emissive>()
                .WithNone<Road, Building, Watercraft, Deleted, Destroyed, Temp, Owner>()
                .Build(this.EntityManager);
            lightingSystem = World.GetOrCreateSystemManaged<LightingSystem>();
            endFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            eqb.Dispose();
            base.RequireForUpdate(propQuery);
        }

        protected override void OnUpdate()
        {
            var entityTypeHandle = SystemAPI.GetEntityTypeHandle();
            var streetLightTypeHandle = SystemAPI.GetComponentTypeHandle<StreetLight>();
            var pseudoRandomSeedHandle = SystemAPI.GetComponentTypeHandle<PseudoRandomSeed>();
            UpdateLightsJob job = new UpdateLightsJob()
            {
                entityTypeHandle = entityTypeHandle,
                streetLightTypeHandle = streetLightTypeHandle,
                pseudoRandomHandle = pseudoRandomSeedHandle,
                ecb = endFrameBarrier.CreateCommandBuffer().AsParallelWriter(),
                brightness = Mathf.RoundToInt(this.lightingSystem.dayLightBrightness * 1000f)
            };
            JobHandle jobHandle = job.ScheduleParallel(propQuery, this.Dependency);
            this.endFrameBarrier.AddJobHandleForProducer(jobHandle);
            this.Dependency = jobHandle;
        }

        [BurstCompile]
        private struct UpdateLightsJob : IJobChunk
        {
            public EntityTypeHandle entityTypeHandle;
            public ComponentTypeHandle<StreetLight> streetLightTypeHandle;
            public ComponentTypeHandle<PseudoRandomSeed> pseudoRandomHandle;
            public int brightness;
            public EntityCommandBuffer.ParallelWriter ecb;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(this.entityTypeHandle);
                NativeArray<StreetLight> streetLights = chunk.GetNativeArray(ref this.streetLightTypeHandle);
                NativeArray<PseudoRandomSeed> pseudoRandomSeeds = chunk.GetNativeArray(ref this.pseudoRandomHandle);
                for (int i = 0; i < entities.Length; i++)
                {
                    Unity.Mathematics.Random random = pseudoRandomSeeds[i].GetRandom((uint)PseudoRandomSeed.kBrightnessLimit);
                    ref var streetLight = ref streetLights.ElementAt(i);
                    bool isDark = this.brightness < random.NextInt(200, 300);
                    if (isDark && streetLight.m_State == StreetLightState.TurnedOff)
                    {
                        streetLight.m_State = StreetLightState.None;
                        this.ecb.AddComponent<EffectsUpdated>(unfilteredChunkIndex, entities[i]);
                    }
                    else if (!isDark && streetLight.m_State == StreetLightState.None)
                    {
                        streetLight.m_State = StreetLightState.TurnedOff;
                        this.ecb.AddComponent<EffectsUpdated>(unfilteredChunkIndex, entities[i]);
                    }
                }
            }
        }
    }
}
