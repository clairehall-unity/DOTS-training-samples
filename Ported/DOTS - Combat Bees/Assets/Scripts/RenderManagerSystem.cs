using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using UnityEngine;
using ReadOnly = Unity.Collections.ReadOnlyAttribute;

public class RenderManagerSystem : ComponentSystem
{
    private EntityQuery Resources;
    
    protected override void OnCreate()
    {
        Resources = GetEntityQuery(typeof(Translation), typeof(ResourceManagerSystem.Resource));
    }

    protected override void OnUpdate()
    {
        var resources = Resources.ToEntityArray(Allocator.TempJob);
        var translations = GetComponentDataFromEntity<Translation>(true);
        var scales = GetComponentDataFromEntity<NonUniformScale>(true);
        
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

        resources.Dispose();
    }
}
