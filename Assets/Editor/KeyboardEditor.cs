using EnigmaVirtual.UI;
using UnityEditor;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(KeyboardScript))]
    public class KeyboardEditor : Editor
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
