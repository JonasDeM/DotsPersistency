using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        private EntityQuery _sceneSectionLoadedCheckQuery;
        private EntityQuery _scenePersistencyInfoQuery;
        
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
                Debug.Log(PersistencySettings.NotFoundMessage);
            }
            PersistentDataStorage = new PersistentDataStorage(1);
            
            _beginFrameSystem = World.GetOrCreateSystem<BeginFramePersistencySystem>();
            _ecbSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();

            _initEntitiesQuery = GetEntityQuery(new EntityQueryDesc(){ 
                All = new [] {ComponentType.ReadOnly<PersistableTypeCombinationHash>(), ComponentType.ReadOnly<PersistencyContainerTag>()},
                None = new []{ ComponentType.ReadOnly<PersistencyArchetypeDataLayout>() },
                Options = EntityQueryOptions.IncludeDisabled
            });
            _scenePersistencyInfoQuery = GetEntityQuery(ComponentType.ReadOnly<ScenePersistencyInfoRef>(), ComponentType.ReadOnly<PersistencyContainerTag>());
            _sceneSectionLoadedCheckQuery = GetEntityQuery(ComponentType.ReadOnly<SceneSection>());
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<RequestPersistentSceneSectionLoaded>(), ComponentType.ReadOnly<SceneSectionData>(), ComponentType.Exclude<PersistentSceneSectionLoadComplete>()));
        }

        protected override void OnUpdate()
        {
            UpdateSceneLoading(out NativeList<SceneSection> sceneSectionsToInit);

            if (sceneSectionsToInit.Length == 0)
            {
                return;
            }
            
            EntityCommandBuffer immediateStructuralChanges = new EntityCommandBuffer(Allocator.TempJob, PlaybackPolicy.SinglePlayback);

            for (int i = 0; i < sceneSectionsToInit.Length; i++)
            {
                InitSceneSection(sceneSectionsToInit[i], immediateStructuralChanges);
            }
            
            // Clean up any init-only data
            _scenePersistencyInfoQuery.ResetFilter();
            _initEntitiesQuery.ResetFilter();
            immediateStructuralChanges.DestroyEntity(_scenePersistencyInfoQuery);
            immediateStructuralChanges.RemoveComponent<PersistableTypeCombinationHash>(_initEntitiesQuery);
            
            // Do the structural changes immediately since BeginFramePersistencySystem needs to to the initial persist
            immediateStructuralChanges.Playback(EntityManager);
            
            immediateStructuralChanges.Dispose();
            sceneSectionsToInit.Dispose();
        }

        public void UpdateSceneLoading(out NativeList<SceneSection> sceneSectionsToInit)
        {
            NativeList<SceneSection> completeSceneSections = new NativeList<SceneSection>(4, Allocator.Temp);
            
            EntityCommandBuffer ecb = _ecbSystem.CreateCommandBuffer();
            Entities.WithNone<PersistentSceneSectionLoadComplete>().ForEach((Entity entity, ref RequestPersistentSceneSectionLoaded requestInfo, in SceneSectionData sceneSectionData) =>
            {
                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneSectionLoaded.Stage.Complete)
                {
                    return;
                }
                
                SceneSection sceneSection = new SceneSection
                {
                    Section = sceneSectionData.SubSectionIndex,
                    SceneGUID = sceneSectionData.SceneGUID
                };
                
                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneSectionLoaded.Stage.InitialStage)
                {
                    ecb.AddComponent<PersistentSceneSection>(entity);
                    requestInfo.CurrentLoadingStage = RequestPersistentSceneSectionLoaded.Stage.WaitingForContainer;
                }
                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneSectionLoaded.Stage.WaitingForContainer && PersistentDataStorage.RequireContainer(sceneSection.SceneGUID))
                {
                    // After the container is available actually start loading the scene
                    ecb.AddComponent(entity, new RequestSceneLoaded()
                    {
                        LoadFlags = requestInfo.LoadFlags
                    });
                    requestInfo.CurrentLoadingStage = RequestPersistentSceneSectionLoaded.Stage.WaitingForSceneLoad;
                }
                if (requestInfo.CurrentLoadingStage == RequestPersistentSceneSectionLoaded.Stage.WaitingForSceneLoad)
                {
                    // Pls make SceneSectionStreamingSystem.StreamingState public :) @unity
                    _sceneSectionLoadedCheckQuery.SetSharedComponentFilter(sceneSection);
                    if (_sceneSectionLoadedCheckQuery.CalculateChunkCount() > 0)
                    {
                        completeSceneSections.Add(sceneSection);
                        ecb.AddComponent<PersistentSceneSectionLoadComplete>(entity);
                        requestInfo.CurrentLoadingStage = RequestPersistentSceneSectionLoaded.Stage.Complete;
                    }
                }
            }).WithoutBurst().Run();

            _sceneSectionLoadedCheckQuery.ResetFilter();
            sceneSectionsToInit = completeSceneSections;
        }

        private unsafe void InitSceneSection(SceneSection sceneSection, EntityCommandBuffer ecb)
        {
            Hash128 containerIdentifier = sceneSection.SceneGUID;
            PersistencyContainerTag containerTag = new PersistencyContainerTag() {DataIdentifier = containerIdentifier};
            
            if (PersistentDataStorage.IsInitialized(containerIdentifier))
            {
                PersistentDataContainer latestState = PersistentDataStorage.GetLatestWrittenState(containerIdentifier, out bool isInitial);
                for (int i = 0; i < latestState.DataLayoutCount; i++)
                {
                    PersistencyArchetypeDataLayout dataLayout = latestState.GetDataLayoutAtIndex(i);
                    _initEntitiesQuery.SetSharedComponentFilter(new PersistableTypeCombinationHash {Value = dataLayout.PersistableTypeHandleCombinationHash}, containerTag);
                    
                    ecb.AddSharedComponent(_initEntitiesQuery, dataLayout);
                }

                _beginFrameSystem.RequestApply(latestState);
            }
            else
            {
                _scenePersistencyInfoQuery.SetSharedComponentFilter(containerTag);
                var scenePersistencyInfoRef = _scenePersistencyInfoQuery.GetSingleton<ScenePersistencyInfoRef>();
                ref ScenePersistencyInfo sceneInfo = ref scenePersistencyInfoRef.InfoRef.Value; 
                
                int offset = 0;
                var dataLayouts = new NativeArray<PersistencyArchetypeDataLayout>(sceneInfo.PersistencyArchetypes.Length, Allocator.Persistent);
                for (var i = 0; i < sceneInfo.PersistencyArchetypes.Length; i++)
                {
                    ref PersistencyArchetype persistencyArchetype = ref sceneInfo.PersistencyArchetypes[i];

                    var hashFilter = new PersistableTypeCombinationHash
                    {
                        Value = persistencyArchetype.PersistableTypeHandleCombinationHash
                    };
                    
                    _initEntitiesQuery.SetSharedComponentFilter(hashFilter, containerTag);
                    
                    var dataLayout = new PersistencyArchetypeDataLayout()
                    {
                        Amount = persistencyArchetype.AmountEntities,
                        ArchetypeIndexInContainer = (ushort)i,
                        PersistedTypeInfoArrayRef = persistencyArchetype.BuildTypeInfoBlobAsset(PersistencySettings, persistencyArchetype.AmountEntities, out int sizePerEntity),
                        SizePerEntity = sizePerEntity,
                        Offset = offset,
                        PersistableTypeHandleCombinationHash = hashFilter.Value
                    };
                    offset += persistencyArchetype.AmountEntities * sizePerEntity;

                    dataLayouts[i] = dataLayout;
                    ecb.AddSharedComponent(_initEntitiesQuery, dataLayout);
                }

                // This also implicitly calculates the amount of component data types
                int amountUniqueBufferTypes = 0;
                for (int i = 0; i < sceneInfo.AllUniqueTypeHandles.Length; i++)
                {
                    if (PersistencySettings.IsBufferType(sceneInfo.AllUniqueTypeHandles[i]))
                    {
                        amountUniqueBufferTypes += 1;
                    }
                }

                // BlobData is disposed if the owning entity is destroyed, we need this data even if the scene is unloaded
                NativeArray<PersistableTypeHandle> allUniqueTypeHandles = new NativeArray<PersistableTypeHandle>(sceneInfo.AllUniqueTypeHandles.Length, Allocator.Persistent);
                UnsafeUtility.MemCpy(allUniqueTypeHandles.GetUnsafePtr(), sceneInfo.AllUniqueTypeHandles.GetUnsafePtr(), allUniqueTypeHandles.Length * sizeof(PersistableTypeHandle));
                
                // this function takes ownership over the dataLayouts array & allUniqueTypeHandles array
                PersistentDataContainer initialStateContainer = PersistentDataStorage.InitializeScene(containerIdentifier, dataLayouts, allUniqueTypeHandles, amountUniqueBufferTypes);
                _beginFrameSystem.RequestInitialStatePersist(initialStateContainer);
            }
        }
        
        public void RequestRollback(int storageIndex)
        {
            PersistentDataStorage.ToIndex(storageIndex);
            Entities.ForEach((in SceneReference sceneReference, in DynamicBuffer<ResolvedSectionEntity> sceneSections) =>
            {
                for (int i = 0; i < sceneSections.Length; i++)
                {
                    Entity sceneSectionEntity = sceneSections[i].SectionEntity;
                    if (HasComponent<PersistentSceneSectionLoadComplete>(sceneSectionEntity))
                    {
                        _beginFrameSystem.RequestApply(PersistentDataStorage.GetReadContainerForCurrentIndex(sceneReference.SceneGUID));
                        return;
                    }
                }
            }).WithoutBurst().Run();
        }
        
        public void RequestFullReset()
        {
            PersistentDataStorage.ToIndex(0);
            
            Entities.ForEach((in SceneReference sceneReference, in DynamicBuffer<ResolvedSectionEntity> sceneSections) =>
            {
                for (int i = 0; i < sceneSections.Length; i++)
                {
                    Entity sceneSectionEntity = sceneSections[i].SectionEntity;
                    if (HasComponent<PersistentSceneSectionLoadComplete>(sceneSectionEntity))
                    {
                        _beginFrameSystem.RequestApply(PersistentDataStorage.GetInitialState(sceneReference.SceneGUID));
                        return;
                    }
                }
            }).WithoutBurst().Run();
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