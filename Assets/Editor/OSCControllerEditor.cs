using UnityEditor;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(OSCController))]
    public class OSCControllerEditor : Editor
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
