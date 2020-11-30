// Author: Jonas De Maeseneer

using System.Runtime.CompilerServices;
using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

[assembly:InternalsVisibleTo("io.jonasdem.dotspersistency.hybrid")]

namespace DotsPersistency
{
    // A persisting entity needs this component
    // It holds which data it needs to persist & where it needs to write it to
    public struct PersistencyArchetypeDataLayout : ISharedComponentData
    {
        public BlobAssetReference<BlobArray<TypeInfo>> PersistedTypeInfoArrayRef;
        public int Amount;
        public int Offset; // Byte Offset in the Byte Array which contains all data for 1 SceneSection
        public int SizePerEntity;
        public ushort ArchetypeIndexInContainer; // Index specifically for any container for this SceneSection
        public Hash128 PersistableTypeHandleCombinationHash;

        public struct TypeInfo
        {
            public PersistableTypeHandle PersistableTypeHandle; // index in PersistencySettings its type list
            public int ElementSize;
            public int MaxElements;
            public bool IsBuffer;
            public int Offset; // Byte Offset in the Byte Sub Array which contains all data for a PersistenceArchetype in 1 SceneSection
        }
    }
    
    // This component gets replaced by PersistencyArchetypeDataLayout on the first frame an entity is loaded
    [Serializable]
    public struct PersistableTypeCombinationHash : ISharedComponentData
    {
        public Hash128 Value;
    }
    
    [Serializable]
    public struct PersistencyContainerTag : ISharedComponentData
    {
        public Hash128 DataIdentifier;
    }

    public struct PersistencyArchetypeIndexInContainer : IComponentData
    {
        public ushort Index;
    }
    
    public struct PersistencyArchetype
    {
        public BlobArray<PersistableTypeHandle> PersistableTypeHandles; // could be a blob array
        public Hash128 PersistableTypeHandleCombinationHash;
        
        // This is the amount of entities with this PersistencyArchetype
        public int AmountEntities;
    }

    public struct ScenePersistencyInfo
    {
        public BlobArray<PersistableTypeHandle> AllUniqueTypeHandles;
        public BlobArray<PersistencyArchetype> PersistencyArchetypes;
    }

    public struct ScenePersistencyInfoRef : IComponentData
    {
        public BlobAssetReference<ScenePersistencyInfo> InfoRef;
    }
    
    // A persisting entity needs this component
    // It holds the index into the sub array that holds the persisted data
    // Entities their data will reside in the same arrays if they have the same SharedComponentData for SceneSection & PersistenceArchetypeDataLayout
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
