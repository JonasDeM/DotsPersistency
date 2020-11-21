using System.Collections.Generic;
using Unity.Entities;
using Unity.Jobs;
using Debug = UnityEngine.Debug;

namespace DotsPersistency
{
    public abstract class PersistencySystemBase : SystemBase
    {
        private readonly Dictionary<ComponentType, EntityQuery> _queryCache = new Dictionary<ComponentType, EntityQuery>(32, new CustomComparer());
        protected PersistencySettings PersistencySettings;
        
        protected void InitializeReadOnly()
        {
            PersistencySettings = PersistencySettings.Get();
            if (PersistencySettings == null)
            {
                Enabled = false;
                return;
            }
            
            foreach (PersistableTypeInfo persistableTypeInfo in PersistencySettings.AllPersistableTypeInfos)
            {
                persistableTypeInfo.ValidityCheck();
                CacheQuery(ComponentType.ReadOnly(persistableTypeInfo.TypeManagerTypeIndex));
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
            
            _queryCache.Add(ComponentType.ReadOnly<PersistenceState>(), CreatePersistenceEntityQuery());

            foreach (PersistableTypeInfo persistableTypeInfo in PersistencySettings.AllPersistableTypeInfos)
            {
                persistableTypeInfo.ValidityCheck();
                ComponentType componentType = ComponentType.FromTypeIndex(persistableTypeInfo.TypeManagerTypeIndex);
                ComponentType excludeComponentType = componentType;
                excludeComponentType.AccessModeType = ComponentType.AccessMode.Exclude;
                
                CacheQuery(componentType);
                CacheQuery(excludeComponentType);
            }
        }

        private void CacheQuery(ComponentType type)
        {
            Debug.Assert(type.TypeIndex != -1, "PersistencyJobSystem::CacheQuery Invalid type, try force updating RuntimePersistableTypesInfo (search the asset & press the force update button)");
            Debug.Assert(PersistencySettings.IsSupported(TypeManager.GetTypeInfo(type.TypeIndex), out string reason), reason);
            
            var query = CreatePersistenceEntityQuery(type);
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
        
        private EntityQuery CreatePersistenceEntityQuery(ComponentType persistedType)
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
                        ComponentType.ReadOnly<PersistenceState>(),
                        ComponentType.ReadOnly<SceneSection>()
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
                        ComponentType.ReadOnly<SceneSection>(),
                        persistedType
                    },
                    Options = EntityQueryOptions.IncludeDisabled
                };
            }

            var query = GetEntityQuery(queryDesc);
            return query;
        }
        
        private EntityQuery CreatePersistenceEntityQuery()
        {
            EntityQueryDesc queryDesc = new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PersistencyArchetypeDataLayout>(),
                    ComponentType.ReadOnly<PersistenceState>(),
                    ComponentType.ReadOnly<SceneSection>()
                },
                Options = EntityQueryOptions.IncludeDisabled
            };

            var query = GetEntityQuery(queryDesc);
            return query;
        }

        protected JobHandle ScheduleCopyToPersistentDataContainer(JobHandle inputDeps, SceneSection sceneSection, PersistentDataContainer dataContainer)
        {
            var returnJobHandle = inputDeps;

            for (int persistenceArchetypeIndex = 0; persistenceArchetypeIndex < dataContainer.Count; persistenceArchetypeIndex++)
            {
                PersistencyArchetypeDataLayout dataLayout = dataContainer.GetPersistenceArchetypeDataLayoutAtIndex(persistenceArchetypeIndex);
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
                    query.SetSharedComponentFilter(sceneSection, dataLayout);
                    
                    // Grab containers
                    var persistenceStateTypeHandle = GetComponentTypeHandle<PersistenceState>(true);
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
                        jobHandle = new UpdateMetaDataForComponentTag()
                        {
                            PersistenceStateType = persistenceStateTypeHandle,
                            OutputData = outputData
                        }.Schedule(query, inputDeps);
                    }

                    returnJobHandle = JobHandle.CombineDependencies(returnJobHandle, jobHandle);
                }
            }

            return returnJobHandle;
        }
        
        protected JobHandle ScheduleApplyToSceneSection(JobHandle inputDeps, SceneSection sceneSection, PersistentDataContainer dataContainer, EntityCommandBufferSystem ecbSystem)
        {
            var returnJobHandle = inputDeps;

            for (int persistenceArchetypeIndex = 0; persistenceArchetypeIndex < dataContainer.Count; persistenceArchetypeIndex++)
            {
                PersistencyArchetypeDataLayout dataLayout = dataContainer.GetPersistenceArchetypeDataLayoutAtIndex(persistenceArchetypeIndex);
                ref BlobArray<PersistencyArchetypeDataLayout.TypeInfo> typeInfoArray = ref dataLayout.PersistedTypeInfoArrayRef.Value;
                var dataForArchetype = dataContainer.GetSubArrayAtIndex(persistenceArchetypeIndex);
                
                for (int typeInfoIndex = 0; typeInfoIndex < typeInfoArray.Length; typeInfoIndex++)
                {
                    // type info
                    PersistencyArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[typeInfoIndex];
                    ComponentType runtimeType = ComponentType.FromTypeIndex(PersistencySettings.GetTypeIndex(typeInfo.PersistableTypeHandle));
                    int stride = typeInfo.ElementSize * typeInfo.MaxElements + PersistenceMetaData.SizeOfStruct;
                    int byteSize = dataLayout.Amount * stride;
                    
                    // query
                    var query = GetCachedQuery(runtimeType);
                    query.SetSharedComponentFilter(sceneSection, dataLayout);
                    var excludeType = runtimeType;
                    excludeType.AccessModeType = ComponentType.AccessMode.Exclude;
                    var excludeQuery = GetCachedQuery(excludeType);
                    excludeQuery.SetSharedComponentFilter(sceneSection, dataLayout);
                    
                    // Grab read-only containers
                    var persistenceStateTypeHandle = GetComponentTypeHandle<PersistenceState>(true);
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
                            EntityType = GetEntityTypeHandle(),
                            InputData = inputData,
                            Ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter()
                        }.Schedule(query, inputDeps);
                        
                        JobHandle compDataJobHandle3 = new AddMissingComponent()
                        {
                            ComponentType = runtimeType,
                            TypeSize = typeInfo.ElementSize,
                            PersistenceStateType = persistenceStateTypeHandle,
                            EntityType = GetEntityTypeHandle(),
                            InputData = inputData,
                            Ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter()
                        }.Schedule(excludeQuery, inputDeps);
                        
                        jobHandle = JobHandle.CombineDependencies(compDataJobHandle1, compDataJobHandle2, compDataJobHandle3);
                    }
                    returnJobHandle = JobHandle.CombineDependencies(returnJobHandle, jobHandle);
                }
            }

            return returnJobHandle;
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
