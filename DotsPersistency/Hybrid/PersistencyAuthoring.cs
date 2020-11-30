﻿// Author: Jonas De Maeseneer

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
    public class PersistencyAuthoring : MonoBehaviour
    {
        public List<string> FullTypeNamesToPersist = new List<string>();

        
        internal void GetConversionInfo(PersistencySettings persistencySettings, List<PersistableTypeHandle> listToFill, out int indexInScene, out Hash128 typeHandleCombinationHash)
        {
            Debug.Assert(listToFill.Count == 0);
            
            foreach (var fullTypeName in FullTypeNamesToPersist)
            {
                if (persistencySettings.GetPersistableTypeHandleFromFullTypeName(fullTypeName, out PersistableTypeHandle typeHandle))
                {
                    listToFill.Add(typeHandle); 
                }
                else if (!string.IsNullOrEmpty(fullTypeName))
                {
                    Debug.LogWarning($"Ignoring non-persistable type {fullTypeName} (Did it get removed from PersistencySettings?)");
                }
            }

            indexInScene = CalculateArrayIndex();
            typeHandleCombinationHash = GetHash(listToFill);
        }

        // The only reason for this is deterministic conversion
        internal int CalculateArrayIndex()
        {
            Transform rootParent = transform;
            
            while (rootParent.parent != null)
            {
                rootParent = rootParent.parent;
            }
            int arrayIndex = 0;
            
            foreach (GameObject rootGameObject in gameObject.scene.GetRootGameObjects())
            {
                if (rootGameObject.transform == rootParent)
                    break;
                var compsInChildren = rootGameObject.GetComponentsInChildren<PersistencyAuthoring>();
                
                arrayIndex += compsInChildren.Count(comp => FullTypeNamesToPersist.SequenceEqual(comp.FullTypeNamesToPersist));
            }
            foreach (PersistencyAuthoring child in rootParent.GetComponentsInChildren<PersistencyAuthoring>())
            {
                if (child == this)
                    break;

                if (FullTypeNamesToPersist.SequenceEqual(child.FullTypeNamesToPersist))
                {
                    arrayIndex += 1;
                }
            }
            return arrayIndex;
        }
        
        internal static Hash128 GetHash(List<PersistableTypeHandle> persistableTypeHandles)
        {
            ulong hash1 = 0;
            ulong hash2 = 0;

            for (int i = 0; i < persistableTypeHandles.Count; i++)
            {
                unchecked
                {
                    if (i%2 == 0)
                    {
                        ulong hash = 17;
                        hash = hash * 31 + hash1;
                        hash = hash * 31 + (ulong)persistableTypeHandles[i].Handle.GetHashCode();
                        hash1 = hash;
                    }
                    else
                    {
                        ulong hash = 17;
                        hash = hash * 31 + hash2;
                        hash = hash * 31 + (ulong)persistableTypeHandles[i].Handle.GetHashCode();
                        hash2 = hash;
                    }
                }
            }
            return new UnityEngine.Hash128(hash1, hash2);
        }
    }
}
