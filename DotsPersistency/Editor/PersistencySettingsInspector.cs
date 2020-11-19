using System.Collections.Generic;
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
        private PersistencySettings _persistableTypesInfo;
        
        private void OnEnable()
        {
            _persistableTypesInfo = (PersistencySettings) target;
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
            FindPersistableTypeWindow window = EditorWindow.GetWindow<FindPersistableTypeWindow>();
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
                    if (PersistencySettings.CreatePersistableTypeInfoFromFullTypeName(oldInfo.FullTypeName, out PersistableTypeInfo updatedInfo))
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
}
