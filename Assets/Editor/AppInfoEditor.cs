using UnityEditor;
using UnityEngine;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(AppInfo))]
    public class AppInfoEditor : Editor
    {
        private AppInfo _info;

        public override void OnInspectorGUI()
        {
            CoreEditorHelper.DrawHeader();
            //DrawDefaultInspector();
            DrawPropertiesExcluding(serializedObject, new string[] { "m_Script" });
            serializedObject.ApplyModifiedProperties();

            _info = (AppInfo)target;
            RectTransform _rect = _info.GetComponent<RectTransform>();
            GUILayout.Space(15);

            if (_rect.anchoredPosition.y != 0)
            {
                if (GUILayout.Button("Show"))
                    _rect.anchoredPosition = new Vector2(_rect.anchoredPosition.x, 0f);
            }
            else
            {
                if (GUILayout.Button("Hide"))
                    _rect.anchoredPosition = new Vector2(_rect.anchoredPosition.x, -_rect.sizeDelta.y);
            }
            GUILayout.Space(15);
        }
    }
}
