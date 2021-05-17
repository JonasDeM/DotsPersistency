// Author: Jonas De Maeseneer

using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace DotsPersistency
{
    public partial class PersistencySettings
    {
#if UNITY_EDITOR
        public void RefreshInEditor()
        {
            OnDisable();
            OnEnable();
        }
        
        public static void CreateInEditor()
        {
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PersistencySettings>(AssetPath);
            if (settings == null)
            {
                System.IO.Directory.CreateDirectory(Folder);

                settings = CreateInstance<PersistencySettings>();
                settings.AddPersistableTypeInEditor(typeof(Translation).FullName);
                settings.AddPersistableTypeInEditor(typeof(Rotation).FullName);
                settings.AddPersistableTypeInEditor(typeof(Disabled).FullName);
                UnityEditor.AssetDatabase.CreateAsset(settings, AssetPath);
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
            RefreshInEditor();
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
            RefreshInEditor();
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