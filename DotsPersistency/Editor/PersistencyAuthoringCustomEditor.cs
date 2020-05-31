﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DotsPersistency.Hybrid;
using JetBrains.Annotations;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DotsPersistency.Editor
{
    [CustomEditor(typeof(PersistencyAuthoring)), CanEditMultipleObjects]
    public class PersistencyBehaviourEditor : UnityEditor.Editor
    {
        List<string> _cachedFullTypeNames;
        private RuntimePersistableTypesInfo _runTimeTypeInfos;

        private void OnEnable()
        {
            PersistableTypesInfo.GetInstance();
            _runTimeTypeInfos = PersistableTypesInfo.GetOrCreateRuntimeVersion();
            _cachedFullTypeNames = new List<string>(_runTimeTypeInfos.FullTypeNames);
            _cachedFullTypeNames.Add("");
        }

        public override void OnInspectorGUI()
        {
            var singleTarget = ((PersistencyAuthoring) target);
            string index = singleTarget.CalculateArrayIndex().ToString();
            string hashAmount = singleTarget.FullTypeNamesToPersist.Count.ToString();
            if (targets.Length != 1)
            {
                index = "--Mixed Values--";
            }

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
            EditorGUILayout.TextField("ArrayIndex", index);
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
                            string.IsNullOrEmpty(s) ? "None" : ComponentType.FromTypeIndex(TypeManager.GetTypeIndexFromStableTypeHash(_runTimeTypeInfos.GetStableTypeHashFromFullTypeName(s))).ToString())).ToArray());

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