// Author: Jonas De Maeseneer

using System.Collections.Generic;
using Unity.Collections;
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
            InitializeReadWrite(PersistencySettings.Get());
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
            if (_initialStatePersistRequests.Count + _applyRequests.Count == 0)
                return;

            EntityTypeHandle entityTypeHandle = GetEntityTypeHandle();
            ComponentTypeHandle<PersistenceState> indexTypeHandle = GetComponentTypeHandle<PersistenceState>(true);
            ComponentTypeHandle<PersistencyArchetypeIndexInContainer> archetypeIndexTypeHandle = GetComponentTypeHandle<PersistencyArchetypeIndexInContainer>(true);
            
            JobHandle inputDependencies = Dependency;
            NativeArray<JobHandle> jobHandles = new NativeArray<JobHandle>(_initialStatePersistRequests.Count + _applyRequests.Count, Allocator.Temp);

            for (var i = 0; i < _initialStatePersistRequests.Count; i++)
            {
                jobHandles[i] = SchedulePersist(inputDependencies, _initialStatePersistRequests[i], indexTypeHandle, archetypeIndexTypeHandle);
            }

            int applyStartIndex = _initialStatePersistRequests.Count;
            for (var i = 0; i < _applyRequests.Count; i++)
            {
                int index = applyStartIndex + i;
                jobHandles[index] = ScheduleApply(inputDependencies, _applyRequests[i], _ecbSystem, entityTypeHandle, indexTypeHandle, archetypeIndexTypeHandle);
            }
            _ecbSystem.AddJobHandleForProducer(JobHandle.CombineDependencies(jobHandles.Slice(applyStartIndex)));
            
            Dependency = JobHandle.CombineDependencies(jobHandles);

            jobHandles.Dispose();
            _initialStatePersistRequests.Clear();
            _applyRequests.Clear();
        }
    }
}