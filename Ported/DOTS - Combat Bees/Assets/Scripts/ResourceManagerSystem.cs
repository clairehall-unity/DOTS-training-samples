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

public class ResourceManagerSystem : JobComponentSystem
{
    public struct Resource : IComponentData
    {
        public float3 Velocity;
        public float3 GridPosition;
        public int GridIndex;
    }

    public struct StackedResource : IComponentData
    {
        public int StackIndex;
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
                CommandBuffer.AddComponent(index, resourceEntity, new Resource { Velocity = new float3(0f, 0f, 0f ) });
            }
            
            CommandBuffer.RemoveComponent<SpawnResourceData>(index, entity);
        }
    }
    
    [BurstCompile]
    public struct ResourceUpdateGridPosJob : IJobForEach<Resource, Translation>
    {
        [ReadOnly] public ResourceManagerData ManagerData;
        public void Execute(ref Resource resource, [ReadOnly] ref Translation translation)
        {
            var position = translation.Value;
            
            int x, y;
            GetGridIndex(ManagerData.MinGridPos, ManagerData.GridSize, ManagerData.GridCounts, position, out x, out y);

            resource.GridPosition = new float3(ManagerData.MinGridPos.x + x * ManagerData.GridSize.x, position.y, ManagerData.MinGridPos.y + y * ManagerData.GridSize.y);
            resource.GridIndex = (y * ManagerData.GridCounts.x) + x;
        }
    }
    
    [BurstCompile]
    public struct CountStackedResourcesJob : IJob
    {
        [ReadOnly] public NativeArray<Resource> StackedResources;
        public NativeArray<int> StackCounts;
        public void Execute()
        {
            //TODO: Look into chunked or parallel iteration
            
            for (int i = 0; i < StackedResources.Length; i++)
            {
                StackCounts[StackedResources[i].GridIndex]++;   
            }
        }
    }
    
    [BurstCompile]
    public struct ResourceMovementJob : IJobForEachWithEntity<Resource, Translation>
    {
        [ReadOnly] public ResourceManagerData ManagerData;
        public float DeltaTime;

        public void Execute(Entity entity, int index, ref Resource resource, ref Translation translation)
        {
            translation.Value = Vector3.Lerp(translation.Value, resource.GridPosition, ManagerData.SnapStiffness * DeltaTime);

            resource.Velocity.y += ManagerData.ResourceGravity * DeltaTime;
            translation.Value += resource.Velocity * DeltaTime;
        }
    }
    
    [BurstCompile]
    public struct ResourceStackJob : IJob
    {
        public EntityCommandBuffer.Concurrent CommandBuffer;
        [ReadOnly] public ResourceManagerData ManagerData;
        
        public NativeArray<int> StackCounts;
        [ReadOnly] public NativeArray<Entity> ResourceEntities;
        
        [ReadOnly] public ComponentDataFromEntity<Resource> ResourceData;
        public ComponentDataFromEntity<Translation> TranslationData;

        public void Execute()
        {
            //TODO: Look into chunked or parallel iteration, also ensure we iterate over entities in y position order bottom to top (to ensure correct stacking order)
            
            for (int i = 0; i < ResourceEntities.Length; i++)
            {
                var entity = ResourceEntities[i];
                var resource = ResourceData[entity];
                var translation = TranslationData[entity];

                var stackCount = StackCounts[resource.GridIndex];
                var stackHeight = ManagerData.FloorHeight + (stackCount * ManagerData.ResourceSize) + (ManagerData.ResourceSize * 0.5f);

                if (translation.Value.y < stackHeight)
                {
                    StackCounts[resource.GridIndex]++;
                    translation.Value.y = stackHeight;

                    CommandBuffer.SetComponent(entity.Index, entity, translation);
                    CommandBuffer.AddComponent(entity.Index, entity, new StackedResource { StackIndex = stackCount});
                }   
            }
        }
    }

    EntityCommandBufferSystem EndInitCommandBufferSystem;
    EntityCommandBufferSystem EndSimCommandBufferSystem;

    EntityQuery FallingResources;
    EntityQuery StackedResources;
    EntityQuery SpawnResources;
    EntityQuery ResourceManager;

    protected override void OnCreate()
    {
        EndInitCommandBufferSystem = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();
        EndSimCommandBufferSystem = World.GetExistingSystem<EndSimulationEntityCommandBufferSystem>();
        
        FallingResources = GetEntityQuery(typeof(Translation), typeof(Resource), ComponentType.Exclude<StackedResource>());
        StackedResources = GetEntityQuery(ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<Resource>(), ComponentType.ReadOnly<StackedResource>());
        SpawnResources = GetEntityQuery(ComponentType.ReadOnly<ResourceManagerData>(), ComponentType.ReadOnly<SpawnResourceData>());

        ResourceManager = GetEntityQuery(ComponentType.ReadOnly<ResourceManagerData>());
    }

    protected override JobHandle OnUpdate(JobHandle inputDependencies)
    {
        var managerData = ResourceManager.GetSingleton<ResourceManagerData>();
        
        var stackedResources = StackedResources.ToComponentDataArray<Resource>(Allocator.TempJob);
        var fallingResources = FallingResources.ToEntityArray(Allocator.TempJob);
        var stackCounts = new NativeArray<int>(managerData.GridCounts.x * managerData.GridCounts.y, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        
        var spawnJobHandle = new SpawnResourcesJob{ CommandBuffer = EndInitCommandBufferSystem.CreateCommandBuffer().ToConcurrent()}.Schedule(SpawnResources, inputDependencies);
        EndInitCommandBufferSystem.AddJobHandleForProducer(spawnJobHandle);
        
        var countJobHandle = new CountStackedResourcesJob{ StackCounts = stackCounts, StackedResources = stackedResources }.Schedule();

        inputDependencies = JobHandle.CombineDependencies(spawnJobHandle, inputDependencies);
        inputDependencies = JobHandle.CombineDependencies( new ResourceUpdateGridPosJob{ ManagerData = managerData }.Schedule(FallingResources, inputDependencies), inputDependencies);
        
        var moveJobHandle = new ResourceMovementJob
        {
            ManagerData = managerData,
            DeltaTime = Time.DeltaTime,
        }.Schedule(FallingResources, inputDependencies);

        inputDependencies = JobHandle.CombineDependencies(moveJobHandle, countJobHandle, inputDependencies);
        
        var stackJobHandle = new ResourceStackJob
        {
            ManagerData = managerData,
            CommandBuffer = EndSimCommandBufferSystem.CreateCommandBuffer().ToConcurrent(),
            StackCounts = stackCounts,
            ResourceEntities = fallingResources,
            ResourceData = GetComponentDataFromEntity<Resource>(true),
            TranslationData = GetComponentDataFromEntity<Translation>()
        }.Schedule(inputDependencies);

        EndSimCommandBufferSystem.AddJobHandleForProducer(stackJobHandle);
        inputDependencies = JobHandle.CombineDependencies(stackJobHandle, inputDependencies);
        
        return JobHandle.CombineDependencies(stackCounts.Dispose(inputDependencies), stackedResources.Dispose(inputDependencies), fallingResources.Dispose(inputDependencies));
    }
    
    static void GetGridIndex(Vector2 minGridPos, Vector2 gridSize, Vector2Int gridCounts, Vector3 pos, out int gridX, out int gridY) {
        gridX=Mathf.FloorToInt((pos.x - minGridPos.x + gridSize.x * .5f) / gridSize.x);
        gridY=Mathf.FloorToInt((pos.z - minGridPos.y + gridSize.y * .5f) / gridSize.y);

        gridX = Mathf.Clamp(gridX,0,gridCounts.x - 1);
        gridY = Mathf.Clamp(gridY,0,gridCounts.y - 1);
    }
    
}
