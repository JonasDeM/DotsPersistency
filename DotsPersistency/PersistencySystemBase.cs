// Author: Jonas De Maeseneer

using System.Collections.Generic;
using DotsPersistency.Containers;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace DotsPersistency
{
    public abstract class PersistencySystemBase : SystemBase
    {
        protected internal PersistencySettings PersistencySettings;
        protected EntityQuery PersistableEntitiesQuery;
        
        protected void InitializeReadOnly(PersistencySettings settings)
        {
            PersistencySettings = settings;
            if (PersistencySettings == null)
            {
                Enabled = false;
                return;
            }

            PersistableEntitiesQuery = CreateEntityQuery();
        }
        
        protected void InitializeReadWrite(PersistencySettings settings)
        {
            PersistencySettings = settings;
            if (PersistencySettings == null)
            {
                Enabled = false;
                return;
            }
            
            PersistableEntitiesQuery = CreateEntityQuery();
        }

        private EntityQuery CreateEntityQuery()
        {
            EntityQueryDesc queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PersistableTypeCombinationHash>(),
                    ComponentType.ReadOnly<PersistencyArchetypeIndexInContainer>(),
                    ComponentType.ReadOnly<PersistenceState>(),
                    ComponentType.ReadOnly<PersistencyContainerTag>()
                },
                Options = EntityQueryOptions.IncludeDisabled
            };

            var query = GetEntityQuery(queryDesc);
            return query;
        }
        
        protected internal void SlowSynchronousPersist(PersistentDataContainer dataContainer)
        {
            ComponentTypeHandle<PersistenceState> persistenceStateTypeHandle = GetComponentTypeHandle<PersistenceState>();
            ComponentTypeHandle<PersistencyArchetypeIndexInContainer> archetypeIndexInContainerTypeHandle = GetComponentTypeHandle<PersistencyArchetypeIndexInContainer>();

            JobHandle jobHandle = default;
            jobHandle = SchedulePersist(jobHandle, dataContainer, persistenceStateTypeHandle, archetypeIndexInContainerTypeHandle);
            jobHandle.Complete();
        }
        
        protected JobHandle SchedulePersist(JobHandle inputDeps, PersistentDataContainer dataContainer, ComponentTypeHandle<PersistenceState> persistenceStateTypeHandle,
            ComponentTypeHandle<PersistencyArchetypeIndexInContainer> persistencyArchetypeIndexInContainerTypeHandle)
        {
            if (PersistencySettings.UseGroupedJobs())
                return SchedulePersistGroupedJob(inputDeps, dataContainer, persistenceStateTypeHandle, persistencyArchetypeIndexInContainerTypeHandle);
            else
                return SchedulePersistJobs(inputDeps, dataContainer, persistenceStateTypeHandle);
        }

        protected JobHandle ScheduleApply(JobHandle inputDeps, PersistentDataContainer dataContainer, EntityCommandBufferSystem ecbSystem,
            EntityTypeHandle entityTypeHandle, ComponentTypeHandle<PersistenceState> persistenceStateTypeHandle, ComponentTypeHandle<PersistencyArchetypeIndexInContainer> indexInContainerTypeHandle)
        {
            if (PersistencySettings.UseGroupedJobs())
                return ScheduleApplyGroupedJob(inputDeps, dataContainer, ecbSystem, entityTypeHandle, persistenceStateTypeHandle, indexInContainerTypeHandle);
            else
                return ScheduleApplyJobs(inputDeps, dataContainer, ecbSystem, entityTypeHandle, persistenceStateTypeHandle);
        }
        
        private JobHandle SchedulePersistJobs(JobHandle inputDeps, PersistentDataContainer dataContainer, ComponentTypeHandle<PersistenceState> persistenceStateTypeHandle)
        {
            var returnJobHandle = inputDeps;

            for (int persistenceArchetypeIndex = 0; persistenceArchetypeIndex < dataContainer.DataLayoutCount; persistenceArchetypeIndex++)
            {
                PersistencyArchetypeDataLayout dataLayout = dataContainer.GetDataLayoutAtIndex(persistenceArchetypeIndex);
                ref BlobArray<PersistencyArchetypeDataLayout.TypeInfo> typeInfoArray = ref dataLayout.PersistedTypeInfoArrayRef.Value;
                var dataForArchetype = dataContainer.GetSubArrayAtIndex(persistenceArchetypeIndex);
                
                PersistencyContainerTag containerTag = new PersistencyContainerTag {DataIdentifier = dataContainer.DataIdentifier};
                PersistableTypeCombinationHash persistableTypeCombinationHash = new PersistableTypeCombinationHash { Value = dataLayout.PersistableTypeHandleCombinationHash};
                
                for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
                {
                    // type info
                    PersistencyArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                    ComponentType runtimeType = ComponentType.ReadOnly(PersistencySettings.GetTypeIndex(typeInfo.PersistableTypeHandle));
                    int stride = typeInfo.ElementSize * typeInfo.MaxElements + PersistenceMetaData.SizeOfStruct;
                    int byteSize = dataLayout.Amount * stride;
                    
                    // query
                    var query = PersistableEntitiesQuery;
                    query.SetSharedComponentFilter(containerTag, persistableTypeCombinationHash);

                    // Grab containers
                    var outputData = dataForArchetype.GetSubArray(typeInfo.Offset, byteSize);
                    
                    JobHandle jobHandle;
                    if (typeInfo.IsBuffer)
                    {
                        jobHandle = new CopyBufferElementsToByteArray
                        {
                            BufferTypeHandle = GetDynamicComponentTypeHandle(runtimeType),
                            MaxElements = typeInfo.MaxElements,
                            PersistenceStateType = persistenceStateTypeHandle,
                            OutputData = outputData
                        }.Schedule(query, inputDeps);
                    }
                    else
                    {
                        jobHandle = new CopyComponentDataToByteArray()
                        {
                            ComponentTypeHandle = GetDynamicComponentTypeHandle(runtimeType),
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = persistenceStateTypeHandle,
                            OutputData = outputData
                        }.Schedule(query, inputDeps);
                    }
                    
                    query.ResetFilter();
                    returnJobHandle = JobHandle.CombineDependencies(returnJobHandle, jobHandle);
                }
            }

            return returnJobHandle;
        }
        
        private JobHandle ScheduleApplyJobs(JobHandle inputDeps, PersistentDataContainer dataContainer, EntityCommandBufferSystem ecbSystem,
            EntityTypeHandle entityTypeHandle, ComponentTypeHandle<PersistenceState> persistenceStateTypeHandle)
        {
            var returnJobHandle = inputDeps;

            for (int persistenceArchetypeIndex = 0; persistenceArchetypeIndex < dataContainer.DataLayoutCount; persistenceArchetypeIndex++)
            {
                PersistencyArchetypeDataLayout dataLayout = dataContainer.GetDataLayoutAtIndex(persistenceArchetypeIndex);
                ref BlobArray<PersistencyArchetypeDataLayout.TypeInfo> typeInfoArray = ref dataLayout.PersistedTypeInfoArrayRef.Value;
                var dataForArchetype = dataContainer.GetSubArrayAtIndex(persistenceArchetypeIndex);
                
                PersistencyContainerTag containerTag = new PersistencyContainerTag {DataIdentifier = dataContainer.DataIdentifier};
                PersistableTypeCombinationHash persistableTypeCombinationHash = new PersistableTypeCombinationHash { Value = dataLayout.PersistableTypeHandleCombinationHash};
                
                for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
                {
                    // type info
                    PersistencyArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                    ComponentType runtimeType = ComponentType.ReadWrite(PersistencySettings.GetTypeIndex(typeInfo.PersistableTypeHandle));
                    int stride = typeInfo.ElementSize * typeInfo.MaxElements + PersistenceMetaData.SizeOfStruct;
                    int byteSize = dataLayout.Amount * stride;

                    // queries
                    var query = PersistableEntitiesQuery;
                    query.SetSharedComponentFilter(containerTag, persistableTypeCombinationHash);
                    
                    // Grab read-only containers
                    var inputData = dataForArchetype.GetSubArray(typeInfo.Offset, byteSize);
                    
                    JobHandle jobHandle;
                    if (typeInfo.IsBuffer)
                    {
                        jobHandle = new CopyByteArrayToBufferElements()
                        {
                            BufferTypeHandle = GetDynamicComponentTypeHandle(runtimeType),
                            MaxElements = typeInfo.MaxElements,
                            PersistenceStateType = persistenceStateTypeHandle,
                            InputData = inputData
                        }.Schedule(query, inputDeps);
                    }
                    else
                    {
                        jobHandle = new CopyByteArrayToComponentData()
                        {
                            ComponentTypeHandle = GetDynamicComponentTypeHandle(runtimeType),
                            ComponentType = runtimeType,
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = persistenceStateTypeHandle,
                            EntityType = entityTypeHandle,
                            InputData = inputData,
                            Ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter()
                        }.Schedule(query, inputDeps);
                    }
                    
                    query.ResetFilter();
                    returnJobHandle = JobHandle.CombineDependencies(returnJobHandle, jobHandle);
                }
            }

            return returnJobHandle;
        }
        
        private JobHandle SchedulePersistGroupedJob(JobHandle inputDeps, PersistentDataContainer dataContainer, ComponentTypeHandle<PersistenceState> persistenceStateTypeHandle,
            ComponentTypeHandle<PersistencyArchetypeIndexInContainer> persistencyArchetypeIndexInContainerTypeHandle)
        {
            PersistableEntitiesQuery.SetSharedComponentFilter(new PersistencyContainerTag { DataIdentifier = dataContainer.DataIdentifier });

            PersistencySettings.TypeIndexLookup typeIndexLookup = PersistencySettings.GetTypeIndexLookup();

            // always temp job allocations
            ComponentTypeHandleArray componentTypeHandles = CreateComponentTypeHandleArray(typeIndexLookup, dataContainer, true);

            inputDeps = new GroupedPersistJob()
            {
                DataContainer = dataContainer,
                PersistenceStateTypeHandle = persistenceStateTypeHandle,
                PersistencyArchetypeIndexInContainerTypeHandle = persistencyArchetypeIndexInContainerTypeHandle,
                DynamicComponentTypeHandles = componentTypeHandles,
                TypeIndexLookup = typeIndexLookup
            }.ScheduleParallel(PersistableEntitiesQuery, 1, inputDeps);

            PersistableEntitiesQuery.ResetFilter();
            return inputDeps;
        }

        private JobHandle ScheduleApplyGroupedJob(JobHandle inputDeps, PersistentDataContainer dataContainer, EntityCommandBufferSystem ecbSystem,
            EntityTypeHandle entityTypeHandle, ComponentTypeHandle<PersistenceState> persistenceStateTypeHandle, ComponentTypeHandle<PersistencyArchetypeIndexInContainer> indexInContainerTypeHandle)
        {
            PersistableEntitiesQuery.SetSharedComponentFilter(new PersistencyContainerTag { DataIdentifier = dataContainer.DataIdentifier });

            PersistencySettings.TypeIndexLookup typeIndexLookup = PersistencySettings.GetTypeIndexLookup();

            // always temp job allocations
            ComponentTypeHandleArray componentTypeHandles = CreateComponentTypeHandleArray(typeIndexLookup, dataContainer);
            
            inputDeps = new GroupedApplyJob()
            {
                DataContainer = dataContainer,
                EntityTypeHandle = entityTypeHandle,
                PersistenceStateTypeHandle = persistenceStateTypeHandle,
                PersistencyArchetypeIndexInContainerTypeHandle = indexInContainerTypeHandle,
                DynamicComponentTypeHandles = componentTypeHandles,
                TypeIndexLookup = typeIndexLookup,
                Ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter()
            }.ScheduleParallel(PersistableEntitiesQuery, 1, inputDeps);

            PersistableEntitiesQuery.ResetFilter();
            return inputDeps;
        }

        private ComponentTypeHandleArray CreateComponentTypeHandleArray(PersistencySettings.TypeIndexLookup typeIndexLookup, PersistentDataContainer dataContainer, bool readOnly = false)
        {
            ComponentTypeHandleArray array = new ComponentTypeHandleArray(dataContainer.UniqueTypeHandleCount, Allocator.TempJob);
            for (int i = 0; i < dataContainer.UniqueTypeHandleCount; i++)
            {
                int typeIndex = typeIndexLookup.GetTypeIndex(dataContainer.GetUniqueTypeHandleAtIndex(i));
                ComponentType componentType = readOnly ? ComponentType.ReadOnly(typeIndex) : ComponentType.ReadWrite(typeIndex);
                
                var typeHandle = GetDynamicComponentTypeHandle(componentType);
                array[i] = typeHandle;
            }

            return array;
        }
        
#if UNITY_INCLUDE_TESTS
        internal void ReplaceSettings(PersistencySettings settings)
        {
            InitializeReadWrite(settings);
        }
#endif
    }
}
