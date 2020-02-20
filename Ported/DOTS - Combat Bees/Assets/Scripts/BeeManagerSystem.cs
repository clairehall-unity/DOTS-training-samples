using System;
using System.Resources;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Assertions.Comparers;
using Random = UnityEngine.Random;
using ReadOnly = Unity.Collections.ReadOnlyAttribute;

public class BeeManagerSystem : JobComponentSystem
{
    public struct Bee : IComponentData
    {
        public Vector3 Velocity;
        public Vector3 SmoothDirection;
        public Vector3 SmoothPosition;
        public float Size;
        public int TeamIndex; //TODO: Can the duplication of data between this and the BeeTeam shared component data be resolved?
        public bool IsAttacking;
        public bool IsHolding;

        public Entity TargetBee;
        public Entity TargetResource;
    }

    public struct DeadBee : IComponentData
    {
        public float DeathTimer;
    }

    public struct BeeTeam : ISharedComponentData
    {
        public int TeamIndex;
    }
    
    EntityCommandBufferSystem EndInitCommandBufferSystem;
    EntityCommandBufferSystem EndUpdateCommandBufferSystem;
    
    EntityQuery BeeManager;
    EntityQuery BeeTeamMembers;
    EntityQuery Resources;

    BeeTeam[] BeeTeams;

    protected override void OnCreate()
    {
        EndInitCommandBufferSystem = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();
        EndUpdateCommandBufferSystem = World.GetExistingSystem<EndSimulationEntityCommandBufferSystem>();

        Resources = GetEntityQuery(ComponentType.ReadOnly<ResourceManagerSystem.Resource>());

        BeeManager = GetEntityQuery(ComponentType.ReadOnly<BeeManagerData>());
        BeeTeamMembers = GetEntityQuery(ComponentType.ReadOnly<BeeTeam>(), ComponentType.ReadOnly<Bee>(), ComponentType.Exclude<DeadBee>());

        BeeTeams = new BeeTeam[2] { new BeeTeam{ TeamIndex = 0 }, new BeeTeam{ TeamIndex = 1 } };
    }

    protected override JobHandle OnUpdate(JobHandle inputDependencies)
    {
        var managerData = BeeManager.GetSingleton<BeeManagerData>();
        var initCommandBuffer = EndInitCommandBufferSystem.CreateCommandBuffer();
        var updateCommandBuffer = EndUpdateCommandBufferSystem.CreateCommandBuffer().ToConcurrent();

        Entities.ForEach((Entity entity, RenderMeshInfo meshInfo, ref SpawnBeeData spawnBeeData) =>
            {
                for (int i = 0; i < spawnBeeData.SpawnCount; i++)
                {
                    var teamIndex = i % 2; 
                    var beeEntity = initCommandBuffer.CreateEntity();
                    
                    var position = Vector3.right * ((-managerData.FieldSize.x * 0.4f) + managerData.FieldSize.x * 0.8f * teamIndex);
      
                    var size = Mathf.Lerp(managerData.MinBeeSize, managerData.MaxBeeSize, Random.value);
                    var velocity = Random.insideUnitSphere * managerData.MaxBeeSpawnSpeed;
                    
                    initCommandBuffer.AddComponent(beeEntity, new Translation { Value = position });
                    initCommandBuffer.AddComponent(beeEntity, new NonUniformScale{ Value = new float3(size,  size, size) });
                    initCommandBuffer.AddComponent(beeEntity, new Bee
                    {
                        Velocity = velocity,
                        SmoothPosition = position + (Vector3.right * 0.1f),
                        SmoothDirection = Vector3.zero,
                        Size = size,
                        IsAttacking = false,
                        TeamIndex = teamIndex
                    });
                    initCommandBuffer.AddComponent(beeEntity, new Rotation{ Value = quaternion.identity });
                    initCommandBuffer.AddSharedComponent(beeEntity, new BeeTeam { TeamIndex =  teamIndex });
                    initCommandBuffer.AddSharedComponent(beeEntity, new RenderMeshWithColourInfo
                    {
                        Colour = teamIndex == 0 ? managerData.TeamAColour : managerData.TeamBColour,
                        Mesh = meshInfo.Mesh,
                        Material = meshInfo.Material
                    });
                }

                initCommandBuffer.RemoveComponent<SpawnBeeData>(entity);
            }).WithoutBurst().Run();

        var deltaTime = Time.DeltaTime;
        var random = new Unity.Mathematics.Random((uint) System.DateTime.Now.Millisecond + 1);

        var deadBees = GetComponentDataFromEntity<DeadBee>(true);
        var translations = GetComponentDataFromEntity<Translation>(true);
        
        var stackedResources = GetComponentDataFromEntity<ResourceManagerSystem.StackedResource>(true);
        var resourceHolders = GetComponentDataFromEntity<ResourceManagerSystem.ResourceHolder>(true);
        var resources = Resources.ToEntityArray(Allocator.TempJob);

        //TODO: Is there a native container that can access the entity arrays by index 
        
        BeeTeamMembers.SetSharedComponentFilter(BeeTeams[0]);
        
        var beeTeamA = BeeTeamMembers.ToEntityArray(Allocator.TempJob);
        
        BeeTeamMembers.SetSharedComponentFilter(BeeTeams[1]);
        
        var beeTeamB = BeeTeamMembers.ToEntityArray(Allocator.TempJob);
        
        var initVelocityJobHandle = Entities.WithNone<DeadBee>().WithReadOnly(translations).ForEach((ref Bee bee, in Translation translation) =>
        {
            bee.Velocity += (Vector3) random.NextFloat3Direction() * (managerData.BeeFlightJitter * deltaTime);
            bee.Velocity *= (1f - managerData.BeeFlightDamping);
            
            var allyBees = bee.TeamIndex == 0 ? beeTeamA : beeTeamB;

            int numAllyBees = allyBees.Length;

            if (numAllyBees > 0)
            {
                var attractiveFriend = allyBees[random.NextInt(0,numAllyBees - 1)];
                var attractivePosition = translations[attractiveFriend].Value;
                Vector3 delta = attractivePosition - translation.Value;
                
                float sqrDist = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
                
                if (sqrDist > 0f) {
                    bee.Velocity += delta * (float)((managerData.BeeTeamAttraction * deltaTime) / Math.Sqrt(sqrDist));
                }
                
                var repellentFriend = allyBees[random.NextInt(0,numAllyBees - 1)];
                var repellentPosition = translations[repellentFriend].Value;
                delta = repellentPosition - translation.Value;
                sqrDist = Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
                if (sqrDist > 0f) {
                    bee.Velocity -= delta * (float)(managerData.BeeTeamRepulsion * deltaTime / Math.Sqrt(sqrDist));
                }
            }
            
        }).Schedule(inputDependencies);
        
        inputDependencies = JobHandle.CombineDependencies(initVelocityJobHandle, inputDependencies);
        
        var deadBeeVelocityJobHandle = Entities.ForEach((ref Bee bee, in DeadBee dead) =>
        {
            bee.Velocity.y += managerData.BeeGravity * deltaTime;
        }).Schedule(initVelocityJobHandle);
        
        inputDependencies = JobHandle.CombineDependencies(deadBeeVelocityJobHandle, inputDependencies);
        
        var targetJobHandle = Entities.WithReadOnly(deadBees).WithReadOnly(translations).WithReadOnly(stackedResources).WithReadOnly(resourceHolders).ForEach((Entity entity, int entityInQueryIndex, ref Bee bee, in Translation translation) =>
        {
            bee.IsAttacking = false;
            bee.IsHolding = false;
            
            if (bee.TargetBee == Entity.Null && bee.TargetResource == Entity.Null)
            {
                if (random.NextFloat() < managerData.BeeAggression)
                {
                    int enemyTeam = (bee.TeamIndex + 1) % 2;
                    var enemyBees = enemyTeam == 0 ? beeTeamA : beeTeamB;
                    int numEnemyBees = enemyBees.Length;
                
                    if (numEnemyBees > 0)
                    {
                        bee.TargetBee = enemyBees[random.NextInt(0, numEnemyBees - 1)];
                    }   
                }
                else
                {
                    var numResources = resources.Length;

                    if (numResources > 0)
                    {
                        var resource = resources[random.NextInt(0, numResources - 1)];
                        if (stackedResources.Exists(resource) && stackedResources[resource].IsOnTop)
                        {
                            bee.TargetResource = resource;
                        }
                    }
                }
            }
            else if (bee.TargetBee != Entity.Null)
            {
                if (deadBees.Exists(bee.TargetBee))
                {
                    bee.TargetBee = Entity.Null;   
                }
                else
                {
                    var delta = translations[bee.TargetBee].Value - translation.Value;
                    var sqrDist = (delta.x * delta.x) + (delta.y * delta.y) + (delta.z * delta.z);
                    
                    if (sqrDist > managerData.BeeAttackRangeSq)
                    {
                        bee.Velocity += (Vector3) delta * ((managerData.BeeChaseForce * deltaTime) / Mathf.Sqrt(sqrDist));
                    }
                    else
                    {
                        bee.IsAttacking = true;
                        
                        if (sqrDist < managerData.BeeHitRangeSq)
                        {
                            //TODO: Death particles
                            updateCommandBuffer.AddComponent(entityInQueryIndex, bee.TargetBee, new DeadBee());
                            bee.TargetBee = Entity.Null;
                        }
                        else
                        {
                            bee.Velocity += (Vector3) delta * ((managerData.BeeAttackForce * deltaTime) / Mathf.Sqrt(sqrDist));
                        }
                    }
                }
            }
            else if (bee.TargetResource != Entity.Null)
            {
                var resource = bee.TargetResource;

                if (resourceHolders.Exists(resource))
                {
                    var holder = resourceHolders[resource].Holder;
                    if (holder != entity)
                    {
                        var allyBees = bee.TeamIndex == 0 ? beeTeamA : beeTeamB;

                        if (allyBees.Contains(holder))
                        {
                            bee.TargetResource = Entity.Null;
                        }
                        else
                        {
                            bee.TargetBee = holder;
                        }
                    }
                    else
                    {
                        bee.IsHolding = true;
                        
                        //TODO: Carry to team location
                    }
                } else if (!stackedResources.Exists(resource) || !stackedResources[resource].IsOnTop)
                {
                    bee.TargetResource = Entity.Null;
                }
                else
                {
                    var delta = translations[resource].Value - translation.Value;
                    float sqrDist = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;
                    if (sqrDist > managerData.BeeGrabRangeSq) {
                        bee.Velocity += (Vector3) delta * (managerData.BeeChaseForce * deltaTime / Mathf.Sqrt(sqrDist));
                    } else {
                        //ResourceManager.GrabResource(bee,resource);
                    }
                }
            }
        }).Schedule(inputDependencies);
        
        EndUpdateCommandBufferSystem.AddJobHandleForProducer(targetJobHandle);
        
        inputDependencies = JobHandle.CombineDependencies(targetJobHandle, inputDependencies);
        inputDependencies = JobHandle.CombineDependencies(beeTeamA.Dispose(inputDependencies), beeTeamB.Dispose(inputDependencies), inputDependencies);
        inputDependencies = JobHandle.CombineDependencies(resources.Dispose(inputDependencies), inputDependencies);

        var scaleJobHandle = Entities.WithNone<DeadBee>().ForEach((ref NonUniformScale scale, in Bee bee) =>
        {
            float stretch = Mathf.Max(1f, bee.Velocity.magnitude * managerData.BeeSpeedStretch);
            
            scale.Value.z = bee.Size * stretch;
            scale.Value.x = scale.Value.y = bee.Size / (((stretch - 1f) / 5f) + 1f);
            
        }).Schedule(inputDependencies);
        
        inputDependencies = JobHandle.CombineDependencies(scaleJobHandle, inputDependencies);
        
        var updateDeadJobHandle = Entities.ForEach((Entity entity, int entityInQueryIndex, ref NonUniformScale scale, ref DeadBee deadBee, in Bee bee) =>
        { 
            scale.Value.x = scale.Value.y = scale.Value.z = bee.Size;
            deadBee.DeathTimer += deltaTime;
            
            //TODO: more death particles

            if (deadBee.DeathTimer > managerData.BeeDeathTime)
            {
                //TODO: Should we be pooling the entities instead
                updateCommandBuffer.DestroyEntity(entityInQueryIndex, entity);
            }
        }).Schedule(inputDependencies);
        
        EndUpdateCommandBufferSystem.AddJobHandleForProducer(updateDeadJobHandle);
        inputDependencies = JobHandle.CombineDependencies(updateDeadJobHandle, inputDependencies);
        
        var moveJobHandle = Entities.ForEach((ref Bee bee, ref Translation translation, ref Rotation rotation) =>
        {
            var position = (Vector3)translation.Value + (bee.Velocity * deltaTime);

            if (Math.Abs(position.x) > managerData.FieldSize.x * .5f) {
                position.x = (managerData.FieldSize.x * .5f) * Mathf.Sign(position.x);
                bee.Velocity.x *= -.5f;
                bee.Velocity.y *= .8f;
                bee.Velocity.z *= .8f;
            }
            if (Math.Abs(position.z) > managerData.FieldSize.z * .5f) {
                position.z = (managerData.FieldSize.z * .5f) * Mathf.Sign(position.z);
                bee.Velocity.z *= -.5f;
                bee.Velocity.x *= .8f;
                bee.Velocity.y *= .8f;
            }
            
            /*float resourceModifier = 0f; TODO: resource modifier to velocity
            if (bee.isHoldingResource) {
                resourceModifier = ResourceManager.instance.resourceSize;
            }
            if (Math.Abs(bee.position.y) > Field.size.y * .5f - resourceModifier) {
                bee.position.y = (Field.size.y * .5f - resourceModifier) * Mathf.Sign(bee.position.y);
                bee.velocity.y *= -.5f;
                bee.velocity.z *= .8f;
                bee.velocity.x *= .8f;
            }*/
            
            translation.Value = position;

            var oldPos = bee.SmoothPosition;
            bee.SmoothPosition = bee.IsAttacking
                ? Vector3.Lerp(bee.SmoothPosition, position, deltaTime * managerData.BeeRotationStiffness)
                : position;
            
            bee.SmoothDirection = bee.SmoothPosition - oldPos;
            rotation.Value = Quaternion.LookRotation(bee.SmoothDirection);
        }).Schedule(inputDependencies);

        return moveJobHandle;
    }

}
