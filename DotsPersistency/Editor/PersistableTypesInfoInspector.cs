using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.WSA;

namespace DotsPersistency.Editor
{
    [CustomEditor(typeof(RuntimePersistableTypesInfo))]
    public class PersistableTypesInfoInspector : UnityEditor.Editor
    {
        private List<string> _allAvailableTypes;
        private ReorderableList _reorderableList;
        private RuntimePersistableTypesInfo _persistableTypesInfo;
        
        private void OnEnable()
        {
            _persistableTypesInfo = (RuntimePersistableTypesInfo) target;
            _reorderableList = new ReorderableList(_persistableTypesInfo.AllPersistableTypeInfos, typeof(PersistableTypeInfo));
            _reorderableList.drawHeaderCallback = DrawHeaderCallback;
            _reorderableList.draggable = false;
            _reorderableList.drawElementCallback = DrawElementCallback;
            _reorderableList.elementHeight = EditorGUIUtility.singleLineHeight + 2;
            _reorderableList.onAddCallback = OnAddCallback;
            _reorderableList.onRemoveCallback = OnRemoveCallback;
        }

        private void OnRemoveCallback(ReorderableList list)
        {
            list.list.RemoveAt(list.index);
            EditorUtility.SetDirty(_persistableTypesInfo);
        }

        private void OnAddCallback(ReorderableList list)
        {
            FindPersistableEcsType window = EditorWindow.GetWindow<FindPersistableEcsType>();
            window.OnTypeChosen = persistableTypeInfo =>
            {
                int existingIndex = _persistableTypesInfo.AllPersistableTypeInfos.FindIndex(info => info.FullTypeName == persistableTypeInfo.FullTypeName);
                if (existingIndex != -1)
                {
                    Debug.Log($"{persistableTypeInfo.FullTypeName} is already in the list!");
                    _reorderableList.index = existingIndex;
                    return;
                }
                _persistableTypesInfo.AllPersistableTypeInfos.Add(persistableTypeInfo);
                _persistableTypesInfo.AllPersistableTypeInfos.Sort((info1, info2) => string.CompareOrdinal(info1.FullTypeName, info2.FullTypeName));
                
                EditorUtility.SetDirty(_persistableTypesInfo);
                // trigger the list to update
                _reorderableList.list = _persistableTypesInfo.AllPersistableTypeInfos;
                Repaint();
            };
        }

        public override void OnInspectorGUI()
        {
            _reorderableList.DoLayoutList();

            if (GUILayout.Button("Force Update Data"))
            {
                for (int i = _persistableTypesInfo.AllPersistableTypeInfos.Count - 1; i >= 0; i--)
                {
                    var oldInfo = _persistableTypesInfo.AllPersistableTypeInfos[i];
                    int maxElements = oldInfo.MaxElements; // this can be chosen by the user, so keep this info
                    if (RuntimePersistableTypesInfo.CreatePersistableTypeInfoFromFullTypeName(oldInfo.FullTypeName, out PersistableTypeInfo updatedInfo))
                    {
                        updatedInfo.MaxElements = updatedInfo.IsBuffer ? maxElements : 1;
                        _persistableTypesInfo.AllPersistableTypeInfos[i] = updatedInfo;
                    }
                    else
                    {
                        Debug.LogWarning($"'Force Update Data' removed {oldInfo.FullTypeName} since the actual type doesn't exist anymore in the ECS TypeManager.");
                        _persistableTypesInfo.AllPersistableTypeInfos.RemoveAt(i);
                    }
                }
                _persistableTypesInfo.AllPersistableTypeInfos.Sort((info1, info2) => string.CompareOrdinal(info1.FullTypeName, info2.FullTypeName));
                EditorUtility.SetDirty(_persistableTypesInfo);
            }
            
            if (EditorUtility.IsDirty(_persistableTypesInfo))
            {
                EditorGUILayout.HelpBox("Save for the changes to go into effect! (Ctrl+S)", MessageType.Info);
            }
        }

        private void DrawElementCallback(Rect rect, int index, bool isactive, bool isfocused)
        {
            var info = _persistableTypesInfo.AllPersistableTypeInfos[index];
            int maxElementBoxWidth = math.max(50, 10 * info.MaxElements.ToString().Length);
            string elementName = info.FullTypeName + (info.IsBuffer ? " [B]" : "");
            rect.xMax -= maxElementBoxWidth + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.LabelField(rect, elementName);

            if (info.IsBuffer)
            {
                rect.xMax += maxElementBoxWidth + EditorGUIUtility.standardVerticalSpacing;
                rect.xMin = rect.xMax - (maxElementBoxWidth + EditorGUIUtility.standardVerticalSpacing);
                rect.height -= 1;
                
                EditorGUI.BeginChangeCheck();
                info.MaxElements = math.clamp(EditorGUI.IntField(rect, info.MaxElements), 1, PersistenceMetaData.MaxValueForAmount);
                if (EditorGUI.EndChangeCheck())
                {
                    _persistableTypesInfo.AllPersistableTypeInfos[index] = info;
                    EditorUtility.SetDirty(_persistableTypesInfo);
                }
            }
        }

        private void DrawHeaderCallback(Rect rect)
        {
            EditorGUI.LabelField(rect, "Persistable Types");
        }
    }
    
    internal class FindPersistableEcsType : EditorWindow
    {
        private List<Type> _allAvailableTypes;
        internal delegate void TypeSelectedDelegate(PersistableTypeInfo persistableTypeInfo);
        internal TypeSelectedDelegate OnTypeChosen = persistableTypeInfo => { };

        [SerializeField]
        private Vector2 _scrollPos;
        [SerializeField]
        private string _currentFilter = "";
        [SerializeField]
        private bool _showUnityTypes = false;
        [SerializeField]
        private bool _showTestTypes = false;
        
        private void OnEnable()
        {
            UpdateTypeList();
            titleContent = new GUIContent("Choose a type");
        }

        private void OnGUI()
        {
            _currentFilter = EditorGUILayout.TextField("Filter: ", _currentFilter);
            EditorGUI.BeginChangeCheck();
            _showUnityTypes = EditorGUILayout.Toggle("Show Unity Types", _showUnityTypes);
            _showTestTypes = EditorGUILayout.Toggle("Show Test Types", _showTestTypes);
            if (EditorGUI.EndChangeCheck())
            {
                UpdateTypeList();
            }
            List<string> filterValues = _currentFilter.Split(' ').ToList();
            
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            bool allHidden = true;
            foreach (Type ecsType in _allAvailableTypes)
            {
                string fullTypeName = ecsType.FullName;
                bool hide = filterValues.Any(filterValue => fullTypeName.IndexOf(filterValue, StringComparison.InvariantCultureIgnoreCase) == -1);
                
                var oldAlignment =  GUI.skin.button.alignment;
                GUI.skin.button.alignment = TextAnchor.MiddleLeft;
                if (!hide && GUILayout.Button(fullTypeName))
                {
                    if (RuntimePersistableTypesInfo.CreatePersistableTypeInfoFromFullTypeName(fullTypeName, out PersistableTypeInfo newInfo))
                    {
                        OnTypeChosen(newInfo);
                        Close();
                    }
                    else
                    {
                        Debug.LogError($"Something went wrong while trying to add {fullTypeName}.");
                    }
                }
                GUI.skin.button.alignment = oldAlignment;
                allHidden &= hide;
            }
            if (allHidden)
            {
                EditorGUILayout.HelpBox("No types found. Try changing the options above.", MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }

        private void UpdateTypeList()
        {
            _allAvailableTypes = TypeManager.GetAllTypes()
                .Where(info => info.Type != null && RuntimePersistableTypesInfo.IsSupported(info, out _))
                .Select(info => info.Type)
                .Where(type => type.Namespace == null || type.Namespace != typeof(PersistenceState).Namespace)
                .Where(type => (_showUnityTypes || !IsUnityType(type)) && (_showTestTypes || !IsTestType(type)))
                .ToList();
            _allAvailableTypes.Sort((type, type1) => string.CompareOrdinal(type.FullName, type1.FullName));
        }

        private static bool IsUnityType(Type type)
        {
            return type.Namespace != null && type.Namespace.Contains("Unity");
        }
        
        private static bool IsTestType(Type type)
        {
            return type.Namespace != null && type.Namespace.Contains("Test");
        }
    }
}
