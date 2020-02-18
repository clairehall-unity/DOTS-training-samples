using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using UnityEngine;
using ReadOnly = Unity.Collections.ReadOnlyAttribute;

public class RenderManagerSystem : ComponentSystem
{
    EntityQuery Resources;
    EntityQuery Bees;
    
    protected override void OnCreate()
    {
        Resources = GetEntityQuery(ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<NonUniformScale>(), ComponentType.ReadOnly<ResourceManagerSystem.Resource>());
        Bees = GetEntityQuery(ComponentType.ReadOnly<BeeManagerSystem.TeamInfo>(), ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<NonUniformScale>(), ComponentType.ReadOnly<BeeManagerSystem.Bee>());
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

        var teams = new List<BeeManagerSystem.TeamInfo>();
        EntityManager.GetAllUniqueSharedComponentData(teams);

        
        Entities.ForEach((BeeManagerSystem.TeamInfo managerTeamInfo, ref BeeManagerData beeManagerData) =>
        {
            for (int i = 0; i < teams.Count; i++)
            {
                var teamInfo = teams[i];
            
                Bees.SetSharedComponentFilter(teamInfo);
            
                var bees = Bees.ToEntityArray(Allocator.TempJob);

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
                
                        colours.Add(teamInfo.Colour);
                    }

                    matProps.SetVectorArray("_Color", colours);
                
                    Graphics.DrawMeshInstanced(managerTeamInfo.Mesh, 0, managerTeamInfo.Material, matrices, matProps);
                }
                
                bees.Dispose();
            }
        });
        
        
        resources.Dispose();
    }
}
