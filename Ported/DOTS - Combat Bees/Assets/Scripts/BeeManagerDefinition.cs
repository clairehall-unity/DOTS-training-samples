﻿using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct BeeManagerData : IComponentData
{
    public Vector3 FieldSize;

    public float MinBeeSize;
    public float MaxBeeSize;

    public float BeeGravity;
    public float MaxBeeSpawnSpeed;
    public float BeeFlightJitter;
    public float BeeFlightDamping;
    public float BeeRotationStiffness;

    public Color TeamAColour;
    public Color TeamBColour;
}

public struct SpawnBeeData : IComponentData
{
    public int SpawnCount;
}

public class BeeManagerDefinition : MonoBehaviour, IConvertGameObjectToEntity
{
    public int StartBeeCount;
    public float MinBeeSize;
    public float MaxBeeSize;
    public float MaxBeeSpawnSpeed;
    public float BeeGravity;
    public float BeeFlightJitter;
    [Range(0,1)]
    public float BeeFlightDamping;

    public float BeeRotationStiffness;
    public Color[] TeamColours;
    
    public Mesh BeeMesh;
    public Material BeeMaterial;
    
    public GameObject FieldObject;
    
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var teamColours = new Vector4[TeamColours.Length];

        for (int i = 0; i < teamColours.Length; i++)
        {
            teamColours[i] = new Vector4(TeamColours[i].r, TeamColours[i].g, TeamColours[i].b, TeamColours[i].a);
        }
        
        var beeManagerData = new BeeManagerData
        {
            FieldSize = FieldObject.transform.localScale,
            MinBeeSize = this.MinBeeSize,
            MaxBeeSize = this.MaxBeeSize,
            MaxBeeSpawnSpeed = this.MaxBeeSpawnSpeed,
            BeeFlightDamping = this.BeeFlightDamping,
            BeeFlightJitter = this.BeeFlightJitter,
            BeeRotationStiffness = this.BeeRotationStiffness,
            BeeGravity = this.BeeGravity,
            TeamAColour = TeamColours[0],
            TeamBColour = TeamColours[1]
        };

        var spawnBeeData = new SpawnBeeData
        {
            SpawnCount = StartBeeCount
        };

        dstManager.AddComponentData(entity, beeManagerData);
        dstManager.AddComponentData(entity, spawnBeeData);
        dstManager.AddSharedComponentData(entity, new RenderMeshInfo {Mesh = BeeMesh, Material = BeeMaterial});
    }
}
