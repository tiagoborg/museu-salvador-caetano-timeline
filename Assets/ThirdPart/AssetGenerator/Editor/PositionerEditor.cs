using UnityEngine;
using UnityEditor;

namespace UIGenerator
{
    [CanEditMultipleObjects]
    [CustomEditor( typeof( Positioner ) )]
    public class PositionerEditor : Editor
    {

        private AssetsVO _data;
        private Positioner _positioner;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            foreach (Object obj in targets)
            {
                _positioner = ( Positioner )obj;
            }

            GUI.enabled = !_positioner.Locked || _data == null;
            if (GUILayout.Button( "Set Position" ))
            {
                foreach (Object obj in targets)
                {
                    _positioner = ( Positioner )obj;
                    _positioner.SetPosition();
                }

            }
            if (GUILayout.Button( "Set & Destroy" ))
            {
                foreach (Object obj in targets)
                {
                    _positioner = ( Positioner )obj;
                    _positioner.SetPositionForget();
                }
            }
            GUI.enabled = !_positioner.Locked;
            if (GUILayout.Button( "Reload Data" ))
            {

                _data = null;
                foreach (Object obj in targets)
                {
                    _positioner = ( Positioner )obj;
                    _positioner.assetData = null;
                }
                OnEnable();
            }
        }

        void OnEnable()
        {
        }
    }

}

