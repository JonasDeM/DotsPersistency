using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotsPersistency
{
    public class RuntimePersistableTypesInfo : ScriptableObject
    {
        public const string RESOURCE_FOLDER = "Assets/DotsPersistency/Resources";
        public const string FOLDER = RESOURCE_FOLDER + "/DotsPersistency";
        public const string RELATIVE_FILE_PATH_NO_EXT = "DotsPersistency/UserDefinedRuntimePersistableTypes";
        public const string RELATIVE_FILE_PATH = RELATIVE_FILE_PATH_NO_EXT+".asset";        
        
        [SerializeField]
        private List<ulong> _stableTypeHashes = new List<ulong>();
        public List<ulong> StableTypeHashes => _stableTypeHashes;
        
        public static RuntimePersistableTypesInfo Load()
        {
            return Resources.Load<RuntimePersistableTypesInfo>(RELATIVE_FILE_PATH_NO_EXT);
        }
        
        public static ResourceRequest LoadAsync()
        {
            return Resources.LoadAsync<RuntimePersistableTypesInfo>(RELATIVE_FILE_PATH_NO_EXT);
        }
    }
}
