using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[assembly:InternalsVisibleTo("io.jonasdem.dotspersistency.editor")]

namespace DotsPersistency
{
    [Serializable]
    public struct PersistableTypeInfo
    {
        public string FullTypeName;
        public ulong StableTypeHash;
        public bool IsBuffer;
        public int MaxElements;

        public bool IsValid => MaxElements > 0 && StableTypeHash != 0 && !string.IsNullOrEmpty(FullTypeName);
    }
    
    public class RuntimePersistableTypesInfo : ScriptableObject
    {
        public const string RESOURCE_FOLDER = "Assets/DotsPersistency/Resources";
        public const string FOLDER = RESOURCE_FOLDER + "/DotsPersistency";
        public const string RELATIVE_FILE_PATH_NO_EXT = "DotsPersistency/UserDefinedRuntimePersistableTypes";
        public const string RELATIVE_FILE_PATH = RELATIVE_FILE_PATH_NO_EXT+".asset";        
        
        [SerializeField]
        private List<PersistableTypeInfo> _allPersistableTypeInfos = new List<PersistableTypeInfo>();
        public List<PersistableTypeInfo> AllPersistableTypeInfos => _allPersistableTypeInfos;

        public ulong GetStableTypeHashFromFullTypeName(string fullTypeName)
        {
            foreach (PersistableTypeInfo persistableTypeInfo in AllPersistableTypeInfos)
            {
                if (persistableTypeInfo.FullTypeName == fullTypeName)
                {
                    return persistableTypeInfo.StableTypeHash;
                }
            }
            return 0;
        }
        
        public static RuntimePersistableTypesInfo Load()
        {
            // This only actually loads it the first time, so multiple calls are totally fine
            return Resources.Load<RuntimePersistableTypesInfo>(RELATIVE_FILE_PATH_NO_EXT);
        }
        
        public static ResourceRequest LoadAsync()
        {
            // This only actually loads it the first time, so multiple calls are totally fine
            return Resources.LoadAsync<RuntimePersistableTypesInfo>(RELATIVE_FILE_PATH_NO_EXT);
        }

        public static bool IsSupported(TypeManager.TypeInfo info, out string notSupportedReason)
        {
            if (info.Category != TypeManager.TypeCategory.BufferData && info.Category != TypeManager.TypeCategory.ComponentData)
            {
                notSupportedReason = $"Persisting components need to be either ComponentData or BufferElementData. Type: {ComponentType.FromTypeIndex(info.TypeIndex).ToString()}";
                return false;
            }
            
            if (info.HasEntities)
            {
                notSupportedReason = $"Persisting components with Entity References is not supported. Type: {ComponentType.FromTypeIndex(info.TypeIndex).ToString()}";
                return false;
            }

            if (info.BlobAssetRefOffsetCount > 0)
            {
                notSupportedReason = $"Persisting components with BlobAssetReferences is not supported. Type: {ComponentType.FromTypeIndex(info.TypeIndex).ToString()}";
                return false;
            }

            if (TypeManager.IsManagedComponent(info.TypeIndex))
            {
                notSupportedReason = $"Persisting managed components supported. Type: {ComponentType.FromTypeIndex(info.TypeIndex).ToString()}";
                return false;
            }

            notSupportedReason = "";
            return true;
        }
        
#if UNITY_EDITOR
        public static RuntimePersistableTypesInfo GetOrCreateInEditor()
        {
            const string assetPath = RESOURCE_FOLDER + "/" + RELATIVE_FILE_PATH;
            
            var runtimeVersion = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimePersistableTypesInfo>(assetPath);
            if (runtimeVersion == null)
            {
                runtimeVersion = CreateInstance<RuntimePersistableTypesInfo>();
                System.IO.Directory.CreateDirectory(FOLDER);
                UnityEditor.AssetDatabase.CreateAsset(runtimeVersion, assetPath);
            }

            return runtimeVersion;
        }
        
        public static bool CreatePersistableTypeInfoFromFullTypeName(string fullTypeName, out PersistableTypeInfo persistableTypeInfo)
        {
            foreach (var typeInfoEntry in TypeManager.AllTypes)
            {
                if (typeInfoEntry.Type != null && typeInfoEntry.Type.FullName == fullTypeName && IsSupported(typeInfoEntry, out _))
                {
                    bool isBuffer = typeInfoEntry.Category == TypeManager.TypeCategory.BufferData;
                    persistableTypeInfo = new PersistableTypeInfo()
                    {
                        FullTypeName = fullTypeName,
                        StableTypeHash = typeInfoEntry.StableTypeHash,
                        IsBuffer = isBuffer,
                        MaxElements = math.max(1, isBuffer ? typeInfoEntry.BufferCapacity : 1) // Initial BufferCapacity seems like a decent default max
                    };
                    return true;
                }
            }

            persistableTypeInfo = default;
            return false;
        }
#endif
    }
}
