using UnityEditor;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(ImageLoaderThread))]
    public class ImageLoaderThreadEditor : Editor
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
