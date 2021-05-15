// Author: Jonas De Maeseneer

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

// OPTIMIZED IMPLEMENTATIONS
// *************************

namespace DotsPersistency
{
    [BurstCompile]
    public unsafe struct CopyComponentDataToByteArray : IJobEntityBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public DynamicComponentTypeHandle ComponentTypeHandle;
        public int TypeSize;
        [ReadOnly] 
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> OutputData;
        
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);

            if (batchInChunk.Has(ComponentTypeHandle))
            {
                if (TypeSize > 0)
                {
                    var byteArray = batchInChunk.GetComponentDataAsByteArray(ComponentTypeHandle);
                    // This execute method also updates meta data
                    Execute(OutputData, TypeSize, byteArray, persistenceStateArray);
                }
                else
                {
                    UpdateMetaDataForComponent.Execute(OutputData, persistenceStateArray, 1);
                }
            }
            else
            {
                UpdateMetaDataForComponent.Execute(OutputData, persistenceStateArray, 0);
            }
        }

        public static void Execute(NativeArray<byte> outputData, int typeSize, NativeArray<byte> componentByteArray, NativeArray<PersistenceState> persistenceStateArray)
        {
            const int amountFound = 1;
            int totalElementSize = typeSize + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (persistenceState.ArrayIndex * totalElementSize >= outputData.Length)
                {
                    throw new IndexOutOfRangeException("CopyComponentDataToByteArray:: persistenceState.ArrayIndex seems to be out of range. Or the totalElementSize is wrong.");
                }
#endif
                
                PersistenceMetaData* outputMetaDataBytePtr = (PersistenceMetaData*)((byte*)outputData.GetUnsafePtr() + (persistenceState.ArrayIndex * totalElementSize));
                void* outputDataBytePtr = outputMetaDataBytePtr + 1;
                void* compDataBytePtr =(byte*)componentByteArray.GetUnsafeReadOnlyPtr() + (i * typeSize);
                
                // Diff
                int diff = outputMetaDataBytePtr->AmountFound - amountFound;
                diff |= UnsafeUtility.MemCmp(outputDataBytePtr, compDataBytePtr, typeSize);
                
                // Write Meta Data
                *outputMetaDataBytePtr = new PersistenceMetaData(diff, amountFound); // 1 branch in this constructor
                
                // Write Data
                UnsafeUtility.MemCpy(outputDataBytePtr, compDataBytePtr, typeSize);
            }
        }
    }
    
    public unsafe struct UpdateMetaDataForComponent
    {
        public static void Execute(NativeArray<byte> outputData, NativeArray<PersistenceState> persistenceStateArray, int amountFound)
        {
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (persistenceState.ArrayIndex * PersistenceMetaData.SizeOfStruct >= outputData.Length)
                {
                    throw new IndexOutOfRangeException("UpdateMetaDataForTagComponent:: persistenceState.ArrayIndex seems to be out of range. Or the PersistenceMetaData.SizeOfStruct is wrong.");
                }
#endif
                
                var metaData = UnsafeUtility.ReadArrayElement<PersistenceMetaData>(outputData.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex);
                
                // Diff
                int diff = metaData.AmountFound - amountFound;
                
                // Write Meta Data
                // 1 branch in PersistenceMetaData constructor
                UnsafeUtility.WriteArrayElement(outputData.GetUnsafePtr(), persistenceState.ArrayIndex, new PersistenceMetaData(diff, (ushort)amountFound));
            }
        }
    }

    [BurstCompile]
    public unsafe struct CopyByteArrayToComponentData : IJobEntityBatch
    {
        [NativeDisableContainerSafetyRestriction]
        public DynamicComponentTypeHandle ComponentTypeHandle;
        public ComponentType ComponentType;
        public int TypeSize;
        [ReadOnly] 
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        [ReadOnly]
        public NativeArray<byte> InputData;
        public EntityCommandBuffer.ParallelWriter Ecb;
        [ReadOnly]
        public EntityTypeHandle EntityType;
            
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);
            var entityArray = batchInChunk.GetNativeArray(EntityType);

            if (batchInChunk.Has(ComponentTypeHandle))
            {
                if (TypeSize > 0)
                {
                    var byteArray = batchInChunk.GetComponentDataAsByteArray(ComponentTypeHandle);
                    Execute(InputData, TypeSize, byteArray, persistenceStateArray);
                }
                
                RemoveComponent.Execute(InputData, ComponentType, TypeSize, entityArray, persistenceStateArray, Ecb, batchIndex);
            }
            else
            {
                AddMissingComponent.Execute(InputData, ComponentType, TypeSize, entityArray, persistenceStateArray, Ecb, batchIndex);
            }
        }

        public static void Execute(NativeArray<byte> inputData, int typeSize, NativeArray<byte> componentByteArray, NativeArray<PersistenceState> persistenceStateArray)
        {
            int totalElementSize = typeSize + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (persistenceState.ArrayIndex * totalElementSize >= inputData.Length)
                {
                    throw new IndexOutOfRangeException("CopyByteArrayToComponentData:: persistenceState.ArrayIndex seems to be out of range. Or the totalElementSize is wrong.");
                }
#endif
                
                byte* inputDataPtr = (byte*)inputData.GetUnsafeReadOnlyPtr() + (persistenceState.ArrayIndex * totalElementSize + PersistenceMetaData.SizeOfStruct);
                byte* compDataBytePtr =(byte*)componentByteArray.GetUnsafePtr() + (i * typeSize);
                
                // Write Data
                UnsafeUtility.MemCpy(compDataBytePtr, inputDataPtr, typeSize);
            }
        }
    }
    
    [BurstCompile]
    public unsafe struct RemoveComponent
    {
        public static void Execute(NativeArray<byte> inputData, ComponentType typeToRemove, int typeSize, NativeArray<Entity> entityArray,
            NativeArray<PersistenceState> persistenceStateArray, EntityCommandBuffer.ParallelWriter ecb, int batchIndex)
        {
            for (int i = 0; i < entityArray.Length; i++)
            {
                var persistenceState = persistenceStateArray[i];
                int stride = typeSize + PersistenceMetaData.SizeOfStruct;
                
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (persistenceState.ArrayIndex * stride >= inputData.Length)
                {
                    throw new IndexOutOfRangeException("RemoveComponent:: persistenceState.ArrayIndex seems to be out of range. Or the stride is wrong.");
                }
#endif

                var metaData = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(inputData.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex, stride);
                if (metaData.AmountFound == 0)
                {
                    ecb.RemoveComponent(batchIndex, entityArray[i], typeToRemove);
                }
            }
        }
    }
    
    [BurstCompile]
    public unsafe struct AddMissingComponent
    {
        public static void Execute(NativeArray<byte> inputData, ComponentType componentType, int typeSize, NativeArray<Entity> entityArray,
            NativeArray<PersistenceState> persistenceStateArray, EntityCommandBuffer.ParallelWriter ecb, int batchIndex)
        {
            int totalElementSize = typeSize + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < entityArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                int inputMetaByteIndex = persistenceState.ArrayIndex * totalElementSize;
                int inputDataByteIndex = inputMetaByteIndex + PersistenceMetaData.SizeOfStruct;
                
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (persistenceState.ArrayIndex * totalElementSize >= inputData.Length)
                {
                    throw new IndexOutOfRangeException("AddMissingComponent:: persistenceState.ArrayIndex seems to be out of range. Or the totalElementSize is wrong.");
                }
#endif
                
                var metaData = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(inputData.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex, totalElementSize);

                if (metaData.AmountFound == 1)
                {
                    ecb.AddComponent(batchIndex, entityArray[i], componentType); 
                    if (typeSize != 0)
                    {
                        ecb.SetComponent(batchIndex, entityArray[i], componentType, typeSize, inputData.GetSubArray(inputDataByteIndex, typeSize));
                    }
                }
            }
        }
    }
    
    [BurstCompile]
    public unsafe struct CopyBufferElementsToByteArray : IJobEntityBatch
    {
        [NativeDisableContainerSafetyRestriction, ReadOnly]
        public DynamicComponentTypeHandle BufferTypeHandle;
        
        public int MaxElements;
        [ReadOnly] 
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> OutputData;
            
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);
            
            if (batchInChunk.Has(BufferTypeHandle))
            {
                var untypedBufferAccessor = batchInChunk.GetUntypedBufferAccessor(ref BufferTypeHandle);
                // This execute method also updates meta data
                Execute(OutputData, MaxElements, untypedBufferAccessor, persistenceStateArray);
            }
            else
            {
                UpdateMetaDataForComponent.Execute(OutputData, persistenceStateArray, 0);
            }
        }

        public static void Execute(NativeArray<byte> outputData, int maxElements, UnsafeUntypedBufferAccessor untypedBufferAccessor,
            NativeArray<PersistenceState> persistenceStateArray)
        {
            int elementSize = untypedBufferAccessor.ElementSize;
            int sizePerEntity = elementSize * maxElements + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (persistenceState.ArrayIndex >= outputData.Length / sizePerEntity)
                {
                    throw new IndexOutOfRangeException("CopyBufferElementsToByteArray:: persistenceState.ArrayIndex seems to be out of range. Or the sizePerEntity is wrong.");
                }
#endif
                
                void* bufferDataPtr = untypedBufferAccessor.GetUnsafeReadOnlyPtrAndLength(i, out int length);; 
                
                PersistenceMetaData* outputMetaBytePtr = (PersistenceMetaData*)((byte*) outputData.GetUnsafePtr() + persistenceState.ArrayIndex * sizePerEntity);
                void* outputDataBytePtr = outputMetaBytePtr + 1;
                int amountToCopy = math.clamp(length, 0, maxElements);
                
                // Diff
                int diff = outputMetaBytePtr->AmountFound - amountToCopy;
                diff |= UnsafeUtility.MemCmp(outputDataBytePtr, bufferDataPtr, amountToCopy * elementSize);
                
                // Write Meta Data
                *outputMetaBytePtr = new PersistenceMetaData(diff, (ushort)amountToCopy); // 1 branch in this constructor

                // Write Data
                UnsafeUtility.MemCpy(outputDataBytePtr, bufferDataPtr, elementSize * amountToCopy);
            }
        }
    }
    
    [BurstCompile]
    public unsafe struct CopyByteArrayToBufferElements : IJobEntityBatch
    {
        [NativeDisableContainerSafetyRestriction]
        public DynamicComponentTypeHandle BufferTypeHandle;
        
        public int MaxElements;
        [ReadOnly] 
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<byte> InputData;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            Debug.Assert(batchInChunk.Has(BufferTypeHandle)); // Removing/Adding buffer data is not supported
            var untypedBufferAccessor = batchInChunk.GetUntypedBufferAccessor(ref BufferTypeHandle);
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);
            
            Execute(InputData, MaxElements, untypedBufferAccessor, persistenceStateArray);
        }
        
        public static void Execute(NativeArray<byte> inputData, int maxElements, UnsafeUntypedBufferAccessor untypedBufferAccessor,
            NativeArray<PersistenceState> persistenceStateArray)
        {
            Debug.Assert(maxElements < PersistenceMetaData.MaxValueForAmount);
            int elementSize = untypedBufferAccessor.ElementSize;
            int sizePerEntity = elementSize * maxElements + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (persistenceState.ArrayIndex >= inputData.Length / sizePerEntity)
                {
                    throw new IndexOutOfRangeException("CopyByteArrayToBufferElements:: persistenceState.ArrayIndex seems to be out of range. Or the sizePerEntity is wrong.");
                }
#endif
                
                PersistenceMetaData* inputMetaDataPtr = (PersistenceMetaData*)((byte*) inputData.GetUnsafeReadOnlyPtr() + persistenceState.ArrayIndex * sizePerEntity);
                void* inputDataBytePtr = inputMetaDataPtr + 1; // + 1 because it's a PersistenceMetaData pointer

                // Resize
                int amountToCopy = inputMetaDataPtr->AmountFound;
                untypedBufferAccessor.ResizeUninitialized(i, amountToCopy);
                
                // Get (Possibly modified because of resize) ptr to buffer data
                void* bufferDataPtr = untypedBufferAccessor.GetUnsafePtrAndLength(i, out int length);
                Debug.Assert(length == amountToCopy);
                
                // Write Data
                UnsafeUtility.MemCpy(bufferDataPtr, inputDataBytePtr, elementSize * amountToCopy);
            }
        }
    }
}