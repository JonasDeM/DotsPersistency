using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Scenes;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DotsPersistency
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(SceneSystemGroup))]
    [UpdateBefore(typeof(BeginFramePersistentDataSystem))]
    public class PersistenceInitializationSystem : SystemBase
    {
        private EntityQuery _initEntitiesQuery;
        private EntityQuery _sceneLoadedCheckQuery;
        public PersistentDataStorage PersistentDataStorage { get; private set; }

        private EntityCommandBufferSystem _ecbSystem;
        private BeginFramePersistentDataSystem _beginFrameSystem;

        protected override void OnCreate()
        {
            _beginFrameSystem = World.GetOrCreateSystem<BeginFramePersistentDataSystem>();
            PersistentDataStorage = new PersistentDataStorage(1);
            _sceneLoadedCheckQuery = GetEntityQuery(ComponentType.ReadOnly<SceneSection>());
            
            _initEntitiesQuery = GetEntityQuery(new EntityQueryDesc(){ 
                All = new [] {ComponentType.ReadOnly<TypeHashesToPersist>(), ComponentType.ReadOnly<SceneSection>()},
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
            
            var uniqueSharedCompData = new List<TypeHashesToPersist>();
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

        private void InitSceneSection(SceneSection sceneSection, List<TypeHashesToPersist> typeHashes, EntityCommandBuffer ecb)
        {
            if (PersistentDataStorage.IsSceneSectionInitialized(sceneSection))
            {
                PersistentDataContainer latestState = PersistentDataStorage.GetLatestWrittenState(sceneSection, out bool isInitial);
                for (int i = 0; i < latestState.Count; i++)
                {
                    PersistenceArchetype archetype = latestState.GetPersistenceArchetypeAtIndex(i);
                    _initEntitiesQuery.SetSharedComponentFilter(archetype.ToHashList(), sceneSection);
                    
                    ecb.AddSharedComponent(_initEntitiesQuery, archetype);
                    ecb.RemoveComponent<TypeHashesToPersist>(_initEntitiesQuery);
                }

                _beginFrameSystem.RequestApply(latestState);
            }
            else
            {
                int offset = 0;
                var archetypes = new NativeArray<PersistenceArchetype>(typeHashes.Count, Allocator.Persistent);
                for (var i = 0; i < typeHashes.Count; i++)
                {
                    var typeHashesToPersist = typeHashes[i];
                    _initEntitiesQuery.SetSharedComponentFilter(typeHashesToPersist, sceneSection);
                    int amount = _initEntitiesQuery.CalculateEntityCount();
                    if (amount <= 0) 
                        continue;
                
                    var persistenceArchetype = new PersistenceArchetype()
                    {
                        Amount = _initEntitiesQuery.CalculateEntityCount(),
                        ArchetypeIndex = i,
                        PersistedTypeInfoArrayRef = BuildTypeInfoBlobAsset(typeHashesToPersist.TypeHashList, amount, out int sizePerEntity),
                        SizePerEntity = sizePerEntity,
                        Offset = offset
                    };
                    offset += amount * sizePerEntity;

                    archetypes[i] = persistenceArchetype;
                    ecb.AddSharedComponent(_initEntitiesQuery, persistenceArchetype);
                    ecb.RemoveComponent<TypeHashesToPersist>(_initEntitiesQuery);
                }

                // this function takes ownership over the archetypes array
                var initialStateContainer = PersistentDataStorage.InitializeSceneSection(sceneSection, archetypes);
                _beginFrameSystem.RequestInitialStatePersist(initialStateContainer);
            }
        }

        internal static BlobAssetReference<BlobArray<PersistedTypeInfo>> BuildTypeInfoBlobAsset(FixedList128<ulong> stableTypeHashes, int amountEntities, out int sizePerEntity)
        {
            BlobAssetReference<BlobArray<PersistedTypeInfo>> blobAssetReference;
            int currentOffset = 0;
            sizePerEntity = 0;
            
            using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref BlobArray<PersistedTypeInfo> blobArray = ref blobBuilder.ConstructRoot<BlobArray<PersistedTypeInfo>>();

                var blobBuilderArray = blobBuilder.Allocate(ref blobArray, stableTypeHashes.Length);

                for (int i = 0; i < blobBuilderArray.Length; i++)
                {
                    var typeInfo = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHashes[i]));

                    int maxElements = typeInfo.Category == TypeManager.TypeCategory.BufferData ? typeInfo.BufferCapacity : 1;

                    ValidateType(typeInfo);
                    
                    blobBuilderArray[i] = new PersistedTypeInfo()
                    {
                        StableHash = stableTypeHashes[i],
                        ElementSize = typeInfo.ElementSize,
                        IsBuffer = typeInfo.Category == TypeManager.TypeCategory.BufferData,
                        MaxElements = maxElements,
                        Offset = currentOffset
                    };
                    int sizeForComponent = (typeInfo.ElementSize * maxElements) + sizeof(ushort); // PersistenceMetaData is one ushort
                    sizePerEntity += sizeForComponent; 
                    currentOffset += sizeForComponent * amountEntities;
                }

                blobAssetReference = blobBuilder.CreateBlobAssetReference<BlobArray<PersistedTypeInfo>>(Allocator.Persistent);
            }
            
            return blobAssetReference;
        }

        [Conditional("DEBUG")]
        private static void ValidateType(TypeManager.TypeInfo typeInfo)
        {
            if (TypeManager.HasEntityReferences(typeInfo.TypeIndex))
            {
                Debug.LogWarning($"Persisting components with Entity References is not supported. Type: {ComponentType.FromTypeIndex(typeInfo.TypeIndex)}");
            }

            if (typeInfo.BlobAssetRefOffsetCount > 0)
            {
                Debug.LogWarning($"Persisting components with BlobAssetReferences is not supported. Type: {ComponentType.FromTypeIndex(typeInfo.TypeIndex)}");
            }
        }
        
        public void ReplaceCurrentDataStorage(PersistentDataStorage storage)
        {
            PersistentDataStorage.Dispose();
            PersistentDataStorage = storage;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            PersistentDataStorage.Dispose();
        }
    }
}