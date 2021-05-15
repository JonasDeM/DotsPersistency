using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Hash128 = UnityEngine.Hash128;

namespace DotsPersistency.Tests
{
    [TestFixture]
    class DynamicPoolTests : EcsTestsFixture
    {
        public enum InstantiationType
        {
            Manager,
            Command,
            ParallelCommand
        }
        
        private static readonly ComponentType[] Archetype = new ComponentType[]
        {
            typeof(DynamicBufferData1),
            typeof(EcsTestData),
            typeof(EmptyEcsTestData)
        };
        
        [Test]
        public void TestDynamicPoolInitialState([Values(1, 22, 333, 4444)] int total)
        {
            // Prepare
            PersistencySettings settings = CreateTestSettings();
            PersistentSceneSystem persistentSceneSystem = World.GetOrCreateSystem<PersistentSceneSystem>();
            BeginFramePersistencySystem beginFramePersistencySystem = World.GetExistingSystem<BeginFramePersistencySystem>();
            beginFramePersistencySystem.ReplaceSettings(settings);
            PersistentDataStorage dataStorage = persistentSceneSystem.PersistentDataStorage;
            Hash128 identifier = Hash128.Compute("DynamicPoolTests");

            NativeArray<PersistableTypeHandle> typeHandles = new NativeArray<PersistableTypeHandle>(Archetype.Length, Allocator.Persistent);
            for (int i = 0; i < typeHandles.Length; i++)
            {
                typeHandles[i] = settings.GetPersistableTypeHandleFromFullTypeName(Archetype[i].GetManagedType().FullName); // This is not advised in a game
            }

            // Action
            Entity prefabEntity = m_Manager.CreateEntity(Archetype);
            m_Manager.SetComponentData(prefabEntity, new EcsTestData(1));
            m_Manager.AddComponent<Prefab>(prefabEntity);
            persistentSceneSystem.InitializePool(prefabEntity, total, identifier, typeHandles);

            // Verify
            {
                Assert.True(dataStorage.IsInitialized(identifier), "PoolContainer is not initialized!");
                PersistentDataContainer container = dataStorage.GetInitialStateReadContainer(identifier);
                int sizePerEntity = container.GetDataLayoutAtIndex(0).SizePerEntity;
                Assert.AreEqual(sizePerEntity * total, container.GetRawData().Length, "The data container was not of the expected size!");
                int amountNonZero = container.GetRawData().Count(dataByte => dataByte != 0);
                Assert.NotZero(amountNonZero, "The datacontainer didn't have valid initial state.");
            }

            // Action 2
            total += 31;
            persistentSceneSystem.InstantChangePoolCapacity(identifier, total);
            
            // Verify
            {
                Assert.True(dataStorage.IsInitialized(identifier), "PoolContainer is not initialized!");
                dataStorage.ToIndex(0);
                PersistentDataContainer container = dataStorage.GetReadContainerForCurrentIndex(identifier);
                int sizePerEntity = container.GetDataLayoutAtIndex(0).SizePerEntity;
                Assert.AreEqual(sizePerEntity * total, container.GetRawData().Length, "The data container was not of the expected size after an InstantResize!");
                int amountNonZero = container.GetRawData().Count(dataByte => dataByte != 0);
                Assert.NotZero(amountNonZero, "The datacontainer grew but didn't have valid initial state.");
            }

            // Cleanup
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }
        
        [Test]
        public void TestDynamicPoolApplyAndPersist([Values(InstantiationType.Manager, InstantiationType.Command, InstantiationType.ParallelCommand)] InstantiationType instantiationType,
            [Values(1, 2, 3, 444, 5555)] int total, [Values(false, true)] bool groupedJobs)
        {
            // Prepare
            PersistencySettings settings = CreateTestSettings();
            settings.ForceUseGroupedJobsInEditor = groupedJobs;
            settings.ForceUseNonGroupedJobsInBuild = !groupedJobs;
            Assert.True(groupedJobs == settings.UseGroupedJobs());
            PersistentSceneSystem persistentSceneSystem = World.GetOrCreateSystem<PersistentSceneSystem>();
            BeginFramePersistencySystem beginFramePersistencySystem = World.GetExistingSystem<BeginFramePersistencySystem>();
            beginFramePersistencySystem.ReplaceSettings(settings);
            EndFramePersistencySystem endFramePersistencySystem = World.GetOrCreateSystem<EndFramePersistencySystem>();
            endFramePersistencySystem.ReplaceSettings(settings);
            EndInitializationEntityCommandBufferSystem ecbSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();
            PersistentDataStorage dataStorage = persistentSceneSystem.PersistentDataStorage;
            Hash128 identifier = Hash128.Compute("DynamicPoolTests");

            NativeArray<PersistableTypeHandle> typeHandles = new NativeArray<PersistableTypeHandle>(Archetype.Length, Allocator.Persistent);
            for (int i = 0; i < typeHandles.Length; i++)
            {
                typeHandles[i] = settings.GetPersistableTypeHandleFromFullTypeName(Archetype[i].GetManagedType().FullName); // This is not advised in a game
            }

            // Action
            Entity prefabEntity = m_Manager.CreateEntity(Archetype);
            m_Manager.AddComponent<Prefab>(prefabEntity);
            persistentSceneSystem.InitializePool(prefabEntity, total, identifier, typeHandles);
            int amountSpawned = 0;
            InstantiatePersistent(instantiationType, m_Manager, prefabEntity, ref amountSpawned, total);
            persistentSceneSystem.InstantChangePoolCapacity(identifier, total*2); // double amount
            InstantiatePersistent(instantiationType, m_Manager, prefabEntity, ref amountSpawned, total);

            m_Manager.RemoveComponent<EmptyEcsTestData>(m_Manager.CreateEntityQuery(typeof(PersistenceState)));
            Entities.ForEach((Entity e, ref EcsTestData testData, DynamicBuffer<DynamicBufferData1> buffer) =>
            {
                testData.Value = 1;
                buffer.Add(new DynamicBufferData1() {Value = 1});
            });
            
            dataStorage.ToIndex(0);
            endFramePersistencySystem.RequestPersist(dataStorage.GetWriteContainerForCurrentIndex(identifier));
            endFramePersistencySystem.Update();
            m_Manager.CompleteAllJobs();
            beginFramePersistencySystem.RequestApply(dataStorage.GetInitialStateReadContainer(identifier));
            beginFramePersistencySystem.Update();
            ecbSystem.Update();
            
            // Test
            int amountSurvivingEntities = m_Manager.CreateEntityQuery(typeof(EcsTestData)).CalculateEntityCount();
            Assert.True(amountSurvivingEntities == total*2, $"{instantiationType} didn't create the expected amount of entities({amountSurvivingEntities}vs{total*2})! (Or they were destroyed by Apply)");
            Entities.ForEach((Entity e, ref EcsTestData testData, DynamicBuffer<DynamicBufferData1> buffer) =>
            {
                Assert.True(testData.Value == 0, "The apply didn't restore the component value!");
                Assert.True(buffer.IsEmpty, "The apply didn't restore the buffer!");
                Assert.True(m_Manager.HasComponent<EmptyEcsTestData>(e), "The apply didn't restore the component!");
            });
            
            // Action
            dataStorage.ToIndex(0);
            beginFramePersistencySystem.RequestApply(dataStorage.GetWriteContainerForCurrentIndex(identifier));
            beginFramePersistencySystem.Update();
            ecbSystem.Update();
            
            // Test
            amountSurvivingEntities = m_Manager.CreateEntityQuery(typeof(EcsTestData)).CalculateEntityCount();
            Assert.True(amountSurvivingEntities == total*2, $"Some entities were unexpectedly destroyed!");
            Entities.ForEach((Entity e, ref EcsTestData testData, DynamicBuffer<DynamicBufferData1> buffer) =>
            {
                Assert.True(testData.Value == 1, "The second apply didn't restore the component value!");
                Assert.True(buffer[0].Value == 1, "The second apply didn't restore the buffer!");
                Assert.False(m_Manager.HasComponent<EmptyEcsTestData>(e), "The second apply didn't restore the component!");
            });

            // Cleanup
            m_Manager.DestroyEntity(m_Manager.UniversalQuery);
        }

        private static void InstantiatePersistent(InstantiationType type, EntityManager mgr, Entity prefabEntity, ref int existingAmount, int extraAmount)
        {
            switch (type)
            {
                case InstantiationType.Manager:
                    if (extraAmount == 1)
                    {
                        mgr.InstantiatePersistent(prefabEntity, ref existingAmount);
                    }
                    else
                    {
                        mgr.InstantiatePersistent(prefabEntity, ref existingAmount, extraAmount);
                    }
                    break;
                case InstantiationType.Command:
                    EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp, PlaybackPolicy.SinglePlayback);
                    ecb.InstantiatePersistent(prefabEntity, ref existingAmount, extraAmount);
                    ecb.Playback(mgr);
                    ecb.Dispose();
                    break;
                case InstantiationType.ParallelCommand:
                    EntityCommandBuffer ecbParallel = new EntityCommandBuffer(Allocator.TempJob, PlaybackPolicy.SinglePlayback);
                    var job = new ParallelInstantiateJob()
                    {
                        Ecb = ecbParallel.AsParallelWriter(),
                        ExistingAmount = existingAmount,
                        ExtraAmount = extraAmount,
                        PrefabEntity = prefabEntity
                    };
                    existingAmount += extraAmount;
                    job.Schedule().Complete();
                    ecbParallel.Playback(mgr);
                    ecbParallel.Dispose();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private struct ParallelInstantiateJob : IJob
        {
            public EntityCommandBuffer.ParallelWriter Ecb;
            public Entity PrefabEntity;
            public int ExistingAmount;
            public int ExtraAmount;

            public void Execute()
            {
                Ecb.InstantiatePersistent(0, PrefabEntity, ExistingAmount, ExtraAmount);
            }
        }
    }
}
