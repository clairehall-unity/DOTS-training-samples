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
using Random = UnityEngine.Random;

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
        public bool IsOnTop;
    }

    public struct ResourceHolder : IComponentData
    {
        public Entity Holder;
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
    EntityQuery CarriedResources;
    EntityQuery ResourceManager;
    protected override void OnCreate()
    {
        EndInitCommandBufferSystem = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();
        EndSimCommandBufferSystem = World.GetExistingSystem<EndSimulationEntityCommandBufferSystem>();
        
        FallingResources = GetEntityQuery(typeof(Translation), typeof(Resource), ComponentType.Exclude<StackedResource>());
        StackedResources = GetEntityQuery(ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<Resource>(), ComponentType.ReadOnly<StackedResource>());
        CarriedResources = GetEntityQuery(ComponentType.ReadOnly<ResourceHolder>());

        ResourceManager = GetEntityQuery(ComponentType.ReadOnly<ResourceManagerData>());
    }

    protected override JobHandle OnUpdate(JobHandle inputDependencies)
    {
        var managerData = ResourceManager.GetSingleton<ResourceManagerData>();
        var commandBuffer = EndInitCommandBufferSystem.CreateCommandBuffer();
        var updateCommandBuffer = EndSimCommandBufferSystem.CreateCommandBuffer().ToConcurrent();
        var deltaTime = Time.DeltaTime;

        Entities.ForEach((Entity entity, RenderMeshInfo meshInfo, ref SpawnResourceData spawnResourceData) =>
        {
            for (int x = 0; x < spawnResourceData.SpawnCount; ++x)
            {
                var resourceEntity = commandBuffer.CreateEntity();

                var position = new float3(
                    (managerData.MinGridPos.x +
                     (Random.value * managerData.GridSize.x * managerData.GridCounts.x)) * 0.25f,
                    Random.value * 10f,
                    managerData.MinGridPos.y +
                    (Random.value * managerData.GridSize.y * managerData.GridCounts.y));

                //TODO: Look into archetypes instead of adding several components in turn
                commandBuffer.AddComponent(resourceEntity, new Translation {Value = position});
                commandBuffer.AddComponent(resourceEntity,
                    new NonUniformScale
                    {
                        Value = new float3(managerData.ResourceSize, managerData.ResourceSize * 0.5f,
                            managerData.ResourceSize)
                    });
                commandBuffer.AddComponent(resourceEntity, new Resource {Velocity = new float3(0f, 0f, 0f)});
                commandBuffer.AddSharedComponent(resourceEntity, meshInfo);
            }

            commandBuffer.RemoveComponent<SpawnResourceData>(entity);
        }).WithoutBurst().Run();
        
        var stackedResources = StackedResources.ToComponentDataArray<Resource>(Allocator.TempJob);
        var fallingResources = FallingResources.ToEntityArray(Allocator.TempJob);
        var stackCounts = new NativeArray<int>(managerData.GridCounts.x * managerData.GridCounts.y, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        
        var countJobHandle = new CountStackedResourcesJob{ StackCounts = stackCounts, StackedResources = stackedResources }.Schedule();
        
        inputDependencies = JobHandle.CombineDependencies( new ResourceUpdateGridPosJob{ ManagerData = managerData }.Schedule(FallingResources, inputDependencies), inputDependencies);

        var translations = GetComponentDataFromEntity<Translation>(true);
        var bees = GetComponentDataFromEntity<BeeManagerSystem.Bee>(true);
        var deadBees = GetComponentDataFromEntity<BeeManagerSystem.DeadBee>(true);

        var carryTranslations = new NativeHashMap<int, float3>(CarriedResources.CalculateEntityCount(), Allocator.TempJob);

        var carriedJobHandle = Entities.WithNativeDisableParallelForRestriction(carryTranslations).WithReadOnly(translations).WithReadOnly(bees).WithReadOnly(deadBees).ForEach((Entity entity, int entityInQueryIndex, ref Resource resource, in Translation translation, in ResourceHolder holder) =>
            {
                if (deadBees.Exists(holder.Holder))
                {
                    updateCommandBuffer.RemoveComponent<ResourceHolder>(entityInQueryIndex, entity);
                    carryTranslations[entity.Index] = translation.Value;
                }
                else
                {
                    var beePos = (Vector3)translations[holder.Holder].Value;
                    var bee = bees[holder.Holder];
                    var targetPos = beePos - Vector3.up * (managerData.ResourceSize + bee.Size)*.5f;
                    carryTranslations[entity.Index] = Vector3.Lerp(translation.Value,targetPos,managerData.CarryStiffness * deltaTime);
                    resource.Velocity = bee.Velocity;
                }
            }).Schedule(inputDependencies);

        EndSimCommandBufferSystem.AddJobHandleForProducer(carriedJobHandle);
        inputDependencies = JobHandle.CombineDependencies(carriedJobHandle, inputDependencies);

        var updateCarriedJobHandle = Entities.WithReadOnly(carryTranslations).ForEach((Entity entity, ref Translation translation, in ResourceHolder holder) =>
            {
                if(carryTranslations.ContainsKey(entity.Index)) translation.Value = carryTranslations[entity.Index];
            }).Schedule(inputDependencies);
        
        inputDependencies = JobHandle.CombineDependencies(updateCarriedJobHandle, inputDependencies);

        var moveJobHandle = new ResourceMovementJob
        {
            ManagerData = managerData,
            DeltaTime = deltaTime,
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

        var stackTopJobHandle = Entities.WithReadOnly(stackCounts).ForEach((ref StackedResource stacked, in Resource resource) =>
            {
                stacked.IsOnTop = ((stackCounts[resource.GridIndex] - 1) == stacked.StackIndex);
            }).Schedule(inputDependencies);
        
        inputDependencies = JobHandle.CombineDependencies(stackTopJobHandle, inputDependencies);

        var disposeJobHandle = JobHandle.CombineDependencies(carryTranslations.Dispose(inputDependencies), stackCounts.Dispose(inputDependencies));
        return JobHandle.CombineDependencies(disposeJobHandle, stackedResources.Dispose(inputDependencies), fallingResources.Dispose(inputDependencies));
    }
    
    static void GetGridIndex(Vector2 minGridPos, Vector2 gridSize, Vector2Int gridCounts, Vector3 pos, out int gridX, out int gridY) {
        gridX=Mathf.FloorToInt((pos.x - minGridPos.x + gridSize.x * .5f) / gridSize.x);
        gridY=Mathf.FloorToInt((pos.z - minGridPos.y + gridSize.y * .5f) / gridSize.y);

        gridX = Mathf.Clamp(gridX,0,gridCounts.x - 1);
        gridY = Mathf.Clamp(gridY,0,gridCounts.y - 1);
    }
    
}
