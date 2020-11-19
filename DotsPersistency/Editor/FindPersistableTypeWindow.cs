using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace DotsPersistency.Editor
{
    internal class FindPersistableTypeWindow : EditorWindow
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
                    if (PersistencySettings.CreatePersistableTypeInfoFromFullTypeName(fullTypeName, out PersistableTypeInfo newInfo))
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
                .Where(info => info.Type != null && PersistencySettings.IsSupported(info, out _))
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