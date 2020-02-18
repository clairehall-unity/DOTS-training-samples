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
    
    EntityQuery BeeManager;

    protected override void OnCreate()
    {
        EndInitCommandBufferSystem = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();
        
        BeeManager = GetEntityQuery(ComponentType.ReadOnly<BeeManagerData>());
    }

    protected override JobHandle OnUpdate(JobHandle inputDependencies)
    {
        var managerData = BeeManager.GetSingleton<BeeManagerData>();
        var commandBuffer = EndInitCommandBufferSystem.CreateCommandBuffer();

        Entities.ForEach((Entity entity, RenderMeshInfo meshInfo, ref SpawnBeeData spawnBeeData) =>
            {
                for (int i = 0; i < spawnBeeData.SpawnCount; i++)
                {
                    var teamIndex = i % 2; 
                    var beeEntity = commandBuffer.CreateEntity();
                    
                    var position = Vector3.right * ((-managerData.FieldSize.x * 0.4f) + managerData.FieldSize.x * 0.8f * teamIndex);
                    position.y += i;
                    
                    commandBuffer.AddComponent(beeEntity, new Translation { Value = position });
                    commandBuffer.AddComponent(beeEntity, new NonUniformScale{ Value = new float3(managerData.MinBeeSize,  managerData.MinBeeSize, managerData.MinBeeSize) });
                    commandBuffer.AddComponent(beeEntity, new Bee{ Velocity = new float3(), TeamIndex = teamIndex});
                    commandBuffer.AddSharedComponent(beeEntity, new RenderMeshWithColourInfo
                    {
                        Colour = teamIndex == 0 ? managerData.TeamAColour : managerData.TeamBColour,
                        Mesh = meshInfo.Mesh,
                        Material = meshInfo.Material
                    });
                }

                commandBuffer.RemoveComponent<SpawnBeeData>(entity);
            }).WithoutBurst().Run();
        

        return inputDependencies;
    }

}
