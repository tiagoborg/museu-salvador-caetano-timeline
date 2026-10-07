using UnityEditor;

namespace EnigmaVirtual
{
    [CustomEditor( typeof( Core ) )]
	public class CoreEditor : Editor
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
