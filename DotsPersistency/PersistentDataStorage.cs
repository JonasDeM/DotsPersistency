// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace DotsPersistency
{
    public class PersistentDataStorage
    {
        struct ArchetypesWithStates
        {
            public int LatestWriteIndex;
            public NativeArray<PersistenceArchetypeDataLayout> Archetypes;
            public PersistentDataContainer InitialSceneState;
            public List<PersistentDataContainer> RingBuffer;
        }
        
        private int _ringBufferIndex;
        private int _latestWriteNonWrappedIndex;
        private int _nonWrappedIndex;
        public int NonWrappedIndex => _nonWrappedIndex;
        public readonly int RingBufferSize;
        private Dictionary<SceneSection, ArchetypesWithStates> _allPersistedData = new Dictionary<SceneSection, ArchetypesWithStates>(8);
        private IPersistencySerializer _serializer = new DefaultPersistencySerializer();
        
        public PersistentDataStorage(int ringBufferSize)
        {
            RingBufferSize = ringBufferSize;
        }
        
        public void Dispose()
        {
            foreach (ArchetypesWithStates value in _allPersistedData.Values)
            {
                value.Archetypes.Dispose();
                value.InitialSceneState.Dispose();
                foreach (var persistentDataContainer in value.RingBuffer)
                {
                    persistentDataContainer.Dispose();
                }
            }
        }

        public void ToIndex(int nonWrappedIndex)
        {
            Debug.Assert(_latestWriteNonWrappedIndex - nonWrappedIndex < RingBufferSize);
            _ringBufferIndex = nonWrappedIndex % RingBufferSize;
            _nonWrappedIndex = nonWrappedIndex;
        }

        public PersistentDataContainer GetWriteContainerForCurrentIndex(SceneSection sceneSection)
        {
            var data = _allPersistedData[sceneSection];
            data.LatestWriteIndex = _ringBufferIndex;
            _allPersistedData[sceneSection] = data;
            _latestWriteNonWrappedIndex = _nonWrappedIndex;
            return data.RingBuffer[_ringBufferIndex];
        }
        
        public PersistentDataContainer GetReadContainerForCurrentIndex(SceneSection sceneSection)
        {
            return _allPersistedData[sceneSection].RingBuffer[_ringBufferIndex];
        }

        public virtual bool RequireContainer(SceneSection sceneSection)
        {
            return true;
        }

        // returns initial state container, so it would be used to persist the initial state
        public PersistentDataContainer InitializeSceneSection(SceneSection sceneSection, NativeArray<PersistenceArchetypeDataLayout> archetypes)
        {
            Debug.Assert(!_allPersistedData.ContainsKey(sceneSection));
            var newData = new ArchetypesWithStates()
            {
                Archetypes = archetypes,
                InitialSceneState = new PersistentDataContainer(sceneSection, archetypes, Allocator.Persistent),
                RingBuffer = new List<PersistentDataContainer>(RingBufferSize),
                LatestWriteIndex = -1
            };
            for (int i = 0; i < RingBufferSize; i++)
            {
                newData.RingBuffer.Add(newData.InitialSceneState.GetCopy());
            }
            _allPersistedData[sceneSection] = newData;
            return newData.InitialSceneState;
        }
        
        public bool IsSceneSectionInitialized(SceneSection sceneSection)
        {
            return _allPersistedData.ContainsKey(sceneSection);
        }
        
        public void RegisterCustomSerializer(IPersistencySerializer serializer)
        {
            _serializer = serializer;
        }
        
        public void SaveToDisk(SceneSection sceneSection)
        {
            _serializer.WriteContainerData(GetReadContainerForCurrentIndex(sceneSection));
        }

        public void ReadContainerDataFromDisk(SceneSection sceneSection)
        {
            _serializer.ReadContainerData(GetWriteContainerForCurrentIndex(sceneSection));
        }

        public PersistentDataContainer GetLatestWrittenState(SceneSection sceneSection, out bool isInitial)
        {
            var data = _allPersistedData[sceneSection];
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
        
        public PersistentDataContainer GetInitialState(SceneSection sceneSection)
        {
            var data = _allPersistedData[sceneSection];
            return data.InitialSceneState;
        }
    }
}