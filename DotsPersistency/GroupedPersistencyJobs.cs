using System;
using System.Diagnostics;
using DotsPersistency.Containers;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Debug = UnityEngine.Debug;

namespace DotsPersistency
{
    // A grouped persistency job does job dependencies correctly, but it can't provide all the safety checks you normally have.
    // There's no way to specify atomic safety handles for jobs yourself. This happens automatically via reflection inside native code.
    // This automatic process (obviously) doesn't pick up the atomic safety handles hidden inside ComponentTypeHandleArray & BufferTypeHandleArray their Malloc allocated memory.
    // This means that if a job writes to a component we write to or read from, they would not be notified of errors in the case that they didn't feed in the correct job dependencies.
    // Since users should be able to rely on these checks in editor, we only run grouped persistency jobs if ENABLE_UNITY_COLLECTIONS_CHECKS is not defined.
    [BurstCompile]
    public struct GroupedApplyJob : IJobEntityBatch
    {
        [WriteOnly] public PersistentDataContainer DataContainer;
        
        [ReadOnly] public EntityTypeHandle EntityTypeHandle;
        [ReadOnly] public ComponentTypeHandle<PersistenceState> PersistenceStateTypeHandle;
        [ReadOnly] public ComponentTypeHandle<PersistencyArchetypeIndexInContainer> PersistencyArchetypeIndexInContainerTypeHandle;
        [ReadOnly] public ComponentTypeHandleArray DynamicComponentTypeHandles;
        [ReadOnly] public BufferTypeHandleArray DynamicBufferTypeHandles;

        [ReadOnly] public PersistencySettings.TypeIndexLookup TypeIndexLookup;
        
        public EntityCommandBuffer.ParallelWriter Ecb;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            // todo perf instead of grabbing these container every time, just grab em once here
            // NativeArray<PersistenceState> persistenceStateArray = batchInChunk.GetNativeArray(PersistenceStateTypeHandle);
            // NativeArray<Entity> entityArray = batchInChunk.GetNativeArray(EntityTypeHandle);
            
            NativeArray<PersistencyArchetypeIndexInContainer> archetypeIndexInContainerArray = batchInChunk.GetNativeArray(PersistencyArchetypeIndexInContainerTypeHandle);
            ValidateArchetypeIndexInContainer(archetypeIndexInContainerArray);
            
            PersistencyArchetypeDataLayout dataLayout = DataContainer.GetPersistenceArchetypeDataLayoutAtIndex(archetypeIndexInContainerArray[0].Index);
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

                if (typeInfo.IsBuffer)
                {
                    bool found = DynamicBufferTypeHandles.GetByTypeIndex(runtimeType.TypeIndex, out DynamicBufferTypeHandle typeHandle);
                    Debug.Assert(found);
                    Debug.Assert(batchInChunk.Has(typeHandle)); 
                    new CopyByteArrayToBufferElements()
                    {
                        BufferTypeHandle = typeHandle,
                        ElementSize = typeInfo.ElementSize,
                        MaxElements = typeInfo.MaxElements,
                        PersistenceStateType = PersistenceStateTypeHandle,
                        InputData = inputData
                    }.Execute(batchInChunk, batchIndex);
                }
                else // if ComponentData
                {
                    bool found = DynamicComponentTypeHandles.GetByTypeIndex(runtimeType.TypeIndex, out DynamicComponentTypeHandle typeHandle);
                    Debug.Assert(found);
                    
                    if (batchInChunk.Has(typeHandle))
                    {
                        // minor optimization: these jobs grab some of the same chunk data, so it's searching for it 2 times.
                        if (typeInfo.ElementSize > 0)
                        {
                            new CopyByteArrayToComponentData()
                            {
                                ComponentTypeHandle = typeHandle,
                                TypeSize = typeInfo.ElementSize,
                                PersistenceStateType = PersistenceStateTypeHandle,
                                InputData = inputData
                            }.Execute(batchInChunk, batchIndex);
                        }

                        new RemoveComponent()
                        {
                            TypeToRemove = runtimeType,
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = PersistenceStateTypeHandle,
                            EntityType = EntityTypeHandle,
                            InputData = inputData,
                            Ecb = Ecb
                        }.Execute(batchInChunk, batchIndex);
                    }
                    else
                    {
                        new AddMissingComponent()
                        {
                            ComponentType = runtimeType,
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = PersistenceStateTypeHandle,
                            EntityType = EntityTypeHandle,
                            InputData = inputData,
                            Ecb = Ecb
                        }.Execute(batchInChunk, batchIndex);
                    }
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void ValidateArchetypeIndexInContainer(NativeArray<PersistencyArchetypeIndexInContainer> dataForOneChunk)
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

    // A grouped persistency job does job dependencies correctly, but it can't provide all the safety checks you normally have.
    // There's no way to specify atomic safety handles for jobs yourself. This happens automatically via reflection inside native code.
    // This automatic process (obviously) doesn't pick up the atomic safety handles hidden inside ComponentTypeHandleArray & BufferTypeHandleArray their Malloc allocated memory.
    // This means that if a job writes to a component we write to or read from, they would not be notified of errors in the case that they didn't feed in the correct job dependencies.
    // Since users should be able to rely on these checks in editor, we only run grouped persistency jobs if ENABLE_UNITY_COLLECTIONS_CHECKS is not defined.
    [BurstCompile]
    public struct GroupedPersistJob : IJobEntityBatch
    {
        [ReadOnly] public NativeArray<PersistencyArchetypeDataLayout> DataLayouts;
        [ReadOnly] public ComponentTypeHandle<PersistenceState> PersistenceStateTypeHandle;
        [ReadOnly] public ComponentTypeHandleArray DynamicComponentTypeHandles;
        [ReadOnly] public BufferTypeHandleArray DynamicBufferTypeHandles;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
        }
    }
}