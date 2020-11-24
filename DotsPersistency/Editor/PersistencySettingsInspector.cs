﻿using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace DotsPersistency.Editor
{
    [CustomEditor(typeof(PersistencySettings))]
    public class PersistencySettingsInspector : UnityEditor.Editor
    {
        private List<string> _allAvailableTypes;
        private ReorderableList _reorderableList;
        private PersistencySettings _persistencySettings;

        private bool _foldoutSettings = false;
        
        private void OnEnable()
        {
            _persistencySettings = (PersistencySettings) target;
            _reorderableList = new ReorderableList(_persistencySettings.AllPersistableTypeInfos, typeof(PersistableTypeInfo));
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
            EditorUtility.SetDirty(_persistencySettings);
        }

        private void OnAddCallback(ReorderableList list)
        {
            FindPersistableTypeWindow window = EditorWindow.GetWindow<FindPersistableTypeWindow>();
            window.OnTypeChosen = fullTypeName =>
            {
                _persistencySettings.AddPersistableTypeInEditor(fullTypeName);
                // trigger the list to update
                _reorderableList.list = _persistencySettings.AllPersistableTypeInfos;
                Repaint();
            };
        }

        public override void OnInspectorGUI()
        {
            GUI.enabled = !EditorApplication.isPlayingOrWillChangePlaymode;
            
            _reorderableList.DoLayoutList();
            if (GUILayout.Button("Force Update Type Data"))
            {
                var oldInfo = new List<PersistableTypeInfo>(_persistencySettings.AllPersistableTypeInfos);
                _persistencySettings.ClearPersistableTypesInEditor();
                for (int i = oldInfo.Count - 1; i >= 0; i--)
                {
                    var infoToRetain = oldInfo[i];
                    _persistencySettings.AddPersistableTypeInEditor(infoToRetain.FullTypeName, infoToRetain.MaxElements);
                }
            }
            
            EditorGUILayout.Space();
            _foldoutSettings = EditorGUILayout.Foldout(_foldoutSettings, "Debug Options", true);
            EditorGUI.BeginChangeCheck();
            if (_foldoutSettings)
            {
                _persistencySettings.ForceUseGroupedJobsInEditor = EditorGUILayout.ToggleLeft("Force Grouped Jobs In Editor (Unsafe)", _persistencySettings.ForceUseGroupedJobsInEditor);
                _persistencySettings.ForceUseNonGroupedJobsInBuild = EditorGUILayout.ToggleLeft("Force Non-Grouped Jobs In Build (Slow)", _persistencySettings.ForceUseNonGroupedJobsInBuild);
            }
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_persistencySettings);
            }
            
            if (EditorUtility.IsDirty(_persistencySettings))
            {
                EditorGUILayout.HelpBox("Save for the changes to go into effect! (Ctrl+S)", MessageType.Info);
            }

            GUI.enabled = true;
        }

        private void DrawElementCallback(Rect rect, int index, bool isactive, bool isfocused)
        {
            var info = _persistencySettings.AllPersistableTypeInfos[index];
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
                    _persistencySettings.AllPersistableTypeInfos[index] = info;
                    EditorUtility.SetDirty(_persistencySettings);
                }
            }
        }

        private void DrawHeaderCallback(Rect rect)
        {
            EditorGUI.LabelField(rect, "Persistable Types");
        }
    }
}
