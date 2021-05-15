// Author: Jonas De Maeseneer

using System.Collections.Generic;
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
        private List<PersistentDataContainer> _persistRequests;
        private EntityQuery _autoPersistRequests;
        private EntityQuery _unloadRequests;
        private EntityCommandBufferSystem _ecbSystem;
        private PersistentSceneSystem _containerSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            
            InitializeReadOnly(PersistencySettings.Get());
            _ecbSystem = World.GetOrCreateSystem<BeginInitializationEntityCommandBufferSystem>();
            _containerSystem = World.GetOrCreateSystem<PersistentSceneSystem>();

            _autoPersistRequests = GetEntityQuery(ComponentType.ReadOnly<SceneSectionData>(),
                ComponentType.ReadOnly<RequestSceneLoaded>(),
                ComponentType.ReadOnly<PersistentSceneSectionLoadComplete>(),
                ComponentType.Exclude<RequestPersistentSceneSectionLoaded>(),
                ComponentType.Exclude<DisableAutoPersistOnUnload>());
            
            _unloadRequests = GetEntityQuery(ComponentType.ReadOnly<SceneSectionData>(),
                ComponentType.ReadOnly<RequestSceneLoaded>(),
                ComponentType.ReadOnly<PersistentSceneSection>(),
                ComponentType.Exclude<RequestPersistentSceneSectionLoaded>());
            
            _persistRequests = new List<PersistentDataContainer>(8);
        }
    
        protected override void OnUpdate()
        {
            JobHandle inputDependencies = Dependency;

            // Add auto persisting scenes to the _persistRequests
            if (!_autoPersistRequests.IsEmpty)
            {
                var sceneSectionsToUnload = _autoPersistRequests.ToComponentDataArray<SceneSectionData>(Allocator.TempJob);
                var tempHashSet = new NativeHashSet<Hash128>(sceneSectionsToUnload.Length, Allocator.Temp);
                foreach (SceneSectionData sceneSectionData in sceneSectionsToUnload)
                {
                    var containerIdentifier = sceneSectionData.SceneGUID;
                    if (!tempHashSet.Contains(containerIdentifier) && _containerSystem.PersistentDataStorage.IsInitialized(containerIdentifier))
                    {
                        var writeContainer = _containerSystem.PersistentDataStorage.GetWriteContainerForCurrentIndex(containerIdentifier);
                        _persistRequests.Add(writeContainer);
                        tempHashSet.Add(containerIdentifier);
                    }
                }
                sceneSectionsToUnload.Dispose();
                tempHashSet.Dispose();
                
                // this will trigger the actual unload
                EntityCommandBuffer ecb = _ecbSystem.CreateCommandBuffer();
                ecb.RemoveComponent(_unloadRequests, new ComponentTypes(typeof(RequestSceneLoaded), typeof(PersistentSceneSection), typeof(PersistentSceneSectionLoadComplete)));
            }
            
            // Schedule the actual persist jobs
            if (_persistRequests.Count > 0)
            {
                ComponentTypeHandle<PersistenceState> indexTypeHandle = GetComponentTypeHandle<PersistenceState>(true);
                ComponentTypeHandle<PersistencyArchetypeIndexInContainer> archetypeIndexTypeHandle = GetComponentTypeHandle<PersistencyArchetypeIndexInContainer>(true);
                var jobHandles = new NativeList<JobHandle>(_persistRequests.Count, Allocator.Temp);
                foreach (PersistentDataContainer container in _persistRequests)
                {
                    JobHandle persistDep = SchedulePersist(inputDependencies, container, indexTypeHandle, archetypeIndexTypeHandle);
                    jobHandles.Add(persistDep);
                }
                Dependency = JobHandle.CombineDependencies(jobHandles);
                jobHandles.Dispose();
                _persistRequests.Clear();
            }
        }

        public void RequestPersist(PersistentDataContainer dataContainer)
        {
            for (int i = 0; i < _persistRequests.Count; i++)
            {
                if (_persistRequests[i].DataIdentifier.Equals(dataContainer.DataIdentifier))
                {
                    if (_persistRequests[i].Tick < dataContainer.Tick)
                    {
                        _persistRequests[i] = dataContainer;
                        Debug.LogWarning($"BeginFramePersistentDataSystem:: Multiple different persist requests for DataIdentifier: {dataContainer.DataIdentifier.ToString()}! (Will apply oldest data)");
                    }
                    return;
                }
            }
            _persistRequests.Add(dataContainer);
        }
    }
}