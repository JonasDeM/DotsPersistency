﻿// Author: Jonas De Maeseneer

using System.Runtime.CompilerServices;
using System;
using DotsPersistency.Containers;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[assembly:InternalsVisibleTo("io.jonasdem.dotspersistency.hybrid")]

namespace DotsPersistency
{
    // A persisting entity needs this component
    // It holds which data it needs to persist
    public struct PersistenceArchetype : ISharedComponentData
    {
        public BlobAssetReference<BlobArray<PersistedTypeInfo>> PersistedTypeInfoArrayRef;
        public int Amount;
        public int Offset; // Byte Offset in the Byte Array which contains all data for 1 SceneSection
        public int ArchetypeIndex; // Index specifically for this SceneSection
        public int SizePerEntity;

        public TypeHashesToPersist ToHashList()
        {
            FixedList128<ulong> list = new FixedList128<ulong>();
            for (int i = 0; i < PersistedTypeInfoArrayRef.Value.Length; i++)
            {
                list.Add(PersistedTypeInfoArrayRef.Value[i].StableHash);
            }

            return new TypeHashesToPersist { TypeHashList = list };
        }
    }
    
    [Serializable]
    public struct TypeHashesToPersist : ISharedComponentData
    {
        // A unity bug makes me have to use this temporary workaround instead of just using FixedList as a field
        // https://fogbugz.unity3d.com/default.asp?1250205_h3i38krgoh6ibpc9
        public FixedList128<ulong> TypeHashList
        {
            get
            {
                FixedList128<ulong> retVal = new FixedList128<ulong>();
                for (int i = 0; i < _length; i++)
                {
                    retVal.Add(_array16[i]);
                }
                return retVal;
            }
            set
            {
                _length = (short)value.Length;
                for (int i = 0; i < value.Length; i++)
                {
                    _array16[i] = value[i];
                }
            }
        } // can store 15 type hashes

        [SerializeField] private short _length;
        [SerializeField] private FixedArray16<ulong> _array16;
    }

    public struct PersistedTypeInfo
    {
        public ulong StableHash;
        public int ElementSize;
        public int MaxElements;
        public bool IsBuffer;
        public int Offset; // Byte Offset in the Byte Sub Array which contains all data for a PersistenceArchetype in 1 SceneSection
    }
    
    // A persisting entity needs this component
    // It holds the index into the sub array that holds the persisted data
    // Entities their data will reside in the same arrays if they have the same SharedComponentData for SceneSection & PersistenceArchetype
    public struct PersistenceState : IComponentData, IEquatable<PersistenceState>
    {
        public int ArrayIndex;

        public bool Equals(PersistenceState other)
        {
            return ArrayIndex == other.ArrayIndex;
        }
    }
    
    // This struct sits in front of every data block in the persisted data array
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
