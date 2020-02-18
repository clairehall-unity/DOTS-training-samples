using System;
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
        
        //var spawnJobHandle = new SpawnBeesJob{ CommandBuffer = EndInitCommandBufferSystem.CreateCommandBuffer().ToConcurrent()}.Schedule(SpawnBees, inputDependencies);
        //EndInitCommandBufferSystem.AddJobHandleForProducer(spawnJobHandle);

        var commandBuffer = EndInitCommandBufferSystem.CreateCommandBuffer();

        Entities.ForEach((Entity entity, RenderMeshInfo meshInfo, ref BeeManagerData beeManagerData, ref SpawnBeeData spawnBeeData) =>
            {
                for (int i = 0; i < spawnBeeData.SpawnCount; i++)
                {
                    var teamIndex = i % 2; 
                    var beeEntity = commandBuffer.CreateEntity();
                    
                    var position = Vector3.right * ((-beeManagerData.FieldSize.x * 0.4f) + beeManagerData.FieldSize.x * 0.8f * teamIndex);
                    position.y += i;
                    
                    commandBuffer.AddComponent(beeEntity, new Translation { Value = position });
                    commandBuffer.AddComponent(beeEntity, new NonUniformScale{ Value = new float3(beeManagerData.MinBeeSize,  beeManagerData.MinBeeSize, beeManagerData.MinBeeSize) });
                    commandBuffer.AddComponent(beeEntity, new Bee{ Velocity = new float3(), TeamIndex = teamIndex});
                    commandBuffer.AddSharedComponent(beeEntity, new RenderMeshWithColourInfo
                    {
                        Colour = teamIndex == 0 ? beeManagerData.TeamAColour : beeManagerData.TeamBColour,
                        Mesh = meshInfo.Mesh,
                        Material = meshInfo.Material
                    });
                }

                commandBuffer.RemoveComponent<SpawnBeeData>(entity);
            }).WithoutBurst().Run();
        

        return inputDependencies;
    }

}
