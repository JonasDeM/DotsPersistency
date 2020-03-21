using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using JetBrains.Annotations;
using Unity.Entities;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace DotsPersistency.Editor
{
    public class PersistableTypesInfo : ScriptableObject
    {
        private const string FOLDER = "Assets/DotsPersistency/Editor";
        private const string FILENAME = "UserDefinedPersistableTypes.asset";
        private static PersistableTypesInfo _instance;
        public List<string> FullTypeNames = new List<string>()
        {
            "Unity.Transforms.Translation",
            "Unity.Transforms.Rotation"
        };
        
        public List<AssemblyDefinitionAsset> Assemblies = new List<AssemblyDefinitionAsset>();
        
        public static PersistableTypesInfo GetInstance()
        {
            if (_instance == null)
            {            
                _instance = AssetDatabase.LoadAssetAtPath<PersistableTypesInfo>(FOLDER + "/" + FILENAME);
                if (_instance == null)
                {
                    Directory.CreateDirectory(FOLDER);
                    _instance = CreateInstance<PersistableTypesInfo>();
                    AssetDatabase.CreateAsset(_instance, FOLDER + "/" + FILENAME);
                }
            }
            return _instance;
        }

        public static RuntimePersistableTypesInfo GetOrCreateRuntimeVersion()
        {
            const string assetPath = RuntimePersistableTypesInfo.RESOURCE_FOLDER + "/" + RuntimePersistableTypesInfo.RELATIVE_FILE_PATH;
            
            var runtimeVersion = AssetDatabase.LoadAssetAtPath<RuntimePersistableTypesInfo>(assetPath);
            if (runtimeVersion == null)
            {
                runtimeVersion = ScriptableObject.CreateInstance<RuntimePersistableTypesInfo>();
                Directory.CreateDirectory(RuntimePersistableTypesInfo.FOLDER);
                AssetDatabase.CreateAsset(runtimeVersion, assetPath);
            }

            return runtimeVersion;
        }
        
        [Serializable]
        private struct AssemblyName
        {
#pragma warning disable 649
            [UsedImplicitly]
            public string name;
#pragma warning restore 649
        }
        public static void UpdateRuntimeVersion()
        {
            var instance = PersistableTypesInfo.GetInstance();
            var runtimeVersion = GetOrCreateRuntimeVersion();
            
            foreach (var fullTypeName in instance.FullTypeNames)
            {
                Type type = null;
                foreach (var assemblyDefAsset in instance.Assemblies)
                {
                    try
                    {
                        var assembly = Assembly.Load(JsonUtility.FromJson<AssemblyName>(assemblyDefAsset.text).name);
                        type = assembly.GetType(fullTypeName);
                        if (type != null)
                        {
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(e);
                    }
                }
                if (type == null)
                {
                    Debug.LogWarning($"PersistableTypesInfo contains \"{fullTypeName}\", but type could not be found");
                }
                else
                {
                    runtimeVersion.StableTypeHashes.Add(TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(type)).StableTypeHash);
                }
            }
            
            EditorUtility.SetDirty(runtimeVersion);
            AssetDatabase.SaveAssets();
        }
    }

    [CustomEditor(typeof(PersistableTypesInfo))]
    public class PersistableTypesInfoInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            
            // todo disable if it would have no effect
            if (GUILayout.Button("Update Runtime Version"))
            {
                PersistableTypesInfo.UpdateRuntimeVersion();
            }
        }
    }
    
}
