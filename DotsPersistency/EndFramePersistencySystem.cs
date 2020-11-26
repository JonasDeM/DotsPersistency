// Author: Jonas De Maeseneer

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public class EndFramePersistencySystem : PersistencySystemBase
    {
        private EntityQuery _persistRequests;
        private EntityQuery _unloadRequests;
        private EntityCommandBufferSystem _ecbSystem;
        private PersistentSceneSystem _containerSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            
            InitializeReadOnly();
            _ecbSystem = World.GetOrCreateSystem<BeginInitializationEntityCommandBufferSystem>();
            _containerSystem = World.GetOrCreateSystem<PersistentSceneSystem>();

            _persistRequests = GetEntityQuery(ComponentType.ReadOnly<SceneSectionData>(),
                ComponentType.ReadOnly<RequestSceneLoaded>(),
                ComponentType.ReadOnly<PersistentSceneSectionLoadComplete>(),
                ComponentType.Exclude<RequestPersistentSceneSectionLoaded>(),
                ComponentType.Exclude<DisableAutoPersistOnUnload>());
            
            _unloadRequests = GetEntityQuery(ComponentType.ReadOnly<SceneSectionData>(),
                ComponentType.ReadOnly<RequestSceneLoaded>(),
                ComponentType.ReadOnly<PersistentSceneSection>(),
                ComponentType.Exclude<RequestPersistentSceneSectionLoaded>());
        }
    
        protected override void OnUpdate()
        {
            JobHandle inputDependencies = Dependency;

            if (!_persistRequests.IsEmpty)
            {
                var sceneSectionsToUnload = _persistRequests.ToComponentDataArray<SceneSectionData>(Allocator.TempJob);
                var tempHashSet = new NativeHashSet<Hash128>(4, Allocator.Temp);
                foreach (SceneSectionData sceneSectionData in sceneSectionsToUnload)
                {
                    var containerIdentifier = sceneSectionData.SceneGUID;
                    if (!tempHashSet.Contains(containerIdentifier))
                    {
                        var writeContainer = _containerSystem.PersistentDataStorage.GetWriteContainerForCurrentIndex(containerIdentifier);
                        Dependency = JobHandle.CombineDependencies(Dependency, SchedulePersist(inputDependencies, writeContainer));
                        tempHashSet.Add(containerIdentifier);
                    }
                }
                sceneSectionsToUnload.Dispose();
                tempHashSet.Dispose();
            }
            
            // this will trigger the actual unload
            if (!_unloadRequests.IsEmpty)
            {
                EntityCommandBuffer ecb = _ecbSystem.CreateCommandBuffer();
                ecb.RemoveComponent(_unloadRequests, new ComponentTypes(typeof(RequestSceneLoaded), typeof(PersistentSceneSection), typeof(PersistentSceneSectionLoadComplete)));
            }
        }
    }
}