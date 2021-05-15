// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using System.Linq;
using DotsPersistency.Hybrid;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Hash128 = UnityEngine.Hash128;
using Object = UnityEngine.Object;

namespace DotsPersistency.Tests
{
    [TestFixture]
    class SystemTests : EcsTestsFixture
    {
        [Test]
        public void TestComponentConversion([Values(0, 1, 2, 3, 60, 61, 400)] int total)
        {
            PersistencySettings settings = CreateTestSettings();
            
            List<(PersistencyAuthoring, Entity)> toConvert = CreateGameObjectsAndPrimaryEntities(total, true, false, false);

            PersistencyConversionSystem.Convert(toConvert, (settings, m_Manager.CreateEntity()), m_Manager, Hash128.Compute("TestPersistencyConversion"));

            VerifyConvertedEntities(toConvert.Select((tuple) => tuple.Item1).ToList());
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }
        
        [Test]
        public void TestTagConversion([Values(0, 1, 2, 3, 60, 63, 400)] int total)
        {
            PersistencySettings settings = CreateTestSettings();
            
            List<(PersistencyAuthoring, Entity)> toConvert = CreateGameObjectsAndPrimaryEntities(total, false, true, false);

            PersistencyConversionSystem.Convert(toConvert, (settings, m_Manager.CreateEntity()), m_Manager, Hash128.Compute("TestPersistencyConversion"));
            
            VerifyConvertedEntities(toConvert.Select((tuple) => tuple.Item1).ToList());
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }
        
        [Test]
        public void TestBufferConversion([Values(0, 1, 2, 3, 60, 65, 400)] int total)
        {
            PersistencySettings settings = CreateTestSettings();
            
            List<(PersistencyAuthoring, Entity)> toConvert = CreateGameObjectsAndPrimaryEntities(total, false, false, true);

            PersistencyConversionSystem.Convert(toConvert, (settings, m_Manager.CreateEntity()), m_Manager, Hash128.Compute("TestPersistencyConversion"));
            
            VerifyConvertedEntities(toConvert.Select((tuple) => tuple.Item1).ToList());
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }

        [Test]
        public void TestCombinedConversion([Values(0, 1, 2, 3, 60, 100)] int total)
        {            
            PersistencySettings settings = CreateTestSettings();
            
            List<(PersistencyAuthoring, Entity)> toConvert = new List<(PersistencyAuthoring, Entity)>();
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(total, false, true, true));
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(total, false, false, true));
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(total, true, false, true));
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(total, true, false, false));
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(total, false, true, false));
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(total, true, true, true));

            PersistencyConversionSystem.Convert(toConvert, (settings, m_Manager.CreateEntity()), m_Manager, Hash128.Compute("TestPersistencyConversion"));
            
            VerifyConvertedEntities(toConvert.Select((tuple) => tuple.Item1).ToList());
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }
        
        [Test]
        public void TestMissingTypeConversion([Values(0, 1, 2, 3, 60, 100)] int total)
        {            
            PersistencySettings settings = CreateTestSettings();
            settings.AllPersistableTypeInfos.RemoveAt(0);
            
            List<(PersistencyAuthoring, Entity)> toConvert = new List<(PersistencyAuthoring, Entity)>();
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(total, true, true, true));

            PersistencyConversionSystem.Convert(toConvert, (settings, m_Manager.CreateEntity()), m_Manager, Hash128.Compute("TestPersistencyConversion"));
            
            VerifyConvertedEntities(toConvert.Select((tuple) => tuple.Item1).ToList());
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }
        
        private void VerifyConvertedEntities(List<PersistencyAuthoring> convertedObjects)
        {
            Assert.True(convertedObjects.Count  == m_Manager.CreateEntityQuery(typeof(PersistenceState)).CalculateEntityCount(), "Not all game objects were converted!");
            
            // Check components are added & arrayindex is unique per persistencyarchetype
            Dictionary<Hash128, NativeHashSet<int>> uniqueIndicesPerArchetype = new Dictionary<Hash128, NativeHashSet<int>>();
            Entities.ForEach((Entity e, ref PersistenceState persistenceState) =>
            {
                Assert.True(m_Manager.HasComponent<PersistencyContainerTag>(e), $"{m_Manager.GetName(e)} did not have a {nameof(PersistencyContainerTag)} component.");
                Assert.True(m_Manager.HasComponent<PersistencyArchetypeIndexInContainer>(e), $"{m_Manager.GetName(e)} did not have a {nameof(PersistencyArchetypeIndexInContainer)} component.");
                Assert.True(m_Manager.HasComponent<PersistableTypeCombinationHash>(e), $"{m_Manager.GetName(e)} did not have a {nameof(PersistableTypeCombinationHash)} component.");
                
                var hash = m_Manager.GetSharedComponentData<PersistableTypeCombinationHash>(e).Value;
                if (!uniqueIndicesPerArchetype.ContainsKey(hash))
                {
                    uniqueIndicesPerArchetype.Add(hash, new NativeHashSet<int>(4, Allocator.Temp));
                }
                
                int index = persistenceState.ArrayIndex;
                Assert.False(uniqueIndicesPerArchetype[hash].Contains(index),  $"{m_Manager.GetName(e)} had the same arrayindex as another entity '{index}'.");
                uniqueIndicesPerArchetype[hash].Add(index);
            });
            
            // Verify SceneInfo entity
            if (uniqueIndicesPerArchetype.Count > 0)
            {
                var scenePersistencyInfoRef = m_Manager.CreateEntityQuery(typeof(ScenePersistencyInfoRef)).GetSingleton<ScenePersistencyInfoRef>();
                Assert.True(scenePersistencyInfoRef.InfoRef.IsCreated);
            }
            
            // Clean
            foreach (NativeHashSet<int> value in uniqueIndicesPerArchetype.Values)
            {
                value.Dispose();
            }
        }

        [Test]
        public void TestSceneLoad([Values(1,2,3,10)] int total)
        {
            PersistencySettings settings = CreateTestSettings();
            
            // override the settings field
            PersistentSceneSystem persistentSceneSystem = World.GetOrCreateSystem<PersistentSceneSystem>();
            persistentSceneSystem.ReplaceSettings(settings);
            
            // Load SubScenes
            for (int i = 0; i < total; i++)
            {
                Unity.Entities.Hash128 sceneGUID = Hash128.Compute(i);
                LoadFakeSubScene(settings, sceneGUID, i+10);
            }
            
            persistentSceneSystem.Update();
            
            for (int i = 0; i < total; i++)
            {
                Unity.Entities.Hash128 sceneGUID = Hash128.Compute(i);
                // Since BeginFramePersistencySystem hasn't run, this data will be uninitialized.
                // We're only interested if it's the container exists & if it's the right size
                Assert.True(persistentSceneSystem.PersistentDataStorage.IsInitialized(sceneGUID), 
                    "Expected the subscene to have an initialized PersistentDataContainer");
                
                PersistentDataContainer container = persistentSceneSystem.PersistentDataStorage.GetReadContainerForLatestWriteIndex(sceneGUID, out bool isInitial);
                
                Assert.True(isInitial);
                Assert.True(container.DataIdentifier == sceneGUID,
                    $"Expected the container to have the sceneGUID {sceneGUID} as data identifier, but it was {container.DataIdentifier}.");
                Assert.True(container.DataLayoutCount == 6, 
                    $"LoadFakeSubScene creates 6 different types of persistable entities so we expect 6 different data layouts in the container, but it reports {container.DataLayoutCount}");

                int entitiesInScene = (i + 10) * 6;
                int entitiesInContainer = container.CalculateEntityCapacity();
                Assert.True(entitiesInContainer == entitiesInScene, 
                    $"LoadFakeSubScene created {entitiesInScene} entities, but the container reports {entitiesInContainer}.");
            }
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }
        
        [Test]
        public void TestPersistAndApply([Values(1,2,3,10)] int total, [Values(false, true)] bool groupedJobs)
        {
            PersistencySettings settings = CreateTestSettings();
            settings.ForceUseGroupedJobsInEditor = groupedJobs;
            settings.ForceUseNonGroupedJobsInBuild = !groupedJobs;
            Assert.True(groupedJobs == settings.UseGroupedJobs());
            
            // Override the settings fields of both systems
            PersistentSceneSystem persistentSceneSystem = World.GetOrCreateSystem<PersistentSceneSystem>();
            persistentSceneSystem.ReplaceSettings(settings);
            BeginFramePersistencySystem beginFramePersistencySystem = World.GetOrCreateSystem<BeginFramePersistencySystem>();
            beginFramePersistencySystem.ReplaceSettings(settings);
            
            EndInitializationEntityCommandBufferSystem ecbSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();

            // Load SubScenes
            for (int i = 0; i < total; i++)
            {
                Unity.Entities.Hash128 sceneGUID = Hash128.Compute(i);
                LoadFakeSubScene(settings, sceneGUID, i+10);
            }

            // The fake subscene entities don't have any actual test data on them yet, so add some here
            Entities.ForEach((Entity e, ref PersistenceState persistenceState) =>
            {
                // tracking add/remove of bufferdata isn't supported so we add all of them
                DynamicBuffer<DynamicBufferData1> buffer1 = m_Manager.AddBuffer<DynamicBufferData1>(e);
                buffer1.Add(new DynamicBufferData1(){Value = persistenceState.ArrayIndex});
                DynamicBuffer<DynamicBufferData2> buffer2 = m_Manager.AddBuffer<DynamicBufferData2>(e);
                buffer2.Add(new DynamicBufferData2(){Value = persistenceState.ArrayIndex});
                DynamicBuffer<DynamicBufferData3> buffer3 = m_Manager.AddBuffer<DynamicBufferData3>(e);
                buffer3.Add(new DynamicBufferData3(){Value = (byte)persistenceState.ArrayIndex});
                
                // Add different components
                if (persistenceState.ArrayIndex % 2 == 0)
                {
                    m_Manager.AddComponentData(e, new EcsTestData(persistenceState.ArrayIndex));
                }
                else
                {
                    m_Manager.AddComponent<EmptyEcsTestData>(e);
                }
            });

            persistentSceneSystem.Update();
            beginFramePersistencySystem.Update();
            ecbSystem.Update();

            // Check if some data was written to the container
            for (int i = 0; i < total; i++)
            {
                Unity.Entities.Hash128 sceneGUID = Hash128.Compute(i);
                Assert.True(persistentSceneSystem.PersistentDataStorage.IsInitialized(sceneGUID), "Expected the subscene to have an initialized PersistentDataContainer");
                PersistentDataContainer container = persistentSceneSystem.PersistentDataStorage.GetReadContainerForLatestWriteIndex(sceneGUID, out bool isInitial);
                Assert.True(isInitial);

                bool hasOnlyZero = true;
                foreach (byte dataByte in container.GetRawData())
                {
                    if (dataByte != 0)
                    {
                        hasOnlyZero = false;
                        break;
                    }
                }
                Assert.False(hasOnlyZero, "");
            }
            
            // Change entities
            Entities.ForEach((Entity e, ref PersistenceState persistenceState) =>
            {
                DynamicBuffer<DynamicBufferData1> buffer1 = m_Manager.GetBuffer<DynamicBufferData1>(e);
                buffer1[0] = default;
                DynamicBuffer<DynamicBufferData2> buffer2 = m_Manager.GetBuffer<DynamicBufferData2>(e);
                buffer2[0] = default;
                DynamicBuffer<DynamicBufferData3> buffer3 = m_Manager.GetBuffer<DynamicBufferData3>(e);
                buffer3[0] = default;
                
                if (persistenceState.ArrayIndex % 2 == 0)
                {
                    m_Manager.RemoveComponent<EcsTestData>(e);
                    m_Manager.AddComponent<EmptyEcsTestData>(e);
                }
                else
                {
                    m_Manager.AddComponentData(e, new EcsTestData(persistenceState.ArrayIndex));
                    m_Manager.RemoveComponent<EmptyEcsTestData>(e);
                }
            });
            
            // Revert entities to initial state
            for (int i = 0; i < total; i++)
            {
                Unity.Entities.Hash128 sceneGUID = Hash128.Compute(i);
                Assert.True(persistentSceneSystem.PersistentDataStorage.IsInitialized(sceneGUID),
                    "Expected the subscene to have an initialized PersistentDataContainer");
                PersistentDataContainer container = persistentSceneSystem.PersistentDataStorage.GetReadContainerForLatestWriteIndex(sceneGUID, out bool isInitial);
                Assert.True(isInitial);
                beginFramePersistencySystem.RequestApply(container);
            }
            beginFramePersistencySystem.Update();
            ecbSystem.Update();
            
            // Test if the revert actually worked on entities that were tracking the components
            Entities.ForEach((Entity e, ref PersistenceState persistenceState) =>
            {
                if (m_Manager.GetName(e).Contains("_B")) // this means it was tracking all buffer components
                {
                    DynamicBuffer<DynamicBufferData1> buffer1 = m_Manager.GetBuffer<DynamicBufferData1>(e);
                    Assert.True(buffer1[0].Value == persistenceState.ArrayIndex, "The entity was expected to be reset and have it's original buffer value (1)");
                    DynamicBuffer<DynamicBufferData2> buffer2 = m_Manager.GetBuffer<DynamicBufferData2>(e);
                    Assert.True(Math.Abs(buffer2[0].Value - persistenceState.ArrayIndex) < 0.001f, "The entity was expected to be reset and have it's original buffer value (2)");
                    DynamicBuffer<DynamicBufferData3> buffer3 = m_Manager.GetBuffer<DynamicBufferData3>(e);
                    Assert.True(buffer3[0].Value == (byte)persistenceState.ArrayIndex, "The entity was expected to be reset and have it's original buffer value (3)");
                }
                else
                {
                    DynamicBuffer<DynamicBufferData1> buffer1 = m_Manager.GetBuffer<DynamicBufferData1>(e);
                    Assert.True(buffer1[0].Value == 0, "The entity was expected to NOT be reset and have a 0 buffer value. (1)");
                    DynamicBuffer<DynamicBufferData2> buffer2 = m_Manager.GetBuffer<DynamicBufferData2>(e);
                    Assert.True(buffer2[0].Value == 0, "The entity was expected to NOT be reset and have a 0 buffer value. (2)");
                    DynamicBuffer<DynamicBufferData3> buffer3 = m_Manager.GetBuffer<DynamicBufferData3>(e);
                    Assert.True(buffer3[0].Value == 0, "The entity was expected to NOT be reset and have a 0 buffer value. (3)");
                }
                
                if (persistenceState.ArrayIndex % 2 == 0)
                {
                    if (m_Manager.GetName(e).Contains("_C")) // this means it was tracking all standard components
                    {
                        Assert.True(m_Manager.HasComponent<EcsTestData>(e), "The entity was expected to be reset and have its original component back.");
                    }
                    else
                    {
                        Assert.False(m_Manager.HasComponent<EcsTestData>(e), "The entity was expected to NOT be reset and NOT have its original component back.");
                    }

                    if (m_Manager.GetName(e).Contains("_T")) // this means it was tracking all tag components
                    {
                        Assert.False(m_Manager.HasComponent<EmptyEcsTestData>(e), "The entity was expected to be reset and have the new TAG component removed.");
                    }
                    else
                    {
                        Assert.True(m_Manager.HasComponent<EmptyEcsTestData>(e), "The entity was expected to NOT be reset and NOT have the new TAG component removed. ");
                    }
                }
                else
                {
                    if (m_Manager.GetName(e).Contains("_C")) // this means it was tracking all standard components
                    {
                        Assert.False(m_Manager.HasComponent<EcsTestData>(e), "The entity was expected to be reset and have the new component removed.");
                    }
                    else
                    {
                        Assert.True(m_Manager.HasComponent<EcsTestData>(e), "The entity was expected to NOT be reset and NOT have the new component removed.");
                    }

                    if (m_Manager.GetName(e).Contains("_T")) // this means it was tracking all tag components
                    {
                        Assert.True(m_Manager.HasComponent<EmptyEcsTestData>(e), "The entity was expected to be reset and have its original TAG component back.");
                    }
                    else
                    {
                        Assert.False(m_Manager.HasComponent<EmptyEcsTestData>(e), "The entity was expected to NOT be reset and NOT have its original TAG component back.");
                    }
                }
            });
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }

        private void LoadFakeSubScene(PersistencySettings settings, Unity.Entities.Hash128 sceneGUID, int amountPerArchetype)
        {
            List<(PersistencyAuthoring, Entity)> toConvert = new List<(PersistencyAuthoring, Entity)>();
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(amountPerArchetype, false, true, true));
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(amountPerArchetype, false, false, true));
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(amountPerArchetype, true, false, true));
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(amountPerArchetype, true, false, false));
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(amountPerArchetype, false, true, false));
            toConvert.AddRange(CreateGameObjectsAndPrimaryEntities(amountPerArchetype, true, true, true));

            PersistencyConversionSystem.Convert(toConvert, (settings, m_Manager.CreateEntity()), m_Manager, sceneGUID);

            foreach ((PersistencyAuthoring, Entity) pair in toConvert)
            {
                m_Manager.AddSharedComponentData(pair.Item2, new SceneSection() {SceneGUID = sceneGUID, Section = 0});
                Object.DestroyImmediate(pair.Item1); // Destroying the gameobjects after conversion is necessary to be able to load multiple fake subscenes
            }

            // Fake the scene section entity, so the PersistentSceneSystem does it's work
            Entity sceneSectionEntity =  m_Manager.CreateEntity();
            m_Manager.AddComponentData(sceneSectionEntity, new SceneSectionData()
            {
                SceneGUID = sceneGUID,
                SubSectionIndex = 0,
            });
            m_Manager.AddComponentData(sceneSectionEntity, new RequestPersistentSceneSectionLoaded());
        }
        
        private List<(PersistencyAuthoring, Entity)> CreateGameObjectsAndPrimaryEntities(int total, bool comp, bool tag, bool buffer)
        {
            List<(PersistencyAuthoring, Entity)> all = new List<(PersistencyAuthoring, Entity)>(total);
            for (int i = 0; i < total; i++)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"TestAuthoring_{i.ToString()}";
                PersistencyAuthoring persistencyAuthoring = go.AddComponent<PersistencyAuthoring>();
                if (comp)
                {
                    persistencyAuthoring.FullTypeNamesToPersist.Add(typeof(EcsTestData).FullName);
                    persistencyAuthoring.FullTypeNamesToPersist.Add(typeof(EcsTestFloatData2).FullName);
                    persistencyAuthoring.FullTypeNamesToPersist.Add(typeof(EcsTestData5).FullName);
                    go.name += "_C";
                }
                if (tag)
                {
                    persistencyAuthoring.FullTypeNamesToPersist.Add(typeof(EmptyEcsTestData).FullName);
                    go.name += "_T";
                }
                if (buffer)
                {
                    persistencyAuthoring.FullTypeNamesToPersist.Add(typeof(DynamicBufferData1).FullName);
                    persistencyAuthoring.FullTypeNamesToPersist.Add(typeof(DynamicBufferData2).FullName);
                    persistencyAuthoring.FullTypeNamesToPersist.Add(typeof(DynamicBufferData3).FullName);
                    go.name += "_B";
                }

                Entity e = m_Manager.CreateEntity();
                m_Manager.SetName(e, go.name);
                all.Add((persistencyAuthoring, e));
            }

            return all;
        }
    }
}
