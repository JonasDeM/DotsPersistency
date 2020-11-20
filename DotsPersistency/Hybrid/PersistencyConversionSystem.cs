// Author: Jonas De Maeseneer

using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency.Hybrid
{
    [ConverterVersion("Jonas", 7)]
    [UpdateInGroup(typeof(GameObjectAfterConversionGroup))]
    public class PersistencyConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            var settings = PersistencySettings.Get();
            if (settings == null)
            {
                Debug.LogWarning(PersistencySettings.NotFoundMessage);
                return;
            }
            
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                Entity e = GetPrimaryEntity(persistencyAuthoring);
                DstEntityManager.AddSharedComponentData(e, new PersistencyArchetype()
                {
                    // these type handles are stable as long as the PersistencySettings asset doesn't change, but we declare it as an asset dependency so it works out
                    PersistableTypeHandles = persistencyAuthoring.GetPersistableTypeHandles(settings) 
                });
                DstEntityManager.AddComponentData(e, new PersistenceState()
                {
                    ArrayIndex = persistencyAuthoring.CalculateArrayIndex()
                });
            });
        }
    }
    
    [ConverterVersion("Jonas", 7)]
    [UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup))]
    public class PersistencyConversionDependencySystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            var settings = PersistencySettings.Get();
            if (settings == null)
            {
                Debug.LogWarning(PersistencySettings.NotFoundMessage);
                return;
            }
            
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                DeclareAssetDependency(persistencyAuthoring.gameObject, settings);
                Entities.ForEach((PersistencyAuthoring other) =>
                {
                    if (persistencyAuthoring.GetHashOfTypeCombination() == other.GetHashOfTypeCombination()
                        && persistencyAuthoring != other)
                    {
                        DeclareDependency(persistencyAuthoring, other);
                    }
                });
            });
        }
    }
}
