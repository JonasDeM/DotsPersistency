// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency
{
    public class PersistentDataStorage
    {
        private struct ArchetypesWithStates : IDisposable
        {
            public int LatestWriteIndex;
            public NativeArray<PersistencyArchetypeDataLayout> Archetypes;
            public PersistentDataContainer InitialSceneState;
            public List<PersistentDataContainer> RingBuffer;
            
            public NativeArray<PersistableTypeHandle> AllUniqueTypeHandles;
            public int UniqueBufferTypeHandleCount;
            public int UniqueComponentDataTypeHandleCount => AllUniqueTypeHandles.Length - UniqueBufferTypeHandleCount;

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
        }
        
        private int _ringBufferIndex;
        private int _latestWriteNonWrappedIndex;
        private int _nonWrappedIndex;
        public int NonWrappedIndex => _nonWrappedIndex;
        public readonly int RingBufferSize;
        private Dictionary<Hash128, ArchetypesWithStates> _allPersistedData = new Dictionary<Hash128, ArchetypesWithStates>(8);
        private IPersistentDataSerializer _serializer = new DefaultPersistentDataSerializer();
        
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
        }

        public void ToIndex(int nonWrappedIndex)
        {
            Debug.Assert(_latestWriteNonWrappedIndex - nonWrappedIndex < RingBufferSize);
            _ringBufferIndex = nonWrappedIndex % RingBufferSize;
            _nonWrappedIndex = nonWrappedIndex;
        }

        public PersistentDataContainer GetWriteContainerForCurrentIndex(Hash128 identifier)
        {
            var data = _allPersistedData[identifier];
            data.LatestWriteIndex = _ringBufferIndex;
            _allPersistedData[identifier] = data;
            _latestWriteNonWrappedIndex = _nonWrappedIndex;
            return data.RingBuffer[_ringBufferIndex];
        }
        
        public PersistentDataContainer GetReadContainerForCurrentIndex(Hash128 identifier)
        {
            return _allPersistedData[identifier].RingBuffer[_ringBufferIndex];
        }

        public virtual bool RequireContainer(Hash128 identifier)
        {
            return true;
        }

        // returns initial state container, so it would be used to persist the initial state
        public PersistentDataContainer InitializeScene(Hash128 identifier, NativeArray<PersistencyArchetypeDataLayout> archetypes, NativeArray<PersistableTypeHandle> allUniqueTypeHandles, int uniqueBufferTypeHandleCount)
        {
            Debug.Assert(!_allPersistedData.ContainsKey(identifier));
            var newData = new ArchetypesWithStates()
            {
                Archetypes = archetypes,
                AllUniqueTypeHandles = allUniqueTypeHandles,
                UniqueBufferTypeHandleCount = uniqueBufferTypeHandleCount,
                InitialSceneState = new PersistentDataContainer(identifier, archetypes, allUniqueTypeHandles, uniqueBufferTypeHandleCount, Allocator.Persistent),
                RingBuffer = new List<PersistentDataContainer>(RingBufferSize),
                LatestWriteIndex = -1
            };
            for (int i = 0; i < RingBufferSize; i++)
            {
                newData.RingBuffer.Add(newData.InitialSceneState.GetCopy(Allocator.Persistent));
            }
            _allPersistedData[identifier] = newData;
            return newData.InitialSceneState;
        }
        
        public bool IsInitialized(Hash128 identifier)
        {
            return _allPersistedData.ContainsKey(identifier);
        }
        
        public void RegisterCustomSerializer(IPersistentDataSerializer serializer)
        {
            _serializer = serializer;
        }
        
        public void SaveToDisk(Hash128 identifier)
        {
            _serializer.WriteContainerData(GetReadContainerForCurrentIndex(identifier));
        }

        public void ReadContainerDataFromDisk(Hash128 identifier)
        {
            _serializer.ReadContainerData(GetWriteContainerForCurrentIndex(identifier));
        }

        public PersistentDataContainer GetLatestWrittenState(Hash128 identifier, out bool isInitial)
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
        
        public PersistentDataContainer GetInitialState(Hash128 identifier)
        {
            var data = _allPersistedData[identifier];
            return data.InitialSceneState;
        }
    }
}