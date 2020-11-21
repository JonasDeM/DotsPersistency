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
            
            int totalElementSize = TypeSize + PersistenceMetaData.SizeOfStruct;
            const int amountFound = 1;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                PersistenceMetaData* outputMetaDataBytePtr = (PersistenceMetaData*)((byte*)OutputData.GetUnsafePtr() + (persistenceState.ArrayIndex * totalElementSize));
                void* outputDataBytePtr = outputMetaDataBytePtr + 1;
                void* compDataBytePtr =(byte*)byteArray.GetUnsafeReadOnlyPtr() + (i * TypeSize);
                
                // Diff
                int diff = outputMetaDataBytePtr->AmountFound - amountFound;
                diff |= UnsafeUtility.MemCmp(outputDataBytePtr, compDataBytePtr, TypeSize);
                
                // Write Meta Data
                *outputMetaDataBytePtr = new PersistenceMetaData(diff, amountFound); // 1 branch in this constructor
                
                // Write Data
                UnsafeUtility.MemCpy(outputDataBytePtr,compDataBytePtr, TypeSize);
            }
        }
    }
    
    [BurstCompile]
    public unsafe struct UpdateMetaDataForComponentTag : IJobEntityBatch
    {
        [ReadOnly] 
        public ComponentTypeHandle<PersistenceState> PersistenceStateType;
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> OutputData;
            
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateType);
            const int amountFound = 1;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                var metaData = UnsafeUtility.ReadArrayElement<PersistenceMetaData>(OutputData.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex);
                
                // Diff
                int diff = metaData.AmountFound - amountFound;
                
                // Write Meta Data
                // 1 branch in PersistenceMetaData constructor
                UnsafeUtility.WriteArrayElement(OutputData.GetUnsafePtr(), persistenceState.ArrayIndex, new PersistenceMetaData(diff, amountFound));
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
            
            int totalElementSize = TypeSize + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                byte* inputDataPtr = (byte*)InputData.GetUnsafeReadOnlyPtr() + (persistenceState.ArrayIndex * totalElementSize + PersistenceMetaData.SizeOfStruct);
                byte* compDataBytePtr =(byte*)byteArray.GetUnsafePtr() + (i * TypeSize);
                
                // Write Data
                UnsafeUtility.MemCpy(compDataBytePtr, inputDataPtr, TypeSize);
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
            var entities = batchInChunk.GetNativeArray(EntityType);
            var persistenceStates = batchInChunk.GetNativeArray(PersistenceStateType);
            
            for (int i = 0; i < batchInChunk.Count; i++)
            {
                var persistenceState = persistenceStates[i];
                int stride = TypeSize + PersistenceMetaData.SizeOfStruct;
                var metaData = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(InputData.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex, stride);
                if (metaData.AmountFound == 0)
                {
                    Ecb.RemoveComponent(batchIndex, entities[i], TypeToRemove);
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
            int totalElementSize = TypeSize + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < entityArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                int inputMetaByteIndex = persistenceState.ArrayIndex * totalElementSize;
                int inputDataByteIndex = inputMetaByteIndex + PersistenceMetaData.SizeOfStruct;
                var metaData = UnsafeUtility.ReadArrayElementWithStride<PersistenceMetaData>(InputData.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex, totalElementSize);

                if (metaData.AmountFound == 1)
                {
                    Ecb.AddComponent(batchIndex, entityArray[i], ComponentType); 
                    if (TypeSize != 0)
                    {
                        Ecb.SetComponent(batchIndex, entityArray[i], ComponentType, TypeSize, InputData.GetSubArray(inputDataByteIndex, TypeSize));
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
            
            int sizePerEntity = ElementSize * MaxElements + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                var byteBuffer = untypedBufferAccessor[i];
                void* bufferDataPtr = byteBuffer.GetUnsafePtr(); 
                
                PersistenceMetaData* outputMetaBytePtr = (PersistenceMetaData*)((byte*) OutputData.GetUnsafePtr() + persistenceState.ArrayIndex * sizePerEntity);
                void* outputDataBytePtr = outputMetaBytePtr + 1;
                int amountElements = byteBuffer.Length / ElementSize;
                int amountToCopy = math.clamp(amountElements, 0, MaxElements);
                
                // Diff
                int diff = outputMetaBytePtr->AmountFound - amountToCopy;
                diff |= UnsafeUtility.MemCmp(outputDataBytePtr, bufferDataPtr, amountToCopy * ElementSize);
                
                // Write Meta Data
                *outputMetaBytePtr = new PersistenceMetaData(diff, (ushort)amountToCopy); // 1 branch in this constructor

                // Write Data
                UnsafeUtility.MemCpy(outputDataBytePtr, bufferDataPtr, ElementSize * amountToCopy);
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
            
            Debug.Assert(MaxElements < PersistenceMetaData.MaxValueForAmount);
            int sizePerEntity = ElementSize * MaxElements + PersistenceMetaData.SizeOfStruct;
            
            for (int i = 0; i < persistenceStateArray.Length; i++)
            {
                PersistenceState persistenceState = persistenceStateArray[i];
                Debug.Assert(persistenceState.ArrayIndex < InputData.Length/sizePerEntity);
                PersistenceMetaData* inputMetaDataPtr = (PersistenceMetaData*)((byte*) InputData.GetUnsafeReadOnlyPtr() + persistenceState.ArrayIndex * sizePerEntity);
                void* inputDataBytePtr = inputMetaDataPtr + 1; // + 1 because it's a PersistenceMetaData pointer

                // Resize
                int amountToCopy = inputMetaDataPtr->AmountFound;
                untypedBufferAccessor.ResizeBufferUninitialized(i, amountToCopy);
                
                // Get (Possibly modified because of resize) ptr to buffer data
                var byteBuffer = untypedBufferAccessor[i];
                void* bufferDataPtr = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(byteBuffer); 
                
                // Write Data
                UnsafeUtility.MemCpy(bufferDataPtr, inputDataBytePtr, ElementSize * amountToCopy);
            }
        }
    }
}

// REFERENCE IMPLEMENTATIONS (No MetaData)
// ***************************************
namespace DotsPersistency
{
    // [BurstCompile]
    // public unsafe struct CopyComponentDataToByteArray : IJobChunk
    // {
    //     [ReadOnly, NativeDisableParallelForRestriction]
    //     public DynamicComponentTypeHandle ComponentTypeHandle;
    //     public int TypeSize;
    //     [ReadOnly] 
    //     public ComponentTypeHandle<PersistenceState> PersistenceStateType;
    //     [WriteOnly, NativeDisableParallelForRestriction]
    //     public NativeArray<byte> OutputData;
    //     [WriteOnly, NativeDisableParallelForRestriction]
    //     public NativeArray<bool> OutputFound;
    //         
    //     public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
    //     {
    //         var byteArray = chunk.GetComponentDataAsByteArray(ComponentTypeHandle);
    //         var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
    //         for (int i = 0; i < persistenceStateArray.Length; i++)
    //         {
    //             PersistenceState persistenceState = persistenceStateArray[i];
    //             int outputByteIndex = persistenceState.ArrayIndex * TypeSize;
    //             int compDataByteIndex = i * TypeSize;
    // 
    //             UnsafeUtility.MemCpy((byte*)OutputData.GetUnsafePtr() + outputByteIndex, (byte*)byteArray.GetUnsafeReadOnlyPtr() + compDataByteIndex, TypeSize);
    //             OutputFound[persistenceState.ArrayIndex] = true;
    //         }
    //     }
    // }
    
    // [BurstCompile]
    // public unsafe struct CopyBufferElementsToByteArray : IJobChunk
    // {
    //     [NativeDisableParallelForRestriction, ReadOnly]
    //     public ArchetypeChunkBufferDataTypeDynamic BufferTypeHandle;
    //     public int ElementSize;
    //     public int MaxElements;
    //     [ReadOnly] 
    //     public ComponentTypeHandle<PersistenceState> PersistenceStateType;
    //     [WriteOnly, NativeDisableParallelForRestriction]
    //     public NativeArray<byte> OutputData;
    //     [WriteOnly, NativeDisableParallelForRestriction]
    //     public NativeArray<int> AmountPersisted;
    //         
    //     public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
    //     {
    //         var untypedBufferAccessor = chunk.GetUntypedBufferAccessor(BufferTypeHandle);
    //         var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
    //         for (int i = 0; i < persistenceStateArray.Length; i++)
    //         {
    //             PersistenceState persistenceState = persistenceStateArray[i];
    //             var untypedBuffer = untypedBufferAccessor[i];
    //             int outputByteIndex = persistenceState.ArrayIndex * ElementSize * MaxElements;
    //             int amountElements = untypedBuffer.Length / ElementSize;
    //             
    //             // todo instead of clamp, Malloc when amountElements exceeds MaxElements (Rename MaxElements to InternalCapacity)
    //             // make sure OutputData can store a pointer per entity & then just store the pointer
    //             // be sure to dispose of the memory when AmountPersisted exceeds InternalCapacity
    //             int amountToCopy = math.clamp(amountElements, 0, MaxElements);
    //             
    //             // when safety check bug is fixed move this back to .GetUnsafePtr
    //             UnsafeUtility.MemCpy((byte*)OutputData.GetUnsafePtr() + outputByteIndex, NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(untypedBuffer), ElementSize * amountToCopy);
    //             AmountPersisted[persistenceState.ArrayIndex] = amountToCopy;
    //         }
    //     }
    // }

    //[BurstCompile]
    //public struct FindPersistentEntities : IJobForEach<PersistenceState>
    //{
    //    [WriteOnly, NativeDisableParallelForRestriction]
    //    public NativeArray<bool> OutputFound;
    //
    //    public void Execute([ReadOnly] ref PersistenceState persistenceState)
    //    {
    //        OutputFound[persistenceState.ArrayIndex] = true;
    //    }
    //}
    
    // [BurstCompile]
    // public struct AddMissingComponent : IJobChunk
    // {        
    //     [ReadOnly, NativeDisableParallelForRestriction]
    //     public EntityTypeHandle EntityType;
    //     [ReadOnly] 
    //     public ComponentTypeHandle<PersistenceState> PersistenceStateType;
    //     public EntityCommandBuffer.ParallelWriter Ecb;
    //     public ComponentType ComponentType;
    //     public int TypeSize;
    //     [ReadOnly, NativeDisableParallelForRestriction]
    //     public NativeArray<byte> InputData;
    //     [ReadOnly, NativeDisableParallelForRestriction]
    //     public NativeArray<bool> InputFound;
    //         
    //     public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
    //     {
    //         var entityArray = chunk.GetNativeArray(EntityType);
    //         var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
    //         for (int i = 0; i < entityArray.Length; i++)
    //         {
    //             PersistenceState persistenceState = persistenceStateArray[i];
    //             int inputByteIndex = persistenceState.ArrayIndex * TypeSize;
    // 
    //             if (InputFound[persistenceState.ArrayIndex])
    //             {
    //                 Ecb.AddComponent(chunkIndex, entityArray[i], ComponentType); 
    //                 if (TypeSize != 0)
    //                 {
    //                     Ecb.SetComponent(chunkIndex, entityArray[i], ComponentType, TypeSize, InputData.GetSubArray(inputByteIndex, TypeSize));
    //                 }
    //             }
    //         }
    //     }
    // }
    
    // [BurstCompile]
    // public struct RemoveComponent : IJobForEachWithEntity<PersistenceState>
    // {        
    //     public EntityCommandBuffer.ParallelWriter Ecb;
    //     public ComponentType ComponentType;
    //     [ReadOnly, NativeDisableParallelForRestriction]
    //     public NativeArray<bool> InputFound;
    //     
    //     public void Execute(Entity entity, int index, [ReadOnly] ref PersistenceState persistenceState)
    //     {
    //         if (!InputFound[persistenceState.ArrayIndex])
    //         {
    //             Ecb.RemoveComponent(index, entity, ComponentType);
    //         }
    //     }
    // }

    // [BurstCompile]
    // public unsafe struct CopyByteArrayToComponentData : IJobChunk
    // {
    //     [NativeDisableContainerSafetyRestriction]
    //     public DynamicComponentTypeHandle ComponentTypeHandle;
    //     public int TypeSize;
    //     [ReadOnly] 
    //     public ComponentTypeHandle<PersistenceState> PersistenceStateType;
    //     [ReadOnly]
    //     public NativeArray<byte> Input;
    //         
    //     public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
    //     {
    //         var byteArray = chunk.GetComponentDataAsByteArray(ComponentTypeHandle);
    //         var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
    //         
    //         for (int i = 0; i < persistenceStateArray.Length; i++)
    //         {
    //             PersistenceState persistenceState = persistenceStateArray[i];
    //             int inputByteIndex = persistenceState.ArrayIndex * TypeSize;
    //             int compDataByteIndex = i * TypeSize;
    // 
    //             //var value1 = UnsafeUtility.ReadArrayElement<Translation>((byte*)Input.GetUnsafeReadOnlyPtr(), persistenceState.ArrayIndex).Value;
    //             UnsafeUtility.MemCpy((byte*)byteArray.GetUnsafePtr() + compDataByteIndex, (byte*)Input.GetUnsafeReadOnlyPtr() + inputByteIndex, TypeSize);
    //             //var value2 = UnsafeUtility.ReadArrayElement<Translation>(ptr, i).Value;
    //             //Debug.Log(persistenceState.ArrayIndex + " - " + value1 + " - " + value2);
    //         }
    //     }
    // }

     // [BurstCompile]
     // public unsafe struct CopyByteArrayToBufferElements : IJobChunk
     // {
     //     [NativeDisableContainerSafetyRestriction]
     //     public ArchetypeChunkBufferDataTypeDynamic BufferTypeHandle;
     //     
     //     public int ElementSize;
     //     public int MaxElements;
     //     [ReadOnly] 
     //     public ComponentTypeHandle<PersistenceState> PersistenceStateType;
     //     [ReadOnly, NativeDisableParallelForRestriction]
     //     public NativeArray<byte> InputData;
     //     [ReadOnly, NativeDisableParallelForRestriction]
     //     public NativeArray<int> AmountPersisted;
     // 
     //     public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
     //     {
     //         var untypedBufferAccessor = chunk.GetUntypedBufferAccessor(BufferTypeHandle);
     //         var persistenceStateArray = chunk.GetNativeArray(PersistenceStateType);
     //         for (int i = 0; i < persistenceStateArray.Length; i++)
     //         {
     //             PersistenceState persistenceState = persistenceStateArray[i];
     //             int inputByteIndex = persistenceState.ArrayIndex * ElementSize * MaxElements;
     //             
     //             int amountToCopy = AmountPersisted[persistenceState.ArrayIndex];
     //             untypedBufferAccessor.ResizeBufferUninitialized(i, amountToCopy);
     //             
     //             // This needs to happen after the ResizeBufferUninitialized
     //             var untypedBuffer = untypedBufferAccessor[i];
     // 
     //             // when safety check bug is fixed move this back to .GetUnsafePtr
     //             UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(untypedBuffer), (byte*)InputData.GetUnsafeReadOnlyPtr() + inputByteIndex, ElementSize * amountToCopy);
     //         }
     //     }
     // }
}