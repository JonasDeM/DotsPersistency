using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DotsPersistency.Hybrid;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hash128 = UnityEngine.Hash128;

namespace DotsPersistency.Tests
{
    [TestFixture]
    class SystemTests : EcsTestsFixture
    {
        [Test]
        public void TestComponentConversion([Values(0, 1, 2, 3, 60, 61, 400)] int total)
        {
            PersistencySettings settings = CreateSettings();
            List<(PersistencyAuthoring, Entity)> toConvert = CreateGameObjectsAndPrimaryEntities(total, true, false, false);

            PersistencyConversionSystem.Convert(toConvert, (settings, m_Manager.CreateEntity()), m_Manager, Hash128.Compute("TestPersistencyConversion"));

            VerifyConvertedEntities(toConvert.Select((tuple) => tuple.Item1).ToList());
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }
        
        [Test]
        public void TestTagConversion([Values(0, 1, 2, 3, 60, 63, 400)] int total)
        {
            PersistencySettings settings = CreateSettings();
            List<(PersistencyAuthoring, Entity)> toConvert = CreateGameObjectsAndPrimaryEntities(total, false, true, false);

            PersistencyConversionSystem.Convert(toConvert, (settings, m_Manager.CreateEntity()), m_Manager, Hash128.Compute("TestPersistencyConversion"));
            
            VerifyConvertedEntities(toConvert.Select((tuple) => tuple.Item1).ToList());
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }
        
        [Test]
        public void TestBufferConversion([Values(0, 1, 2, 3, 60, 65, 400)] int total)
        {
            PersistencySettings settings = CreateSettings();
            List<(PersistencyAuthoring, Entity)> toConvert = CreateGameObjectsAndPrimaryEntities(total, false, false, true);

            PersistencyConversionSystem.Convert(toConvert, (settings, m_Manager.CreateEntity()), m_Manager, Hash128.Compute("TestPersistencyConversion"));
            
            VerifyConvertedEntities(toConvert.Select((tuple) => tuple.Item1).ToList());
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }

        [Test]
        public void TestCombinedConversion([Values(0, 1, 2, 3, 60, 100)] int total)
        {            
            PersistencySettings settings = CreateSettings();
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
            PersistencySettings settings = CreateSettings();
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

        // [Test]
        public void TestSceneLoad([Values(0, 1, 2, 3, 60, 71, 400)] int total, [Values(true, false)] bool groupedJobs)
        {
            // todo
            PersistencySettings settings = CreateSettings(new List<Type>()
            {
                
            });
            
            for (int i = 0; i < total; i++)
            {
                LoadFakeSceneSection(Hash128.Compute(i), i, settings);
            }

            PersistentSceneSystem persistentSceneSystem = World.GetOrCreateSystem<PersistentSceneSystem>();
            
            // override the settings field
            FieldInfo settingsField = typeof(PersistentSceneSystem)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic).First(info => typeof(PersistencySettings).IsAssignableFrom(info.FieldType));
            settingsField.SetValue(persistentSceneSystem, settings);
            
            persistentSceneSystem.Update();
            
            for (int i = 0; i < total; i++)
            {
                VerifyProcessedSceneSection(Hash128.Compute(i));
            }
        }
        
        // [Test]
        public void TestPersist([Values(0, 1, 2, 3, 60, 76)] int total, [Values(true, false)] bool groupedJobs)
        {
            throw new NotImplementedException();
        }
        
        // [Test]
        public void TestApply([Values(0, 1, 2, 3, 60, 83)] int total, [Values(true, false)] bool groupedJobs)
        {
            throw new NotImplementedException();
        }

        
        private void LoadFakeSceneSection(Hash128 sceneGUID, int total, PersistencySettings settings)
        {
            throw new NotImplementedException();
        }
        
        private void VerifyProcessedSceneSection(Hash128 sceneGUID)
        {
            throw new NotImplementedException();
        }

        private PersistencySettings CreateSettings()
        {
            return CreateSettings(new List<Type>()
            {
                typeof(EcsTestData), 
                typeof(EcsTestFloatData2),
                typeof(EcsTestData5),
                typeof(DynamicBufferData1),
                typeof(DynamicBufferData2),
                typeof(DynamicBufferData3),
                typeof(EmptyEcsTestData)
            });
        }
        
        private List<(PersistencyAuthoring, Entity)> CreateGameObjectsAndPrimaryEntities(int total, bool comp, bool tag, bool buffer)
        {
            List<(PersistencyAuthoring, Entity)> all = new List<(PersistencyAuthoring, Entity)>(total);
            for (int i = 0; i < total; i++)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "TestAuthoring_" + i.ToString();
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
