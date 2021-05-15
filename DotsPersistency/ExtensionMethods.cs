// Author: Jonas De Maeseneer

using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace DotsPersistency
{
    public static class ExtensionMethods
    {
        [Pure]
        public static unsafe BlobAssetReference<BlobArray<PersistencyArchetypeDataLayout.TypeInfo>> BuildTypeInfoBlobAsset(
            this ref PersistencyArchetype persistencyArchetype, PersistencySettings persistencySettings, int amountEntities, out int sizePerEntity)
        {
            NativeArray<PersistableTypeHandle> blobArrayAsNative =
                NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<PersistableTypeHandle>(persistencyArchetype.PersistableTypeHandles.GetUnsafePtr(),
                    persistencyArchetype.PersistableTypeHandles.Length, Allocator.None);
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref blobArrayAsNative, safety);
#endif

            var blobAssetReference = BuildTypeInfoBlobAsset(blobArrayAsNative, persistencySettings, amountEntities, out sizePerEntity);
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(safety);
#endif
            
            return blobAssetReference;
        }

        public static BlobAssetReference<BlobArray<PersistencyArchetypeDataLayout.TypeInfo>> BuildTypeInfoBlobAsset(
            NativeArray<PersistableTypeHandle> persistableTypeHandles, PersistencySettings persistencySettings, int amountEntities, out int sizePerEntity)
        {
            BlobAssetReference<BlobArray<PersistencyArchetypeDataLayout.TypeInfo>> blobAssetReference;
            int currentOffset = 0;
            sizePerEntity = 0;

            using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref BlobArray<PersistencyArchetypeDataLayout.TypeInfo> blobArray =
                    ref blobBuilder.ConstructRoot<BlobArray<PersistencyArchetypeDataLayout.TypeInfo>>();

                BlobBuilderArray<PersistencyArchetypeDataLayout.TypeInfo> blobBuilderArray = blobBuilder.Allocate(ref blobArray, persistableTypeHandles.Length);

                for (int i = 0; i < blobBuilderArray.Length; i++)
                {
                    PersistableTypeHandle persistableTypeHandle = persistableTypeHandles[i];
                    TypeManager.TypeInfo typeInfo = TypeManager.GetTypeInfo(persistencySettings.GetTypeIndex(persistableTypeHandle));
                    int maxElements = persistencySettings.GetMaxElements(persistableTypeHandle);
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

        [Conditional("DEBUG")]
        private static void ValidateType(TypeManager.TypeInfo typeInfo)
        {
            if (!PersistencySettings.IsSupported(typeInfo, out string notSupportedReason))
            {
                throw new NotSupportedException(notSupportedReason);
            }
        }

        public static Entity InstantiatePersistent(ref this EntityManager entityManager, Entity prefabEntity, ref int amountExistingPoolEntities)
        {
            Entity newEntity = entityManager.Instantiate(prefabEntity);
            entityManager.SetComponentData(newEntity, new PersistenceState {ArrayIndex = amountExistingPoolEntities});
            amountExistingPoolEntities += 1;
            return newEntity;
        }

        public static NativeArray<Entity> InstantiatePersistent(ref this EntityManager entityManager, Entity prefabEntity, ref int amountExistingPoolEntities,
            int amount, Allocator allocatorForEntityArray = Allocator.Temp)
        {
            NativeArray<Entity> newEntities = entityManager.Instantiate(prefabEntity, amount, allocatorForEntityArray);

            for (int i = 0; i < newEntities.Length; i++)
            {
                entityManager.SetComponentData(newEntities[i], new PersistenceState {ArrayIndex = amountExistingPoolEntities + i});
            }

            amountExistingPoolEntities += amount;
            return newEntities;
        }
        
        public static Entity InstantiatePersistent(ref this EntityCommandBuffer cmdBuffer, Entity prefabEntity, ref int amountExistingPoolEntities)
        {
            Entity newEntity = cmdBuffer.Instantiate(prefabEntity);
            cmdBuffer.SetComponent(newEntity, new PersistenceState {ArrayIndex = amountExistingPoolEntities});
            amountExistingPoolEntities += 1;
            return newEntity;
        }

        public static void InstantiatePersistent(ref this EntityCommandBuffer cmdBuffer, Entity prefabEntity, ref int amountExistingPoolEntities, int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                Entity newEntity = cmdBuffer.Instantiate(prefabEntity);
                cmdBuffer.SetComponent(newEntity, new PersistenceState {ArrayIndex = amountExistingPoolEntities + i});
            }

            amountExistingPoolEntities += amount;
        }

        public static void InstantiatePersistent(ref this EntityCommandBuffer.ParallelWriter cmdBuffer, int sortKey, Entity prefabEntity,
            int startIndex, int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                Entity newEntity = cmdBuffer.Instantiate(sortKey, prefabEntity);
                cmdBuffer.SetComponent(sortKey, newEntity, new PersistenceState {ArrayIndex = startIndex + i});
            }
        }
    }
}