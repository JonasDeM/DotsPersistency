// Author: Jonas De Maeseneer

using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
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
            var byteArray = batchInChunk.GetComponentDataAsByteArray(ComponentTypeHandle);
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);
            
            Execute(OutputData, TypeSize, byteArray, persistenceStateArray);
        }

        public static void Execute(NativeArray<byte> outputData, int typeSize, NativeArray<byte> componentByteArray, NativeArray<PersistenceState> persistenceStateArray)
        {
            int totalElementSize = typeSize + PersistenceMetaData.SizeOfStruct;
            const int amountFound = 1;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
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
    
    [BurstCompile]
    public unsafe struct UpdateMetaDataForTagComponent : IJobEntityBatch
    {
        [ReadOnly] 
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> OutputData;
            
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);

            Execute(OutputData, persistenceStateArray);
        }
        
        public static void Execute(NativeArray<byte> outputData, NativeArray<PersistenceState> persistenceStateArray)
        {
            const int amountFound = 1;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                var metaData = UnsafeUtility.ReadArrayElement<PersistenceMetaData>(outputData.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex);
                
                // Diff
                int diff = metaData.AmountFound - amountFound;
                
                // Write Meta Data
                // 1 branch in PersistenceMetaData constructor
                UnsafeUtility.WriteArrayElement(outputData.GetUnsafePtr(), persistenceState.ArrayIndex, new PersistenceMetaData(diff, amountFound));
            }
        }
    }
    
    [BurstCompile]
    public unsafe struct CopyByteArrayToComponentData : IJobEntityBatch
    {
        [NativeDisableContainerSafetyRestriction]
        public DynamicComponentTypeHandle ComponentTypeHandle;
        public int TypeSize;
        [ReadOnly] 
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        [ReadOnly]
        public NativeArray<byte> InputData;
            
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var byteArray = batchInChunk.GetComponentDataAsByteArray(ComponentTypeHandle);
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);

            Execute(InputData, TypeSize, byteArray, persistenceStateArray);
        }

        public static void Execute(NativeArray<byte> inputData, int typeSize, NativeArray<byte> componentByteArray, NativeArray<PersistenceState> persistenceStateArray)
        {
            int totalElementSize = typeSize + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                byte* inputDataPtr = (byte*)inputData.GetUnsafeReadOnlyPtr() + (persistenceState.ArrayIndex * totalElementSize + PersistenceMetaData.SizeOfStruct);
                byte* compDataBytePtr =(byte*)componentByteArray.GetUnsafePtr() + (i * typeSize);
                
                // Write Data
                UnsafeUtility.MemCpy(compDataBytePtr, inputDataPtr, typeSize);
            }
        }
    }
    
    [BurstCompile]
    public unsafe struct RemoveComponent : IJobEntityBatch
    {        
        public ComponentType TypeToRemove;
        public int TypeSize;
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<byte> InputData;

        public EntityCommandBuffer.ParallelWriter Ecb;
        [ReadOnly]
        public EntityTypeHandle EntityType;
        [ReadOnly]
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var entityArray = batchInChunk.GetNativeArray(EntityType);
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);

            Execute(InputData, TypeToRemove, TypeSize, entityArray, persistenceStateArray, Ecb, batchIndex);
        }

        public static void Execute(NativeArray<byte> inputData, ComponentType typeToRemove, int typeSize, NativeArray<Entity> entityArray,
            NativeArray<PersistenceState> persistenceStateArray, EntityCommandBuffer.ParallelWriter ecb, int batchIndex)
        {
            for (int i = 0; i < entityArray.Length; i++)
            {
                var persistenceState = persistenceStateArray[i];
                int stride = typeSize + PersistenceMetaData.SizeOfStruct;
                var metaData = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(inputData.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex, stride);
                if (metaData.AmountFound == 0)
                {
                    ecb.RemoveComponent(batchIndex, entityArray[i], typeToRemove);
                }
            }
        }
    }
    
    [BurstCompile]
    public unsafe struct AddMissingComponent : IJobEntityBatch
    {        
        [ReadOnly, NativeDisableParallelForRestriction]
        public EntityTypeHandle EntityType;
        [ReadOnly] 
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        public EntityCommandBuffer.ParallelWriter Ecb;
        public ComponentType ComponentType;
        public int TypeSize;
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<byte> InputData;
            
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var entityArray = batchInChunk.GetNativeArray(EntityType);
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);
            
            Execute(InputData, ComponentType, TypeSize, entityArray, persistenceStateArray, Ecb, batchIndex);
        }

        public static void Execute(NativeArray<byte> inputData, ComponentType componentType, int typeSize, NativeArray<Entity> entityArray,
            NativeArray<PersistenceState> persistenceStateArray, EntityCommandBuffer.ParallelWriter ecb, int batchIndex)
        {
            int totalElementSize = typeSize + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < entityArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                int inputMetaByteIndex = persistenceState.ArrayIndex * totalElementSize;
                int inputDataByteIndex = inputMetaByteIndex + PersistenceMetaData.SizeOfStruct;
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
        public DynamicBufferTypeHandle BufferTypeHandle;
        public int ElementSize;
        public int MaxElements;
        [ReadOnly] 
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> OutputData;
            
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var untypedBufferAccessor = batchInChunk.GetUntypedBufferAccessor(BufferTypeHandle);
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);

            Execute(OutputData, ElementSize, MaxElements, untypedBufferAccessor, persistenceStateArray);
        }

        public static void Execute(NativeArray<byte> outputData, int elementSize, int maxElements, UntypedBufferAccessor untypedBufferAccessor,
            NativeArray<PersistenceState> persistenceStateArray)
        {
            int sizePerEntity = elementSize * maxElements + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                var byteBuffer = untypedBufferAccessor[i];
                void* bufferDataPtr = byteBuffer.GetUnsafePtr(); 
                
                PersistenceMetaData* outputMetaBytePtr = (PersistenceMetaData*)((byte*) outputData.GetUnsafePtr() + persistenceState.ArrayIndex * sizePerEntity);
                void* outputDataBytePtr = outputMetaBytePtr + 1;
                int amountElements = byteBuffer.Length / elementSize;
                int amountToCopy = math.clamp(amountElements, 0, maxElements);
                
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
        public DynamicBufferTypeHandle BufferTypeHandle;
        
        public int ElementSize;
        public int MaxElements;
        [ReadOnly] 
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<byte> InputData;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var untypedBufferAccessor = batchInChunk.GetUntypedBufferAccessor(BufferTypeHandle);
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);
            
            Execute(InputData, ElementSize, MaxElements, untypedBufferAccessor, persistenceStateArray);
        }
        
        public static void Execute(NativeArray<byte> inputData, int elementSize, int maxElements, UntypedBufferAccessor untypedBufferAccessor,
            NativeArray<PersistenceState> persistenceStateArray)
        {
            Debug.Assert(maxElements < PersistenceMetaData.MaxValueForAmount);
            int sizePerEntity = elementSize * maxElements + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                Debug.Assert(persistenceState.ArrayIndex < inputData.Length/sizePerEntity);
                PersistenceMetaData* inputMetaDataPtr = (PersistenceMetaData*)((byte*) inputData.GetUnsafeReadOnlyPtr() + persistenceState.ArrayIndex * sizePerEntity);
                void* inputDataBytePtr = inputMetaDataPtr + 1; // + 1 because it's a PersistenceMetaData pointer

                // Resize
                int amountToCopy = inputMetaDataPtr->AmountFound;
                untypedBufferAccessor.ResizeBufferUninitialized(i, amountToCopy);
                
                // Get (Possibly modified because of resize) ptr to buffer data
                var byteBuffer = untypedBufferAccessor[i];
                void* bufferDataPtr = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(byteBuffer); 
                
                // Write Data
                UnsafeUtility.MemCpy(bufferDataPtr, inputDataBytePtr, elementSize * amountToCopy);
            }
        }
    }
}