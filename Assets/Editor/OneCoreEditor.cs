using EnigmaVirtual.OneCoreClient;
using UnityEditor;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(OneCore))]
    public class OneCoreEditor : Editor
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
