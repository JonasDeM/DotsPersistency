// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency
{
    public class PersistentDataStorage
    {
        private class ArchetypesWithStates : IDisposable
        {
            public int LatestWriteIndex;
            public NativeArray<PersistencyArchetypeDataLayout> Archetypes;
            public PersistentDataContainer InitialSceneState;
            public List<PersistentDataContainer> RingBuffer;
            
            public NativeArray<PersistableTypeHandle> AllUniqueTypeHandles;
            
            public int EntityCapacity;
            public bool IsPool;

            public void Dispose()
            {
                Archetypes.Dispose();
                AllUniqueTypeHandles.Dispose();
                InitialSceneState.Dispose();
                foreach (var persistentDataContainer in RingBuffer)
                {
                    persistentDataContainer.Dispose();
                }
            }

            public void ChangeEntityCapacity(int capacity)
            {
                Debug.Assert(IsPool);
                Debug.Assert(Archetypes.Length == 1); // Only pools with 1 PersistenceArchetype are supported
                Debug.Assert(capacity != EntityCapacity);
                
                // Grow InitialSceneState & fill with default 
                InitialSceneState.ResizeByInitializingWithFirstEntry(capacity);
                
                // for each in ringbuffer
                // Copy that array & copy existing smaller data into it
                // Dispose of old array
                for (var i = 0; i < RingBuffer.Count; i++)
                {
                    PersistentDataContainer persistentDataContainer = RingBuffer[i];
                    persistentDataContainer.ResizeByInitializingFromOther(capacity, InitialSceneState);
                    RingBuffer[i] = persistentDataContainer;
                }

                // Change ArchetypeDataLayout
                PersistencyArchetypeDataLayout dataLayout = Archetypes[0];
                dataLayout.Amount = capacity;
                Archetypes[0] = dataLayout;
                
                ref BlobArray<PersistencyArchetypeDataLayout.TypeInfo> typeInfoArray = ref dataLayout.PersistedTypeInfoArrayRef.Value;
                int offset = 0;
                for (int i = 0; i < typeInfoArray.Length; i++)
                {
                    PersistencyArchetypeDataLayout.TypeInfo typeInfo = typeInfoArray[i];
                    typeInfo.Offset = offset;
                    typeInfoArray[i] = typeInfo;

                    offset += typeInfo.SizePerEntityForThisType * capacity;
                }
                
                EntityCapacity = capacity;
            }
        }
        
        private int RingBufferIndex => _nonWrappedIndex % RingBufferSize;
        private int _latestWriteNonWrappedIndex;
        private int _nonWrappedIndex;
        public int NonWrappedIndex => _nonWrappedIndex;
        public readonly int RingBufferSize;
        private Dictionary<Hash128, ArchetypesWithStates> _allPersistedData = new Dictionary<Hash128, ArchetypesWithStates>(8);
        private Dictionary<Hash128, (int, NativeArray<byte>)> _uninitializedSceneContainers = new Dictionary<Hash128, (int, NativeArray<byte>)>(4);
        
        public PersistentDataStorage(int ringBufferSize)
        {
            RingBufferSize = ringBufferSize;
        }
        
        public void Dispose()
        {
            foreach (ArchetypesWithStates value in _allPersistedData.Values)
            {
                value.Dispose();
            }
            _allPersistedData.Clear();
            foreach ((int, NativeArray<byte>) value in _uninitializedSceneContainers.Values)
            {
                NativeArray<byte> toDispose = value.Item2;
                toDispose.Dispose();
            }
            _uninitializedSceneContainers.Clear();
        }
        
        public void ToIndex(int nonWrappedIndex)
        {
            Debug.Assert(_latestWriteNonWrappedIndex - nonWrappedIndex < RingBufferSize);
            _nonWrappedIndex = nonWrappedIndex;
        }
        
        public void AddUninitializedSceneContainer(Hash128 identifier, NativeArray<byte> rawData)
        {
            Debug.Assert(!_uninitializedSceneContainers.ContainsKey(identifier));
            _uninitializedSceneContainers.Add(identifier, (_nonWrappedIndex, rawData));
        }

        public PersistentDataContainer GetWriteContainerForCurrentIndex(Hash128 identifier)
        {
            ArchetypesWithStates data = _allPersistedData[identifier];
            data.LatestWriteIndex = RingBufferIndex;
            _latestWriteNonWrappedIndex = _nonWrappedIndex;
            return data.RingBuffer[RingBufferIndex];
        }
        
        public PersistentDataContainer GetReadContainerForCurrentIndex(Hash128 identifier)
        {
            return _allPersistedData[identifier].RingBuffer[RingBufferIndex];
        }

        public void InitializeSceneContainer(Hash128 identifier, NativeArray<PersistencyArchetypeDataLayout> archetypes, NativeArray<PersistableTypeHandle> allUniqueTypeHandles,
            out PersistentDataContainer initialContainer, out PersistentDataContainer containerToApply)
        {
            Debug.Assert(!_allPersistedData.ContainsKey(identifier));
            initialContainer = new PersistentDataContainer(identifier, archetypes, allUniqueTypeHandles, Allocator.Persistent);
            var newData = new ArchetypesWithStates()
            {
                Archetypes = archetypes,
                AllUniqueTypeHandles = allUniqueTypeHandles,
                InitialSceneState = initialContainer,
                RingBuffer = new List<PersistentDataContainer>(RingBufferSize),
                LatestWriteIndex = -1,
                IsPool = false,
            };
            newData.EntityCapacity = newData.InitialSceneState.CalculateEntityCapacity();

            if (_uninitializedSceneContainers.TryGetValue(identifier, out var uninitializedEntry))
            {
                containerToApply = initialContainer.GetCopyWithCustomData(uninitializedEntry.Item2);
                for (int i = 0; i < RingBufferSize; i++)
                {
                    var container = i == uninitializedEntry.Item1 % RingBufferSize ? containerToApply : initialContainer.GetCopy(Allocator.Persistent);
                    newData.RingBuffer.Add(container);
                }
                _uninitializedSceneContainers.Remove(identifier);
            }
            else
            {
                for (int i = 0; i < RingBufferSize; i++)
                {
                    newData.RingBuffer.Add(initialContainer.GetCopy(Allocator.Persistent));
                }
                containerToApply = default;
            }
            
            _allPersistedData[identifier] = newData;
        }
        
        internal void InitializePool(Entity prefabEntity, Hash128 identifier, NativeArray<PersistableTypeHandle> typeHandles, PersistencySystemBase persistencySystem)
        {
            Debug.Assert(!_allPersistedData.ContainsKey(identifier));
            
            PersistencySettings settings = persistencySystem.PersistencySettings;

            var persistableTypeHandleCombinationHash = new PersistableTypeCombinationHash() {Value = settings.GetTypeHandleCombinationHash(typeHandles)};
            
            NativeArray<PersistencyArchetypeDataLayout> archetypes = new NativeArray<PersistencyArchetypeDataLayout>(1, Allocator.Persistent);
            archetypes[0] = new PersistencyArchetypeDataLayout()
            {
                Amount = 1,
                ArchetypeIndexInContainer = 0,
                PersistedTypeInfoArrayRef = ExtensionMethods.BuildTypeInfoBlobAsset(typeHandles, settings, 1, out int sizePerEntity),
                SizePerEntity = sizePerEntity,
                Offset = 0,
                PersistableTypeHandleCombinationHash = persistableTypeHandleCombinationHash.Value
            };
            
            var newData = new ArchetypesWithStates()
            {
                Archetypes = archetypes,
                AllUniqueTypeHandles = typeHandles,
                InitialSceneState = new PersistentDataContainer(identifier, archetypes, typeHandles, Allocator.Persistent),
                RingBuffer = new List<PersistentDataContainer>(RingBufferSize),
                LatestWriteIndex = -1,
                IsPool = true,
                EntityCapacity = 1
            };

            EntityManager mgr = persistencySystem.EntityManager;
            Debug.Assert(mgr.HasComponent<Prefab>(prefabEntity), "The entity was expected to be a prefab.");
            mgr.AddComponents(prefabEntity, new ComponentTypes(ComponentType.ReadWrite<PersistencyArchetypeIndexInContainer>()
                , ComponentType.ReadWrite<PersistenceState>(), ComponentType.ReadWrite<PersistencyContainerTag>(), ComponentType.ReadWrite<PersistableTypeCombinationHash>()));
            mgr.SetSharedComponentData(prefabEntity, new PersistencyContainerTag() {DataIdentifier = identifier});
            mgr.SetSharedComponentData(prefabEntity, persistableTypeHandleCombinationHash);
            
            // Persisting initial state (slow)
            Entity tempEntity = mgr.Instantiate(prefabEntity);
            persistencySystem.SlowSynchronousPersist(newData.InitialSceneState);
            mgr.DestroyEntity(tempEntity);
            
            for (int i = 0; i < RingBufferSize; i++)
            {
                newData.RingBuffer.Add(newData.InitialSceneState.GetCopy(Allocator.Persistent));
            }
            _allPersistedData[identifier] = newData;
        }

        internal void ChangeEntityCapacity(Hash128 identifier, int minSize)
        {
            Debug.Assert(IsInitialized(identifier));
            ArchetypesWithStates states = _allPersistedData[identifier];
            
            if (minSize == states.EntityCapacity)
                return;

            states.ChangeEntityCapacity(minSize);
        }

        public bool IsInitialized(Hash128 identifier)
        {
            return _allPersistedData.ContainsKey(identifier);
        }

        public PersistentDataContainer GetReadContainerForLatestWriteIndex(Hash128 identifier, out bool isInitial)
        {
            var data = _allPersistedData[identifier];
            if (data.LatestWriteIndex == -1)
            {
                isInitial = true;
                return data.InitialSceneState;
            }
            else
            {
                isInitial = false;
                return data.RingBuffer[data.LatestWriteIndex];
            }
        }
        
        public PersistentDataContainer GetInitialStateReadContainer(Hash128 identifier)
        {
            var data = _allPersistedData[identifier];
            return data.InitialSceneState;
        }

        public int GetEntityCapacity(Hash128 identifier, out bool b)
        {
            ArchetypesWithStates states = _allPersistedData[identifier];
            b = states.IsPool;
            return states.EntityCapacity;
        }
    }
}