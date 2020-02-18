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

    public struct TeamInfo : ISharedComponentData, IEquatable<TeamInfo>
    {
        public Color Colour;
        public Mesh Mesh;
        public Material Material;
    
        public bool Equals(TeamInfo other)
        {
            return Mesh == other.Mesh 
                   && Material == other.Material 
                   && Colour == other.Colour;
        }
    
        public override int GetHashCode()
        {
            int hash = 0;

            if (!ReferenceEquals(Colour, null)) hash ^= Colour.GetHashCode();
            if (!ReferenceEquals(Mesh, null)) hash ^= Mesh.GetHashCode();
            if (!ReferenceEquals(Material, null)) hash ^= Material.GetHashCode();
        
            return hash;
        }
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
                var beeEntity = CommandBuffer.CreateEntity(index);

                var position = Vector3.right * ((-beeManagerData.FieldSize.x * 0.4f) + beeManagerData.FieldSize.x * 0.8f * teamIndex);
                position.y += i;
                
                //TODO: Look into archetypes instead of adding several components in turn
                CommandBuffer.AddComponent(index, beeEntity, new Translation { Value = position });
                CommandBuffer.AddComponent(index, beeEntity, new NonUniformScale{ Value = new float3(beeManagerData.MinBeeSize,  beeManagerData.MinBeeSize, beeManagerData.MinBeeSize) });
                CommandBuffer.AddComponent(index, beeEntity, new Bee{ Velocity = new float3(), TeamIndex = teamIndex});
                CommandBuffer.AddSharedComponent(index, beeEntity, new TeamInfo{Colour = teamIndex == 0 ? beeManagerData.TeamAColour : beeManagerData.TeamBColour});
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
