using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

[assembly:InternalsVisibleTo("io.jonasdem.dotspersistency.editor")]

namespace DotsPersistency
{
    [Serializable]
    public struct PersistableTypeHandle
    {
        [SerializeField]
        internal ushort Handle;

        public bool IsValid => Handle > 0;
    }
    
    [Serializable]
    internal struct PersistableTypeInfo
    {
        // If any of these values change on the type itself the validity check will pick that up & the user needs to force update
        public string FullTypeName;
        public ulong StableTypeHash;
        public bool IsBuffer;
        public int MaxElements;

        // Non-serialized fields gotten from TypeManager on deserialize
        // These values can easily change without our knowledge, so that's why we don't serialize them
        [NonSerialized]
        public int TypeManagerTypeIndex;

        [Conditional("DEBUG")]
        public void ValidityCheck()
        {
            if (MaxElements < 1 || string.IsNullOrEmpty(FullTypeName))
            {
                throw new DataException("Invalid PersistableTypeInfo in the RuntimePersistableTypesInfo asset! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
            }

            int typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(StableTypeHash);
            
            if (typeIndex == -1)
            {
                throw new DataException($"{FullTypeName} has an invalid StableTypeHash in PersistableTypeInfo in the RuntimePersistableTypesInfo asset! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
            }
            
            if (TypeManager.IsBuffer(typeIndex) != IsBuffer)
            {
                throw new DataException($"{FullTypeName} is set as a buffer in the RuntimePersistableTypesInfo asset, but is not actually a buffer type! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
            }

            if (!IsBuffer && MaxElements > 1)
            {
                throw new DataException($"{FullTypeName} has {MaxElements.ToString()} as MaxElements the RuntimePersistableTypesInfo asset, but it is not a buffer type! Try force updating RuntimePersistableTypesInfo (search the asset & press the force update button & look at console)");
            }
        }
    }
    
    public class PersistencySettings : ScriptableObject
    {
        private const string ResourceFolder = "Assets/DotsPersistency/Resources";
        private const string Folder = ResourceFolder + "/DotsPersistency";
        private const string RelativeFilePathNoExt = "DotsPersistency/PersistencySettings";
        private const string RelativeFilePath = RelativeFilePathNoExt + ".asset";
        private const string AssetPath = ResourceFolder + "/" + RelativeFilePath;
        private const string MenuItem = "DotsPersistency/CreatePersistencySettings";
        internal const string NotFoundMessage = "PersistencySettings.asset was not found, attach the PersistencyAuthoring script to a gameobject & press the create settings file.";
        
        [SerializeField]
        private List<PersistableTypeInfo> _allPersistableTypeInfos = new List<PersistableTypeInfo>();
        internal List<PersistableTypeInfo> AllPersistableTypeInfos => _allPersistableTypeInfos;

        private bool _isInitialized = false;
        
        private void Initialize()
        {
            for (int i = 0; i < _allPersistableTypeInfos.Count; i++)
            {
                var infoToUpdate = _allPersistableTypeInfos[i];
                infoToUpdate.TypeManagerTypeIndex = TypeManager.GetTypeIndexFromStableTypeHash(infoToUpdate.StableTypeHash);
                infoToUpdate.ValidityCheck();
                _allPersistableTypeInfos[i] = infoToUpdate;
            }
        }
        
        public static PersistencySettings Get()
        {
            // This only actually loads it the first time, so multiple calls are totally fine
            PersistencySettings settings = Resources.Load<PersistencySettings>(RelativeFilePathNoExt);
#if UNITY_EDITOR
            // This covers an edge case with the conversion background process
            if (settings == null)
            {
                settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PersistencySettings>(AssetPath);
            }
#endif
            if (settings != null && !settings._isInitialized)
            {
                settings.Initialize();
            }
            return settings;
        }
        
        // fastest way to get the type index
        public int GetTypeIndex(PersistableTypeHandle typeHandle)
        {
            Debug.Assert(typeHandle.IsValid, "Expected a valid type handle");
            return _allPersistableTypeInfos[typeHandle.Handle - 1].TypeManagerTypeIndex;
        }
        
        public int GetMaxElements(PersistableTypeHandle typeHandle)
        {
            Debug.Assert(typeHandle.IsValid, "Expected a valid type handle");
            return _allPersistableTypeInfos[typeHandle.Handle - 1].MaxElements;
        }
        
        // Should only be used in conversion
        public bool GetPersistableTypeHandleFromFullTypeName(string fullTypeName, out PersistableTypeHandle persistableTypeHandle)
        {
            for (int i = 0; i < _allPersistableTypeInfos.Count; i++)
            {
                if (_allPersistableTypeInfos[i].FullTypeName == fullTypeName)
                {
                    Debug.Assert(i < ushort.MaxValue);
                    persistableTypeHandle = new PersistableTypeHandle {Handle = (ushort)(i+1)};
                    return true;
                }
            }

            persistableTypeHandle = default;
            return false;
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
        [UnityEditor.Callbacks.DidReloadScripts]
        public static void CreateInEditor()
        {
            try
            {
                var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PersistencySettings>(AssetPath);
                if (settings == null)
                {
                    settings = CreateInstance<PersistencySettings>();
                    System.IO.Directory.CreateDirectory(Folder);
                    UnityEditor.AssetDatabase.CreateAsset(settings, AssetPath);
                }
            }
            catch (Exception e)
            {
                Debug.Log($"Something went wrong while auto-creating the PersistencySettings asset. trace:\n{e}");
                throw;
            }
        }

        public bool AddPersistableTypeInEditor(string fullTypeName, int maxElements = -1)
        {
            if (_allPersistableTypeInfos.Any(info => info.FullTypeName == fullTypeName))
            {
                Debug.Log($"Failed to add {fullTypeName} is already in the list!");
                return false;
            }
            
            bool creationSuccess = CreatePersistableTypeInfoFromFullTypeName(fullTypeName, out PersistableTypeInfo persistableTypeInfo);
            if (!creationSuccess)
            {
                Debug.Log($"Removed {fullTypeName}, type doesn't exist in the ECS TypeManager");
                return false;
            }

            if (maxElements > 0 && persistableTypeInfo.IsBuffer)
            {
                persistableTypeInfo.MaxElements = maxElements;
            }

            _allPersistableTypeInfos.Add(persistableTypeInfo);
            _allPersistableTypeInfos.Sort((info1, info2) => string.CompareOrdinal(info1.FullTypeName, info2.FullTypeName));
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }
        
        private static bool CreatePersistableTypeInfoFromFullTypeName(string fullTypeName, out PersistableTypeInfo persistableTypeInfo)
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

        public void ClearPersistableTypesInEditor()
        {
            _allPersistableTypeInfos.Clear();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        public string GetPrettyNameInEditor(string fullTypeName)
        {
            for (int i = 0; i < _allPersistableTypeInfos.Count; i++)
            {
                var typeInfo = _allPersistableTypeInfos[i];
                if (_allPersistableTypeInfos[i].FullTypeName == fullTypeName)
                {
                    string prettyName = fullTypeName.Substring(math.clamp(fullTypeName.LastIndexOf('.') + 1, 0, fullTypeName.Length)) 
                                        + (typeInfo.IsBuffer ? " [B]" : "");
                    return prettyName;
                }
            }

            return "Invalid";
        }
#endif // UNITY_EDITOR
    }
}
