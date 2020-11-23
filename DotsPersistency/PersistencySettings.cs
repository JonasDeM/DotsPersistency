using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Debug = UnityEngine.Debug;

[assembly:InternalsVisibleTo("io.jonasdem.dotspersistency.editor")]

namespace DotsPersistency
{
    public partial class PersistencySettings : ScriptableObject
    {
        private const string ResourceFolder = "Assets/DotsPersistency/Resources";
        private const string Folder = ResourceFolder + "/DotsPersistency";
        private const string RelativeFilePathNoExt = "DotsPersistency/PersistencySettings";
        private const string RelativeFilePath = RelativeFilePathNoExt + ".asset";
        private const string AssetPath = ResourceFolder + "/" + RelativeFilePath;
        internal const string NotFoundMessage = "PersistencySettings.asset was not found, attach the PersistencyAuthoring script to a gameobject & press the 'create settings file' button.";
        
        [SerializeField]
        private List<PersistableTypeInfo> _allPersistableTypeInfos = new List<PersistableTypeInfo>();
        internal List<PersistableTypeInfo> AllPersistableTypeInfos => _allPersistableTypeInfos;

        [SerializeField]
        internal bool ForceUseNonGroupedJobsInBuild = false;
        [SerializeField]
        internal bool ForceUseGroupedJobsInEditor = false;
        private TypeIndexLookup _typeIndexLookup;
        private bool _initialized;
        
        private void Initialize()
        {
            var typeIndexArray = new NativeArray<int>(_allPersistableTypeInfos.Count, Allocator.Persistent);
            for (int i = 0; i < _allPersistableTypeInfos.Count; i++)
            {
                PersistableTypeInfo typeInfo = _allPersistableTypeInfos[i];
                typeInfo.ValidityCheck();
                typeIndexArray[i] = TypeManager.GetTypeIndexFromStableTypeHash(typeInfo.StableTypeHash);
            }
            _typeIndexLookup = new TypeIndexLookup()
            {
                TypeIndexArray = typeIndexArray
            };
            _initialized = true;
        }

        internal void Dispose()
        {
            _typeIndexLookup.Dispose();
            _initialized = false;
        }

        // fastest way to get the type index
        public int GetTypeIndex(PersistableTypeHandle typeHandle)
        {
            Debug.Assert(typeHandle.IsValid, "Expected a valid type handle");
            return _typeIndexLookup.GetTypeIndex(typeHandle);
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

        public bool UseGroupedJobs()
        {
#if UNITY_EDITOR
            return ForceUseGroupedJobsInEditor;
#else
            return !ForceUseNonGroupedJobsInBuild;
#endif
        }

        public TypeIndexLookup GetTypeIndexLookup()
        {
            return _typeIndexLookup;
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
        
        public static PersistencySettings Get()
        {
            // This only actually loads it the first time, so multiple calls are totally fine
            PersistencySettings settings = Resources.Load<PersistencySettings>(RelativeFilePathNoExt);
#if UNITY_EDITOR
            // This covers an edge case with the conversion background process in editor
            if (settings == null)
            {
                settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PersistencySettings>(AssetPath);
            }
#endif
            if (settings != null && !settings._initialized)
            {
                settings.Initialize();
            }
            return settings;
        }
        
        // The lookup just indexes into a native array, so almost instant
        public struct TypeIndexLookup
        {
            [ReadOnly] internal NativeArray<int> TypeIndexArray;

            public int GetTypeIndex(PersistableTypeHandle typeHandle)
            {
                Debug.Assert(typeHandle.IsValid, "Expected a valid type handle");
                return TypeIndexArray[typeHandle.Handle - 1];
            }

            internal void Dispose()
            {
                TypeIndexArray.Dispose();
            }

            public bool IsCreated => TypeIndexArray.IsCreated;
        }
    }
}
