// Author: Jonas De Maeseneer

using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency.Hybrid
{
    [ConverterVersion("Jonas", 5)]
    [UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup))]
    public class PersistencyConversionReferenceSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            var typeHashesFile = RuntimePersistableTypesInfo.Load();
            if (typeHashesFile == null)
            {
                Debug.LogError("No RuntimePersistableTypesInfo asset found! (Nothing will persist)");
                return;
            }
            
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                DeclareAssetDependency(persistencyAuthoring.gameObject, typeHashesFile);
                Entities.ForEach((PersistencyAuthoring other) =>
                {
                    if (persistencyAuthoring.GetStablePersistenceArchetypeHash() == other.GetStablePersistenceArchetypeHash()
                        && persistencyAuthoring != other)
                    {
                        DeclareDependency(persistencyAuthoring, other);
                    }
                });
            });
        }
    }

    [ConverterVersion("Jonas", 4)]
    [UpdateInGroup(typeof(GameObjectAfterConversionGroup))]
    public class PersistencyConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Convert();
        }

        private void Convert()
        {
            var typeHashesFile = RuntimePersistableTypesInfo.Load();
            if (typeHashesFile == null)
            {
                Debug.LogError("No RuntimePersistableTypesInfo asset found! (Nothing will persist)");
                return;
            }
            
            Entities.ForEach((PersistencyAuthoring persistencyAuthoring) =>
            {
                Entity e = GetPrimaryEntity(persistencyAuthoring);
                DstEntityManager.AddSharedComponentData(e, new TypeHashesToPersist()
                {
                    TypeHashList = persistencyAuthoring.GetPersistingTypeHashes(typeHashesFile)
                });
                DstEntityManager.AddComponentData(e, new PersistenceState()
                {
                    ArrayIndex = persistencyAuthoring.CalculateArrayIndex()
                });
            });
        }
    }
}
