// Author: Jonas De Maeseneer

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace DotsPersistency
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public class EndFramePersistencySystem : PersistencySystemBase
    {
        private EntityQuery _unloadStreamRequests;
        private EntityCommandBufferSystem _ecbSystem;
        private PersistentSceneSystem _containerSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            
            InitializeReadOnly();
            _ecbSystem = World.GetOrCreateSystem<BeginInitializationEntityCommandBufferSystem>();
            _containerSystem = World.GetOrCreateSystem<PersistentSceneSystem>();

            _unloadStreamRequests = GetEntityQuery(ComponentType.Exclude<RequestPersistentSceneLoaded>()
                , ComponentType.ReadOnly<RequestSceneLoaded>(), ComponentType.ReadOnly<SceneSectionData>(), ComponentType.ReadOnly<PersistingSceneType>());
            _unloadStreamRequests.SetSharedComponentFilter(new PersistingSceneType() {PersistingStrategy = PersistingStrategy.OnUnLoad});
            RequireForUpdate(_unloadStreamRequests);
        }
    
        protected override void OnUpdate()
        {
            JobHandle inputDependencies = Dependency;

            var sceneSectionsToUnload = _unloadStreamRequests.ToComponentDataArray<SceneSectionData>(Allocator.TempJob);
            foreach (SceneSectionData sceneSectionData in sceneSectionsToUnload)
            {
                SceneSection sceneSectionToPersist = new SceneSection()
                {
                    Section = sceneSectionData.SubSectionIndex,
                    SceneGUID = sceneSectionData.SceneGUID
                };

                var writeContainer = _containerSystem.PersistentDataStorage.GetWriteContainerForCurrentIndex(sceneSectionToPersist);
                Dependency = JobHandle.CombineDependencies(Dependency,
                    ScheduleCopyToPersistentDataContainer(inputDependencies, sceneSectionToPersist, writeContainer));
            }
            sceneSectionsToUnload.Dispose();
            
            // this will trigger the actual unload
            _ecbSystem.CreateCommandBuffer().RemoveComponent<RequestSceneLoaded>(_unloadStreamRequests);
        }
    }
}