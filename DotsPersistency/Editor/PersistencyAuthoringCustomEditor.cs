using System;
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
        List<ulong> _cachedTypes;

        private void OnEnable()
        {
            PersistableTypesInfo.GetInstance();
            _cachedTypes = new List<ulong>(PersistableTypesInfo.GetOrCreateRuntimeVersion().StableTypeHashes);
            _cachedTypes.Add(0);
        }

        public override void OnInspectorGUI()
        {
            var singleTarget = ((PersistencyAuthoring) target);
            string index = singleTarget.CalculateArrayIndex().ToString();
            string hashAmount = singleTarget.TypesToPersistHashes.Count.ToString();
            if (targets.Length != 1)
            {
                index = "--Mixed Values--";
            }

            bool sameHashes = true;
            List<ulong> typeHashes = singleTarget.TypesToPersistHashes;
            foreach (Object o in targets)
            {
                var persistenceAuthoring = (PersistencyAuthoring) o;

                // ReSharper disable once PossibleNullReferenceException
                if (typeHashes.Count != persistenceAuthoring.TypesToPersistHashes.Count || typeHashes.Except(persistenceAuthoring.TypesToPersistHashes).Any())
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
                for (int i = 0; i < singleTarget.TypesToPersistHashes.Count; i++)
                {
                    int selectedIndex = _cachedTypes.FindIndex(hash => hash == singleTarget.TypesToPersistHashes[i]);

                    EditorGUILayout.BeginHorizontal();
                    int newSelectedIndex = EditorGUILayout.Popup($"Type {i}", selectedIndex,
                        _cachedTypes.Select((hash =>
                            hash == 0 ? "None" : ComponentType.FromTypeIndex(TypeManager.GetTypeIndexFromStableTypeHash(hash)).ToString())).ToArray());

                    if (GUILayout.Button("-", GUILayout.Width(18)))
                    {
                        foreach (var o in targets)
                        {
                            var persistenceAuthoring = (PersistencyAuthoring) o;
                            persistenceAuthoring.TypesToPersistHashes.RemoveAt(i);
                            EditorUtility.SetDirty(persistenceAuthoring);
                        }
                    }
                    
                    EditorGUILayout.EndHorizontal();
                    if (newSelectedIndex != selectedIndex)
                    {
                        foreach (var o in targets)
                        {
                            var persistenceAuthoring = (PersistencyAuthoring) o;
                            persistenceAuthoring.TypesToPersistHashes[i] = _cachedTypes[newSelectedIndex];
                            persistenceAuthoring.TypesToPersistHashes = persistenceAuthoring.TypesToPersistHashes.Distinct().ToList();
                            persistenceAuthoring.TypesToPersistHashes.Sort(new TypeComparer());
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
            
            GUI.enabled = !singleTarget.TypesToPersistHashes.Contains(0);
            string buttonText = GUI.enabled ? "Add" : "Add (You still have a \"None\" Value)";
            if (sameHashes && GUILayout.Button(buttonText))
            {
                foreach (var o in targets)
                {
                    var persistenceAuthoring = (PersistencyAuthoring) o;
                    persistenceAuthoring.TypesToPersistHashes.Add(0);
                    EditorUtility.SetDirty(persistenceAuthoring);
                }
            }

            GUI.enabled = true;
            if (GUILayout.Button("Clear"))
            {
                foreach (var o in targets)
                {
                    var persistenceAuthoring = (PersistencyAuthoring) o;
                    persistenceAuthoring.TypesToPersistHashes.Clear();
                    EditorUtility.SetDirty(persistenceAuthoring);
                }
            }
        }

        struct TypeComparer : IComparer<ulong>
        {
            public int Compare(ulong x, ulong y)
            {
                if (x == 0)
                {
                    return 1;
                }
                
                bool xIsBuffer = TypeManager.IsBuffer(TypeManager.GetTypeIndexFromStableTypeHash(x));
                bool yIsBuffer = TypeManager.IsBuffer(TypeManager.GetTypeIndexFromStableTypeHash(y));
                if (xIsBuffer != yIsBuffer)
                {
                    if (xIsBuffer)
                        return 1;
                    return -1;
                }
                
                return (int)(x - y);
            }
        }
    }
}