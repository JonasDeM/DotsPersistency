// Author: Jonas De Maeseneer

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency
{
    public struct PersistentDataContainer : IDisposable
    {
        // All data for a single subscene is stored in this array
        [NativeDisableParallelForRestriction]
        private NativeArray<byte> _data;

        private Allocator _allocatorLabel;
        
        // Containers with the same data identifier contain data from the same entities
        public Hash128 DataIdentifier => _dataIdentifier;
        private Hash128 _dataIdentifier;
        
        // Doesn't own these containers
        [ReadOnly] [NativeDisableParallelForRestriction]
        private NativeArray<PersistencyArchetypeDataLayout> _persistenceArchetypesDataLayouts;
        [ReadOnly] [NativeDisableParallelForRestriction]
        private NativeArray<PersistableTypeHandle> _allUniqueTypeHandles;
        public int UniqueTypeHandleCount => _allUniqueTypeHandles.Length;

        public int Tick
        {
            get => _tick;
            set => _tick = value;
        }
        private int _tick;

        public int DataLayoutCount => _persistenceArchetypesDataLayouts.Length;
        public bool IsCreated => _data.IsCreated;

        public PersistentDataContainer(Hash128 dataIdentifier, NativeArray<PersistencyArchetypeDataLayout> persistenceArchetypesDataLayouts, NativeArray<PersistableTypeHandle> allUniqueTypeHandles, Allocator allocator)
        {
            _dataIdentifier = dataIdentifier;
            _tick = 0;
            _persistenceArchetypesDataLayouts = persistenceArchetypesDataLayouts;
            _allUniqueTypeHandles = allUniqueTypeHandles;

            int size = 0;
            foreach (var persistenceArchetype in persistenceArchetypesDataLayouts)
            {
                size += persistenceArchetype.Amount * persistenceArchetype.SizePerEntity;
            }

            _allocatorLabel = allocator;
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
        
        internal PersistentDataContainer GetCopyWithCustomData(NativeArray<byte> ownData)
        {
            PersistentDataContainer copy = this;
            copy._data = ownData;
            return copy;
        }

        public int CalculateEntityCapacity()
        {
            int total = 0;
            for (int i = 0; i < _persistenceArchetypesDataLayouts.Length; i++)
            {
                total += _persistenceArchetypesDataLayouts[i].Amount;
            }

            return total;
        }

        public void Dispose()
        {
            if (_data.IsCreated)
            {
                _data.Dispose();
            }
        }

        internal unsafe void ResizeByInitializingWithFirstEntry(int newAmount)
        {
            Debug.Assert(_persistenceArchetypesDataLayouts.Length == 1);
            PersistencyArchetypeDataLayout dataLayout = _persistenceArchetypesDataLayouts[0];
            int oldAmount = dataLayout.Amount;
            int sizePerEntity = dataLayout.SizePerEntity;
            Debug.Assert(oldAmount > 0);
            NativeArray<byte> newData = new NativeArray<byte>(sizePerEntity * newAmount, _allocatorLabel);

            byte* destPtr = (byte*)newData.GetUnsafePtr();
            byte* sourcePtr = (byte*)_data.GetUnsafeReadOnlyPtr();

            ref BlobArray<PersistencyArchetypeDataLayout.TypeInfo> infoArray = ref dataLayout.PersistedTypeInfoArrayRef.Value;

            int amountMemCpy = math.min(oldAmount, newAmount);
            int growAmount = newAmount - oldAmount;
            int newOffset = 0;
            for (int i = 0; i < infoArray.Length; i++)
            {
                PersistencyArchetypeDataLayout.TypeInfo info = infoArray[i];
                int oldOffset = info.Offset;

                // copy over old data
                UnsafeUtility.MemCpy(destPtr+newOffset, sourcePtr+oldOffset, info.SizePerEntityForThisType * amountMemCpy);
                
                // replicate first entry into all the new indices
                if (growAmount > 0)
                {
                    int oldAmountBytes = info.SizePerEntityForThisType * oldAmount;
                    UnsafeUtility.MemCpyReplicate(destPtr+(newOffset+oldAmountBytes), sourcePtr+oldOffset, info.SizePerEntityForThisType, growAmount);
                }
                
                // Important!
                // We don't actually change the offsets in _persistenceArchetypesDataLayouts because we don't own it, it should be done outside & after of this method
                newOffset += info.SizePerEntityForThisType * newAmount;
            }
            
            // Replace old container
            _data.Dispose();
            _data = newData;
        }
        
        internal unsafe void ResizeByInitializingFromOther(int newAmount, PersistentDataContainer initialData)
        {
            Debug.Assert(initialData.GetRawData().Length != _data.Length, "The container we're taking as base is supposed to already be resized!");
            Debug.Assert(_persistenceArchetypesDataLayouts.Length == 1, "Only Pools of 1 PersistenceArchetype are supported!");
            PersistencyArchetypeDataLayout dataLayout = _persistenceArchetypesDataLayouts[0];
            int oldAmount = dataLayout.Amount;
            Debug.Assert(oldAmount > 0);
            NativeArray<byte> newData = new NativeArray<byte>(initialData.GetRawData(), _allocatorLabel);

            byte* destPtr = (byte*)newData.GetUnsafePtr();
            byte* sourcePtr = (byte*)_data.GetUnsafeReadOnlyPtr();

            ref BlobArray<PersistencyArchetypeDataLayout.TypeInfo> infoArray = ref dataLayout.PersistedTypeInfoArrayRef.Value;
            
            int amountMemCpy = math.min(oldAmount, newAmount);
            int newOffset = 0;
            for (int i = 0; i < infoArray.Length; i++)
            {
                PersistencyArchetypeDataLayout.TypeInfo info = infoArray[i];
                int oldOffset = info.Offset;
                
                // copy over old data
                UnsafeUtility.MemCpy(destPtr + newOffset, sourcePtr + oldOffset, info.SizePerEntityForThisType * amountMemCpy);
                
                // Important!
                // We don't actually change the offsets in _persistenceArchetypesDataLayouts because we don't own it, it should be done outside & after of this method
                newOffset += info.SizePerEntityForThisType * newAmount;
            }
            
            // Replace old container
            _data.Dispose();
            _data = newData;
        }
    }

}