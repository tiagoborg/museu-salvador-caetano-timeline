using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEditor;
using System;
using UIGenerator;

public class AssetsList : EditorWindow
{
    bool groupEnabled;
    bool myBool;
    private static Sprite[] _sprites;
    private Vector2 scrollPos;
    private string _fileText;
    private TextAsset[] _assetFiles;
    private GUIStyle blue;
    private Texture2D[] sprites;
    private GUIStyle error;
    private bool _value;

    private string _path = "";
    private static UIGeneratorData _data;
    private GameObject mainUI;

    [MenuItem("Window/UIGenerator")]
    static void Init()
    {

        // Get existing open window or if none, make a new one:
        AssetsList window = (AssetsList)EditorWindow.GetWindow(typeof(AssetsList));
        window.titleContent = new GUIContent("UI Generator"); ;


        // AssetDatabase paths are always forward-slash, on every platform. A backslash here does not
        // throw - it silently fails to resolve, so the null branch below overwrites the authored asset.
        _data = (UIGeneratorData)AssetDatabase.LoadAssetAtPath( "Assets/Resources/UIGeneratorData.asset", typeof( UIGeneratorData ) );
        if (_data == null)
        {
            _data = CreateInstance<UIGeneratorData>();
            AssetDatabase.CreateAsset(_data, "Assets/Resources/UIGeneratorData.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        _sprites = Resources.LoadAll<Sprite>("");

        window.Show();
    }

    void OnEnable()
    {

    }


    void OnGUI()
    {

        blue = new GUIStyle(EditorStyles.label);
        blue.normal.textColor = Color.blue;

        error = new GUIStyle(EditorStyles.boldLabel);
        error.normal.textColor = Color.red;

        if (_data == null) return;

        if (_data.psdData != null && _data.psdData.assets != null)
        {
            _data.dataHasLoaded = true;
        }

        GUILayout.Label("Design Settings", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Width", GUILayout.ExpandWidth(false));
        _data.DesignWidth = EditorGUILayout.IntField(_data.DesignWidth);
        GUILayout.Label("Height", GUILayout.ExpandWidth(false));
        _data.DesignHeight = EditorGUILayout.IntField(_data.DesignHeight);
        EditorGUILayout.EndHorizontal();

        if (_data.dataHasLoaded)
        {
            GUILayout.Space(20);

            GUILayout.Label("Commands", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset"))
            {
                _data.dataHasLoaded = false;
                _data.psdData = null;
                _data.objectName = "";
            }
            if (GUILayout.Button("Generate"))
            {
                mainUI = GameObject.Find(_data.MainCanvas);
                if (mainUI != null)
                {
                    DestroyImmediate(mainUI);
                }
                //AddCanvas();
                //AddAssets();
                GenerateLayout();
            }
            EditorGUILayout.EndHorizontal();

            ListFiles();
            return;
        }

        AddDropArea();
        GUILayout.Label("No Data file, or file not valid!", error);
    }

    private void GenerateLayout()
    {
        if (_data.psdData == null || _data.psdData.assets == null) return;

        foreach (UIItemsVO item in _data.psdData.assets)
        {
            if (item.type == "canvas")
            {
                Transform canvas = AddCanvas(item.name.Replace(":Canvas", ""));
                foreach (AssetsVO layer in item.layers)
                {
                    AddAssets(layer, canvas);
                }
            }

        }
    }

    private Transform AddCanvas(string canvasName)
    {
        mainUI = new GameObject(canvasName);
        mainUI.AddComponent<RectTransform>();
        Canvas mainCanvas = mainUI.AddComponent<Canvas>();
        mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        mainCanvas.pixelPerfect = true;
        CanvasScaler scaler = mainUI.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(_data.DesignWidth, _data.DesignHeight);
        scaler.matchWidthOrHeight = 1;
        mainUI.AddComponent<GraphicRaycaster>();
        return mainCanvas.transform;
    }

    private void AddAssets(AssetsVO vo, Transform canvas)
    {
        //for (int i = _data.psdData.assets.Length - 1; i >= 0; i--)
        //{
        //    UIItemsVO vo = _data.psdData.assets[i] as UIItemsVO;
        //    if (vo.layers.hasSprite)
        //    {
        string strippedName = vo.name.Replace(".png", "").Replace(".jpg", "");
        GameObject newGO = new GameObject(strippedName);
        newGO.AddComponent<RectTransform>();
        newGO.transform.SetParent(canvas);
        Image image = newGO.AddComponent<Image>();
        image.sprite = GetSprite(strippedName);
        Positioner positioner = newGO.AddComponent<Positioner>();
        positioner.assetData = vo;
        positioner.designSize = new Vector2(_data.DesignWidth, _data.DesignHeight);
        positioner.SetPosition();
        //newGO.name = image.sprite.name;
        //    }
        //}
    }

    private void AddDropArea()
    {
        GUILayout.Label("UIGenerator Data File", EditorStyles.boldLabel);
        // Targets Drop Area
        Event evt = Event.current;
        Rect drop_area = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
        var style = new GUIStyle("box");
        if (EditorGUIUtility.isProSkin)
            style.normal.textColor = Color.white;
        GUI.Box(drop_area, "\nDROP ASSET FILE HERE ", style);

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                if (!drop_area.Contains(evt.mousePosition))
                    return;

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();


                    EnigmaVirtual.Development.Log(DragAndDrop.paths[0]);

                    var obj = DragAndDrop.objectReferences[0];
                    if (obj.GetType() == typeof(TextAsset))
                    {
                        EnigmaVirtual.Development.Log("ok");
                        _path = DragAndDrop.paths[0];
                        LoadDataFile(_path);
                        break;
                    }
                }
                break;
        }
    }

    private void Save()
    {
        AssetDatabase.Refresh();
        EditorUtility.SetDirty(_data);
        AssetDatabase.SaveAssets();
    }

    private void ListFiles()
    {
        //if (_data.psdData == null || _data.psdData.assets.Length == 0) return;
        //GUILayout.Label( "Assets Files", EditorStyles.boldLabel );
        //scrollPos = EditorGUILayout.BeginScrollView( scrollPos, false, false );
        //for (int i = 0; i < _data.psdData.assets.Length; i++)
        //{
        //    AssetsVO vo = _data.psdData.assets[i] as AssetsVO;
        //    vo.hasSprite = FindSprite( vo.name );
        //    GUILayout.Toggle( vo.hasSprite, vo.name );
        //}
        //EditorGUILayout.EndScrollView();
        if (_data.psdData == null || _data.psdData.assets == null) return;


        //foreach (UIItemsVO item in _data.psdData.assets)
        //{
        //    foreach (AssetsVO layer in item.layers)
        //    {
        //        //Development.Log( layer.name );
        //    }
        //}
    }

    private bool FindSprite(string searchName)
    {
        EnigmaVirtual.Development.Log(searchName);
        bool found = false;
        for (int i = 0; i < _sprites.Length; i++)
        {
            Sprite sp = _sprites[i] as Sprite;
            if (sp.name == searchName) found = true;
            continue;
        }
        return found;
    }

    private Sprite GetSprite(string searchName)
    {
        Sprite sp = null;
        for (int i = 0; i < _sprites.Length; i++)
        {
            sp = _sprites[i] as Sprite;
            if (sp.name == searchName)
                break;
        }
        return sp;
    }

    private void ListSprites()
    {
        for (int i = 0; i < _sprites.Length; i++)
        {
            Sprite sp = _sprites[i] as Sprite;
            EnigmaVirtual.Development.Log(sp.name);
        }
    }

    private void LoadDataFile(string path)
    {
        _fileText = System.IO.File.ReadAllText(path);
        _data.Load(_fileText);
        _data.objectName = path;
        Save();
    }
}
