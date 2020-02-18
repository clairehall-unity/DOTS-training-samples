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
using Random = UnityEngine.Random;
using ReadOnly = Unity.Collections.ReadOnlyAttribute;

public class BeeManagerSystem : JobComponentSystem
{
    public struct Bee : IComponentData
    {
        public float3 Velocity;
        public float3 SmoothDirection;
        public float3 SmoothPosition;
        public bool IsAttacking;
    }

    public struct BeeTeam : ISharedComponentData
    {
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
      
                    var size = Mathf.Lerp(managerData.MinBeeSize, managerData.MaxBeeSize, Random.value);
                    var velocity = Random.insideUnitSphere * managerData.MaxBeeSpawnSpeed;
                    
                    commandBuffer.AddComponent(beeEntity, new Translation { Value = position });
                    commandBuffer.AddComponent(beeEntity, new NonUniformScale{ Value = new float3(size,  size, size) });
                    commandBuffer.AddComponent(beeEntity, new Bee
                    {
                        Velocity = velocity,
                        SmoothPosition = position + (Vector3.right * 0.1f),
                        SmoothDirection = Vector3.zero,
                        IsAttacking = false
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

        var moveJobHandle = Entities.ForEach((ref Bee bee, ref Translation translation, ref Rotation rotation) =>
        {
            bee.Velocity += random.NextFloat3Direction() * (managerData.BeeFlightJitter * deltaTime);
            bee.Velocity *= (1f - managerData.BeeFlightDamping);

            bee.Velocity.y += managerData.BeeGravity * deltaTime;

            var position = translation.Value + (bee.Velocity * deltaTime);
            
            
            if (System.Math.Abs(position.x) > managerData.FieldSize.x * .5f) {
                position.x = (managerData.FieldSize.x * .5f) * Mathf.Sign(position.x);
                bee.Velocity.x *= -.5f;
                bee.Velocity.y *= .8f;
                bee.Velocity.z *= .8f;
            }
            if (System.Math.Abs(position.z) > managerData.FieldSize.z * .5f) {
                position.z = (managerData.FieldSize.z * .5f) * Mathf.Sign(position.z);
                bee.Velocity.z *= -.5f;
                bee.Velocity.x *= .8f;
                bee.Velocity.y *= .8f;
            }
            
            /*float resourceModifier = 0f; TODO: resource modifier to velocity
            if (bee.isHoldingResource) {
                resourceModifier = ResourceManager.instance.resourceSize;
            }
            if (System.Math.Abs(bee.position.y) > Field.size.y * .5f - resourceModifier) {
                bee.position.y = (Field.size.y * .5f - resourceModifier) * Mathf.Sign(bee.position.y);
                bee.velocity.y *= -.5f;
                bee.velocity.z *= .8f;
                bee.velocity.x *= .8f;
            }*/
            
            translation.Value = position;

            var oldPos = bee.SmoothPosition;
            bee.SmoothPosition = bee.IsAttacking
                ? (float3) Vector3.Lerp(bee.SmoothPosition, position, deltaTime * managerData.BeeRotationStiffness)
                : position;
            
            bee.SmoothDirection = bee.SmoothPosition - oldPos;
            rotation.Value = Quaternion.LookRotation(bee.SmoothDirection);
        }).Schedule(inputDependencies);

        return moveJobHandle;
    }

}
