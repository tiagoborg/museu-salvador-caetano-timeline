using EnigmaVirtual.Screens;
using UnityEditor;

namespace EnigmaVirtual
{
    // Targets the framework base with editorForChildClasses, not the project's subclass:
    // this assembly must not name a type from App/, which every branched project redefines.
    [CustomEditor(typeof(ScreenViewBase), true)]
    public class ScreenViewEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CoreEditorHelper.DrawHeader();
            //DrawDefaultInspector();
            DrawPropertiesExcluding(serializedObject, new string[] { "m_Script" });
            serializedObject.ApplyModifiedProperties();
        }
    }
}
