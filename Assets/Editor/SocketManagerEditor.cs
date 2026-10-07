using UnityEditor;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(SocketManager))]
    public class SocketManagerEditor : Editor
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
