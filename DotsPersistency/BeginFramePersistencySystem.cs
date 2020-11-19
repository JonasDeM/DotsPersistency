// Author: Jonas De Maeseneer

using System.Collections.Generic;
using Unity.Entities;
using Unity.Jobs;
using Unity.Scenes;
using UnityEngine;

namespace DotsPersistency
{
    [AlwaysUpdateSystem]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    public class BeginFramePersistencySystem : PersistencySystemBase
    {
        private EntityCommandBufferSystem _ecbSystem;

        private List<PersistentDataContainer> _initialStatePersistRequests;
        private List<PersistentDataContainer> _applyRequests;
        
        protected override void OnCreate()
        {
            base.OnCreate();
            InitializeReadWrite();
            _applyRequests = new List<PersistentDataContainer>(8);
            _ecbSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();
            _initialStatePersistRequests = new List<PersistentDataContainer>(8);
        }

        internal void RequestInitialStatePersist(PersistentDataContainer sceneSectionState)
        {
            for (int i = 0; i < _initialStatePersistRequests.Count; i++)
            {
                if (_initialStatePersistRequests[i].SceneSection.Equals(sceneSectionState.SceneSection))
                {
                    _initialStatePersistRequests[i] = sceneSectionState;
                    Debug.LogWarning($"BeginFramePersistentDataSystem:: Double persist request for scene section ({sceneSectionState.SceneSection.SceneGUID.ToString()} - {sceneSectionState.SceneSection.Section.ToString()})! (Will only do last request)");
                    return;
                }
            }
            _initialStatePersistRequests.Add(sceneSectionState);
        }

        public void RequestApply(PersistentDataContainer sceneSectionState)
        {
            for (int i = 0; i < _applyRequests.Count; i++)
            {
                if (_applyRequests[i].SceneSection.Equals(sceneSectionState.SceneSection))
                {
                    if (_applyRequests[i].Tick < sceneSectionState.Tick)
                    {
                        _applyRequests[i] = sceneSectionState;
                        Debug.LogWarning($"BeginFramePersistentDataSystem:: Multiple different apply request for scene section ({sceneSectionState.SceneSection.SceneGUID.ToString()} - {sceneSectionState.SceneSection.Section.ToString()})! (Will apply oldest data)");
                    }
                    return;
                }
            }
            _applyRequests.Add(sceneSectionState);
        }

        protected override void OnUpdate()
        {
            JobHandle inputDependencies = Dependency;
            
            foreach (var container in _initialStatePersistRequests)
            {
                Dependency = JobHandle.CombineDependencies(Dependency, ScheduleCopyToPersistentDataContainer(inputDependencies, container.SceneSection, container));
            }

            JobHandle applyDependencies = new JobHandle();
            foreach (var container in _applyRequests)
            {
                applyDependencies = JobHandle.CombineDependencies(applyDependencies,  ScheduleApplyToSceneSection(inputDependencies, container.SceneSection, container, _ecbSystem));
            }
            _ecbSystem.AddJobHandleForProducer(applyDependencies);
            Dependency = JobHandle.CombineDependencies(Dependency, applyDependencies);
            
            _initialStatePersistRequests.Clear();
            _applyRequests.Clear();
        }
    }
}