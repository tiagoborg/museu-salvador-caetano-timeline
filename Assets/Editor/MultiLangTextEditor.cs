using UnityEditor;
using UnityEngine;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(MultiLangText))]
    public class MultiLangTextEditor : Editor
    {

        private MultiLangText _controller;

        public override void OnInspectorGUI()
        {
            _controller = (MultiLangText)target;

            CoreEditorHelper.DrawHeader();

            EditorGUILayout.BeginHorizontal();
            {
                _controller.staticText = EditorGUILayout.Toggle("Static Text", _controller.staticText);

                EditorGUI.BeginDisabledGroup(!_controller.staticText);
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Language", GUILayout.Width(100));
                    _controller.language = EditorGUILayout.TextField(_controller.language);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.Space(5);

                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            ///DrawDefaultInspector();
            DrawPropertiesExcluding(serializedObject, new string[] { "m_Script" });
            serializedObject.ApplyModifiedProperties();
        }
    }
}
