using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;
using Debug = UnityEngine.Debug;

namespace DotsPersistency
{
    // All the work this system does could theoretically just happen during conversion.
    // But that would make it so every little change (code & game object hierarchy) would need to trigger a reconvert & that doesn't scale very well.
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    [UpdateBefore(typeof(BeginFramePersistencySystem))]
    public class PersistentSceneSystem : SystemBase
    {
        private EntityQuery _initEntitiesQuery;
        private EntityQuery _sceneLoadedCheckQuery;
        public PersistentDataStorage PersistentDataStorage { get; private set; }
        protected PersistencySettings PersistencySettings;

        private EntityCommandBufferSystem _ecbSystem;
        private BeginFramePersistencySystem _beginFrameSystem;

        protected override void OnCreate()
        {
            PersistencySettings = PersistencySettings.Get();
            if (PersistencySettings == null)
            {
                Enabled = false;
                Debug.LogError(PersistencySettings.NotFoundMessage);
            }
            
            _beginFrameSystem = World.GetOrCreateSystem<BeginFramePersistencySystem>();
            PersistentDataStorage = new PersistentDataStorage(1);
            _sceneLoadedCheckQuery = GetEntityQuery(ComponentType.ReadOnly<SceneSection>());
            
            _initEntitiesQuery = GetEntityQuery(new EntityQueryDesc(){ 
                All = new [] {ComponentType.ReadOnly<PersistencyArchetype>(), ComponentType.ReadOnly<SceneSection>()},
                Options = EntityQueryOptions.IncludeDisabled
            });
            
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<RequestPersistentSceneLoaded>(), ComponentType.ReadOnly<SceneSectionData>()));
            
            _ecbSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();
        }

        protected override void OnUpdate()
        {
            EntityCommandBuffer ecb = _ecbSystem.CreateCommandBuffer();
            NativeList<SceneSection> sceneSectionsToInit = new NativeList<SceneSection>(2, Allocator.Temp);
            
            Entities.ForEach((Entity entity, ref RequestPersistentSceneLoaded requestInfo, in SceneSectionData sceneSectionData) =>
            {
                SceneSection sceneSection = new SceneSection
                {
                    Section = sceneSectionData.SubSectionIndex,
                    SceneGUID = sceneSectionData.SceneGUID
                };

                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneLoaded.Stage.Complete)
                {
                    return;
                }
                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneLoaded.Stage.WaitingForSceneLoad)
                {
                    _sceneLoadedCheckQuery.SetSharedComponentFilter(sceneSection);
                    if (_sceneLoadedCheckQuery.CalculateChunkCount() > 0)
                    {
                        sceneSectionsToInit.Add(sceneSection);
                        requestInfo.CurrentLoadingStage = RequestPersistentSceneLoaded.Stage.Complete;
                    }
                }
                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneLoaded.Stage.InitialStage)
                {
                    requestInfo.CurrentLoadingStage = RequestPersistentSceneLoaded.Stage.WaitingForContainer;
                }
                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneLoaded.Stage.WaitingForContainer && PersistentDataStorage.RequireContainer(sceneSection))
                {
                    // After the container is available actually start loading the scene
                    ecb.AddComponent(entity, new RequestSceneLoaded()
                    {
                        LoadFlags = requestInfo.LoadFlags
                    });
                    requestInfo.CurrentLoadingStage = RequestPersistentSceneLoaded.Stage.WaitingForSceneLoad;
                }
            }).WithoutBurst().Run();
            
            var uniqueSharedCompData = new List<PersistencyArchetype>();
            EntityManager.GetAllUniqueSharedComponentData(uniqueSharedCompData);
            uniqueSharedCompData.Remove(default);

            EntityCommandBuffer immediateStructuralChanges = new EntityCommandBuffer(Allocator.TempJob, PlaybackPolicy.SinglePlayback);
            foreach (var sceneSection in sceneSectionsToInit)
            {
                InitSceneSection(sceneSection, uniqueSharedCompData, immediateStructuralChanges);
            }
            immediateStructuralChanges.Playback(EntityManager);
            immediateStructuralChanges.Dispose();
        }

        public void RequestRollback(int index)
        {
            PersistentDataStorage.ToIndex(index);
            Entities.ForEach((ref RequestPersistentSceneLoaded requestInfo, in SceneSectionData sceneSectionData) =>
            {
                SceneSection sceneSection = new SceneSection{Section = sceneSectionData.SubSectionIndex, SceneGUID = sceneSectionData.SceneGUID};
                if (requestInfo.CurrentLoadingStage != RequestPersistentSceneLoaded.Stage.Complete)
                {
                    Debug.Log($"A scene section ({sceneSection.SceneGUID}, {sceneSection.Section}) was loading while a rollback was requested, the scene section won't be rolled back. TODO This scene should on load get 'rolled forward' to a recent tick on the server then the rollback can happen again to the oldest tick not only from input but from roll forward too.");
                    return;
                }
                _beginFrameSystem.RequestApply(PersistentDataStorage.GetReadContainerForCurrentIndex(sceneSection));
            }).WithoutBurst().Run();
        }
        
        public void ResetScene()
        {
            PersistentDataStorage.ToIndex(0);
            Entities.ForEach((ref RequestPersistentSceneLoaded requestInfo, in SceneSectionData sceneSectionData) =>
            {
                SceneSection sceneSection = new SceneSection{Section = sceneSectionData.SubSectionIndex, SceneGUID = sceneSectionData.SceneGUID};
                _beginFrameSystem.RequestApply(PersistentDataStorage.GetInitialState(sceneSection));
            }).WithoutBurst().Run();
        }

        private void InitSceneSection(SceneSection sceneSection, List<PersistencyArchetype> persistencyArchetypes, EntityCommandBuffer ecb)
        {
            if (PersistentDataStorage.IsSceneSectionInitialized(sceneSection))
            {
                PersistentDataContainer latestState = PersistentDataStorage.GetLatestWrittenState(sceneSection, out bool isInitial);
                for (int i = 0; i < latestState.Count; i++)
                {
                    PersistencyArchetypeDataLayout dataLayout = latestState.GetPersistenceArchetypeDataLayoutAtIndex(i);
                    _initEntitiesQuery.SetSharedComponentFilter(dataLayout.ToPersistencyArchetype(), sceneSection);
                    
                    ecb.AddSharedComponent(_initEntitiesQuery, dataLayout);
                    ecb.RemoveComponent<PersistencyArchetype>(_initEntitiesQuery);
                }

                _beginFrameSystem.RequestApply(latestState);
            }
            else
            {
                int offset = 0;
                var dataLayouts = new NativeArray<PersistencyArchetypeDataLayout>(persistencyArchetypes.Count, Allocator.Persistent);
                for (var i = 0; i < persistencyArchetypes.Count; i++)
                {
                    var persistencyArchetype = persistencyArchetypes[i];
                    _initEntitiesQuery.SetSharedComponentFilter(persistencyArchetype, sceneSection);
                    int amount = _initEntitiesQuery.CalculateEntityCount();
                    if (amount <= 0) 
                        continue;
                
                    var dataLayout = new PersistencyArchetypeDataLayout()
                    {
                        Amount = _initEntitiesQuery.CalculateEntityCount(),
                        ArchetypeIndex = i,
                        PersistedTypeInfoArrayRef = persistencyArchetype.BuildTypeInfoBlobAsset(PersistencySettings, amount, out int sizePerEntity),
                        SizePerEntity = sizePerEntity,
                        Offset = offset
                    };
                    offset += amount * sizePerEntity;

                    dataLayouts[i] = dataLayout;
                    ecb.AddSharedComponent(_initEntitiesQuery, dataLayout);
                    ecb.RemoveComponent<PersistencyArchetype>(_initEntitiesQuery);
                }

                // this function takes ownership over the archetypes array
                var initialStateContainer = PersistentDataStorage.InitializeSceneSection(sceneSection, dataLayouts);
                _beginFrameSystem.RequestInitialStatePersist(initialStateContainer);
            }
        }
        
        public void ReplaceCurrentDataStorage(PersistentDataStorage storage)
        {
            PersistentDataStorage.Dispose();
            PersistentDataStorage = storage;
        }

        protected override void OnDestroy()
        {
            PersistentDataStorage.Dispose();
        }
    }
}