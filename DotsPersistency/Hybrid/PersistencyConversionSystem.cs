// Author: Jonas De Maeseneer

using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;
// ReSharper disable AccessToDisposedClosure

[assembly:InternalsVisibleTo("io.jonasdem.quicksave.editor")]

namespace DotsPersistency.Hybrid
{
    [ConverterVersion("Jonas", 15)]
    [UpdateInGroup(typeof(GameObjectAfterConversionGroup))]
    public class PersistencyConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            var settings = PersistencySettings.Get();
            if (settings == null)
            {
                return;
            }
            
            Hash128 sceneGUID = ForkSettings(1).SceneGUID;
            NativeList<PersistencyArchetype> allPersistencyArchetypes = new NativeList<PersistencyArchetype>(16, Allocator.Temp);
            NativeHashMap<Hash128, ushort> hashToArchetypeIndexInContainer = new NativeHashMap<Hash128, ushort>(16, Allocator.Temp);

            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                if (sceneGUID == default)
                {
                    sceneGUID = GetSceneGUID(persistencyAuthoring.gameObject);
                }

                Entity e = GetPrimaryEntity(persistencyAuthoring);
                
                // Gather info for own components & persistencySceneInfo entity
                var persistableTypeHandles = persistencyAuthoring.GetPersistableTypeHandles(settings);
                Hash128 typeCombinationHash = settings.GetPersistableTypeHandleCombinationHash(persistableTypeHandles);
                if (!hashToArchetypeIndexInContainer.ContainsKey(typeCombinationHash))
                {
                    Debug.Assert(allPersistencyArchetypes.Length < ushort.MaxValue);
                    hashToArchetypeIndexInContainer.Add(typeCombinationHash, (ushort)allPersistencyArchetypes.Length);
                    allPersistencyArchetypes.Add(new PersistencyArchetype()
                    {
                        Amount = 0,
                        PersistableTypeHandles = persistableTypeHandles,
                        PersistableTypeHandleCombinationHash = typeCombinationHash
                    });
                }
                
                ushort archetypeIndexInContainer = hashToArchetypeIndexInContainer[typeCombinationHash];
                PersistencyArchetype persistencyArchetype = allPersistencyArchetypes[archetypeIndexInContainer];
                
                // Add components that an entity needs to be persistable: What to store & Where to store it
                DstEntityManager.AddComponentData(e, new PersistencyArchetypeIndexInContainer()
                {
                    Index = archetypeIndexInContainer
                });
                DstEntityManager.AddComponentData(e, new PersistenceState()
                {
                    ArrayIndex = persistencyAuthoring.CalculateArrayIndex(settings)
                });
                DstEntityManager.AddSharedComponentData(e, new PersistableTypeCombinationHash()
                {
                    Value = typeCombinationHash
                });
                DstEntityManager.AddSharedComponentData(e, new PersistencyContainerTag()
                {
                    DataIdentifier = sceneGUID
                });

                persistencyArchetype.Amount += 1;
                allPersistencyArchetypes[archetypeIndexInContainer] = persistencyArchetype;
            });

            if (sceneGUID == default) // there was nothing converted
            {
                return;
            }

            // Create the entity that contains all the info PersistentSceneSystem needs for initializing a loaded scene
            Entity scenePersistencyInfoEntity = GetPrimaryEntity(settings);
            DstEntityManager.SetName(scenePersistencyInfoEntity, nameof(ScenePersistencyInfo));
            ScenePersistencyInfo scenePersistencyInfo = new ScenePersistencyInfo();
            using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref BlobArray<PersistencyArchetype> blobArray = ref blobBuilder.ConstructRoot<BlobArray<PersistencyArchetype>>();

                var blobBuilderArray = blobBuilder.Allocate(ref blobArray, allPersistencyArchetypes.Length);

                for (int i = 0; i < blobBuilderArray.Length; i++)
                {
                    blobBuilderArray[i] = allPersistencyArchetypes[i];
                }

                scenePersistencyInfo.PersistencyArchetypes = blobBuilder.CreateBlobAssetReference<BlobArray<PersistencyArchetype>>(Allocator.Persistent);
            }
            DstEntityManager.AddComponentData(scenePersistencyInfoEntity, scenePersistencyInfo);
            DstEntityManager.AddSharedComponentData(scenePersistencyInfoEntity, new PersistencyContainerTag()
            {
                DataIdentifier = sceneGUID
            });
            
            allPersistencyArchetypes.Dispose();
            hashToArchetypeIndexInContainer.Dispose();
        }

        private static Hash128 GetSceneGUID(GameObject gameObject)
        {
#if UNITY_EDITOR
            return new Hash128(AssetDatabase.AssetPathToGUID(gameObject.scene.path));
#else
            throw new NotSupportedException("Runtime conversion is not supported!");
#endif
        }
    }
    
    [ConverterVersion("Jonas", 15)]
    [UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup))]
    public class PersistencyConversionDependencySystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            var settings = PersistencySettings.Get();
            DeclareReferencedAsset(settings);
            if (settings == null)
            {
                return;
            }
            
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                DeclareAssetDependency(persistencyAuthoring.gameObject, settings);
                Entities.ForEach((PersistencyAuthoring other) =>
                {
                    if (persistencyAuthoring != other)
                    {
                        DeclareDependency(persistencyAuthoring, other);
                    }
                });
            });
        }
    }
}
