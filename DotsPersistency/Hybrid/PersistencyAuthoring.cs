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

        public FixedList128<PersistableTypeHandle> GetPersistableTypeHandles(PersistencySettings persistencySettings)
        {
            var retVal = new FixedList128<PersistableTypeHandle>();
            Debug.Assert(FullTypeNamesToPersist.Count <= retVal.Capacity, $"More than {retVal.Capacity} persisted ComponentData types is not supported");
            foreach (var fullTypeName in FullTypeNamesToPersist)
            {
                if (persistencySettings.GetPersistableTypeHandleFromFullTypeName(fullTypeName, out PersistableTypeHandle typeHandle))
                {
                    retVal.Add(typeHandle); 
                }
                else if (!string.IsNullOrEmpty(fullTypeName))
                {
                    Debug.LogWarning($"Ignoring non-persistable type {fullTypeName} (Did it get removed from PersistencySettings?)");
                }
            }
            return retVal;
        }

        [Obsolete]
        private int CalculateArrayIndex()
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
    }
}
