using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct BeeManagerData : IComponentData
{
    public Vector3 FieldSize;

    public float MinBeeSize;
    public float MaxBeeSize;
    
    public Entity BeePrefabEntity;
}

public struct SpawnBeeData : IComponentData
{
    public int SpawnCount;
}

public class BeeManagerDefinition : MonoBehaviour, IConvertGameObjectToEntity, IDeclareReferencedPrefabs
{
    public int StartBeeCount;
    public float MinBeeSize;
    public float MaxBeeSize;
    public GameObject BeePrefab;
    public GameObject FieldObject;

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        referencedPrefabs.Add(BeePrefab);
    }

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var beePrefab = conversionSystem.GetPrimaryEntity(BeePrefab);

        var beeManagerData = new BeeManagerData
        {
            FieldSize = FieldObject.transform.localScale,
            MinBeeSize = this.MinBeeSize,
            MaxBeeSize = this.MaxBeeSize,
            BeePrefabEntity = beePrefab
        };

        var spawnBeeData = new SpawnBeeData
        {
            SpawnCount = StartBeeCount
        };

        dstManager.AddComponentData(entity, beeManagerData);
        dstManager.AddComponentData(entity, spawnBeeData);
    }
}
