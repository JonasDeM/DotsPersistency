// Author: Jonas De Maeseneer

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Hash128 = Unity.Entities.Hash128;
// ReSharper disable AccessToDisposedClosure

[assembly:InternalsVisibleTo("io.jonasdem.quicksave.editor")]
[assembly:InternalsVisibleTo("io.jonasdem.dotspersistency.tests")]

namespace DotsPersistency.Hybrid
{
    [ConverterVersion("Jonas", 20)]
    [UpdateInGroup(typeof(GameObjectAfterConversionGroup))]
    public class PersistencyConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            PersistencySettings settings = PersistencySettings.Get();
            if (settings == null)
            {
                return;
            }
            
            var objectsToConvert = new List<(PersistencyAuthoring, Entity)>();
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                objectsToConvert.Add((persistencyAuthoring, GetPrimaryEntity(persistencyAuthoring)));
            });

            Convert(objectsToConvert, (settings, GetPrimaryEntity(settings)), DstEntityManager);
        }

        // static so we can test this method
        internal static void Convert(List<(PersistencyAuthoring, Entity)> objectsToConvert, (PersistencySettings, Entity) settingsAndEntity, EntityManager dstManager, Hash128 containerGUID = default)
        {
            PersistencySettings settings = settingsAndEntity.Item1;
            if (settings == null)
            {
                return;
            }
            
            List<BlobCreationInfo> blobCreationInfo = new List<BlobCreationInfo>(16);
            HashSet<PersistableTypeHandle> allUniqueTypeHandles = new HashSet<PersistableTypeHandle>();
            Dictionary<Hash128, ushort> hashToArchetypeIndexInContainer = new Dictionary<Hash128, ushort>(16);
            
            // to avoid tons of GC allocations
            List<PersistableTypeHandle> typeHandleList = new List<PersistableTypeHandle>(16);

            foreach (var pair in objectsToConvert)
            {
                PersistencyAuthoring persistencyAuthoring = pair.Item1;
                Entity e = pair.Item2;
                
                if (containerGUID == default)
                {
                    containerGUID = GetSceneGUID(persistencyAuthoring.gameObject);
                }

                // Gather info for own components & persistencySceneInfo entity
                typeHandleList.Clear();
                persistencyAuthoring.GetConversionInfo(settings, typeHandleList, out int indexInScene, out Hash128 typeCombinationHash);
                if (!hashToArchetypeIndexInContainer.ContainsKey(typeCombinationHash))
                {
                    Debug.Assert(blobCreationInfo.Count < ushort.MaxValue);
                    hashToArchetypeIndexInContainer.Add(typeCombinationHash, (ushort)blobCreationInfo.Count);
                    blobCreationInfo.Add(new BlobCreationInfo()
                    {
                        AmountEntities = 0,
                        PersistableTypeHandles = new List<PersistableTypeHandle>(typeHandleList),
                        PersistableTypeHandleCombinationHash = typeCombinationHash
                    });
                    foreach (var persistableTypeHandle in typeHandleList)
                    {
                        allUniqueTypeHandles.Add(persistableTypeHandle);
                    }
                }
                
                ushort archetypeIndexInContainer = hashToArchetypeIndexInContainer[typeCombinationHash];
                
                // Add components that an entity needs to be persistable: What to store & Where to store it
                dstManager.AddComponentData(e, new PersistencyArchetypeIndexInContainer()
                {
                    Index = archetypeIndexInContainer
                });
                dstManager.AddComponentData(e, new PersistenceState()
                {
                    ArrayIndex = indexInScene
                });
                dstManager.AddSharedComponentData(e, new PersistableTypeCombinationHash()
                {
                    Value = typeCombinationHash
                });
                dstManager.AddSharedComponentData(e, new PersistencyContainerTag()
                {
                    DataIdentifier = containerGUID
                });

                blobCreationInfo[archetypeIndexInContainer].AmountEntities += 1;
            }
            
            // Create the entity that contains all the info the PersistentSceneSystem needs for initializing a loaded scene
            Entity scenePersistencyInfoEntity = settingsAndEntity.Item2;
            dstManager.SetName(scenePersistencyInfoEntity, nameof(ScenePersistencyInfoRef));
            
            if (blobCreationInfo.Count == 0) // there was nothing converted
            {
                return;
            }
            
            List<PersistableTypeHandle> allUniqueTypeHandlesSorted = allUniqueTypeHandles.ToList();
            allUniqueTypeHandlesSorted.Sort((left, right) => left.Handle.CompareTo(right.Handle));

            ScenePersistencyInfoRef scenePersistencyInfoRef = new ScenePersistencyInfoRef();
            using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref ScenePersistencyInfo scenePersistencyInfo = ref blobBuilder.ConstructRoot<ScenePersistencyInfo>();

                BlobBuilderArray<PersistableTypeHandle> blobBuilderArray1 = blobBuilder.Allocate(ref scenePersistencyInfo.AllUniqueTypeHandles, allUniqueTypeHandlesSorted.Count);
                for (int i = 0; i < blobBuilderArray1.Length; i++)
                {
                    blobBuilderArray1[i] = allUniqueTypeHandlesSorted[i];
                }
                
                BlobBuilderArray<PersistencyArchetype> blobBuilderArray2 = blobBuilder.Allocate(ref scenePersistencyInfo.PersistencyArchetypes, blobCreationInfo.Count);
                for (int i = 0; i < blobCreationInfo.Count; i++)
                {
                    BlobCreationInfo info = blobCreationInfo[i];
                    ref PersistencyArchetype persistencyArchetype = ref blobBuilderArray2[i];
                    persistencyArchetype.AmountEntities = info.AmountEntities;
                    persistencyArchetype.PersistableTypeHandleCombinationHash = info.PersistableTypeHandleCombinationHash;
                    BlobBuilderArray<PersistableTypeHandle> blobBuilderArray3 = blobBuilder.Allocate(ref persistencyArchetype.PersistableTypeHandles, info.PersistableTypeHandles.Count);

                    for (int j = 0; j < info.PersistableTypeHandles.Count; j++)
                    {
                        blobBuilderArray3[j] = info.PersistableTypeHandles[j];
                    }
                }

                scenePersistencyInfoRef.InfoRef = blobBuilder.CreateBlobAssetReference<ScenePersistencyInfo>(Allocator.Persistent);
            }
            
            dstManager.AddComponentData(scenePersistencyInfoEntity, scenePersistencyInfoRef);
            dstManager.AddSharedComponentData(scenePersistencyInfoEntity, new PersistencyContainerTag()
            {
                DataIdentifier = containerGUID
            });

            if (settings.VerboseConversionLog)
            {
                VerboseLog(settings, scenePersistencyInfoRef);
            }
        }

        private static Hash128 GetSceneGUID(GameObject gameObject)
        {
#if UNITY_EDITOR
            return new Hash128(AssetDatabase.AssetPathToGUID(gameObject.scene.path));
#else
            throw new NotSupportedException("Runtime conversion is not supported!");
#endif
        }
        
        private class BlobCreationInfo
        {
            public List<PersistableTypeHandle> PersistableTypeHandles;
            public Hash128 PersistableTypeHandleCombinationHash;
            public int AmountEntities;
        }

        [Conditional("UNITY_EDITOR")]
        private static void VerboseLog(PersistencySettings settings, ScenePersistencyInfoRef scenePersistencyInfoRef)
        {
            ref ScenePersistencyInfo a = ref scenePersistencyInfoRef.InfoRef.Value;
            for (int i = 0; i < a.PersistencyArchetypes.Length; i++)
            {
                Debug.Log($"---Archetype{i.ToString()}---");
                Debug.Log(a.PersistencyArchetypes[i].PersistableTypeHandleCombinationHash.ToString());
                Debug.Log(a.PersistencyArchetypes[i].AmountEntities.ToString());
                for (int j = 0; j < a.PersistencyArchetypes[i].PersistableTypeHandles.Length; j++)
                {
                    Debug.Log(ComponentType.FromTypeIndex(settings.GetTypeIndex(a.PersistencyArchetypes[i].PersistableTypeHandles[j])).ToString());
                }
            }
            Debug.Log("------AllUniqueTypes------");
            for (int i = 0; i < a.AllUniqueTypeHandles.Length; i++)
            {
                Debug.Log(ComponentType.FromTypeIndex(settings.GetTypeIndex(a.AllUniqueTypeHandles[i])).ToString());
            }
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
