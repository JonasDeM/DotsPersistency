using System.Collections.Generic;
using System.Linq;
using DotsPersistency.Hybrid;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DotsPersistency.Editor
{
    [CustomEditor(typeof(PersistencyAuthoring)), CanEditMultipleObjects]
    public class PersistencyAuthoringInspector : UnityEditor.Editor
    {
        List<string> _cachedFullTypeNames;
        private PersistencySettings _persistencySettings;

        private void OnEnable()
        {
            _persistencySettings = PersistencySettings.Get();
            if (_persistencySettings)
            {
                _cachedFullTypeNames = _persistencySettings.AllPersistableTypeInfos.Select(info => info.FullTypeName).ToList();
                _cachedFullTypeNames.Add("");
            }
        }

        public override void OnInspectorGUI()
        {
            if (_persistencySettings == null)
            {
                if (GUILayout.Button("Create PersistencySettings", GUILayout.Height(50)))
                {
                    PersistencySettings.CreateInEditor();
                    OnEnable();
                }
                return;
            }
            
            var singleTarget = ((PersistencyAuthoring) target);
            // string index = singleTarget.CalculateArrayIndex().ToString();
            string hashAmount = singleTarget.FullTypeNamesToPersist.Count.ToString();
            // if (targets.Length != 1)
            // {
            //     index = "--Mixed Values--";
            // }

            bool sameHashes = true;
            List<string> fullTypeNames = singleTarget.FullTypeNamesToPersist;
            foreach (Object o in targets)
            {
                var persistenceAuthoring = (PersistencyAuthoring) o;

                // ReSharper disable once PossibleNullReferenceException
                if (fullTypeNames.Count != persistenceAuthoring.FullTypeNamesToPersist.Count || fullTypeNames.Except(persistenceAuthoring.FullTypeNamesToPersist).Any())
                {
                    sameHashes = false;
                    break;
                }
            }

            GUI.enabled = false;
            // EditorGUILayout.TextField("ArrayIndex", index);
            EditorGUILayout.TextField("Amount Persistent Components", sameHashes ? hashAmount : "--Mixed Values--");
            GUI.enabled = true;

            if (sameHashes)
            {
                for (int i = 0; i < singleTarget.FullTypeNamesToPersist.Count; i++)
                {
                    int selectedIndex = _cachedFullTypeNames.FindIndex(s => s == singleTarget.FullTypeNamesToPersist[i]);

                    EditorGUILayout.BeginHorizontal();
                    int newSelectedIndex = EditorGUILayout.Popup($"Type {i}", selectedIndex,
                        _cachedFullTypeNames.Select((s =>
                            string.IsNullOrEmpty(s) ? "None" : _persistencySettings.GetPrettyNameInEditor(s))).ToArray());

                    if (GUILayout.Button("-", GUILayout.Width(18)))
                    {
                        foreach (var o in targets)
                        {
                            var persistenceAuthoring = (PersistencyAuthoring) o;
                            persistenceAuthoring.FullTypeNamesToPersist.RemoveAt(i);
                            EditorUtility.SetDirty(persistenceAuthoring);
                        }
                    }
                    
                    EditorGUILayout.EndHorizontal();
                    if (newSelectedIndex != selectedIndex)
                    {
                        foreach (var o in targets)
                        {
                            var persistenceAuthoring = (PersistencyAuthoring) o;
                            persistenceAuthoring.FullTypeNamesToPersist[i] = _cachedFullTypeNames[newSelectedIndex];
                            persistenceAuthoring.FullTypeNamesToPersist = persistenceAuthoring.FullTypeNamesToPersist.Distinct().ToList();
                            persistenceAuthoring.FullTypeNamesToPersist.Sort();
                            EditorUtility.SetDirty(persistenceAuthoring);
                        }
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                EditorGUILayout.TextField("--Mixed Type Combinations--");
                GUI.enabled = true;
            }
            
            GUI.enabled = !singleTarget.FullTypeNamesToPersist.Contains("");
            string buttonText = GUI.enabled ? "Add" : "Add (You still have a \"None\" Value)";
            if (sameHashes && GUILayout.Button(buttonText))
            {
                foreach (var o in targets)
                {
                    var persistenceAuthoring = (PersistencyAuthoring) o;
                    persistenceAuthoring.FullTypeNamesToPersist.Add("");
                    EditorUtility.SetDirty(persistenceAuthoring);
                }
            }

            GUI.enabled = true;
            if (GUILayout.Button("Clear"))
            {
                foreach (var o in targets)
                {
                    var persistenceAuthoring = (PersistencyAuthoring) o;
                    persistenceAuthoring.FullTypeNamesToPersist.Clear();
                    EditorUtility.SetDirty(persistenceAuthoring);
                }
            }
        }
    }
}