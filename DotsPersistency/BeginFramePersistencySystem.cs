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

        internal void RequestInitialStatePersist(PersistentDataContainer dataContainer)
        {
            for (int i = 0; i < _initialStatePersistRequests.Count; i++)
            {
                if (_initialStatePersistRequests[i].DataIdentifier.Equals(dataContainer.DataIdentifier))
                {
                    _initialStatePersistRequests[i] = dataContainer;
                    Debug.LogWarning($"BeginFramePersistentDataSystem:: Double persist request for container with DataIdentifier: {dataContainer.DataIdentifier.ToString()}! (Will only do last request)");
                    return;
                }
            }
            _initialStatePersistRequests.Add(dataContainer);
        }

        public void RequestApply(PersistentDataContainer dataContainer)
        {
            for (int i = 0; i < _applyRequests.Count; i++)
            {
                if (_applyRequests[i].DataIdentifier.Equals(dataContainer.DataIdentifier))
                {
                    if (_applyRequests[i].Tick < dataContainer.Tick)
                    {
                        _applyRequests[i] = dataContainer;
                        Debug.LogWarning($"BeginFramePersistentDataSystem:: Multiple different apply request for container with DataIdentifier: {dataContainer.DataIdentifier.ToString()}! (Will apply oldest data)");
                    }
                    return;
                }
            }
            _applyRequests.Add(dataContainer);
        }

        protected override void OnUpdate()
        {
            JobHandle inputDependencies = Dependency;
            
            foreach (var container in _initialStatePersistRequests)
            {
                Dependency = JobHandle.CombineDependencies(Dependency, SchedulePersist(inputDependencies, container));
            }

            JobHandle applyDependencies = new JobHandle();
            foreach (var container in _applyRequests)
            {
                applyDependencies = JobHandle.CombineDependencies(applyDependencies,  ScheduleApply(inputDependencies, container, _ecbSystem));
            }
            _ecbSystem.AddJobHandleForProducer(applyDependencies);
            Dependency = JobHandle.CombineDependencies(Dependency, applyDependencies);
            
            _initialStatePersistRequests.Clear();
            _applyRequests.Clear();
        }
    }
}