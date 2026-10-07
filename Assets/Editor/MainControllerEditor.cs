using UnityEngine;
using UnityEditor;
using EnigmaVirtual.OneCoreClient;

namespace EnigmaVirtual
{
    [CustomEditor( typeof( MainController ) )]
    public class MainControllerEditor : Editor
    {
        private MainController _main;
        private ActivityMonitor _activity;
        private SocketManager _socket;
        private ApiController _api;
        private PhidgetsManager _phidgets;
        private OSCController _oscController;
        private FileController _fileController;
        private KeyboardController _keyboard;
        private OneCore _oneCore;

        void OnEnable()
        {
            //_activity = _main.gameObject.GetComponent<ActivityMonitor>();
        }

        public override void OnInspectorGUI()
        {
            _main = ( MainController )target;
            // Modules
            _activity = _main.gameObject.GetComponentInChildren<ActivityMonitor>();
            _socket = _main.gameObject.GetComponentInChildren<SocketManager>();
            _api = _main.gameObject.GetComponentInChildren<ApiController>();
            _phidgets = _main.gameObject.GetComponentInChildren<PhidgetsManager>();
            _oscController = _main.gameObject.GetComponentInChildren<OSCController>();
            _fileController = _main.gameObject.GetComponentInChildren<FileController>();
            _oneCore = _main.gameObject.GetComponentInChildren<OneCore>();
            // components
            _keyboard = _main.gameObject.GetComponentInChildren<KeyboardController>();

            CoreEditorHelper.DrawHeader();

            //EditorGUILayout.Space();
            //GUI.color = Color.white;
            //DrawDefaultInspector();
            DrawPropertiesExcluding(serializedObject, new string[] { "m_Script" });
            serializedObject.ApplyModifiedProperties();
            //EditorGUILayout.Space();

            EditorGUILayout.LabelField( "Modules" );
            //GUI.color = Color.green;

            if (_activity == null)
            {
                if (GUILayout.Button("Add Activity Monitor"))
                {
                    GameObject go = new GameObject( "ActivityMonitor" );
                    go.transform.SetParent( _main.gameObject.transform );
                    go.AddComponent<ActivityMonitor>();
                }
            }
            if (_socket == null)
            {
                if (GUILayout.Button("Add Sockets Manager"))
                {
                    GameObject go = new GameObject( "SocketManager" );
                    go.transform.SetParent( _main.gameObject.transform );
                    go.AddComponent<SocketManager>();
                    go.AddComponent<SocketConnection>();
                }
            }
            if (_api == null)
            {
                if (GUILayout.Button("Add Api Controller"))
                {
                    GameObject go = new GameObject("ApiController");
                    go.transform.SetParent(_main.gameObject.transform);
                    go.AddComponent<ApiController>();
                }
            }
            if (_phidgets == null)
            {
                if (GUILayout.Button( "Add Phidgets Controller" ))
                {
                    GameObject go = new GameObject( "PhidgetsManager" );
                    go.transform.SetParent( _main.gameObject.transform );
                    go.AddComponent<PhidgetsManager>();
                }
            }
            if (_oscController == null)
            {
                if (GUILayout.Button("Add OSC Controller"))
                {
                    GameObject go = new GameObject("OSCController");
                    go.transform.SetParent(_main.gameObject.transform);
                    go.AddComponent<OSCController>();
                }
            }
            if (_fileController == null)
            {
                //GUI.color = Color.green;
                if (GUILayout.Button("Add File Controller"))
                {
                    GameObject go = new GameObject("FileController");
                    go.transform.SetParent(_main.gameObject.transform);
                    go.AddComponent<FileController>();
                }
            }
            if (_oneCore == null)
            {
                if (GUILayout.Button("Add OneCore Controller"))
                {
                    GameObject go = new GameObject("OneCore");
                    go.transform.SetParent(_main.gameObject.transform);
                    go.AddComponent<OneCore>();
                }
            }

            EditorGUILayout.Space();
            //GUI.color = Color.white;
            EditorGUILayout.LabelField( "Components" );

            //GUI.color = Color.cyan;
            if (_keyboard == null)
            {
                if (GUILayout.Button( "Add Virtual Keyboard" ))
                {
                    GameObject go = Instantiate( Resources.Load( "Gema/Prefabs/Ui/VirtualKeyboard" ), Vector3.zero, Quaternion.identity ) as GameObject;
                    go.name = "VirtualKeyboard";
                    go.transform.SetParent( _main.gameObject.transform );
                }
            }

            EditorGUILayout.Space();
        }
    }


}
