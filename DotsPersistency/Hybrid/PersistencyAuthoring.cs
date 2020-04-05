// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency.Hybrid
{
    [RequiresEntityConversion]
    public class PersistencyAuthoring : MonoBehaviour
    {
        public List<ulong> TypesToPersistHashes = new List<ulong>();

        public FixedList128<ulong> GetPersistingTypeHashes()
        {
            var retVal = new FixedList128<ulong>();
            Debug.Assert(TypesToPersistHashes.Count <= retVal.Capacity, $"more than {retVal.Capacity} persisted ComponentData types is not supported");
            foreach (var hash in TypesToPersistHashes)
            {
                retVal.Add(hash); 
            }
            return retVal;
        }

        public Hash128 GetStablePersistenceArchetypeHash()
        {
            ulong hash1 = 0;
            ulong hash2 = 0;

            for (int i = 0; i < TypesToPersistHashes.Count; i++)
            {
                unchecked
                {
                    if (i%2 == 0)
                    {
                        ulong hash = 17;
                        hash = hash * 31 + hash1;
                        hash = hash * 31 + TypesToPersistHashes[i];
                        hash1 = hash;
                    }
                    else
                    {
                        ulong hash = 17;
                        hash = hash * 31 + hash2;
                        hash = hash * 31 + TypesToPersistHashes[i];
                        hash2 = hash;
                    }
                }
            }
            return new UnityEngine.Hash128(hash1, hash2);
        }
        
        public int CalculateArrayIndex()
        {
            Transform rootParent = transform;
            while (rootParent.parent)
            {
                rootParent = rootParent.parent;
            }
            int arrayIndex = 0;
            
            foreach (GameObject rootGameObject in gameObject.scene.GetRootGameObjects())
            {
                if (rootGameObject.transform == rootParent)
                    break;
                var compsInChildren = rootGameObject.GetComponentsInChildren<PersistencyAuthoring>();
                
                arrayIndex += compsInChildren.Count(comp => TypesToPersistHashes.SequenceEqual(comp.TypesToPersistHashes));
            }
            foreach (PersistencyAuthoring child in rootParent.GetComponentsInChildren<PersistencyAuthoring>())
            {
                if (child == this)
                    break;

                if (TypesToPersistHashes.SequenceEqual(child.TypesToPersistHashes))
                {
                    arrayIndex += 1;
                }
            }
            return arrayIndex;
        }
    }
}
