using UnityEditor;
using UnityEngine;

namespace EnigmaVirtual
{
    // The `true` is editorForChildClasses. Without it a project's own subclass - which is the
    // whole extension point, and the component actually on the GameObject - silently falls back to
    // Unity's default inspector.
    [CustomEditor(typeof(AppDataController), true)]
    public class AppDataControllerEditor : Editor
    {
        private AppDataController _controller;
        public override void OnInspectorGUI()
        {
            _controller = (AppDataController)target;

            CoreEditorHelper.DrawHeader();

            EditorGUI.BeginDisabledGroup(true);
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Data Loaded", GUILayout.Width(100));
                EditorGUILayout.Toggle(_controller.dataLoaded);
                EditorGUILayout.EndHorizontal();
                
            }
            EditorGUI.EndDisabledGroup();

            //DrawDefaultInspector();
            DrawPropertiesExcluding(serializedObject, new string[] { "m_Script", "dataLoaded" });
            serializedObject.ApplyModifiedProperties();
        }
    }
}
