using System.Collections;
using System.Collections.Generic;
using System.Resources;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class ResourceManagerSystem : JobComponentSystem
{
    public struct Resource : IComponentData
    {
        public float Gravity;
    }

    public struct StackedResource : IComponentData
    {
        
    }

    //[BurstCompile]
    public struct SpawnResourcesJob : IJobForEachWithEntity<ResourceManagerData,SpawnResourceData>
    {
        public EntityCommandBuffer.Concurrent CommandBuffer;
        
        public void Execute(Entity entity, int index, [ReadOnly] ref ResourceManagerData resourceManagerData, [ReadOnly] ref SpawnResourceData spawnResourceData)
        {
            var random = new Unity.Mathematics.Random((uint)System.DateTime.Now.Millisecond);
            
            for (int x = 0; x < spawnResourceData.SpawnCount; ++x)
            {
                var resourceEntity = CommandBuffer.Instantiate(index, resourceManagerData.ResourcePrefabEntity);
                
                var position = new float3((resourceManagerData.MinGridPos.x + (random.NextFloat() * resourceManagerData.GridSize.x * resourceManagerData.GridCounts.x)) * 0.25f,
                            random.NextFloat() * 10f,resourceManagerData.MinGridPos.y + (random.NextFloat() * resourceManagerData.GridSize.y * resourceManagerData.GridCounts.y));
                
                CommandBuffer.SetComponent(index, resourceEntity, new Translation { Value = position });
                CommandBuffer.AddComponent(index, resourceEntity, new NonUniformScale{ Value = new float3(resourceManagerData.ResourceSize,  resourceManagerData.ResourceSize * 0.5f, resourceManagerData.ResourceSize) });
                CommandBuffer.AddComponent(index, resourceEntity, new Resource{ Gravity = resourceManagerData.ResourceGravity });
            }
            
            CommandBuffer.RemoveComponent<SpawnResourceData>(index, entity);
        }
    }
    
    //[BurstCompile]
    public struct ResourceMovementJob : IJobForEach<Resource, Translation>
    {
        public float DeltaTime;

        public void Execute([ReadOnly] ref Resource resource, ref Translation translation)
        {
            translation.Value.y += resource.Gravity * DeltaTime;
        }
    }

    private EntityCommandBufferSystem CommandBufferSystem;
    
    protected override void OnCreate()
    {
        CommandBufferSystem = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();
    }

    protected override JobHandle OnUpdate(JobHandle inputDependencies)
    {
        JobHandle jobHandle = new SpawnResourcesJob{ CommandBuffer = CommandBufferSystem.CreateCommandBuffer().ToConcurrent()}.Schedule(this, inputDependencies);

        inputDependencies = JobHandle.CombineDependencies(jobHandle, inputDependencies);

        jobHandle = JobHandle.CombineDependencies(jobHandle, new ResourceMovementJob{ DeltaTime = Time.DeltaTime }.Schedule(this, inputDependencies));

        return jobHandle;
    }
    
    static Vector3 NearestSnappedPos(ResourceManagerData resourceManagerData, Vector3 pos) {
        int x, y;
        GetGridIndex(resourceManagerData, pos,out x,out y);
        return new Vector3(resourceManagerData.MinGridPos.x + x * resourceManagerData.GridSize.x, pos.y,resourceManagerData.MinGridPos.y + y * resourceManagerData.GridSize.y);
    }
    static void GetGridIndex(ResourceManagerData resourceManagerData, Vector3 pos, out int gridX, out int gridY) {
        gridX=Mathf.FloorToInt((pos.x - resourceManagerData.MinGridPos.x + resourceManagerData.GridSize.x * .5f) / resourceManagerData.GridSize.x);
        gridY=Mathf.FloorToInt((pos.z - resourceManagerData.MinGridPos.y + resourceManagerData.GridSize.y * .5f) / resourceManagerData.GridSize.y);

        gridX = Mathf.Clamp(gridX,0,resourceManagerData.GridCounts.x - 1);
        gridY = Mathf.Clamp(gridY,0,resourceManagerData.GridCounts.y - 1);
    }
}
