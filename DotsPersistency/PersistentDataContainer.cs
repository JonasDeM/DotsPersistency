using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace DotsPersistency
{
    public struct PersistentDataContainer : IDisposable
    {
        // All data for a single subscene is stored in this array
        [NativeDisableParallelForRestriction]
        private NativeArray<byte> _data;
        
        // Containers with the same data identifier contain data from the same entities
        public Hash128 DataIdentifier => _dataIdentifier;
        private Hash128 _dataIdentifier;
        
        // Doesn't own these containers
        [ReadOnly] [NativeDisableParallelForRestriction]
        private NativeArray<PersistencyArchetypeDataLayout> _persistenceArchetypesDataLayouts;
        [ReadOnly] [NativeDisableParallelForRestriction]
        private NativeArray<PersistableTypeHandle> _allUniqueTypeHandles;
        public int UniqueTypeHandleCount => _allUniqueTypeHandles.Length;
        public int UniqueComponentDataTypeHandleCount => _allUniqueTypeHandles.Length - UniqueBufferTypeHandleCount;
        public readonly int UniqueBufferTypeHandleCount;

        public int Tick
        {
            get => _tick;
            set => _tick = value;
        }
        private int _tick;

        public int DataLayoutCount => _persistenceArchetypesDataLayouts.Length;

        public PersistentDataContainer(Hash128 dataIdentifier, NativeArray<PersistencyArchetypeDataLayout> persistenceArchetypesDataLayouts, NativeArray<PersistableTypeHandle> allUniqueTypeHandles, int uniqueBufferTypeHandleCount, Allocator allocator)
        {
            _dataIdentifier = dataIdentifier;
            _tick = 0;
            _persistenceArchetypesDataLayouts = persistenceArchetypesDataLayouts;
            _allUniqueTypeHandles = allUniqueTypeHandles;
            UniqueBufferTypeHandleCount = uniqueBufferTypeHandleCount;

            int size = 0;
            foreach (var persistenceArchetype in persistenceArchetypesDataLayouts)
            {
                size += persistenceArchetype.Amount * persistenceArchetype.SizePerEntity;
            }
            _data = new NativeArray<byte>(size, allocator);
        }
            
        public PersistencyArchetypeDataLayout GetDataLayoutAtIndex(int index)
        {
            return _persistenceArchetypesDataLayouts[index];
        }
        
        public PersistableTypeHandle GetUniqueTypeHandleAtIndex(int index)
        {
            return _allUniqueTypeHandles[index];
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

        public PersistentDataContainer GetCopy(Allocator allocator)
        {
            PersistentDataContainer copy = this;
            copy._data = new NativeArray<byte>(_data, allocator);
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