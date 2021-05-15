// Author: Jonas De Maeseneer

using System.Collections.Generic;
using System.Linq;
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
            typeHandleCombinationHash = persistencySettings.GetTypeHandleCombinationHash(FullTypeNamesToPersist);
        }

        // The reason for the extra complexity is deterministic conversion
        internal int CalculateArrayIndex(PersistencySettings settings)
        {
            Transform rootParent = transform;
            
            while (rootParent.parent != null)
            {
                rootParent = rootParent.parent;
            }
            int arrayIndex = 0;

            var typeHandleCombinationHash = settings.GetTypeHandleCombinationHash(FullTypeNamesToPersist);
            
            foreach (GameObject rootGameObject in gameObject.scene.GetRootGameObjects())
            {
                if (rootGameObject.transform == rootParent)
                    break;
                var compsInChildren = rootGameObject.GetComponentsInChildren<PersistencyAuthoring>();
                
                arrayIndex += compsInChildren.Count(comp => typeHandleCombinationHash.Equals(settings.GetTypeHandleCombinationHash(comp.FullTypeNamesToPersist)));
            }
            foreach (PersistencyAuthoring child in rootParent.GetComponentsInChildren<PersistencyAuthoring>())
            {
                if (child == this)
                    break;

                if (typeHandleCombinationHash.Equals(settings.GetTypeHandleCombinationHash(child.FullTypeNamesToPersist)))
                {
                    arrayIndex += 1;
                }
            }
            return arrayIndex;
        }
    }
}
