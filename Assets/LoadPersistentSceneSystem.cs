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
            
            Entities.ForEach((DynamicBuffer<MyBufferData1> bufferData1) =>
            {
                bufferData1.Add(new MyBufferData1() {Data = 3});
            });
        }
        
        if (Input.GetKeyDown(KeyCode.S))
        {
            var storage = World.GetOrCreateSystem<BeginFramePersistentDataSystem>().PersistentDataStorage;
            storage.SaveAllToDisk();
        }
        
        if (Input.GetKeyDown(KeyCode.R))
        {
            var storage = World.GetOrCreateSystem<BeginFramePersistentDataSystem>().PersistentDataStorage;
            Entities.ForEach((Entity e, ref SceneSectionData sceneSectionData) =>
            {
                var sceneSection = new SceneSection {Section = sceneSectionData.SubSectionIndex, SceneGUID = sceneSectionData.SceneGUID};
                storage.ReadContainerDataFromDisk(sceneSection);
                World.GetOrCreateSystem<BeginFramePersistentDataSystem>().RequestApply(sceneSection);
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
