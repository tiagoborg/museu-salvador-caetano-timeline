using UnityEditor;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(PhidgetsManager))]
    public class PhidgetsManagerEditor : Editor
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
