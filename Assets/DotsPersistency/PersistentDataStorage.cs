// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace DotsPersistency
{
    public class PersistentDataStorage : IDisposable
    {
        private Dictionary<SceneSection, PersistentDataContainer> _sceneToData;

        public PersistentDataStorage()
        {
            _sceneToData = new Dictionary<SceneSection, PersistentDataContainer>(32);
        }

        public PersistentDataContainer GetCopyOfContainer(SceneSection sceneSection)
        {
            Debug.Assert(_sceneToData.ContainsKey(sceneSection));
            return _sceneToData[sceneSection].GetCopy();
        }
        
        public void Dispose()
        {
            foreach (var value in _sceneToData.Values)
            {
                value.Dispose();
            }
        }
        
        public void Dispose(JobHandle jobHandle)
        {
            foreach (var value in _sceneToData.Values)
            {
                value.Dispose(jobHandle);
            }
        }

        public PersistentDataContainer GetExistingContainer(SceneSection sceneSection)
        {
            return _sceneToData[sceneSection];
        }
        
        // you need to pass a persistent array, this function takes ownership
        internal void CreateContainer(SceneSection sceneSection, NativeArray<PersistenceArchetype> archetypes)
        {
            _sceneToData.Add(sceneSection, new PersistentDataContainer(sceneSection, archetypes, Allocator.Persistent));
        }
        
        public bool HasContainer(SceneSection sceneSection)
        {
            return _sceneToData.ContainsKey(sceneSection);
        }

        public bool IsWaitingForContainer(SceneSection sceneSection)
        {
            // Can do an async file read & return false once it's fully read into memory
            // Can do a web request & return false once you got the data
            return false;
        }
    }
    
    public struct PersistentDataContainer : IDisposable
    {
        private NativeArray<byte> _data;
        private NativeArray<PersistenceArchetype> _persistenceArchetypes;
            
        public SceneSection SceneSection => _sceneSection;
        private SceneSection _sceneSection;

        public int Count => _persistenceArchetypes.Length;

        public PersistentDataContainer(SceneSection sceneSection, NativeArray<PersistenceArchetype> persistenceArchetypes, Allocator allocator)
        {
            _sceneSection = sceneSection;
            _persistenceArchetypes = new NativeArray<PersistenceArchetype>(persistenceArchetypes.Length, Allocator.Persistent);
            persistenceArchetypes.CopyTo(_persistenceArchetypes);

            int size = 0;
            foreach (var persistenceArchetype in persistenceArchetypes)
            {
                size += persistenceArchetype.Amount * persistenceArchetype.SizePerEntity;
            }
            _data = new NativeArray<byte>(size, allocator);
        }
            
        public PersistenceArchetype GetPersistenceArchetypeAtIndex(int index)
        {
            return _persistenceArchetypes[index];
        }
            
        public NativeArray<byte> GetSubArrayAtIndex(int index)
        {
            var archetype = _persistenceArchetypes[index];
            return _data.GetSubArray(archetype.Offset, archetype.Amount * archetype.SizePerEntity);
        }

        public NativeArray<byte> GetRawData()
        {
            return _data;
        }
            
        public PersistentDataContainer GetCopy()
        {
            PersistentDataContainer copy = this;
            _data.CopyTo(copy._data);
            return copy;
        }

        public void Dispose()
        {
            _data.Dispose();
        }
            
        public void Dispose(JobHandle jobHandle)
        {
            _data.Dispose(jobHandle);
        }
    }

}