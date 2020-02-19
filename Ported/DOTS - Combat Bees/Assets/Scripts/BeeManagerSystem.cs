using System;
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

        public Entity TargetBee;
        public Entity TargetResource;
    }

    public struct DeadBee : IComponentData
    {
        
    }

    public struct BeeTeam : ISharedComponentData
    {
        public int TeamIndex;
    }
    
    EntityCommandBufferSystem EndInitCommandBufferSystem;
    
    EntityQuery BeeManager;
    EntityQuery Bees;
    EntityQuery[] BeeTeams;

    protected override void OnCreate()
    {
        EndInitCommandBufferSystem = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();
        
        BeeManager = GetEntityQuery(ComponentType.ReadOnly<BeeManagerData>());
        
        BeeTeams = new EntityQuery[2] { GetEntityQuery(ComponentType.ReadOnly<BeeTeam>(), ComponentType.ReadOnly<Bee>()), GetEntityQuery(ComponentType.ReadOnly<BeeTeam>(), ComponentType.ReadOnly<Bee>()) };
        
        BeeTeams[0].SetSharedComponentFilter(new BeeTeam{TeamIndex = 0});
        BeeTeams[1].SetSharedComponentFilter(new BeeTeam{TeamIndex = 1});

        Bees = GetEntityQuery(typeof(Bee));
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
      
                    var size = Mathf.Lerp(managerData.MinBeeSize, managerData.MaxBeeSize, Random.value);
                    var velocity = Random.insideUnitSphere * managerData.MaxBeeSpawnSpeed;
                    
                    commandBuffer.AddComponent(beeEntity, new Translation { Value = position });
                    commandBuffer.AddComponent(beeEntity, new NonUniformScale{ Value = new float3(size,  size, size) });
                    commandBuffer.AddComponent(beeEntity, new Bee
                    {
                        Velocity = velocity,
                        SmoothPosition = position + (Vector3.right * 0.1f),
                        SmoothDirection = Vector3.zero,
                        Size = size,
                        IsAttacking = false,
                        TeamIndex = teamIndex
                    });
                    commandBuffer.AddComponent(beeEntity, new Rotation{ Value = quaternion.identity });
                    commandBuffer.AddSharedComponent(beeEntity, new BeeTeam { TeamIndex =  teamIndex });
                    commandBuffer.AddSharedComponent(beeEntity, new RenderMeshWithColourInfo
                    {
                        Colour = teamIndex == 0 ? managerData.TeamAColour : managerData.TeamBColour,
                        Mesh = meshInfo.Mesh,
                        Material = meshInfo.Material
                    });
                }

                commandBuffer.RemoveComponent<SpawnBeeData>(entity);
            }).WithoutBurst().Run();

        var deltaTime = Time.DeltaTime;
        var random = new Unity.Mathematics.Random((uint) System.DateTime.Now.Millisecond + 1);

        var deadBees = GetComponentDataFromEntity<DeadBee>(true);
        var translations = GetComponentDataFromEntity<Translation>(true);
        
        //TODO: Is there a native container that can access the entity arrays by index 
        var beeTeamA = BeeTeams[0].ToEntityArray(Allocator.TempJob);
        var beeTeamB = BeeTeams[1].ToEntityArray(Allocator.TempJob);
        
        var initVelocityJobHandle = Entities.WithNone<DeadBee>().ForEach((ref Bee bee, ref Translation translation, ref Rotation rotation, ref NonUniformScale scale) =>
        {
            bee.Velocity += (Vector3) random.NextFloat3Direction() * (managerData.BeeFlightJitter * deltaTime);
            bee.Velocity *= (1f - managerData.BeeFlightDamping);
        }).Schedule(inputDependencies);
        
        inputDependencies = JobHandle.CombineDependencies(initVelocityJobHandle, inputDependencies);
        
        var deadBeeVelocityJobHandle = Entities.ForEach((ref Bee bee, in DeadBee dead) =>
        {
            bee.Velocity.y += managerData.BeeGravity * deltaTime;
        }).Schedule(initVelocityJobHandle);
        
        inputDependencies = JobHandle.CombineDependencies(deadBeeVelocityJobHandle, inputDependencies);
        
        var targetJobHandle = Entities.WithReadOnly(deadBees).WithReadOnly(translations).ForEach((ref Bee bee, in Translation translation) =>
        {
            bee.IsAttacking = false;
            
            if (bee.TargetBee == Entity.Null && bee.TargetResource == Entity.Null && random.NextFloat() < managerData.BeeAggression)
            {
                int enemyTeam = (bee.TeamIndex + 1) % 2;
                var enemyBees = enemyTeam == 0 ? beeTeamA : beeTeamB;
                int numEnemyBees = enemyBees.Length;
                
                if (numEnemyBees > 0)
                {
                    bee.TargetBee = enemyBees[random.NextInt(0, numEnemyBees - 1)];
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
                        bee.Velocity += (Vector3) delta *
                                        ((managerData.BeeChaseForce * deltaTime) / Mathf.Sqrt(sqrDist));
                    }
                    else
                    {
                        bee.IsAttacking = true;
                        
                        if (sqrDist < managerData.BeeHitRangeSq)
                        {
                            bee.TargetBee = Entity.Null;
                            
                            //TODO: kill the other bee
                        }
                        else
                        {
                            bee.Velocity += (Vector3) delta *
                                            ((managerData.BeeAttackForce * deltaTime) / Mathf.Sqrt(sqrDist));
                        }
                    }
                }
            }
        }).Schedule(inputDependencies);
        
        inputDependencies = JobHandle.CombineDependencies(targetJobHandle, inputDependencies);
        inputDependencies = JobHandle.CombineDependencies(beeTeamA.Dispose(inputDependencies), beeTeamB.Dispose(inputDependencies), inputDependencies);

        var scaleJobHandle = Entities.WithNone<DeadBee>().ForEach((ref NonUniformScale scale, in Bee bee) =>
        {
            float stretch = Mathf.Max(1f, bee.Velocity.magnitude * managerData.BeeSpeedStretch);
            
            scale.Value.z = bee.Size * stretch;
            scale.Value.x = scale.Value.y = bee.Size / (((stretch - 1f) / 5f) + 1f);
            
        }).Schedule(inputDependencies);
        
        inputDependencies = JobHandle.CombineDependencies(scaleJobHandle, inputDependencies);
        
        var scaleDeadJobHandle = Entities.ForEach((ref NonUniformScale scale, in Bee bee, in DeadBee deadBee) =>
        { 
            scale.Value.x = scale.Value.y = scale.Value.z = bee.Size;
        }).Schedule(inputDependencies);
        
        inputDependencies = JobHandle.CombineDependencies(scaleDeadJobHandle, inputDependencies);
        
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
