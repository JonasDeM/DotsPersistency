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
    public class BeginFramePersistentDataSystem : PersistencyJobSystem
    {
        public PersistentDataStorage PersistentDataStorage { get; private set; }

        private EntityCommandBufferSystem _ecbSystem;

        private NativeList<SceneSection> _persistRequests;
        private NativeList<SceneSection> _applyRequests;
        
        protected override void OnCreate()
        {
            InitializeReadWrite(RuntimePersistableTypesInfo.Load());
            PersistentDataStorage = new PersistentDataStorage();
            _ecbSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();
            _persistRequests = new NativeList<SceneSection>(8, Allocator.Persistent);
            _applyRequests = new NativeList<SceneSection>(8, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            PersistentDataStorage.Dispose();
            _persistRequests.Dispose();
            _applyRequests.Dispose();
        }

        internal void RequestPersist(SceneSection sceneSection)
        {
            Debug.Assert(!_persistRequests.Contains(sceneSection), "Double request to persist data!");
            _persistRequests.Add(sceneSection);
        }
        
        public void RequestApply(SceneSection sceneSection)
        {
            Debug.Assert(!_applyRequests.Contains(sceneSection), "Double request to apply data!");
            _applyRequests.Add(sceneSection);
        }
        
        public void RequestApply(PersistentDataContainer dataContainer)
        {
            Debug.LogWarning("Implement me");
        }

        protected override void OnUpdate()
        {
            JobHandle inputDependencies = Dependency;
            
            foreach (var sceneSection in _persistRequests)
            {
                Dependency = JobHandle.CombineDependencies(Dependency,
                    ScheduleCopyToPersistentDataContainer(inputDependencies, sceneSection, PersistentDataStorage.GetExistingContainer(sceneSection)));
            }
            foreach (var sceneSection in _applyRequests)
            {
                Dependency = JobHandle.CombineDependencies(Dependency,
                    ScheduleApplyToSceneSection(inputDependencies, sceneSection, PersistentDataStorage.GetExistingContainer(sceneSection), _ecbSystem));
            }
            _ecbSystem.AddJobHandleForProducer(Dependency);
            
            _persistRequests.Clear();
            _applyRequests.Clear();
        }
    }
}