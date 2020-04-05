// Author: Jonas De Maeseneer

using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

// ReSharper disable AccessToDisposedClosure

namespace DotsPersistency.Tests
{
    [TestFixture]
    class ComponentDataTests : EcsTestsFixture
    {
        [Test]
        public unsafe void TestReadComponentData([Values(0, 1, 2, 3, 60, 400)] int total)
        {
            // Preparation
            CreateEntities(total);

            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            
            var array1IntData = new NativeArray<byte>(entityAmount1 * (UnsafeUtility.SizeOf<EcsPersistingTestData>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);
            var array2FloatData = new NativeArray<byte>(entityAmount2 * (UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);
            var array5IntData = new NativeArray<byte>(entityAmount3 * (UnsafeUtility.SizeOf<EcsPersistingTestData5>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);

            var query1 = m_Manager.CreateEntityQuery(typeof(EcsPersistingTestData), typeof(PersistenceState));
            var query2 = m_Manager.CreateEntityQuery(typeof(EcsPersistingFloatTestData2), typeof(PersistenceState));
            var query3 = m_Manager.CreateEntityQuery(typeof(EcsPersistingTestData5), typeof(PersistenceState));

            Entities.With(query3).ForEach(entity =>
            {
                if (m_Manager.GetComponentData<PersistenceState>(entity).ArrayIndex < 3)
                {
                    m_Manager.DestroyEntity(entity);
                } 
            });

            // Action
            var job1 = new CopyComponentDataToByteArray()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingTestData)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData>(),
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array1IntData
            }.Schedule(query1);
            
            var job2 =new CopyComponentDataToByteArray()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingFloatTestData2)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() ,
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array2FloatData
            }.Schedule(query2);
            
            var job3 =new CopyComponentDataToByteArray()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingTestData5)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData5>(),
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array5IntData
            }.Schedule(query3);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            
            // Check Results
            Entities.With(query1).ForEach(entity =>
            {
                var originalData = m_Manager.GetComponentData<EcsPersistingTestData>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                int byteIndex = persistenceState.ArrayIndex * (UnsafeUtility.SizeOf<EcsPersistingTestData>() + PersistenceMetaData.SizeOfStruct) + PersistenceMetaData.SizeOfStruct;
                var copiedData = *(EcsPersistingTestData*)((byte*)array1IntData.GetUnsafeReadOnlyPtr() + byteIndex);
                Assert.True(originalData.Equals(copiedData), "Data output by CopyComponentDataToByteArray does not match data on entity.");
            });
            
            Entities.With(query2).ForEach(entity =>
            {
                var originalData = m_Manager.GetComponentData<EcsPersistingFloatTestData2>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                int byteIndex = persistenceState.ArrayIndex * (UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() + PersistenceMetaData.SizeOfStruct) + PersistenceMetaData.SizeOfStruct;
                var copiedData = *(EcsPersistingFloatTestData2*)((byte*)array2FloatData.GetUnsafeReadOnlyPtr() + byteIndex);
                Assert.True(originalData.Equals(copiedData), "Data output by CopyComponentDataToByteArray does not match data on entity.");
            });
            
            Entities.With(query3).ForEach(entity =>
            {
                var originalData = m_Manager.GetComponentData<EcsPersistingTestData5>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                int byteIndex = persistenceState.ArrayIndex * (UnsafeUtility.SizeOf<EcsPersistingTestData5>() + PersistenceMetaData.SizeOfStruct) + PersistenceMetaData.SizeOfStruct;
                var copiedData = *(EcsPersistingTestData5*)((byte*)array5IntData.GetUnsafeReadOnlyPtr() + byteIndex);
                Assert.True(originalData.Equals(copiedData), "Data output by CopyComponentDataToByteArray does not match data on entity.");
            });
            
            for (int i = 0; i < entityAmount1; i++)
            {
                var stride = (UnsafeUtility.SizeOf<EcsPersistingTestData>() + PersistenceMetaData.SizeOfStruct);
                var metaData = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(array1IntData.GetUnsafeReadOnlyPtr(), i, stride);
                Assert.True(metaData.AmountFound == 1, "Entity was not found even though it existed.");
                Assert.True(metaData.HasChanged, "Data changed but meta data didn't record the change.");
            }
            for (int i = 0; i < entityAmount2; i++)
            {
                var stride = (UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() + PersistenceMetaData.SizeOfStruct);
                var metaData = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(array2FloatData.GetUnsafeReadOnlyPtr(), i, stride);
                Assert.True(metaData.AmountFound == 1, "Entity was not found even though it existed.");
                Assert.True(metaData.HasChanged, "Data changed but meta data didn't record the change.");
            }
            int amount5Found = 0;
            for (int i = 0; i < entityAmount3; i++)
            {
                var stride = (UnsafeUtility.SizeOf<EcsPersistingTestData5>() + PersistenceMetaData.SizeOfStruct);
                var metaData = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(array5IntData.GetUnsafeReadOnlyPtr(), i, stride);
                Assert.True(metaData.AmountFound == 1 || metaData.AmountFound == 0, "Incorrect AmountFound on meta data.");
                amount5Found += metaData.AmountFound;
                if (i > 2)
                {
                    Assert.True(metaData.HasChanged, "Data changed but meta data didn't record the change.");
                }
                else
                {
                    Assert.False(metaData.HasChanged, "Data did not change but meta data recorded a change.");
                }
            }
            Assert.True(amount5Found < entityAmount3 || amount5Found == 0, "More values were persisted than entities existed.");

            // Cleanup
            array1IntData.Dispose();
            array2FloatData.Dispose();
            array5IntData.Dispose();
            m_Manager.DestroyEntity(m_Manager.CreateEntityQuery(typeof(PersistenceState)));
        }
        
        [Test]
        public unsafe void TestApplyStoredData([Values(0, 1, 2, 3, 60, 401)] int total)
        {
            // Preparation
            CreateEntities(total);
            
            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            
            var array1IntData = new NativeArray<byte>(entityAmount1 * (UnsafeUtility.SizeOf<EcsPersistingTestData>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);
            var array2FloatData = new NativeArray<byte>(entityAmount2 * (UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);
            var array5IntData = new NativeArray<byte>(entityAmount3 * (UnsafeUtility.SizeOf<EcsPersistingTestData5>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);

            var query1 = m_Manager.CreateEntityQuery(typeof(EcsPersistingTestData), typeof(PersistenceState));
            var query2 = m_Manager.CreateEntityQuery(typeof(EcsPersistingFloatTestData2), typeof(PersistenceState));
            var query3 = m_Manager.CreateEntityQuery(typeof(EcsPersistingTestData5), typeof(PersistenceState));

            Entities.WithAll<PersistenceState>().ForEach(entity =>
            {
                int index = m_Manager.GetComponentData<PersistenceState>(entity).ArrayIndex;
                if (index < 3)
                {
                    m_Manager.DestroyEntity(entity);
                }
                else if (m_Manager.HasComponent<EcsPersistingTestData>(entity))
                {
                    PersistenceMetaData* metaDataPtr = (PersistenceMetaData*)((byte*) array1IntData.GetUnsafePtr() + index * (UnsafeUtility.SizeOf<EcsPersistingTestData>() + PersistenceMetaData.SizeOfStruct));
                    *metaDataPtr = new PersistenceMetaData(0, 1);
                    EcsPersistingTestData* dataPtr = (EcsPersistingTestData*)(metaDataPtr + 1);
                    *dataPtr = m_Manager.GetComponentData<EcsPersistingTestData>(entity).Modified();
                }
                else if (m_Manager.HasComponent<EcsPersistingFloatTestData2>(entity))
                {
                    PersistenceMetaData* metaDataPtr = (PersistenceMetaData*)((byte*) array2FloatData.GetUnsafePtr() + index * (UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>() + PersistenceMetaData.SizeOfStruct));
                    *metaDataPtr = new PersistenceMetaData(0, 1);
                    EcsPersistingFloatTestData2* dataPtr = (EcsPersistingFloatTestData2*)(metaDataPtr + 1);
                    *dataPtr = m_Manager.GetComponentData<EcsPersistingFloatTestData2>(entity).Modified();
                }
                else if (m_Manager.HasComponent<EcsPersistingTestData5>(entity))
                {
                    PersistenceMetaData* metaDataPtr = (PersistenceMetaData*)((byte*) array5IntData.GetUnsafePtr() + index * (UnsafeUtility.SizeOf<EcsPersistingTestData5>() + PersistenceMetaData.SizeOfStruct));
                    *metaDataPtr = new PersistenceMetaData(0, 1);
                    EcsPersistingTestData5* dataPtr = (EcsPersistingTestData5*)(metaDataPtr + 1);
                    *dataPtr = m_Manager.GetComponentData<EcsPersistingTestData5>(entity).Modified();
                }
            });
            int amount1 = query1.CalculateEntityCount();
            int amount2 = query2.CalculateEntityCount();
            int amount3 = query3.CalculateEntityCount();

            // Action
            new CopyByteArrayToComponentData()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingTestData)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData>(),
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                InputData = array1IntData
            }.Run(query1);
            
            new CopyByteArrayToComponentData()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingFloatTestData2)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>(),
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                InputData = array2FloatData
            }.Run(query2);

            new CopyByteArrayToComponentData()
            {
                ChunkComponentType = m_Manager.GetArchetypeChunkComponentTypeDynamic(typeof(EcsPersistingTestData5)),
                TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData5>(),
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                InputData = array5IntData
            }.Run(query3);

            // Check Results
            Entities.With(query1).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingTestData>(entity);
                var originalData = new EcsPersistingTestData() { data = m_Manager.GetComponentData<EcsTestData>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "CopyByteArrayToComponentData:: Restored data on entity is incorrect.");
            });
            
            Entities.With(query2).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingFloatTestData2>(entity);
                var originalData = new EcsPersistingFloatTestData2() { data = m_Manager.GetComponentData<EcsTestFloatData2>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "CopyByteArrayToComponentData:: Restored data on entity is incorrect.");
            });
            
            Entities.With(query3).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingTestData5>(entity);
                var originalData = new EcsPersistingTestData5() { data = m_Manager.GetComponentData<EcsTestData5>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "CopyByteArrayToComponentData:: Restored data on entity is incorrect.");
            });
            
            // Preparation
            Entities.WithAll<PersistenceState>().ForEach((Entity entity) =>
            {
                int index = m_Manager.GetComponentData<PersistenceState>(entity).ArrayIndex;
                if (index % 2 == 1)
                {
                    if (m_Manager.HasComponent<EcsPersistingTestData>(entity))
                    {
                        m_Manager.RemoveComponent<EcsPersistingTestData>(entity);
                    }
                    else if (m_Manager.HasComponent<EcsPersistingFloatTestData2>(entity))
                    {
                        m_Manager.RemoveComponent<EcsPersistingFloatTestData2>(entity);
                    }
                    else if (m_Manager.HasComponent<EcsPersistingTestData5>(entity))
                    {
                        m_Manager.RemoveComponent<EcsPersistingTestData5>(entity);
                    }
                }
            });

            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                new AddMissingComponent()
                {
                    ComponentType = typeof(EcsPersistingTestData),
                    TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData>(),
                    EntityType = m_Manager.GetArchetypeChunkEntityType(),
                    PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                    InputData = array1IntData,
                    Ecb = cmds.ToConcurrent()
                }.Run(m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(PersistenceState)));
                cmds.Playback(m_Manager);
            }

            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                new AddMissingComponent()
                {
                    ComponentType = typeof(EcsPersistingFloatTestData2),
                    TypeSize = UnsafeUtility.SizeOf<EcsPersistingFloatTestData2>(),
                    EntityType = m_Manager.GetArchetypeChunkEntityType(),
                    PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                    InputData = array2FloatData,
                    Ecb = cmds.ToConcurrent()
                }.Run(m_Manager.CreateEntityQuery(typeof(EcsTestFloatData2), typeof(PersistenceState)));
                cmds.Playback(m_Manager);
            }

            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                new AddMissingComponent()
                {
                    ComponentType = typeof(EcsPersistingTestData5),
                    TypeSize = UnsafeUtility.SizeOf<EcsPersistingTestData5>(),
                    EntityType = m_Manager.GetArchetypeChunkEntityType(),
                    PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                    InputData = array5IntData,
                    Ecb = cmds.ToConcurrent()
                }.Run(m_Manager.CreateEntityQuery(typeof(EcsTestData5), typeof(PersistenceState)));
                cmds.Playback(m_Manager);
            }

            // Check Results
            Assert.AreEqual(amount1, query1.CalculateEntityCount(), "AddMissingComponents:: Not all missing components have not been restored");
            Entities.With(query1).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingTestData>(entity);
                var originalData = new EcsPersistingTestData() { data = m_Manager.GetComponentData<EcsTestData>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "AddMissingComponents:: Restored data on entity is incorrect.");
            });
            
            Assert.AreEqual(amount2, query2.CalculateEntityCount(), "AddMissingComponents:: Not all missing components have not been restored");
            Entities.With(query2).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingFloatTestData2>(entity);
                var originalData = new EcsPersistingFloatTestData2() { data = m_Manager.GetComponentData<EcsTestFloatData2>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "AddMissingComponents:: Restored data on entity is incorrect.");
            });
            
            Assert.AreEqual(amount3, query3.CalculateEntityCount(), "AddMissingComponents:: Not all missing components have not been restored");
            Entities.With(query3).ForEach(entity =>
            {
                var newData = m_Manager.GetComponentData<EcsPersistingTestData5>(entity);
                var originalData = new EcsPersistingTestData5() { data = m_Manager.GetComponentData<EcsTestData5>(entity)};
                Assert.True(newData.Equals(originalData.Modified()), "AddMissingComponents:: Restored data on entity is incorrect.");
            });

            // Cleanup
            array1IntData.Dispose();
            array2FloatData.Dispose();
            array5IntData.Dispose();
            m_Manager.DestroyEntity(m_Manager.CreateEntityQuery(typeof(PersistenceState)));
        }

        [Test]
        public void TestApplyAndReadEmptyData()
        {
            var entity1 = m_Manager.CreateEntity(typeof(PersistenceState), typeof(EmptyEcsPersistingTestData));
            var entity2 = m_Manager.CreateEntity(typeof(PersistenceState));
            var entity3 = m_Manager.CreateEntity(typeof(PersistenceState), typeof(EmptyEcsPersistingTestData));
            
            m_Manager.SetComponentData(entity1, new PersistenceState {ArrayIndex = 0});
            m_Manager.SetComponentData(entity2, new PersistenceState {ArrayIndex = 1});
            m_Manager.SetComponentData(entity3, new PersistenceState {ArrayIndex = 2});
            
            var query = m_Manager.CreateEntityQuery(typeof(EmptyEcsPersistingTestData), typeof(PersistenceState));
            var excludeQuery = m_Manager.CreateEntityQuery(ComponentType.Exclude<EmptyEcsPersistingTestData>(), typeof(PersistenceState));
            var data = new NativeArray<byte>(3 * PersistenceMetaData.SizeOfStruct, Allocator.TempJob);

            // Action
            var readJob = new UpdateMetaDataForComponentTag()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = data
            }.Schedule(query);
            readJob.Complete();

            m_Manager.AddComponentData(entity2, new EmptyEcsPersistingTestData());
            m_Manager.RemoveComponent<EmptyEcsPersistingTestData>(entity3);
            
            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                var removeJob = new RemoveComponent()
                {
                    TypeToRemove = typeof(EmptyEcsPersistingTestData),
                    TypeSize = TypeManager.GetTypeInfo<EmptyEcsPersistingTestData>().ElementSize,
                    PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                    InputData = data,
                    EntityType = m_Manager.GetArchetypeChunkEntityType(),
                    Ecb = cmds.ToConcurrent()
                }.Schedule(query);
                removeJob.Complete();
                cmds.Playback(m_Manager);
            }
            
            using (EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.TempJob))
            {
                var addJob = new AddMissingComponent()
                {
                    ComponentType = typeof(EmptyEcsPersistingTestData),
                    TypeSize = TypeManager.GetTypeInfo<EmptyEcsPersistingTestData>().ElementSize,
                    PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                    InputData = data,
                    EntityType = m_Manager.GetArchetypeChunkEntityType(),
                    Ecb = cmds.ToConcurrent()
                }.Schedule(excludeQuery);
                addJob.Complete();
                cmds.Playback(m_Manager);
            }
            
            // Check Results
            Assert.True(m_Manager.HasComponent<EmptyEcsPersistingTestData>(entity1), "The entity does not have the component EmptyEcsPersistingTestData BUT the entity did have the component at the time of persisting.");
            Assert.False(m_Manager.HasComponent<EmptyEcsPersistingTestData>(entity2), "The entity has the component EmptyEcsPersistingTestData BUT the entity did not have the component at the time of persisting.");
            Assert.True(m_Manager.HasComponent<EmptyEcsPersistingTestData>(entity3), "The entity does not have the component EmptyEcsPersistingTestData BUT the entity did have the component at the time of persisting.");
            
            // Cleanup
            data.Dispose();
        }

        struct EmptyEcsPersistingTestData : IComponentData
        {
            
        }
        
        struct EcsPersistingTestData : IComponentData, IEquatable<EcsTestData>, IEquatable<EcsPersistingTestData>
        {
            public EcsTestData data;
            public bool Equals(EcsTestData other)
            {
                return data.Equals(other);
            }

            public bool Equals(EcsPersistingTestData other)
            {
                return data.Equals(other.data);
            }
            
            public EcsPersistingTestData Modified()
            {
                return new EcsPersistingTestData()
                {
                    data = new EcsTestData(data.value * data.value * data.value)
                };
            }
        }
        
        struct EcsPersistingFloatTestData2 : IComponentData, IEquatable<EcsTestFloatData2>, IEquatable<EcsPersistingFloatTestData2>
        {
            public EcsTestFloatData2 data;

            public bool Equals(EcsTestFloatData2 other)
            {
                return data.Equals(other);
            }

            public bool Equals(EcsPersistingFloatTestData2 other)
            {
                return data.Equals(other.data);
            }
            
            public EcsPersistingFloatTestData2 Modified()
            {
                return new EcsPersistingFloatTestData2()
                {
                    data = new EcsTestFloatData2() { Value0 = data.Value0 + 1.0f, Value1 = data.Value1 + 1.0f}
                };
            }
        }
        
        struct EcsPersistingTestData5 : IComponentData, IEquatable<EcsTestData5>, IEquatable<EcsPersistingTestData5>
        {
            public EcsTestData5 data;

            public bool Equals(EcsTestData5 other)
            {
                return data.Equals(other);
            }

            public bool Equals(EcsPersistingTestData5 other)
            {
                return data.Equals(other.data);
            }

            public EcsPersistingTestData5 Modified()
            {
                return new EcsPersistingTestData5()
                {
                    data = new EcsTestData5(data.value0 * data.value0)
                };
            }
        }
        
        public Entity CreateEntity<T, U>(int index, T value, U value2) 
            where T : struct, IComponentData
            where U : struct, IComponentData
        {
            var entity = m_Manager.CreateEntity(typeof(T), typeof(U), typeof(PersistenceState));
            m_Manager.SetComponentData<T>(entity, value);
            m_Manager.SetComponentData<U>(entity, value2);
            m_Manager.SetComponentData(entity, new PersistenceState() {ArrayIndex = index});
            return entity;
        }

        void CreateEntities(int count)
        {
            for (int i = 0; i != count; i++)
            {
                int zeroOneTwo = i % 3;
                int peristenceIndex = i / 3;
                switch (zeroOneTwo)
                {
                    case 0:
                        var ecsData = new EcsTestData(i);
                        CreateEntity(peristenceIndex, ecsData, new EcsPersistingTestData() { data = ecsData});
                        break;
                    case 1: 
                        var ecsData2 = new EcsTestFloatData2() {Value0 = i * 1.1111f, Value1 = i * 7.7777f};
                        CreateEntity(peristenceIndex, ecsData2, new EcsPersistingFloatTestData2() { data = ecsData2});
                        break;
                    case 2: 
                        var ecsData5 = new EcsTestData5(i * i);
                        CreateEntity(peristenceIndex, ecsData5, new EcsPersistingTestData5() { data = ecsData5});
                        break;
                    default: throw new Exception("zeroOneTwo was not 0, 1 or 2");
                }
            }
        }
    }
}
