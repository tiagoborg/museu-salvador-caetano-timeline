using UnityEngine;
using UnityEditor;


namespace EnigmaVirtual
{
    [CustomEditor( typeof( DesignScaler ) )]
    public class DesignScalerEditor : Editor {

        SerializedProperty _width;
        SerializedProperty _height;
        private ApplicationDescription _data;

        void OnEnable()
        {
            // Shared const, and no create-branch: this ran on every selection and would have
            // CreateAsset'd over the descriptor if the load ever returned null. AppDescriptor owns
            // creation; this is a read-only consumer.
            _data = AssetDatabase.LoadAssetAtPath<ApplicationDescription>(AppVersionSync.DescriptorPath);

            _width = serializedObject.FindProperty( "_designWidth" );
            _height = serializedObject.FindProperty( "_designHeight" );
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            serializedObject.Update();
            DesignScaler _scaler = ( DesignScaler )target;

            if (GUILayout.Button( "Apply from Settings" ))
            {
                if (_data == null)
                {
                    Debug.LogWarning("No Descriptor.asset - open Gema > App Descriptor to create one.");
                }
                else
                {
                    _width.floatValue = _data.DesignWidth;
                    _height.floatValue = _data.DesignHeight;
                    _scaler.Setup( _data.DesignWidth, _data.DesignHeight );
                }
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty( _scaler );
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
