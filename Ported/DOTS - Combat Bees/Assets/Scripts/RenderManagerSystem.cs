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
    EntityQuery Resources;
    EntityQuery ColourMeshes;
    
    protected override void OnCreate()
    {
        Resources = GetEntityQuery(ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<NonUniformScale>(), ComponentType.ReadOnly<ResourceManagerSystem.Resource>());
        ColourMeshes = GetEntityQuery(ComponentType.ReadOnly<RenderMeshWithColourInfo>(), ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<NonUniformScale>());
    }

    protected override void OnUpdate()
    {
        var translations = GetComponentDataFromEntity<Translation>(true);
        var scales = GetComponentDataFromEntity<NonUniformScale>(true);
        
        var resources = Resources.ToEntityArray(Allocator.TempJob);

        Entities.ForEach((ResourceMesh renderData, ref ResourceManagerData resourceManagerData) =>
        {
            List<Matrix4x4> matrices = new List<Matrix4x4>();

            foreach (var resource in resources)
            {
                var translation = translations[resource].Value;
                var scale = scales[resource].Value;
                
                matrices.Add(new Matrix4x4(new Vector4(scale.x, 0, 0, 0), new Vector4(0, scale.y, 0, 0), new Vector4(0, 0, scale.z, 0),
                    new Vector4(translation.x, translation.y, translation.z, 1)));
            }

            Graphics.DrawMeshInstanced(renderData.Mesh, 0, renderData.Material, matrices);
        });

        var meshes = new List<RenderMeshWithColourInfo>();
        EntityManager.GetAllUniqueSharedComponentData(meshes);
        
        for (int i = 0; i < meshes.Count; i++)
        {
            var meshInfo = meshes[i];
        
            ColourMeshes.SetSharedComponentFilter(meshInfo);
        
            var bees = ColourMeshes.ToEntityArray(Allocator.TempJob);

            if (bees.Length > 0)
            {
                List<Matrix4x4> matrices = new List<Matrix4x4>();
                List<Vector4> colours = new List<Vector4>();
        
                var matProps = new MaterialPropertyBlock();

                foreach (var bee in bees)
                {
                    var translation = translations[bee].Value;
                    var scale = scales[bee].Value;
            
                    matrices.Add(new Matrix4x4(new Vector4(scale.x, 0, 0, 0), new Vector4(0, scale.y, 0, 0), new Vector4(0, 0, scale.z, 0),
                        new Vector4(translation.x, translation.y, translation.z, 1)));
            
                    colours.Add(meshInfo.Colour);
                }

                matProps.SetVectorArray("_Color", colours);
            
                Graphics.DrawMeshInstanced(meshInfo.Mesh, 0, meshInfo.Material, matrices, matProps);
            }
            
            bees.Dispose();
        }

        resources.Dispose();
    }
}
