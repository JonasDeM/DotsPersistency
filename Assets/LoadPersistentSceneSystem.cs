using System.Collections;
using System.Collections.Generic;
using DotsPersistency;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class LoadPersistentSceneSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            var cmdBuffer = World.GetOrCreateSystem<BeginInitializationEntityCommandBufferSystem>().CreateCommandBuffer();
            
            Entities.ForEach((Entity e, ref SceneSectionData sceneSectionData) =>
            {
                cmdBuffer.AddComponent(e, new RequestPersistentSceneLoaded());
            });
        }
        if (Input.GetKeyDown(KeyCode.B))
        {
            var cmdBuffer = World.GetOrCreateSystem<BeginInitializationEntityCommandBufferSystem>().CreateCommandBuffer();
            
            Entities.ForEach((Entity e, ref SceneSectionData sceneSectionData) =>
            {
                cmdBuffer.RemoveComponent<RequestPersistentSceneLoaded>(e);
            });
        }
        
        if (Input.GetKeyDown(KeyCode.M))
        {
            Entities.ForEach((ref Translation t) =>
            {
                t.Value += new float3(0,1,0);
            });
            
            Entities.ForEach((ref Rotation r) =>
            {
                r.Value = quaternion.Euler(0,90,0);
            });
        }

        if (Input.GetKeyDown(KeyCode.D))
        {
            Entities.ForEach((Entity e, ref Translation t) =>
            {
                EntityManager.AddComponentData(e, new Disabled());
            });
        }
    }
}
