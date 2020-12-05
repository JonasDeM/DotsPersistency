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
    public class PersistencyAuthoring : MonoBehaviour
    {
        public List<string> FullTypeNamesToPersist = new List<string>();

        
        internal void GetConversionInfo(PersistencySettings persistencySettings, List<PersistableTypeHandle> listToFill, out int indexInScene, out Hash128 typeHandleCombinationHash)
        {
            Debug.Assert(listToFill.Count == 0);
            
            foreach (var fullTypeName in FullTypeNamesToPersist)
            {
                if (persistencySettings.ContainsType(fullTypeName))
                {
                    listToFill.Add(persistencySettings.GetPersistableTypeHandleFromFullTypeName(fullTypeName)); 
                }
                else if (!string.IsNullOrEmpty(fullTypeName))
                {
                    Debug.LogWarning($"Ignoring non-persistable type {fullTypeName} (Did it get removed from PersistencySettings?)");
                }
            }

            indexInScene = CalculateArrayIndex(persistencySettings);
            typeHandleCombinationHash = GetTypeHandleCombinationHash(persistencySettings);
        }

        // The only reason for this is deterministic conversion
        internal int CalculateArrayIndex(PersistencySettings settings)
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
                
                arrayIndex += compsInChildren.Count(comp => GetTypeHandleCombinationHash(settings).Equals(comp.GetTypeHandleCombinationHash(settings)));
            }
            foreach (PersistencyAuthoring child in rootParent.GetComponentsInChildren<PersistencyAuthoring>())
            {
                if (child == this)
                    break;

                if (GetTypeHandleCombinationHash(settings).Equals(child.GetTypeHandleCombinationHash(settings)))
                {
                    arrayIndex += 1;
                }
            }
            return arrayIndex;
        }
        
        private Hash128 GetTypeHandleCombinationHash(PersistencySettings settings)
        {
            ulong hash1 = 0;
            ulong hash2 = 0;

            for (int i = 0; i < FullTypeNamesToPersist.Count; i++)
            {
                string fullTypeName = FullTypeNamesToPersist[i];
                if (settings.ContainsType(fullTypeName))
                {
                    PersistableTypeHandle handle = settings.GetPersistableTypeHandleFromFullTypeName(fullTypeName); 
                    
                    unchecked
                    {
                        if (i%2 == 0)
                        {
                            ulong hash = 17;
                            hash = hash * 31 + hash1;
                            hash = hash * 31 + (ulong)handle.Handle.GetHashCode();
                            hash1 = hash;
                        }
                        else
                        {
                            ulong hash = 17;
                            hash = hash * 31 + hash2;
                            hash = hash * 31 + (ulong)handle.Handle.GetHashCode();
                            hash2 = hash;
                        }
                    }
                }
            }
            return new UnityEngine.Hash128(hash1, hash2);
        }
    }
}
