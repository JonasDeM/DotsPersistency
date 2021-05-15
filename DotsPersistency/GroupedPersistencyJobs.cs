// Author: Jonas De Maeseneer

using System;
using System.Diagnostics;
using DotsPersistency.Containers;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Debug = UnityEngine.Debug;

namespace DotsPersistency
{
    // This job uses nested native containers, the unity job safety system doesn't support that, so we only run these jobs in a build.
    [BurstCompile]
    public struct GroupedApplyJob : IJobEntityBatch
    {
        public PersistentDataContainer DataContainer;
        
        [ReadOnly] public EntityTypeHandle EntityTypeHandle;
        [ReadOnly] public ComponentTypeHandle<PersistenceState> PersistenceStateTypeHandle;
        [ReadOnly] public ComponentTypeHandle<PersistencyArchetypeIndexInContainer> PersistencyArchetypeIndexInContainerTypeHandle;
        
        [DeallocateOnJobCompletion]
        [ReadOnly] public ComponentTypeHandleArray DynamicComponentTypeHandles;

        [ReadOnly] public PersistencySettings.TypeIndexLookup TypeIndexLookup;
        
        public EntityCommandBuffer.ParallelWriter Ecb;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            NativeArray<PersistenceState> persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateTypeHandle);
            NativeArray<Entity> entityArray = batchInChunk.GetNativeArray(EntityTypeHandle);
            
            NativeArray<PersistencyArchetypeIndexInContainer> archetypeIndexInContainerArray = batchInChunk.GetNativeArray(PersistencyArchetypeIndexInContainerTypeHandle);
            ValidateArchetypeIndexInContainer(archetypeIndexInContainerArray);
            
            PersistencyArchetypeDataLayout dataLayout = DataContainer.GetDataLayoutAtIndex(archetypeIndexInContainerArray[0].Index);
            ref BlobArray<PersistencyArchetypeDataLayout.TypeInfo> typeInfoArray = ref dataLayout.PersistedTypeInfoArrayRef.Value;
            var dataForArchetype = DataContainer.GetSubArrayAtIndex(dataLayout.ArchetypeIndexInContainer);

            for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
            {
                // type info
                PersistencyArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                ComponentType runtimeType = ComponentType.ReadWrite(TypeIndexLookup.GetTypeIndex(typeInfo.PersistableTypeHandle)); 
                int stride = typeInfo.ElementSize * typeInfo.MaxElements + PersistenceMetaData.SizeOfStruct;
                int byteSize = dataLayout.Amount * stride;

                // Grab read-only containers
                var inputData = dataForArchetype.GetSubArray(typeInfo.Offset, byteSize);
                
                bool found = DynamicComponentTypeHandles.GetByTypeIndex(runtimeType.TypeIndex, out DynamicComponentTypeHandle typeHandle);
                Debug.Assert(found);

                if (typeInfo.IsBuffer)
                {
                    // Debug.Assert(batchInChunk.Has(typeHandle)); 
                    if (batchInChunk.Has(typeHandle))
                    {
                        var untypedBufferAccessor = batchInChunk.GetUntypedBufferAccessor(ref typeHandle);
                        CopyByteArrayToBufferElements.Execute(inputData, typeInfo.MaxElements, untypedBufferAccessor, persistenceStateArray);
                    }
                }
                else // if ComponentData
                {
                    if (batchInChunk.Has(typeHandle))
                    {
                        if (typeInfo.ElementSize > 0)
                        {
                            var byteArray = batchInChunk.GetComponentDataAsByteArray(typeHandle);
                            CopyByteArrayToComponentData.Execute(inputData, typeInfo.ElementSize, byteArray, persistenceStateArray);
                        }

                        RemoveComponent.Execute(inputData, runtimeType, typeInfo.ElementSize, entityArray, persistenceStateArray, Ecb, batchIndex);
                    }
                    else
                    {
                        AddMissingComponent.Execute(inputData, runtimeType,  typeInfo.ElementSize, entityArray, persistenceStateArray, Ecb, batchIndex);
                    }
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void ValidateArchetypeIndexInContainer(NativeArray<PersistencyArchetypeIndexInContainer> dataForOneChunk)
        {
            if (dataForOneChunk.Length == 0)
            {
                throw new Exception("Chunk must have at least one entity!");
            }

            ushort value = dataForOneChunk[0].Index;
            
            for (int i = 0; i < dataForOneChunk.Length; i++)
            {
                if (dataForOneChunk[i].Index != value)
                {
                    throw new Exception("All Persisted Entities in the same chunk need to have the same PersistencyArchetypeIndexInContainer.Index value!");
                }
            }
        }
    }

    // This job uses nested native containers, the unity job safety system doesn't support that, so we only run these jobs in a build.
    [BurstCompile]
    public struct GroupedPersistJob : IJobEntityBatch
    {
        public PersistentDataContainer DataContainer;
        
        [ReadOnly] public ComponentTypeHandle<PersistenceState> PersistenceStateTypeHandle;
        [ReadOnly] public ComponentTypeHandle<PersistencyArchetypeIndexInContainer> PersistencyArchetypeIndexInContainerTypeHandle;
        
        [DeallocateOnJobCompletion]
        [ReadOnly] public ComponentTypeHandleArray DynamicComponentTypeHandles;

        [ReadOnly] public PersistencySettings.TypeIndexLookup TypeIndexLookup;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            NativeArray<PersistenceState> persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateTypeHandle);
            
            NativeArray<PersistencyArchetypeIndexInContainer> archetypeIndexInContainerArray = batchInChunk.GetNativeArray(PersistencyArchetypeIndexInContainerTypeHandle);
            GroupedApplyJob.ValidateArchetypeIndexInContainer(archetypeIndexInContainerArray);
            
            PersistencyArchetypeDataLayout dataLayout = DataContainer.GetDataLayoutAtIndex(archetypeIndexInContainerArray[0].Index);
            ref BlobArray<PersistencyArchetypeDataLayout.TypeInfo> typeInfoArray = ref dataLayout.PersistedTypeInfoArrayRef.Value;
            var dataForArchetype = DataContainer.GetSubArrayAtIndex(dataLayout.ArchetypeIndexInContainer);
            
            for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
            {
                // type info
                PersistencyArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                ComponentType runtimeType = ComponentType.ReadOnly(TypeIndexLookup.GetTypeIndex(typeInfo.PersistableTypeHandle));
                int stride = typeInfo.ElementSize * typeInfo.MaxElements + PersistenceMetaData.SizeOfStruct;
                int byteSize = dataLayout.Amount * stride;

                // Grab containers
                var outputData = dataForArchetype.GetSubArray(typeInfo.Offset, byteSize);
                
                bool found = DynamicComponentTypeHandles.GetByTypeIndex(runtimeType.TypeIndex, out DynamicComponentTypeHandle typeHandle);
                Debug.Assert(found);
                
                if (typeInfo.IsBuffer)
                {
                    var untypedBufferAccessor = batchInChunk.GetUntypedBufferAccessor(ref typeHandle);
                    CopyBufferElementsToByteArray.Execute(outputData, typeInfo.MaxElements, untypedBufferAccessor, persistenceStateArray);
                }
                else
                {
                    if (batchInChunk.Has(typeHandle))
                    {
                        if (typeInfo.ElementSize > 0)
                        {
                            var byteArray = batchInChunk.GetComponentDataAsByteArray(typeHandle);
                            CopyComponentDataToByteArray.Execute(outputData, typeInfo.ElementSize, byteArray, persistenceStateArray);
                        }
                        else
                        {
                            UpdateMetaDataForComponent.Execute(outputData, persistenceStateArray, 1);
                        }
                    }
                    else
                    {
                        UpdateMetaDataForComponent.Execute(outputData, persistenceStateArray, 0);
                    }
                }
            }
        }
    }
}