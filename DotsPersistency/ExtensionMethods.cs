﻿using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using DotsPersistency;
using Unity.Collections;
using Unity.Entities;

public static class ExtensionMethods 
{
    [Pure]
    public static BlobAssetReference<BlobArray<PersistencyArchetypeDataLayout.TypeInfo>> BuildTypeInfoBlobAsset(this ref PersistencyArchetype persistencyArchetype, PersistencySettings persistencySettings, int amountEntities, out int sizePerEntity)
    {
        BlobAssetReference<BlobArray<PersistencyArchetypeDataLayout.TypeInfo>> blobAssetReference;
        int currentOffset = 0;
        sizePerEntity = 0;
        
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            ref BlobArray<PersistencyArchetypeDataLayout.TypeInfo> blobArray = ref blobBuilder.ConstructRoot<BlobArray<PersistencyArchetypeDataLayout.TypeInfo>>();

            var persistableTypeHandles = persistencyArchetype.PersistableTypeHandles;
            var blobBuilderArray = blobBuilder.Allocate(ref blobArray, persistableTypeHandles.Length);

            for (int i = 0; i < blobBuilderArray.Length; i++)
            {
                var persistableTypeHandle = persistableTypeHandles[i];
                var typeInfo = TypeManager.GetTypeInfo(persistencySettings.GetTypeIndex(persistableTypeHandle));
                var maxElements = persistencySettings.GetMaxElements(persistableTypeHandle);
                ValidateType(typeInfo);
                
                blobBuilderArray[i] = new PersistencyArchetypeDataLayout.TypeInfo()
                {
                    PersistableTypeHandle = persistableTypeHandles[i],
                    ElementSize = typeInfo.ElementSize,
                    IsBuffer = typeInfo.Category == TypeManager.TypeCategory.BufferData,
                    MaxElements = maxElements,
                    Offset = currentOffset
                };
                int sizeForComponent = (typeInfo.ElementSize * maxElements) + sizeof(ushort); // PersistenceMetaData is one ushort
                sizePerEntity += sizeForComponent; 
                currentOffset += sizeForComponent * amountEntities;
            }

            blobAssetReference = blobBuilder.CreateBlobAssetReference<BlobArray<PersistencyArchetypeDataLayout.TypeInfo>>(Allocator.Persistent);
        }
        
        return blobAssetReference;
    }
    
    [Pure]
    public static PersistencyArchetype ToPersistencyArchetype(this ref PersistencyArchetypeDataLayout persistencyArchetypeDataLayout)
    {
        FixedList128<PersistableTypeHandle> list = new FixedList128<PersistableTypeHandle>();
        for (int i = 0; i < persistencyArchetypeDataLayout.PersistedTypeInfoArrayRef.Value.Length; i++)
        {
            list.Add(persistencyArchetypeDataLayout.PersistedTypeInfoArrayRef.Value[i].PersistableTypeHandle);
        }

        return new PersistencyArchetype { PersistableTypeHandles = list };
    }

    [Conditional("DEBUG")]
    private static void ValidateType(TypeManager.TypeInfo typeInfo)
    {
        if (!PersistencySettings.IsSupported(typeInfo, out string notSupportedReason))
        {
            throw new NotSupportedException(notSupportedReason);
        }
    }
}