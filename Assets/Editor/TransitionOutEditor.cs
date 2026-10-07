using UnityEditor;
using UnityEngine;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(TransitionOut))]
    public class TransitionOutEditor : Editor
    {
        private TransitionType _prevType;

        public override void OnInspectorGUI()
        {
            CoreEditorHelper.DrawHeader();
            DrawDefaultInspector();

            TransitionOut script = target as TransitionOut;

            switch (script.TransitionIn)
            {
                case TransitionType.SmoothOut:
                    script.scaleStart = EditorGUILayout.FloatField("scaleStart", script.scaleStart);
                    script.scaleEnd = EditorGUILayout.FloatField("scaleEnd", script.scaleEnd);
                    script.fadeToo = EditorGUILayout.Toggle("fadeToo", script.fadeToo);
                    break;
                case TransitionType.SlideOutBottom:
                case TransitionType.SlideOutLeft:
                    script.Padding = EditorGUILayout.FloatField("Padding", script.Padding);
                    break;

            }
        }
    }
}