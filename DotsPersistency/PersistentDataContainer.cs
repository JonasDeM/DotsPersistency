using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace DotsPersistency
{
    public struct PersistentDataContainer : IDisposable
    {
        // All data for a single subscene is stored in this array
        private NativeArray<byte> _data;
        
        // Containers with the same data identifier contain data from the same entities
        public Hash128 DataIdentifier => _dataIdentifier;
        private Hash128 _dataIdentifier;
        
        // Doesn't own this container
        private NativeArray<PersistencyArchetypeDataLayout> _persistenceArchetypesDataLayouts;

        public int Tick
        {
            get => _tick;
            set => _tick = value;
        }
        private int _tick;

        public int Count => _persistenceArchetypesDataLayouts.Length;

        public PersistentDataContainer(Hash128 dataIdentifier, NativeArray<PersistencyArchetypeDataLayout> persistenceArchetypesDataLayouts, Allocator allocator)
        {
            _dataIdentifier = dataIdentifier;
            _tick = 0;
            _persistenceArchetypesDataLayouts = persistenceArchetypesDataLayouts;

            int size = 0;
            foreach (var persistenceArchetype in persistenceArchetypesDataLayouts)
            {
                size += persistenceArchetype.Amount * persistenceArchetype.SizePerEntity;
            }
            _data = new NativeArray<byte>(size, allocator);
        }
            
        public PersistencyArchetypeDataLayout GetPersistenceArchetypeDataLayoutAtIndex(int index)
        {
            return _persistenceArchetypesDataLayouts[index];
        }
        
        public NativeArray<byte> GetSubArrayAtIndex(int index)
        {
            var archetype = _persistenceArchetypesDataLayouts[index];
            return _data.GetSubArray(archetype.Offset, archetype.Amount * archetype.SizePerEntity);
        }

        public NativeArray<byte> GetRawData()
        {
            return _data;
        }

        public PersistentDataContainer GetCopy()
        {
            PersistentDataContainer copy = this;
            copy._data = new NativeArray<byte>(_data, Allocator.Persistent);
            return copy;
        }

        public void Dispose()
        {
            if (_data.IsCreated)
            {
                _data.Dispose();
            }
        }
    }

}