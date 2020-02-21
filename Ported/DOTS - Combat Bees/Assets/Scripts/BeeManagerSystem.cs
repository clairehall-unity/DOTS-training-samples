using System;
using System.Resources;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Assertions.Comparers;
using ReadOnly = Unity.Collections.ReadOnlyAttribute;

[UpdateBefore(typeof(RenderManagerSystem))]
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
    EntityQuery ResourceManager;
    
    EntityQuery BeeTeamMembers;
    EntityQuery AvailableResources;

    BeeTeam[] BeeTeams;

    double LastRunTime = -999;

    protected override void OnCreate()
    {
        EndInitCommandBufferSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();
        EndUpdateCommandBufferSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();

        AvailableResources = GetEntityQuery(ComponentType.ReadOnly<ResourceManagerSystem.Resource>(), ComponentType.Exclude<ResourceManagerSystem.ResourceHolder>());

        ResourceManager = GetEntityQuery(ComponentType.ReadOnly<ResourceManagerData>());
        BeeManager = GetEntityQuery(ComponentType.ReadOnly<BeeManagerData>());
        BeeTeamMembers = GetEntityQuery(ComponentType.ReadOnly<BeeTeam>(), ComponentType.ReadOnly<Bee>(), ComponentType.Exclude<DeadBee>());

        BeeTeams = new BeeTeam[2] { new BeeTeam{ TeamIndex = 0 }, new BeeTeam{ TeamIndex = 1 } };
    }

    protected override JobHandle OnUpdate(JobHandle inputDependencies)
    {
        var deltaTime = (float)(Time.ElapsedTime - LastRunTime);

        if (deltaTime > 0.02)
        {
            LastRunTime = Time.ElapsedTime;
            
            var managerData = BeeManager.GetSingleton<BeeManagerData>();
            var resourceManagerData = ResourceManager.GetSingleton<ResourceManagerData>();
            var initCommandBuffer = EndInitCommandBufferSystem.CreateCommandBuffer();
            var updateCommandBuffer = EndUpdateCommandBufferSystem.CreateCommandBuffer().ToConcurrent();

            var random = new Unity.Mathematics.Random((uint) System.DateTime.Now.Millisecond + 1);

            Entities.ForEach((Entity entity, RenderMeshInfo meshInfo, ref SpawnBeeData spawnBeeData) =>
            {
                for (int i = 0; i < spawnBeeData.SpawnCount; i++)
                {
                    var teamIndex = i % 2;
                    var beeEntity = initCommandBuffer.CreateEntity();

                    var position = Vector3.right *
                                   ((-managerData.FieldSize.x * 0.4f) + managerData.FieldSize.x * 0.8f * teamIndex);

                    var size = Mathf.Lerp(managerData.MinBeeSize, managerData.MaxBeeSize, random.NextFloat());
                    var velocity = random.NextFloat3Direction() * managerData.MaxBeeSpawnSpeed;

                    initCommandBuffer.AddComponent(beeEntity, new Translation {Value = position});
                    initCommandBuffer.AddComponent(beeEntity,
                        new NonUniformScale {Value = new float3(size, size, size)});
                    initCommandBuffer.AddComponent(beeEntity, new Bee
                    {
                        Velocity = velocity,
                        SmoothPosition = position + (Vector3.right * 0.1f),
                        SmoothDirection = Vector3.zero,
                        Size = size,
                        IsAttacking = false,
                        TeamIndex = teamIndex
                    });
                    initCommandBuffer.AddComponent(beeEntity, new Rotation {Value = quaternion.identity});
                    initCommandBuffer.AddSharedComponent(beeEntity, new BeeTeam {TeamIndex = teamIndex});
                    initCommandBuffer.AddSharedComponent(beeEntity, new RenderMeshWithColourInfo
                    {
                        Colour = teamIndex == 0 ? managerData.TeamAColour : managerData.TeamBColour,
                        Mesh = meshInfo.Mesh,
                        Material = meshInfo.Material
                    });
                }

                initCommandBuffer.RemoveComponent<SpawnBeeData>(entity);
            }).WithoutBurst().Run();
            
            var deadBees = GetComponentDataFromEntity<DeadBee>(true);
            var translations = GetComponentDataFromEntity<Translation>(true);

            var stackedResources = GetComponentDataFromEntity<ResourceManagerSystem.StackedResource>(true);
            var resourceHolders = GetComponentDataFromEntity<ResourceManagerSystem.ResourceHolder>(true);
            var resources = AvailableResources.ToEntityArray(Allocator.TempJob);

            //TODO: Is there a native container that can access the entity arrays by index 

            BeeTeamMembers.SetSharedComponentFilter(BeeTeams[0]);

            var beeTeamA = BeeTeamMembers.ToEntityArray(Allocator.TempJob);

            BeeTeamMembers.SetSharedComponentFilter(BeeTeams[1]);

            var beeTeamB = BeeTeamMembers.ToEntityArray(Allocator.TempJob);

            var initVelocityJobHandle = Entities.WithNone<DeadBee>().WithReadOnly(translations).ForEach(
                (ref Bee bee, in Translation translation) =>
                {
                    bee.Velocity += (Vector3) random.NextFloat3Direction() * (managerData.BeeFlightJitter * deltaTime);
                    bee.Velocity *= (1f - managerData.BeeFlightDamping);

                    var allyBees = bee.TeamIndex == 0 ? beeTeamA : beeTeamB;

                    int numAllyBees = allyBees.Length;

                    if (numAllyBees > 0)
                    {
                        var attractiveFriend = allyBees[random.NextInt(0, numAllyBees - 1)];
                        var attractivePosition = translations[attractiveFriend].Value;
                        Vector3 delta = attractivePosition - translation.Value;

                        float sqrDist = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;

                        if (sqrDist > 0f)
                        {
                            bee.Velocity +=
                                delta * (float) ((managerData.BeeTeamAttraction * deltaTime) / Math.Sqrt(sqrDist));
                        }

                        var repellentFriend = allyBees[random.NextInt(0, numAllyBees - 1)];
                        var repellentPosition = translations[repellentFriend].Value;
                        delta = repellentPosition - translation.Value;
                        sqrDist = Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
                        if (sqrDist > 0f)
                        {
                            bee.Velocity -=
                                delta * (float) (managerData.BeeTeamRepulsion * deltaTime / Math.Sqrt(sqrDist));
                        }
                    }

                }).Schedule(inputDependencies);

            inputDependencies = JobHandle.CombineDependencies(initVelocityJobHandle, inputDependencies);

            var deadBeeVelocityJobHandle = Entities.ForEach((ref Bee bee, in DeadBee dead) =>
            {
                bee.Velocity.y += managerData.BeeGravity * deltaTime;
            }).Schedule(initVelocityJobHandle);

            inputDependencies = JobHandle.CombineDependencies(deadBeeVelocityJobHandle, inputDependencies);

            var targetJobHandle = Entities.WithReadOnly(deadBees).WithReadOnly(translations)
                .WithReadOnly(stackedResources).WithReadOnly(resourceHolders).ForEach(
                    (Entity entity, int entityInQueryIndex, ref Bee bee, in Translation translation) =>
                    {
                        bee.IsAttacking = false;
                        bee.IsHolding = false;

                        if (bee.TargetBee == Entity.Null && bee.TargetResource == Entity.Null)
                        {
                            var aggression = random.NextFloat();
                            if (aggression < managerData.BeeAggression)
                            {
                                int enemyTeam = (bee.TeamIndex + 1) % 2;
                                var enemyBees = enemyTeam == 0 ? beeTeamA : beeTeamB;
                                int numEnemyBees = enemyBees.Length;

                                if (numEnemyBees > 0)
                                {
                                    bee.TargetBee = enemyBees[random.NextInt(0, numEnemyBees - 1)];
                                    //Debug.Log($"picked target: {bee.TargetBee.Index}");
                                }
                            }
                            else
                            {
                                var numResources = resources.Length;

                                if (numResources > 0)
                                {
                                    bee.TargetResource = resources[random.NextInt(0, numResources - 1)];
                                    //Debug.Log($"picked resource: {bee.TargetResource.Index}");
                                }
                            }
                        }
                        else if (bee.TargetBee != Entity.Null)
                        {
                            if (deadBees.Exists(bee.TargetBee))
                            {
                                //Debug.Log($"target dead: {bee.TargetBee.Index}");
                                bee.TargetBee = Entity.Null;
                            }
                            else
                            {
                                var delta = translations[bee.TargetBee].Value - translation.Value;
                                var sqrDist = (delta.x * delta.x) + (delta.y * delta.y) + (delta.z * delta.z);

                                if (sqrDist > managerData.BeeAttackRangeSq)
                                {
                                    bee.Velocity += (Vector3) delta *
                                                    ((managerData.BeeChaseForce * deltaTime) / Mathf.Sqrt(sqrDist));
                                }
                                else
                                {
                                    bee.IsAttacking = true;

                                    if (sqrDist < managerData.BeeHitRangeSq)
                                    {
                                        //Debug.Log($"target killed: {bee.TargetBee.Index}");
                                        //TODO: Death particles
                                        updateCommandBuffer.AddComponent(entityInQueryIndex, bee.TargetBee,
                                            new DeadBee());
                                        bee.TargetBee = Entity.Null;
                                    }
                                    else
                                    {
                                        bee.Velocity +=
                                            (Vector3) delta *
                                            ((managerData.BeeAttackForce * deltaTime) / Mathf.Sqrt(sqrDist));
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

                                        //Debug.Log($"abandon ally resource: {bee.TargetResource.Index}");
                                    }
                                    else
                                    {
                                        //Debug.Log($"target resource holder: {holder.Index}");
                                        bee.TargetBee = holder;
                                    }
                                }
                                else
                                {
                                    var position = translation.Value;

                                    var targetPos =
                                        new float3(
                                            -managerData.FieldSize.x * .45f +
                                            (managerData.FieldSize.x * .9f * bee.TeamIndex), 0f, position.z);
                                    var delta = targetPos - position;
                                    var sqrDist = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;

                                    if (sqrDist < 1f)
                                    {
                                        bee.TargetResource = Entity.Null;

                                        //Debug.Log($"dropped resource: {bee.TargetResource.Index}");

                                        updateCommandBuffer.RemoveComponent<ResourceManagerSystem.ResourceHolder>(
                                            entityInQueryIndex, resource);
                                    }
                                    else
                                    {
                                        bee.Velocity +=
                                            (Vector3) delta *
                                            (managerData.BeeCarryForce * deltaTime / Mathf.Sqrt(sqrDist));
                                        bee.IsHolding = true;
                                    }
                                }
                            }
                            else if (!stackedResources.Exists(resource) || !stackedResources[resource].IsOnTop)
                            {
                                //Debug.Log($"abandon resource: {bee.TargetResource.Index}");
                                bee.TargetResource = Entity.Null;
                            }
                            else
                            {
                                var delta = translations[resource].Value - translation.Value;
                                var sqrDist = delta.x * delta.x + delta.y * delta.y + delta.z * delta.z;

                                if (sqrDist > managerData.BeeGrabRangeSq)
                                {
                                    bee.Velocity += (Vector3) delta *
                                                    (managerData.BeeChaseForce * deltaTime / Mathf.Sqrt(sqrDist));
                                }
                                else
                                {
                                    updateCommandBuffer.RemoveComponent<ResourceManagerSystem.StackedResource>(
                                        entityInQueryIndex, resource);
                                    updateCommandBuffer.AddComponent(entityInQueryIndex, resource,
                                        new ResourceManagerSystem.ResourceHolder {Holder = entity});
                                }
                            }
                        }
                    }).Schedule(inputDependencies);

            EndUpdateCommandBufferSystem.AddJobHandleForProducer(targetJobHandle);

            inputDependencies = JobHandle.CombineDependencies(targetJobHandle, inputDependencies);
            inputDependencies = JobHandle.CombineDependencies(beeTeamA.Dispose(inputDependencies),
                beeTeamB.Dispose(inputDependencies), inputDependencies);
            inputDependencies = JobHandle.CombineDependencies(resources.Dispose(inputDependencies), inputDependencies);

            var scaleJobHandle = Entities.WithNone<DeadBee>().ForEach((ref NonUniformScale scale, in Bee bee) =>
            {
                float stretch = Mathf.Max(1f, bee.Velocity.magnitude * managerData.BeeSpeedStretch);

                scale.Value.z = bee.Size * stretch;
                scale.Value.x = scale.Value.y = bee.Size / (((stretch - 1f) / 5f) + 1f);

            }).Schedule(inputDependencies);

            inputDependencies = JobHandle.CombineDependencies(scaleJobHandle, inputDependencies);

            var updateDeadJobHandle = Entities.ForEach(
                (Entity entity, int entityInQueryIndex, ref NonUniformScale scale, ref DeadBee deadBee, in Bee bee) =>
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
                var position = (Vector3) translation.Value + (bee.Velocity * deltaTime);
                float resourceModifier = bee.IsHolding ? resourceManagerData.ResourceSize : 0f;

                if (Math.Abs(position.x) > managerData.FieldSize.x * .5f)
                {
                    position.x = (managerData.FieldSize.x * .5f) * Mathf.Sign(position.x);
                    bee.Velocity.x *= -.5f;
                    bee.Velocity.y *= .8f;
                    bee.Velocity.z *= .8f;
                }

                if (Math.Abs(position.z) > managerData.FieldSize.z * .5f)
                {
                    position.z = (managerData.FieldSize.z * .5f) * Mathf.Sign(position.z);
                    bee.Velocity.z *= -.5f;
                    bee.Velocity.x *= .8f;
                    bee.Velocity.y *= .8f;
                }

                if (Math.Abs(position.y) > managerData.FieldSize.y * .5f - resourceModifier)
                {
                    position.y = (managerData.FieldSize.y * .5f - resourceModifier) * Mathf.Sign(position.y);
                    bee.Velocity.y *= -.5f;
                    bee.Velocity.z *= .8f;
                    bee.Velocity.x *= .8f;
                }

                translation.Value = position;

                var oldPos = bee.SmoothPosition;
                bee.SmoothPosition = bee.IsAttacking
                    ? Vector3.Lerp(bee.SmoothPosition, position, deltaTime * managerData.BeeRotationStiffness)
                    : position;

                var smoothDirection = bee.SmoothPosition - oldPos;
                if (smoothDirection.magnitude > float.Epsilon) bee.SmoothDirection = smoothDirection;
                rotation.Value = Quaternion.LookRotation(bee.SmoothDirection);
            }).Schedule(inputDependencies);

            return moveJobHandle;
        }

        return inputDependencies;
    }

}
