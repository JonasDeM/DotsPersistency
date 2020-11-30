using System;
using System.Collections.Generic;
using DotsPersistency.Containers;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Debug = UnityEngine.Debug;

namespace DotsPersistency
{
    public abstract class PersistencySystemBase : SystemBase
    {
        private readonly Dictionary<ComponentType, EntityQuery> _queryCache = new Dictionary<ComponentType, EntityQuery>(32, new CustomComparer());
        protected PersistencySettings PersistencySettings;
        protected EntityQuery PersistableEntitiesQuery;
        
        protected void InitializeReadOnly()
        {
            PersistencySettings = PersistencySettings.Get();
            if (PersistencySettings == null)
            {
                Enabled = false;
                return;
            }

            PersistableEntitiesQuery = CreateEntityQuery();

            if (!PersistencySettings.UseGroupedJobs())
            {
                foreach (PersistableTypeInfo persistableTypeInfo in PersistencySettings.AllPersistableTypeInfos)
                {
                    persistableTypeInfo.ValidityCheck();
                    CacheQuery(ComponentType.ReadOnly(TypeManager.GetTypeIndexFromStableTypeHash(persistableTypeInfo.StableTypeHash)));
                }
            }
        }
        
        protected void InitializeReadWrite()
        {
            PersistencySettings = PersistencySettings.Get();
            if (PersistencySettings == null)
            {
                Enabled = false;
                return;
            }
            
            PersistableEntitiesQuery = CreateEntityQuery();
            
            if (!PersistencySettings.UseGroupedJobs())
            {
                foreach (PersistableTypeInfo persistableTypeInfo in PersistencySettings.AllPersistableTypeInfos)
                {
                    persistableTypeInfo.ValidityCheck();
                    ComponentType componentType = ComponentType.ReadWrite(TypeManager.GetTypeIndexFromStableTypeHash(persistableTypeInfo.StableTypeHash));
                    ComponentType excludeComponentType = componentType;
                    excludeComponentType.AccessModeType = ComponentType.AccessMode.Exclude;

                    CacheQuery(componentType);
                    CacheQuery(excludeComponentType);
                }
            }
        }

        private void CacheQuery(ComponentType type)
        {
            Debug.Assert(type.TypeIndex != -1, "PersistencyJobSystem::CacheQuery Invalid type, try force updating RuntimePersistableTypesInfo (search the asset & press the force update button)");
            Debug.Assert(PersistencySettings.IsSupported(TypeManager.GetTypeInfo(type.TypeIndex), out string reason), reason);
            
            var query = CreateEntityQuery(type);
            _queryCache.Add(type, query);
            
            // a read/write query can also be used for read only operations
            if (type.AccessModeType == ComponentType.AccessMode.ReadWrite)
            {
                type.AccessModeType = ComponentType.AccessMode.ReadOnly;
                _queryCache.Add(type, query);
            }
        }

        private EntityQuery GetCachedQuery(ComponentType persistedType)
        {
            return _queryCache[persistedType];
        }
        
        private EntityQuery CreateEntityQuery(ComponentType persistedType)
        {
            EntityQueryDesc queryDesc;

            if (persistedType.AccessModeType == ComponentType.AccessMode.Exclude)
            {
                persistedType.AccessModeType = ComponentType.AccessMode.ReadOnly;
                queryDesc = new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<PersistencyArchetypeDataLayout>(),
                        ComponentType.ReadOnly<PersistenceState>()
                    },
                    None = new [] {persistedType},
                    Options = EntityQueryOptions.IncludeDisabled
                };
            }
            else
            {
                queryDesc = new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<PersistencyArchetypeDataLayout>(),
                        ComponentType.ReadOnly<PersistenceState>(),
                        persistedType
                    },
                    Options = EntityQueryOptions.IncludeDisabled
                };
            }

            var query = GetEntityQuery(queryDesc);
            return query;
        }
        
        private EntityQuery CreateEntityQuery()
        {
            EntityQueryDesc queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PersistencyArchetypeDataLayout>(),
                    ComponentType.ReadOnly<PersistencyArchetypeIndexInContainer>(),
                    ComponentType.ReadOnly<PersistenceState>(),
                    ComponentType.ReadOnly<PersistencyContainerTag>()
                },
                Options = EntityQueryOptions.IncludeDisabled
            };

            var query = GetEntityQuery(queryDesc);
            return query;
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
                
                for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
                {
                    // type info
                    PersistencyArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                    ComponentType runtimeType = ComponentType.ReadOnly(PersistencySettings.GetTypeIndex(typeInfo.PersistableTypeHandle));
                    int stride = typeInfo.ElementSize * typeInfo.MaxElements + PersistenceMetaData.SizeOfStruct;
                    int byteSize = dataLayout.Amount * stride;
                    
                    // query
                    var query = GetCachedQuery(runtimeType);
                    query.SetSharedComponentFilter(dataLayout);
                    
                    // Grab containers
                    var outputData = dataForArchetype.GetSubArray(typeInfo.Offset, byteSize);
                    
                    JobHandle jobHandle;
                    if (typeInfo.IsBuffer)
                    {
                        jobHandle = new CopyBufferElementsToByteArray
                        {
                            BufferTypeHandle = this.GetDynamicBufferTypeHandle(runtimeType),
                            ElementSize = typeInfo.ElementSize,
                            MaxElements = typeInfo.MaxElements,
                            PersistenceStateType = persistenceStateTypeHandle,
                            OutputData = outputData
                        }.Schedule(query, inputDeps);
                    }
                    else if (typeInfo.ElementSize > 0)
                    {
                        jobHandle = new CopyComponentDataToByteArray()
                        {
                            ComponentTypeHandle = GetDynamicComponentTypeHandle(runtimeType),
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = persistenceStateTypeHandle,
                            OutputData = outputData,
                        }.Schedule(query, inputDeps);
                    }
                    else
                    {
                        jobHandle = new UpdateMetaDataForTagComponent()
                        {
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
                
                for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
                {
                    // type info
                    PersistencyArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                    ComponentType runtimeType = ComponentType.ReadWrite(PersistencySettings.GetTypeIndex(typeInfo.PersistableTypeHandle));
                    int stride = typeInfo.ElementSize * typeInfo.MaxElements + PersistenceMetaData.SizeOfStruct;
                    int byteSize = dataLayout.Amount * stride;
                    
                    // query
                    var query = GetCachedQuery(runtimeType);
                    query.SetSharedComponentFilter(dataLayout);
                    var excludeType = runtimeType;
                    excludeType.AccessModeType = ComponentType.AccessMode.Exclude;
                    var excludeQuery = GetCachedQuery(excludeType);
                    excludeQuery.SetSharedComponentFilter(dataLayout);
                    
                    // Grab read-only containers
                    var inputData = dataForArchetype.GetSubArray(typeInfo.Offset, byteSize);
                    
                    JobHandle jobHandle;
                    if (typeInfo.IsBuffer)
                    {
                        jobHandle = new CopyByteArrayToBufferElements()
                        {
                            BufferTypeHandle = this.GetDynamicBufferTypeHandle(runtimeType),
                            ElementSize = typeInfo.ElementSize,
                            MaxElements = typeInfo.MaxElements,
                            PersistenceStateType = persistenceStateTypeHandle,
                            InputData = inputData
                        }.Schedule(query, inputDeps);
                    }
                    else
                    {
                        JobHandle compDataJobHandle1 = new JobHandle();
                        if (typeInfo.ElementSize > 0)
                        {
                            compDataJobHandle1 = new CopyByteArrayToComponentData()
                            {
                                ComponentTypeHandle = GetDynamicComponentTypeHandle(runtimeType),
                                TypeSize = typeInfo.ElementSize,
                                PersistenceStateType = persistenceStateTypeHandle,
                                InputData = inputData
                            }.Schedule(query, inputDeps);
                        }
                        
                        JobHandle compDataJobHandle2 = new RemoveComponent()
                        {
                            TypeToRemove = runtimeType,
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = persistenceStateTypeHandle,
                            EntityType = entityTypeHandle,
                            InputData = inputData,
                            Ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter()
                        }.Schedule(query, inputDeps);
                        
                        JobHandle compDataJobHandle3 = new AddMissingComponent()
                        {
                            ComponentType = runtimeType,
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = persistenceStateTypeHandle,
                            EntityType = entityTypeHandle,
                            InputData = inputData,
                            Ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter()
                        }.Schedule(excludeQuery, inputDeps);
                        
                        jobHandle = JobHandle.CombineDependencies(compDataJobHandle1, compDataJobHandle2, compDataJobHandle3);
                    }
                    
                    query.ResetFilter();
                    excludeQuery.ResetFilter();
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
            BufferTypeHandleArray bufferTypeHandles = CreateBufferTypeHandleArray(typeIndexLookup, dataContainer, true);

            inputDeps = new GroupedPersistJob()
            {
                DataContainer = dataContainer,
                PersistenceStateTypeHandle = persistenceStateTypeHandle,
                PersistencyArchetypeIndexInContainerTypeHandle = persistencyArchetypeIndexInContainerTypeHandle,
                DynamicComponentTypeHandles = componentTypeHandles,
                DynamicBufferTypeHandles = bufferTypeHandles,
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
            BufferTypeHandleArray bufferTypeHandles = CreateBufferTypeHandleArray(typeIndexLookup, dataContainer);
            
            inputDeps = new GroupedApplyJob()
            {
                DataContainer = dataContainer,
                EntityTypeHandle = entityTypeHandle,
                PersistenceStateTypeHandle = persistenceStateTypeHandle,
                PersistencyArchetypeIndexInContainerTypeHandle = indexInContainerTypeHandle,
                DynamicComponentTypeHandles = componentTypeHandles,
                DynamicBufferTypeHandles = bufferTypeHandles,
                TypeIndexLookup = typeIndexLookup,
                Ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter()
            }.ScheduleParallel(PersistableEntitiesQuery, 1, inputDeps);

            PersistableEntitiesQuery.ResetFilter();
            return inputDeps;
        }

        private ComponentTypeHandleArray CreateComponentTypeHandleArray(PersistencySettings.TypeIndexLookup typeIndexLookup, PersistentDataContainer dataContainer, bool readOnly = false)
        {
            ComponentTypeHandleArray array = new ComponentTypeHandleArray(dataContainer.UniqueComponentDataTypeHandleCount, Allocator.TempJob);
            int amountAdded = 0;
            for (int i = 0; i < dataContainer.UniqueTypeHandleCount; i++)
            {
                int typeIndex = typeIndexLookup.GetTypeIndex(dataContainer.GetUniqueTypeHandleAtIndex(i));
                ComponentType componentType = readOnly ? ComponentType.ReadOnly(typeIndex) : ComponentType.ReadWrite(typeIndex);
                if (!componentType.IsBuffer)
                {
                    var typeHandle = GetDynamicComponentTypeHandle(componentType);
                    array[amountAdded] = typeHandle;
                    amountAdded += 1;
                }
            }

            return array;
        }
        
        private BufferTypeHandleArray CreateBufferTypeHandleArray(PersistencySettings.TypeIndexLookup typeIndexLookup, PersistentDataContainer dataContainer, bool readOnly = false)
        {
            BufferTypeHandleArray array = new BufferTypeHandleArray(dataContainer.UniqueBufferTypeHandleCount, Allocator.TempJob);
            int amountAdded = 0;
            for (int i = 0; i < dataContainer.UniqueTypeHandleCount; i++)
            {
                int typeIndex = typeIndexLookup.GetTypeIndex(dataContainer.GetUniqueTypeHandleAtIndex(i));
                ComponentType componentType = readOnly ? ComponentType.ReadOnly(typeIndex) : ComponentType.ReadWrite(typeIndex);
                if (componentType.IsBuffer)
                {
                    array[amountAdded] = this.GetDynamicBufferTypeHandle(componentType);
                    amountAdded += 1;
                }
            }

            return array;
        }

        // The equality/hash implementation of ComponentType does not take into account access mode type
        private class CustomComparer : IEqualityComparer<ComponentType>
        {
            public bool Equals(ComponentType x, ComponentType y)
            {
                return x.TypeIndex == y.TypeIndex && x.AccessModeType == y.AccessModeType;
            }

            public int GetHashCode(ComponentType obj)
            {
                return obj.GetHashCode() * ((int)obj.AccessModeType + 11);
            }
        }
    }
}
