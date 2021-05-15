// Author: Jonas De Maeseneer

using System;
using System.Data;
using Unity.Entities;
using UnityEngine;

namespace DotsPersistency
{
    [Serializable]
    public struct PersistableTypeHandle
    {
        [SerializeField]
        internal ushort Handle;

        public bool IsValid => Handle > 0;
    }
    
    [Serializable]
    internal struct PersistableTypeInfo
    {
        // If any of these values change on the type definition itself the validity check will pick that up & the user needs to force update
        // example: user makes a his previous IComponentData into an IBufferElementData
        // example: user renames his type or changes the namespace
        // example: something in unity changes that makes the stable type hash different than before
        public string FullTypeName;
        public ulong StableTypeHash;
        public bool IsBuffer;
        public int MaxElements;

        [System.Diagnostics.Conditional("DEBUG")]
        public void ValidityCheck()
        {
            if (MaxElements < 1 || string.IsNullOrEmpty(FullTypeName))
            {
                throw new DataException("Invalid PersistableTypeInfo in the RuntimePersistableTypesInfo asset! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
            }

            int typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(StableTypeHash);
            
            if (typeIndex == -1)
            {
                throw new DataException($"{FullTypeName} has an invalid StableTypeHash in PersistableTypeInfo in the RuntimePersistableTypesInfo asset! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
            }
            
            if (TypeManager.IsBuffer(typeIndex) != IsBuffer)
            {
                throw new DataException($"{FullTypeName} is set as a buffer in the RuntimePersistableTypesInfo asset, but is not actually a buffer type! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
            }

            if (!IsBuffer && MaxElements > 1)
            {
                throw new DataException($"{FullTypeName} has {MaxElements.ToString()} as MaxElements the RuntimePersistableTypesInfo asset, but it is not a buffer type! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
            }
        }
    }
}