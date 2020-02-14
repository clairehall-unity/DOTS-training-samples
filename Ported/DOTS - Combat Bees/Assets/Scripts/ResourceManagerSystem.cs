using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Resources;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using UnityEngine;
using ReadOnly = Unity.Collections.ReadOnlyAttribute;

public class ResourceManagerSystem : JobComponentSystem
{
    public struct Resource : IComponentData
    {
        public float3 Velocity;
        public float3 GridPosition;
        public int GridIndex;
        
        //TODO: This should be shared data
        public float Size;
        public float Gravity;
        public float SnapStiffness;
        public Vector2 MinGridPos;
        public Vector2 GridSize;
        public Vector2Int GridCounts;
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
                
                //TODO: Look into archetypes instead of adding several components in turn
                CommandBuffer.SetComponent(index, resourceEntity, new Translation { Value = position });
                CommandBuffer.AddComponent(index, resourceEntity, new NonUniformScale{ Value = new float3(resourceManagerData.ResourceSize,  resourceManagerData.ResourceSize * 0.5f, resourceManagerData.ResourceSize) });
                CommandBuffer.AddComponent(index, resourceEntity, new Resource
                {
                    Size = resourceManagerData.ResourceSize,
                    Gravity = resourceManagerData.ResourceGravity,
                    SnapStiffness = resourceManagerData.SnapStiffness,
                    Velocity = new float3(0f, 0f, 0f ),
                    MinGridPos = resourceManagerData.MinGridPos,
                    GridSize = resourceManagerData.GridSize,
                    GridCounts = resourceManagerData.GridCounts
                });
            }
            
            CommandBuffer.RemoveComponent<SpawnResourceData>(index, entity);
        }
    }
    
    //[BurstCompile]
    public struct ResourceUpdateGridPosJob : IJobForEach<Resource, Translation>
    {
        public void Execute(ref Resource resource, [ReadOnly] ref Translation translation)
        {
            var position = translation.Value;
            
            int x, y;
            GetGridIndex(resource.MinGridPos, resource.GridSize, resource.GridCounts, position, out x, out y);

            resource.GridPosition = new float3(resource.MinGridPos.x + x * resource.GridSize.x, position.y, resource.MinGridPos.y + y * resource.GridSize.y);
            resource.GridIndex = (y * resource.GridCounts.x) + x;
        }
    }
    
    //[BurstCompile]
    public struct CountStackedResourcesJob : IJob
    {
        [ReadOnly] public NativeArray<Resource> StackedResources;
        public NativeArray<int> StackCounts;
        public void Execute()
        {
            for (int i = 0; i < StackedResources.Length; i++)
            {
                StackCounts[StackedResources[i].GridIndex]++;   
            }
        }
    }
    
    //[BurstCompile]
    public struct ResourceMovementJob : IJobForEachWithEntity<Resource, Translation>
    {
        public float DeltaTime;

        public void Execute(Entity entity, int index, ref Resource resource, ref Translation translation)
        {
            translation.Value = Vector3.Lerp(translation.Value, resource.GridPosition, resource.SnapStiffness * DeltaTime);

            resource.Velocity.y += resource.Gravity * DeltaTime;
            translation.Value += resource.Velocity * DeltaTime;
        }
    }
    
    //[BurstCompile]
    public struct ResourceStackJob : IJobForEachWithEntity<Resource, Translation>
    {
        public EntityCommandBuffer.Concurrent CommandBuffer;
        [ReadOnly] public NativeArray<int> StackCounts;

        public void Execute(Entity entity, int index, ref Resource resource, ref Translation translation)
        {
            var stackHeight = -10 + (StackCounts[resource.GridIndex] * resource.Size) + (resource.Size * 0.5f); //TODO: Remove magic number

            if (translation.Value.y < stackHeight) 
            {
                translation.Value.y = stackHeight;

                CommandBuffer.AddComponent(index, entity, new StackedResource());
            }
        }
    }

    EntityCommandBufferSystem EndInitCommandBufferSystem;
    EntityCommandBufferSystem EndSimCommandBufferSystem;

    EntityQuery FallingResources;
    EntityQuery StackedResources;
    EntityQuery SpawnResources;

    protected override void OnCreate()
    {
        EndInitCommandBufferSystem = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();
        EndSimCommandBufferSystem = World.GetExistingSystem<EndSimulationEntityCommandBufferSystem>();
        
        FallingResources = GetEntityQuery(typeof(Translation), typeof(Resource), ComponentType.Exclude<StackedResource>());
        StackedResources = GetEntityQuery(ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<Resource>(), ComponentType.ReadOnly<StackedResource>());
        SpawnResources = GetEntityQuery(ComponentType.ReadOnly<ResourceManagerData>(), ComponentType.ReadOnly<SpawnResourceData>());
    }

    protected override JobHandle OnUpdate(JobHandle inputDependencies)
    {
        var stackedResources = StackedResources.ToComponentDataArray<Resource>(Allocator.TempJob);
        var stackCounts = new NativeArray<int>(10000, Allocator.TempJob, NativeArrayOptions.ClearMemory); //TODO: calculate number from resourcemanager data
        
        var spawnJobHandle = new SpawnResourcesJob{ CommandBuffer = EndInitCommandBufferSystem.CreateCommandBuffer().ToConcurrent()}.Schedule(SpawnResources, inputDependencies);
        EndInitCommandBufferSystem.AddJobHandleForProducer(spawnJobHandle);
        
        var countJobHandle = new CountStackedResourcesJob{ StackCounts = stackCounts, StackedResources = stackedResources }.Schedule();

        inputDependencies = JobHandle.CombineDependencies(spawnJobHandle, inputDependencies);
        inputDependencies = JobHandle.CombineDependencies( new ResourceUpdateGridPosJob().Schedule(FallingResources, inputDependencies), inputDependencies);
        
        var moveJobHandle = new ResourceMovementJob
        {
            DeltaTime = Time.DeltaTime,
        }.Schedule(FallingResources, inputDependencies);

        inputDependencies = JobHandle.CombineDependencies(moveJobHandle, countJobHandle, inputDependencies);
        
        var stackJobHandle = new ResourceStackJob
        {
            CommandBuffer = EndSimCommandBufferSystem.CreateCommandBuffer().ToConcurrent(),
            StackCounts = stackCounts
        }.Schedule(FallingResources, inputDependencies);
        
        EndSimCommandBufferSystem.AddJobHandleForProducer(stackJobHandle);
        inputDependencies = JobHandle.CombineDependencies(stackJobHandle, inputDependencies);
        
        return JobHandle.CombineDependencies(stackCounts.Dispose(inputDependencies), stackedResources.Dispose(inputDependencies));
    }
    
    static void GetGridIndex(Vector2 minGridPos, Vector2 gridSize, Vector2Int gridCounts, Vector3 pos, out int gridX, out int gridY) {
        gridX=Mathf.FloorToInt((pos.x - minGridPos.x + gridSize.x * .5f) / gridSize.x);
        gridY=Mathf.FloorToInt((pos.z - minGridPos.y + gridSize.y * .5f) / gridSize.y);

        gridX = Mathf.Clamp(gridX,0,gridCounts.x - 1);
        gridY = Mathf.Clamp(gridY,0,gridCounts.y - 1);
    }
}
