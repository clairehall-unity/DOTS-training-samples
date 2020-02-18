using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

public struct ResourceManagerData : IComponentData
{
    public float FloorHeight;
    public float ResourceSize;
    public float ResourceGravity;
    public float SnapStiffness;
    public float CarryStiffness;
    public float SpawnRate;
    public int BeesPerResource;
    
    public Vector2 MinGridPos;
    public Vector2 GridSize;
    public Vector2Int GridCounts;
}

public struct SpawnResourceData : IComponentData
{
    public int SpawnCount;
}

public class ResourceManagerDefinition : MonoBehaviour, IConvertGameObjectToEntity
{
    public float ResourceSize;
    public float ResourceGravity;
    public float SnapStiffness;
    public float CarryStiffness;
    public float SpawnRate;
    public int BeesPerResource;
    public int StartResourceCount;

    public Mesh ResourceMesh;
    public Material ResourceMaterial;
    
    public GameObject FieldObject;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var fieldTransform = FieldObject.transform.localScale;

        var gridCounts = Vector2Int.RoundToInt(new Vector2(fieldTransform.x, fieldTransform.z) / this.ResourceSize);
        var gridSize = new Vector2(fieldTransform.x / gridCounts.x, fieldTransform.z / gridCounts.y);
        var minGridPos = new Vector2((gridCounts.x - 1f) * -(0.5f * gridSize.x),(gridCounts.y-1f) * -(0.5f * gridSize.y));
        
        var resourceManagerData = new ResourceManagerData
        {
            FloorHeight = -(0.5f * fieldTransform.y),
            ResourceSize = this.ResourceSize,
            ResourceGravity = this.ResourceGravity,
            SnapStiffness = this.SnapStiffness,
            CarryStiffness = this.CarryStiffness,
            SpawnRate = this.SpawnRate,
            BeesPerResource = this.BeesPerResource,
            GridCounts = gridCounts,
            GridSize = gridSize,
            MinGridPos = minGridPos,
        };

        var resourceMeshData = new RenderMeshInfo()
        {
            Mesh = this.ResourceMesh,
            Material = this.ResourceMaterial
        };

        var spawnResourceData = new SpawnResourceData
        {
            SpawnCount = StartResourceCount
        };

        dstManager.AddComponentData(entity, resourceManagerData);
        dstManager.AddComponentData(entity, spawnResourceData);
        dstManager.AddSharedComponentData(entity, resourceMeshData);
    }
}
