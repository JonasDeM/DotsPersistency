// Author: Jonas De Maeseneer

using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency.Hybrid
{
    [ConverterVersion("Jonas", 3)]
    [UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup))]
    public class PersistencyConversionReferenceSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                Entities.ForEach((PersistencyAuthoring other) =>
                {
                    if (persistencyAuthoring.GetStablePersistenceArchetypeHash() == other.GetStablePersistenceArchetypeHash()
                        && persistencyAuthoring != other)
                    {
                        DeclareDependency(persistencyAuthoring, other);
                    }
                });
            });
        }
    }
    
    [UpdateInGroup(typeof(GameObjectAfterConversionGroup))]
    public class MyPersistencyConversionSystem : PersistencyConversionSystem
    {
        protected override void OnUpdate()
        {
            Convert();
        }
    }

    [UpdateInGroup(typeof(GameObjectAfterConversionGroup))]
    public abstract class PersistencyConversionSystem : GameObjectConversionSystem
    {
        protected void Convert()
        {
            List<SceneSection> sections = new List<SceneSection>();
            DstEntityManager.GetAllUniqueSharedComponentData(sections);
            var sceneGUID = sections.Select(section => section.SceneGUID).First(guid => guid != new Hash128());

            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                Entity e = GetPrimaryEntity(persistencyAuthoring);
                DstEntityManager.AddSharedComponentData(e, new TypeHashesToPersist()
                {
                    TypeHashList = persistencyAuthoring.GetPersistingTypeHashes()
                });
                DstEntityManager.AddComponentData(e, new PersistenceState()
                {
                    ArrayIndex = persistencyAuthoring.CalculateArrayIndex()
                });
            });
        }

        // Don't use this unless you know what consequences it has
        // This only works with a non-incremental convert:
        // This conversion would need to do a non-incremental convert every time:
        //                                      - 1 type is changed (Not yet found a way on how to achieve this)
        //                                      - 1 PersistencyAuthoring is moved in the hierarchy (Just make every PersistencyAuthoring depend on each other)
        // For this to be able to be used during development I would need to find a way to disable incremental convert for PersistencyAuthoring & make this conversion depend on the types the type hashes represent
        // Currently this could be used as a potential optimization before a release build, you can then also leave out any call to PersistenceArchetypeSystem.RequestInitSceneSection
        protected void ConvertExperimental()
        {
            var hashMap = new NativeHashMap<Hash128, PersistenceArchetype>();

            CreatePersistenceArchetypes(hashMap);
            
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                Entity e = GetPrimaryEntity(persistencyAuthoring);
                DstEntityManager.AddSharedComponentData(e, hashMap[persistencyAuthoring.GetStablePersistenceArchetypeHash()]);
                DstEntityManager.AddComponentData(e, new PersistenceState()
                {
                    ArrayIndex = persistencyAuthoring.CalculateArrayIndex()
                });
            });

            hashMap.Dispose();
        }
        
        private void CreatePersistenceArchetypes(NativeHashMap<Hash128, PersistenceArchetype> hashMap)
        {
            int archetypeIndex = 0;
            var hashToStableTypeHashes = new Dictionary<Hash128, FixedList128<ulong>>(8);
            
            List<SceneSection> sections = new List<SceneSection>();
            DstEntityManager.GetAllUniqueSharedComponentData(sections);
            var sceneGUID = sections.Select(section => section.SceneGUID).First(guid => guid != new Hash128());
            
            // Phase 1: Calculate PersistenceArchetypeIndices & Amount Entities per PersistenceArchetype
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                var persistenceArchetypeHash = persistencyAuthoring.GetStablePersistenceArchetypeHash();
                if (!hashMap.TryGetValue(persistenceArchetypeHash, out var persistenceArchetype))
                {
                    persistenceArchetype = new PersistenceArchetype()
                    {
                        Amount = 0,
                        ArchetypeIndex = archetypeIndex
                    };
                    archetypeIndex++;
                    hashMap.Add(persistenceArchetypeHash, persistenceArchetype);
                    hashToStableTypeHashes.Add(persistenceArchetypeHash, persistencyAuthoring.GetPersistingTypeHashes());
                }

                persistenceArchetype.Amount += 1;
                hashMap[persistenceArchetypeHash] = persistenceArchetype;
            });
            
            // Phase 2: Calculate Byte Offset Per Type into the PersistenceArchetype SubArray
            var keyArray = hashMap.GetKeyArray(Allocator.Temp);
            var valueArray = hashMap.GetValueArray(Allocator.Temp);
            foreach (var keyToUpdate in keyArray)
            {
                var archetypeToUpdate = hashMap[keyToUpdate];

                archetypeToUpdate.PersistedTypeInfoArrayRef = PersistenceInitializationSystem.BuildTypeInfoBlobAsset(hashToStableTypeHashes[keyToUpdate], archetypeToUpdate.Amount, out int sizePerEntity);
                archetypeToUpdate.SizePerEntity = sizePerEntity;
                
                hashMap[keyToUpdate] = archetypeToUpdate;
            }

            // Phase 3: Calculate Byte Offset into the SceneSection Array
            valueArray.Dispose();
            valueArray = hashMap.GetValueArray(Allocator.Temp);
            foreach (var keyToUpdate in keyArray)
            {
                var archetypeToUpdate = hashMap[keyToUpdate];

                foreach (var otherArchetype in valueArray)
                {
                    if (otherArchetype.ArchetypeIndex < archetypeToUpdate.ArchetypeIndex)
                    {
                        archetypeToUpdate.Offset += otherArchetype.Amount * otherArchetype.SizePerEntity;
                    }
                }

                hashMap[keyToUpdate] = archetypeToUpdate;
            }
        }
    }
}
