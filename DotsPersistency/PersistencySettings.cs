// Author: Jonas De Maeseneer

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Debug = UnityEngine.Debug;

[assembly:InternalsVisibleTo("io.jonasdem.quicksave.editor")]
[assembly:InternalsVisibleTo("io.jonasdem.dotspersistency.tests")]

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
        [SerializeField]
        internal bool VerboseConversionLog = false;
        
        private TypeIndexLookup _typeIndexLookup;
        private NativeHashMap<int, PersistableTypeHandle> _typeIndexToHandle;
        private bool _initialized;
        
        private void Initialize()
        {
            var typeIndexArray = new NativeArray<int>(_allPersistableTypeInfos.Count, Allocator.Persistent);
            _typeIndexToHandle = new NativeHashMap<int, PersistableTypeHandle>(_allPersistableTypeInfos.Count, Allocator.Persistent);
            for (int i = 0; i < _allPersistableTypeInfos.Count; i++)
            {
                PersistableTypeInfo typeInfo = _allPersistableTypeInfos[i];
                typeInfo.ValidityCheck();
                int typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(typeInfo.StableTypeHash);
                typeIndexArray[i] = typeIndex;
                _typeIndexToHandle[typeIndex] = new PersistableTypeHandle {Handle = (ushort)(i+1)};
            }
            _typeIndexLookup = new TypeIndexLookup()
            {
                TypeIndexArray = typeIndexArray
            };
            _initialized = true;
        }
        
        public PersistableTypeHandle GetTypeHandleFromTypeIndex(int typeIndex)
        {
            return _typeIndexToHandle[typeIndex];
        }

        // fastest way to get the type index
        public int GetTypeIndex(PersistableTypeHandle typeHandle)
        {
            Debug.Assert(typeHandle.IsValid, "Expected a valid type handle");
            return _typeIndexLookup.GetTypeIndex(typeHandle);
        }
        
        public bool IsBufferType(PersistableTypeHandle typeHandle)
        {
            Debug.Assert(typeHandle.IsValid, "Expected a valid type handle");
            return _allPersistableTypeInfos[typeHandle.Handle - 1].IsBuffer;
        }
        
        public int GetMaxElements(PersistableTypeHandle typeHandle)
        {
            Debug.Assert(typeHandle.IsValid, "Expected a valid type handle");
            return _allPersistableTypeInfos[typeHandle.Handle - 1].MaxElements;
        }
        
        public PersistableTypeHandle GetPersistableTypeHandleFromFullTypeName(string fullTypeName)
        {
            for (int i = 0; i < _allPersistableTypeInfos.Count; i++)
            {
                if (_allPersistableTypeInfos[i].FullTypeName == fullTypeName)
                {
                    Debug.Assert(i < ushort.MaxValue);
                    return new PersistableTypeHandle {Handle = (ushort)(i+1)};
                }
            }
            
            throw new ArgumentException($"{fullTypeName} was not registered as a persistable type.");
        }
        
        internal Unity.Entities.Hash128 GetTypeHandleCombinationHash(NativeArray<PersistableTypeHandle> typeHandles)
        {
            ulong hash1 = 0;
            ulong hash2 = 0;

            for (int i = 0; i < typeHandles.Length; i++)
            {
                PersistableTypeHandle handle = typeHandles[i];
                
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
            return new UnityEngine.Hash128(hash1, hash2);
        }
        
        internal Unity.Entities.Hash128 GetTypeHandleCombinationHash(List<string> fullTypeNames)
        {
            var typeHandles = new NativeList<PersistableTypeHandle>(fullTypeNames.Count, Allocator.Temp);
            
            foreach (var fullTypeName in fullTypeNames)
            {
                if (ContainsType(fullTypeName))
                {
                    typeHandles.Add(GetPersistableTypeHandleFromFullTypeName(fullTypeName));
                }
            }

            var hash = GetTypeHandleCombinationHash(typeHandles);
            typeHandles.Dispose();
            return hash;
        }
        
        public bool ContainsType(string fullTypeName)
        {
            return _allPersistableTypeInfos.Any(info => info.FullTypeName == fullTypeName);
        }

        public bool UseGroupedJobs()
        {
#if UNITY_EDITOR
            return ForceUseGroupedJobsInEditor;
#elif ENABLE_UNITY_COLLECTIONS_CHECKS
            return false;
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
            
            if (info.EntityOffsetCount > 0)
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
#if UNITY_EDITOR
            PersistencySettings settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PersistencySettings>(AssetPath);
#else
            // This only actually loads it the first time, so multiple calls are totally fine
            PersistencySettings settings = Resources.Load<PersistencySettings>(RelativeFilePathNoExt);
#endif
            return settings;
        }
        
        internal void OnEnable()
        {
            if (!_initialized)
            {
                Initialize();
            }
        }

        internal void OnDisable()
        {
            if (_initialized)
            {
                _typeIndexLookup.Dispose();
                _typeIndexToHandle.Dispose();
                _initialized = false;
            }
        }

        // The lookup just indexes into a native array, so almost instant
        public struct TypeIndexLookup
        {
            [ReadOnly] internal NativeArray<int> TypeIndexArray;

            public int GetTypeIndex(PersistableTypeHandle typeHandle)
            {
                Debug.Assert(typeHandle.IsValid); // "Expected a valid type handle"
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
