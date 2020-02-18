using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using UnityEngine;
using ReadOnly = Unity.Collections.ReadOnlyAttribute;

public struct RenderMeshInfo : ISharedComponentData, IEquatable<RenderMeshInfo>
{
    public Mesh Mesh;
    public Material Material;
    
    public bool Equals(RenderMeshInfo other)
    {
        return Mesh == other.Mesh 
               && Material == other.Material;
    }
    
    public override int GetHashCode()
    {
        int hash = 0;
        
        if (!ReferenceEquals(Mesh, null)) hash ^= Mesh.GetHashCode();
        if (!ReferenceEquals(Material, null)) hash ^= Material.GetHashCode();
        
        return hash;
    }
}

public struct RenderMeshWithColourInfo : ISharedComponentData, IEquatable<RenderMeshWithColourInfo>
{
    public Color Colour;
    public Mesh Mesh;
    public Material Material;
    
    public bool Equals(RenderMeshWithColourInfo other)
    {
        return Mesh == other.Mesh 
               && Material == other.Material 
               && Colour == other.Colour;
    }
    
    public override int GetHashCode()
    {
        int hash = 0;
        
        if (!ReferenceEquals(Colour, null)) hash ^= Colour.GetHashCode();
        if (!ReferenceEquals(Mesh, null)) hash ^= Mesh.GetHashCode();
        if (!ReferenceEquals(Material, null)) hash ^= Material.GetHashCode();
        
        return hash;
    }
}

public class RenderManagerSystem : ComponentSystem
{
    EntityQuery Meshes;
    EntityQuery ColourMeshes;
    
    protected override void OnCreate()
    {
        Meshes = GetEntityQuery(ComponentType.ReadOnly<RenderMeshInfo>(), ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<NonUniformScale>());
        ColourMeshes = GetEntityQuery(ComponentType.ReadOnly<RenderMeshWithColourInfo>(), ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<NonUniformScale>());
    }

    protected override void OnUpdate()
    {
        var translations = GetComponentDataFromEntity<Translation>(true);
        var scales = GetComponentDataFromEntity<NonUniformScale>(true);
        var rotations = GetComponentDataFromEntity<Rotation>(true);
        
        var meshes = new List<RenderMeshInfo>();
        EntityManager.GetAllUniqueSharedComponentData(meshes);

        for (int i = 0; i < meshes.Count; i++)
        {
            var meshInfo = meshes[i];
        
            Meshes.SetSharedComponentFilter(meshInfo);
        
            var entities = Meshes.ToEntityArray(Allocator.TempJob);

            if (entities.Length > 0)
            {
                List<Matrix4x4> matrices = new List<Matrix4x4>();

                for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
                {
                    var entity = entities[entityIndex];
                    var translation = translations[entity].Value;
                    var scale = scales[entity].Value;
            
                    matrices.Add(new Matrix4x4(new Vector4(scale.x, 0, 0, 0), new Vector4(0, scale.y, 0, 0), new Vector4(0, 0, scale.z, 0),
                        new Vector4(translation.x, translation.y, translation.z, 1)));
                }

                Graphics.DrawMeshInstanced(meshInfo.Mesh, 0, meshInfo.Material, matrices);
            }
            
            entities.Dispose();
        }

        var colourMeshes = new List<RenderMeshWithColourInfo>();
        EntityManager.GetAllUniqueSharedComponentData(colourMeshes);
        
        for (int i = 0; i < colourMeshes.Count; i++)
        {
            var meshInfo = colourMeshes[i];
        
            ColourMeshes.SetSharedComponentFilter(meshInfo);
        
            var entities = ColourMeshes.ToEntityArray(Allocator.TempJob);

            if (entities.Length > 0)
            {
                List<Matrix4x4> matrices = new List<Matrix4x4>();
                List<Vector4> colours = new List<Vector4>();
        
                var matProps = new MaterialPropertyBlock();

                for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
                {
                    var entity = entities[entityIndex];
                    var translation = translations[entity].Value;
                    var rotation = rotations[entity].Value;
                    var scale = scales[entity].Value;
            
                    matrices.Add(Matrix4x4.TRS(translation, rotation, scale));

                    colours.Add(meshInfo.Colour);
                }

                matProps.SetVectorArray("_Color", colours);
            
                Graphics.DrawMeshInstanced(meshInfo.Mesh, 0, meshInfo.Material, matrices, matProps);
            }
            
            entities.Dispose();
        }
    }
}
