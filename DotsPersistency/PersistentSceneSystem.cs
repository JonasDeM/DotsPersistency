// Author: Jonas De Maeseneer

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Scenes;
using Debug = UnityEngine.Debug;

namespace DotsPersistency
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    [UpdateBefore(typeof(BeginFramePersistencySystem))]
    public class PersistentSceneSystem : SystemBase
    {
        private EntityQuery _sceneSectionLoadedCheckQuery;
        private EntityQuery _scenePersistencyInfoQuery;
        private EntityQuery _resizeContainerDependencyQuery;
        
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
            
            _scenePersistencyInfoQuery = GetEntityQuery(ComponentType.ReadOnly<ScenePersistencyInfoRef>(), ComponentType.ReadOnly<PersistencyContainerTag>());
            _sceneSectionLoadedCheckQuery = GetEntityQuery(ComponentType.ReadOnly<SceneSection>());
            _resizeContainerDependencyQuery = GetEntityQuery(ComponentType.ReadOnly<PersistenceState>());
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<RequestPersistentSceneSectionLoaded>(), ComponentType.ReadOnly<SceneSectionData>(), ComponentType.Exclude<PersistentSceneSectionLoadComplete>()));
        }

        protected override void OnUpdate()
        {
            UpdateSceneLoading(out NativeList<SceneSection> sceneSectionsToInit);

            if (sceneSectionsToInit.Length == 0)
            {
                return;
            }
            
            for (int i = 0; i < sceneSectionsToInit.Length; i++)
            {
                InitSceneSection(sceneSectionsToInit[i]);
            }
        }

        private void UpdateSceneLoading(out NativeList<SceneSection> sceneSectionsToInit)
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

        private unsafe void InitSceneSection(SceneSection sceneSection)
        {
            Hash128 containerIdentifier = sceneSection.SceneGUID;
            PersistencyContainerTag containerTag = new PersistencyContainerTag() {DataIdentifier = containerIdentifier};
            
            if (PersistentDataStorage.IsInitialized(containerIdentifier))
            {
                PersistentDataContainer latestState = PersistentDataStorage.GetReadContainerForLatestWriteIndex(containerIdentifier, out bool isInitial);
                _beginFrameSystem.RequestApply(latestState);
            }
            else
            {
                _scenePersistencyInfoQuery.SetSharedComponentFilter(containerTag);
                if (_scenePersistencyInfoQuery.CalculateEntityCount() != 1)
                {
                    return;
                }
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
                }

                // BlobData is disposed if the owning entity is destroyed, we need this data even if the scene is unloaded
                NativeArray<PersistableTypeHandle> allUniqueTypeHandles = new NativeArray<PersistableTypeHandle>(sceneInfo.AllUniqueTypeHandles.Length, Allocator.Persistent);
                UnsafeUtility.MemCpy(allUniqueTypeHandles.GetUnsafePtr(), sceneInfo.AllUniqueTypeHandles.GetUnsafePtr(), allUniqueTypeHandles.Length * sizeof(PersistableTypeHandle));
                
                // this function takes ownership over the dataLayouts array & allUniqueTypeHandles array
                PersistentDataStorage.InitializeSceneContainer(containerIdentifier, dataLayouts, allUniqueTypeHandles,
                    out PersistentDataContainer initialStateContainer, out PersistentDataContainer containerToApply);
                _beginFrameSystem.RequestInitialStatePersist(initialStateContainer);
                if (containerToApply.IsCreated)
                {
                    // containerToApply only exists if it was already added as uninitialized raw data beforehand (e.g. read from disk)
                    _beginFrameSystem.RequestApply(containerToApply);
                }
            }
        }
        
        // This method takes ownership of the typeHandles NativeArray
        public void InitializePool(Entity prefabEntity, int amount, Hash128 identifier, NativeArray<PersistableTypeHandle> typeHandles)
        {
            Debug.Assert(amount > 0);
            PersistentDataStorage.InitializePool(prefabEntity, identifier, typeHandles, _beginFrameSystem);
            PersistentDataStorage.ChangeEntityCapacity(identifier, amount);
        }
        
        public void InstantChangePoolCapacity(Hash128 identifier, int minSize)
        {
            _resizeContainerDependencyQuery.CompleteDependency();
            PersistentDataStorage.ChangeEntityCapacity(identifier, minSize);
        }

        public void RequestApplyAllLoadedScenes(int storageIndex)
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
        
        public void RequestApplyInitialToAllLoadedScenes()
        {
            PersistentDataStorage.ToIndex(0);
            
            Entities.ForEach((in SceneReference sceneReference, in DynamicBuffer<ResolvedSectionEntity> sceneSections) =>
            {
                for (int i = 0; i < sceneSections.Length; i++)
                {
                    Entity sceneSectionEntity = sceneSections[i].SectionEntity;
                    if (HasComponent<PersistentSceneSectionLoadComplete>(sceneSectionEntity))
                    {
                        _beginFrameSystem.RequestApply(PersistentDataStorage.GetInitialStateReadContainer(sceneReference.SceneGUID));
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
        
#if UNITY_INCLUDE_TESTS
        internal void ReplaceSettings(PersistencySettings settings)
        {
            PersistencySettings = settings;
        }
#endif
    }
}