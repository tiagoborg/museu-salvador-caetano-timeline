using UnityEditor;
using UnityEngine;

namespace EnigmaVirtual
{
    [CustomEditor(typeof(TransitionIn))]
    public class TransitionInEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CoreEditorHelper.DrawHeader();
            DrawDefaultInspector();

            TransitionIn script = target as TransitionIn;

            switch (script.TransitionIn)
            {
                case TransitionType.SmoothIn:
                    script.scaleStart = EditorGUILayout.FloatField("scaleStart", script.scaleStart);
                    script.scaleEnd = EditorGUILayout.FloatField("scaleEnd", script.scaleEnd);
                    script.fadeToo = EditorGUILayout.Toggle("fadeToo", script.fadeToo);
                    break;

                case TransitionType.SlideInBottom:
                case TransitionType.SlideInLeft:
                case TransitionType.SlideInRight:
                case TransitionType.SlideInTop:
                    script.Padding = EditorGUILayout.FloatField("Padding", script.Padding);
                    break;

            }
        }
    }
}