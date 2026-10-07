using EnigmaVirtual.Screens.Navigation;
using UnityEditor;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(NavigationHistory))]
    public class NavigationHistoryEditor : Editor
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
