// Author: Jonas De Maeseneer

using System.Runtime.CompilerServices;
using System;
using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

[assembly:InternalsVisibleTo("io.jonasdem.dotspersistency.hybrid")]

namespace DotsPersistency
{
    // This structure has all the details needed to write/read from a sub-array of a large contiguous array
    public struct PersistencyArchetypeDataLayout 
    {
        public BlobAssetReference<BlobArray<TypeInfo>> PersistedTypeInfoArrayRef;
        public int Amount;
        public int Offset; // Byte Offset in the larger Byte Array
        public int SizePerEntity;
        public ushort ArchetypeIndexInContainer; // Index specifically for any container for this SceneSection
        public Hash128 PersistableTypeHandleCombinationHash;

        public struct TypeInfo
        {
            public PersistableTypeHandle PersistableTypeHandle; // index in PersistencySettings its type list
            public int ElementSize;
            public int MaxElements;
            public bool IsBuffer;
            public int Offset; // Byte Offset in the Byte Sub Array which contains all data for a PersistenceArchetype
            public int SizePerEntityForThisType => (ElementSize * MaxElements) + PersistenceMetaData.SizeOfStruct;
        }
    }
    
    // PersistableTypeCombinationHash & PersistencyArchetypeIndexInContainer work together so we can grab the correct sub-array & dataLayout for a chunk
    [Serializable]
    public struct PersistableTypeCombinationHash : ISharedComponentData
    {
        public Hash128 Value;
    }
    public struct PersistencyArchetypeIndexInContainer : IComponentData
    {
        public ushort Index;
    }
    
    // This component makes it so we can iterate the right chunks for a container
    [Serializable]
    public struct PersistencyContainerTag : ISharedComponentData
    {
        public Hash128 DataIdentifier;
    }

    public struct PersistencyArchetype
    {
        public BlobArray<PersistableTypeHandle> PersistableTypeHandles;
        public Hash128 PersistableTypeHandleCombinationHash;
        
        // This is the amount of entities with this PersistencyArchetype
        public int AmountEntities;
    }

    public struct ScenePersistencyInfo
    {
        public BlobArray<PersistableTypeHandle> AllUniqueTypeHandles;
        public BlobArray<PersistencyArchetype> PersistencyArchetypes;
    }

    // Each subScene with persisting entities will have a singleton entity with this component.
    // It contains info to quickly construct PersistencyArchetypeDataLayout at runtime
    public struct ScenePersistencyInfoRef : IComponentData
    {
        public BlobAssetReference<ScenePersistencyInfo> InfoRef;
    }
    
    // This component holds the index into the sub array 
    public struct PersistenceState : IComponentData, IEquatable<PersistenceState>
    {
        public int ArrayIndex;

        public bool Equals(PersistenceState other)
        {
            return ArrayIndex == other.ArrayIndex;
        }
    }
    
    // This struct sits in front of every data block in a persisted data array
    public struct PersistenceMetaData
    {
        public ushort Data;

        public PersistenceMetaData(int diff, ushort amount)
        {
            Debug.Assert(amount <= MaxValueForAmount);
            Data = amount;
            if (diff != 0)
            {
                Data |= 1 << 15;
            }
        }

        public bool HasChanged => (Data & ~MaxValueForAmount) != 0;
        public int AmountFound => Data & MaxValueForAmount;
        public bool FoundOne => AmountFound != 0;
        public const ushort MaxValueForAmount = 0x7FFF; // 0111 1111 1111 1111
        public const int SizeOfStruct = sizeof(ushort);
    }
}
