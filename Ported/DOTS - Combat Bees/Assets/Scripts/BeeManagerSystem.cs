using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Resources;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using UnityEngine;
using ReadOnly = Unity.Collections.ReadOnlyAttribute;

public class BeeManagerSystem : JobComponentSystem
{
    public struct Bee : IComponentData
    {
        public float3 Velocity;
        public int TeamIndex;
    }

    //[BurstCompile]
    public struct SpawnBeesJob : IJobForEachWithEntity<BeeManagerData,SpawnBeeData>
    {
        public EntityCommandBuffer.Concurrent CommandBuffer;
        
        public void Execute(Entity entity, int index, [ReadOnly] ref BeeManagerData beeManagerData, [ReadOnly] ref SpawnBeeData spawnBeeData)
        {
            var random = new Unity.Mathematics.Random((uint)System.DateTime.Now.Millisecond + 1);
            
            for (int i = 0; i < spawnBeeData.SpawnCount; i++)
            {
                var teamIndex = i % 2; 
                var resourceEntity = CommandBuffer.Instantiate(index, beeManagerData.BeePrefabEntity);

                var position = Vector3.right * ((-beeManagerData.FieldSize.x * 0.4f) + beeManagerData.FieldSize.x * 0.8f * teamIndex);
                    
                
                //TODO: Look into archetypes instead of adding several components in turn
                CommandBuffer.SetComponent(index, resourceEntity, new Translation { Value = position });
                CommandBuffer.AddComponent(index, resourceEntity, new NonUniformScale{ Value = new float3(beeManagerData.MinBeeSize,  beeManagerData.MinBeeSize, beeManagerData.MinBeeSize) });
                CommandBuffer.AddComponent(index, resourceEntity, new Bee{ Velocity = new float3(), TeamIndex = teamIndex});
            }
            
            CommandBuffer.RemoveComponent<SpawnBeeData>(index, entity);
        }
    }
    
    EntityCommandBufferSystem EndInitCommandBufferSystem;

    EntityQuery SpawnBees;
    EntityQuery BeeManager;

    protected override void OnCreate()
    {
        EndInitCommandBufferSystem = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();
        
        SpawnBees = GetEntityQuery(ComponentType.ReadOnly<BeeManagerData>(), ComponentType.ReadOnly<SpawnBeeData>());
        BeeManager = GetEntityQuery(ComponentType.ReadOnly<BeeManagerData>());
    }

    protected override JobHandle OnUpdate(JobHandle inputDependencies)
    {
        //var managerData = BeeManager.GetSingleton<BeeManagerData>();
        
        var spawnJobHandle = new SpawnBeesJob{ CommandBuffer = EndInitCommandBufferSystem.CreateCommandBuffer().ToConcurrent()}.Schedule(SpawnBees, inputDependencies);
        EndInitCommandBufferSystem.AddJobHandleForProducer(spawnJobHandle);

        return spawnJobHandle;
    }

}
