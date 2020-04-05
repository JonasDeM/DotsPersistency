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
    class BufferDataTests : EcsTestsFixture
    {
        [Test]
        public unsafe void TestReadBufferData([Values(0, 1, 2, 3, 60, 400)] int total)
        {
            CreateEntities(total);

            int maxElements = total + 4;
            
            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            
            var array1Data = new NativeArray<byte>(entityAmount1 * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData1>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);
            var array2Data = new NativeArray<byte>(entityAmount2 * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData2>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);
            var array3Data = new NativeArray<byte>(entityAmount3 * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData3>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);
            
            var query1 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData1), typeof(PersistenceState));
            var query2 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData2), typeof(PersistenceState));
            var query3 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData3), typeof(PersistenceState));

            const int deletedIndex = 5;
            Entities.WithAll<PersistenceState>().ForEach(entity =>
            {
                int index = m_Manager.GetComponentData<PersistenceState>(entity).ArrayIndex;
                if (index == deletedIndex)
                {
                    m_Manager.DestroyEntity(entity);
                }
            });
            
            // Action
            var job1 = new CopyBufferElementsToByteArray()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array1Data,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData1)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData1>().ElementSize,
                MaxElements = maxElements
            }.Schedule(query1);
            
            var job2 = new CopyBufferElementsToByteArray()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array2Data,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData2)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData2>().ElementSize,
                MaxElements = maxElements
            }.Schedule(query2);
            
            var job3 = new CopyBufferElementsToByteArray()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                OutputData = array3Data,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData3)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData3>().ElementSize,
                MaxElements = maxElements
            }.Schedule(query3);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            
            // Check Results
            Entities.With(query1).ForEach(entity =>
            {
                var originalData = m_Manager.GetBuffer<PersistentDynamicBufferData1>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                var byteIndex = persistenceState.ArrayIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData1>() + PersistenceMetaData.SizeOfStruct);

                var metaDataPtr = (PersistenceMetaData*)((byte*) array1Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData1*)(metaDataPtr + 1);
                
                Assert.AreEqual(originalData.Length, metaDataPtr->AmountFound, "CopyBufferElementsToByteArray returned the wrong amount persisted.");
                Assert.True(metaDataPtr->HasChanged, "CopyBufferElementsToByteArray did not record a change when it should have.");

                for (var i = 0; i < originalData.Length; i++)
                {
                    if (i < maxElements)
                    {
                        var originalElement = originalData[i];
                        Assert.AreEqual(originalElement, *(dataPtr + i), "Data output by CopyBufferElementsToByteArray does not match data on entity.");
                    }
                }
            });
            
            Entities.With(query2).ForEach(entity =>
            {
                var originalData = m_Manager.GetBuffer<PersistentDynamicBufferData2>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                var byteIndex = persistenceState.ArrayIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData2>() + PersistenceMetaData.SizeOfStruct);

                var metaDataPtr = (PersistenceMetaData*)((byte*) array2Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData2*)(metaDataPtr + 1);
                
                Assert.AreEqual(originalData.Length, metaDataPtr->AmountFound, "CopyBufferElementsToByteArray returned the wrong amount persisted.");
                // this query only has empty buffers
                Assert.False(metaDataPtr->HasChanged, "CopyBufferElementsToByteArray recorded a change while it should not have.");

                for (var i = 0; i < originalData.Length; i++)
                {
                    if (i < maxElements)
                    {
                        var originalElement = originalData[i];
                        Assert.AreEqual(originalElement, *(dataPtr + i), "Data output by CopyBufferElementsToByteArray does not match data on entity.");
                    }
                }
            });
            
            Entities.With(query3).ForEach(entity =>
            {
                var originalData = m_Manager.GetBuffer<PersistentDynamicBufferData3>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                var byteIndex = persistenceState.ArrayIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData3>() + PersistenceMetaData.SizeOfStruct);

                var metaDataPtr = (PersistenceMetaData*)((byte*) array3Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData3*)(metaDataPtr + 1);
                
                Assert.AreEqual(Mathf.Clamp(originalData.Length, 0, maxElements), metaDataPtr->AmountFound, "CopyBufferElementsToByteArray returned the wrong amount persisted.");
                Assert.True(metaDataPtr->HasChanged, "CopyBufferElementsToByteArray did not record a change when it should have.");

                for (var i = 0; i < originalData.Length; i++)
                {
                    if (i < maxElements)
                    {
                        var originalElement = originalData[i];
                        Assert.AreEqual(originalElement, *(dataPtr + i), "Data output by CopyBufferElementsToByteArray does not match data on entity.");
                    }
                }
            });

            int stride1 = (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData1>() + PersistenceMetaData.SizeOfStruct);
            if (array1Data.Length >= stride1 * deletedIndex)
            {
                bool hasChanged = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(array1Data.GetUnsafeReadOnlyPtr(), deletedIndex, stride1).HasChanged;
                Assert.False(hasChanged, "CopyBufferElementsToByteArray recorded a change while it should not have.");
            }
            
            int stride2 = (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData2>() + PersistenceMetaData.SizeOfStruct);
            if (array2Data.Length >= stride2 * deletedIndex)
            {
                bool hasChanged = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(array2Data.GetUnsafeReadOnlyPtr(), deletedIndex, stride2).HasChanged;
                Assert.False(hasChanged, "CopyBufferElementsToByteArray recorded a change while it should not have.");
            }
            
            int stride3 = (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData3>() + PersistenceMetaData.SizeOfStruct);
            if (array3Data.Length >= stride3 * deletedIndex)
            {
                bool hasChanged = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(array3Data.GetUnsafeReadOnlyPtr(), deletedIndex, stride3).HasChanged;
                Assert.False(hasChanged, "CopyBufferElementsToByteArray recorded a change while it should not have.");
            }

            // Cleanup
            array1Data.Dispose();
            array2Data.Dispose();
            array3Data.Dispose();
            m_Manager.DestroyEntity(m_Manager.CreateEntityQuery(typeof(PersistenceState)));
        }
        
        [Test]
        public unsafe void TestApplyStoredBufferData([Values(0, 1, 2, 3, 60, 400)] int total)
        {
            CreateEntities(total);

            int maxElements = total + 4;
            ushort persistedElementAmount = (ushort)total;
            
            int entityAmount3 = total / 3;
            int entityAmount1 = entityAmount3 + (total%3 > 0 ? 1 : 0);
            int entityAmount2 = entityAmount3 + (total%3 > 1 ? 1 : 0);
            
            var array1Data = new NativeArray<byte>(entityAmount1 * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData1>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);
            var array2Data = new NativeArray<byte>(entityAmount2 * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData2>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);
            var array3Data = new NativeArray<byte>(entityAmount3 * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData3>() + PersistenceMetaData.SizeOfStruct), Allocator.TempJob);

            for (int i = 0; i < entityAmount1; i++)
            {
                PersistenceMetaData* mateDataPtr = (PersistenceMetaData*)((byte*)array1Data.GetUnsafePtr() + i * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData1>() + PersistenceMetaData.SizeOfStruct));
                *mateDataPtr = new PersistenceMetaData(0, persistedElementAmount);
                PersistentDynamicBufferData1* dataPtr = (PersistentDynamicBufferData1*)(mateDataPtr + 1);
                for (int j = 0; j < maxElements; j++)
                {
                    *(dataPtr + j) = new PersistentDynamicBufferData1 {Value = i};
                }
            }
            for (int i = 0; i < entityAmount2; i++)
            {
                PersistenceMetaData* mateDataPtr = (PersistenceMetaData*)((byte*)array2Data.GetUnsafePtr() + i * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData2>() + PersistenceMetaData.SizeOfStruct));
                *mateDataPtr = new PersistenceMetaData(0, persistedElementAmount);
                PersistentDynamicBufferData2* dataPtr = (PersistentDynamicBufferData2*)(mateDataPtr + 1);
                for (int j = 0; j < maxElements; j++)
                {
                    *(dataPtr + j) = new PersistentDynamicBufferData2 {Value = i};
                }
            }
            for (int i = 0; i < entityAmount3; i++)
            {
                PersistenceMetaData* mateDataPtr = (PersistenceMetaData*)((byte*)array3Data.GetUnsafePtr() + i * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData3>() + PersistenceMetaData.SizeOfStruct));
                *mateDataPtr = new PersistenceMetaData(0, persistedElementAmount);
                PersistentDynamicBufferData3* dataPtr = (PersistentDynamicBufferData3*)(mateDataPtr + 1);
                for (int j = 0; j < maxElements; j++)
                {
                    *(dataPtr + j) = new PersistentDynamicBufferData3 {Value = (byte)i};
                }
            }

            var query1 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData1), typeof(PersistenceState));
            var query2 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData2), typeof(PersistenceState));
            var query3 = m_Manager.CreateEntityQuery(typeof(PersistentDynamicBufferData3), typeof(PersistenceState));
            
            // Action
            var job1 = new CopyByteArrayToBufferElements()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                InputData = array1Data,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData1)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData1>().ElementSize,
                MaxElements = maxElements
            }.Schedule(query1);
            
            var job2 = new CopyByteArrayToBufferElements()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                InputData = array2Data,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData2)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData2>().ElementSize,
                MaxElements = maxElements
            }.Schedule(query2);
            
            var job3 = new CopyByteArrayToBufferElements()
            {
                PersistenceStateType = m_Manager.GetArchetypeChunkComponentType<PersistenceState>(true),
                InputData = array3Data,
                ChunkBufferType = m_Manager.GetArchetypeChunkBufferTypeDynamic(typeof(PersistentDynamicBufferData3)),
                ElementSize = TypeManager.GetTypeInfo<PersistentDynamicBufferData3>().ElementSize,
                MaxElements = maxElements
            }.Schedule(query3);
            
            JobHandle.CombineDependencies(job1, job2, job3).Complete();
            
            // Check Results
            Entities.With(query1).ForEach(entity =>
            {
                var newData = m_Manager.GetBuffer<PersistentDynamicBufferData1>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                var byteIndex = persistenceState.ArrayIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData1>() + PersistenceMetaData.SizeOfStruct);

                var metaDataPtr = (PersistenceMetaData*)((byte*) array1Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData1*)(metaDataPtr + 1);

                Assert.AreEqual(metaDataPtr->AmountFound, newData.Length, "CopyByteArrayToBufferElements made a buffer that was the wrong size.");

                for (var i = 0; i < newData.Length; i++)
                {
                    Assert.AreEqual(newData[i], *(dataPtr+i), "Data on entity set by CopyBufferElementsToByteArray does not match data in InputArray.");
                }
            });
            
            Entities.With(query2).ForEach(entity =>
            {
                var newData = m_Manager.GetBuffer<PersistentDynamicBufferData2>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                var byteIndex = persistenceState.ArrayIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData2>() + PersistenceMetaData.SizeOfStruct);

                var metaDataPtr = (PersistenceMetaData*)((byte*) array2Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData2*)(metaDataPtr + 1);

                Assert.AreEqual(metaDataPtr->AmountFound, newData.Length, "CopyByteArrayToBufferElements made a buffer that was the wrong size.");

                for (var i = 0; i < newData.Length; i++)
                {
                    Assert.AreEqual(newData[i], *(dataPtr+i), "Data on entity set by CopyBufferElementsToByteArray does not match data in InputArray.");
                }
            });
            
            Entities.With(query3).ForEach(entity =>
            {
                var newData = m_Manager.GetBuffer<PersistentDynamicBufferData3>(entity);
                var persistenceState = m_Manager.GetComponentData<PersistenceState>(entity);
                var byteIndex = persistenceState.ArrayIndex * (maxElements * UnsafeUtility.SizeOf<PersistentDynamicBufferData3>() + PersistenceMetaData.SizeOfStruct);

                var metaDataPtr = (PersistenceMetaData*)((byte*) array3Data.GetUnsafeReadOnlyPtr() + byteIndex);
                var dataPtr = (PersistentDynamicBufferData3*)(metaDataPtr + 1);
                
                Assert.AreEqual(metaDataPtr->AmountFound, newData.Length, "CopyByteArrayToBufferElements made a buffer that was the wrong size.");
                
                for (var i = 0; i < newData.Length; i++)
                {
                    Assert.AreEqual(newData[i], *(dataPtr+i), "Data on entity set by CopyBufferElementsToByteArray does not match data in InputArray.");
                }
            });

            // Cleanup
            array1Data.Dispose();
            array2Data.Dispose();
            array3Data.Dispose();
            m_Manager.DestroyEntity(m_Manager.CreateEntityQuery(typeof(PersistenceState)));
        }
        
        public Entity CreateEntity<T, U>(int index) 
            where T : struct, IBufferElementData
            where U : struct, IBufferElementData
        {
            var entity = m_Manager.CreateEntity(typeof(T), typeof(U), typeof(PersistenceState));
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
                        var e1 = CreateEntity<DynamicBufferData1, PersistentDynamicBufferData1>(peristenceIndex);
                        var buffer1 = m_Manager.GetBuffer<DynamicBufferData1>(e1);
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 1});
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 2});
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 3});
                        buffer1.Add(new DynamicBufferData1() {Value = peristenceIndex * 4});
                        var buffer1P = m_Manager.GetBuffer<PersistentDynamicBufferData1>(e1);
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 1});
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 2});
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 3});
                        buffer1P.Add(new PersistentDynamicBufferData1() {Value = peristenceIndex * 4});
                        break;
                    case 1: 
                        CreateEntity<DynamicBufferData2, PersistentDynamicBufferData2>(peristenceIndex);
                        break;
                    case 2: 
                        var e3 = CreateEntity<DynamicBufferData3, PersistentDynamicBufferData3>(peristenceIndex);
                        var buffer3 = m_Manager.GetBuffer<DynamicBufferData3>(e3);
                        buffer3.Add(new DynamicBufferData3() {Value = 1});
                        buffer3.Add(new DynamicBufferData3() {Value = 2});
                        buffer3.Add(new DynamicBufferData3() {Value = 3});
                        buffer3.Add(new DynamicBufferData3() {Value = 4});
                        buffer3.Add(new DynamicBufferData3() {Value = 5});
                        var buffer3P = m_Manager.GetBuffer<PersistentDynamicBufferData3>(e3);
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 1});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 2});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 3});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 4});
                        buffer3P.Add(new PersistentDynamicBufferData3() {Value = 5});
                        break;
                    default: throw new Exception("zeroOneTwo was not 0, 1 or 2");
                }
            }
        }
        
        [InternalBufferCapacity(2)]
        struct DynamicBufferData1 : IBufferElementData
        {
            public int Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        struct DynamicBufferData2 : IBufferElementData
        {
#pragma warning disable 649
            public float Value;
#pragma warning restore 649
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        struct DynamicBufferData3 : IBufferElementData
        {
            public byte Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        
        [InternalBufferCapacity(2)]
        struct PersistentDynamicBufferData1 : IBufferElementData
        {
            public int Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        struct PersistentDynamicBufferData2 : IBufferElementData
        {
            public float Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
        struct PersistentDynamicBufferData3 : IBufferElementData
        {
            public byte Value;
            
            public override string ToString()
            {
                return Value.ToString();
            }
        }
    }
}
