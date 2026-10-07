using System;
using System.Collections.Generic;
using System.Linq;
using EnigmaVirtual.Screens;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Wires the Home and Timeline screens into <c>Intro.unity</c>. Idempotent: it reuses what exists,
/// fills only empty references, and never deletes anything. Run from <c>Gema/Timeline/Setup Scene</c>
/// or through the pipeline's eval as <c>TimelineSceneSetup.Setup()</c>.
/// </summary>
/// <remarks>
/// The two screen components are App types, and code in <c>Assets/Editor/</c> must not name an App
/// type - a branch that renames one would break the whole editor assembly. So they are found by name
/// through <c>TypeCache</c> and configured through <c>SerializedObject</c>, which also gives undo and
/// prefab-override handling for free.
/// </remarks>
public static class TimelineSceneSetup
{
    private const string ScenePath = "Assets/00_Scenes/Intro.unity";
    private const string ControllerName = "Canvas ScreenController";
    private const string HomeName = "Home ScreenView";
    private const string TimelineName = "Timeline ScreenView";
    private const string BackButtonName = "Back Button";

    // The design's charcoal background. Call keeps its own colour.
    private static readonly Color32 Background = new Color32(0x19, 0x19, 0x19, 0xFF);

    private const string HomeType = "HomeScreenview";
    private const string TimelineType = "TimelineScreenview";

    // GUIDs first, so a renamed asset is still found; the file name is the fallback.
    private static readonly AssetRef BoldFont = new AssetRef("d282e79f768b21f4bb47c3afeec44ca4", "Montserrat-Bold SDF", false);
    private static readonly AssetRef RegularFont = new AssetRef("df3b6f14f14ed2343ba69063833b3da8", "Montserrat-Regular SDF", false);
    private static readonly AssetRef FrameSprite = new AssetRef("bedbcf25707e56744a1ac16ab3848ccf", "Rectangle 45@2x", true);
    private static readonly AssetRef ArrowSprite = new AssetRef("e27f04e6c89d22d45bfbadadf5f8e4be", "Group 120@2x", true);

    [MenuItem("Gema/Timeline/Setup Scene")]
    private static void SetupFromMenu()
    {
        Debug.Log(Setup());
    }

    public static string Setup()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("TimelineSceneSetup: exit play mode first.");

        List<string> report = new List<string>();
        Scene scene = OpenIntro(report);

        GameObject controller = scene.GetRootGameObjects().FirstOrDefault(go => go.name == ControllerName);
        if (controller == null)
            throw new InvalidOperationException($"TimelineSceneSetup: no root object named [{ControllerName}] in {ScenePath}.");

        ConfigureCanvas(controller, report);

        Transform home = controller.transform.Find(HomeName);
        if (home == null)
            throw new InvalidOperationException($"TimelineSceneSetup: no [{HomeName}] under [{ControllerName}].");

        SetBackground(home.gameObject, report);

        Component homeView = EnsureComponent(home.gameObject, FindScreenType(HomeType), report);
        SerializedObject homeSo = new SerializedObject(homeView);
        SetScreenType(homeSo, ScreenType.Home, report, HomeName);
        WireBackButton(homeSo, home, report);
        FillStyle(homeSo, report, HomeName);
        homeSo.ApplyModifiedProperties();

        Transform timeline = controller.transform.Find(TimelineName);
        if (timeline == null)
            timeline = CreateTimelineObject(controller.transform, home, report);
        else
            report.Add($"reused [{TimelineName}]");

        SetBackground(timeline.gameObject, report);

        Component timelineView = EnsureComponent(timeline.gameObject, FindScreenType(TimelineType), report);
        SerializedObject timelineSo = new SerializedObject(timelineView);
        SetScreenType(timelineSo, ScreenType.Timeline, report, TimelineName);
        FillStyle(timelineSo, report, TimelineName);
        timelineSo.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException($"TimelineSceneSetup: saving {ScenePath} failed.");

        report.Add("saved " + ScenePath);
        return "TimelineSceneSetup: " + string.Join("; ", report);
    }

    private static Scene OpenIntro(List<string> report)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isLoaded)
        {
            report.Add("Intro already open");
            return scene;
        }

        // Opening replaces the open scenes, so refuse rather than discard someone's unsaved work. No
        // dialog either: this runs from eval, where a modal would block the editor.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene open = SceneManager.GetSceneAt(i);
            if (open.isDirty)
                throw new InvalidOperationException($"TimelineSceneSetup: [{open.path}] has unsaved changes. "
                    + "Save or discard them, then run again.");
        }

        report.Add("opened Intro");
        return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private static void ConfigureCanvas(GameObject controller, List<string> report)
    {
        CanvasScaler scaler = controller.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            report.Add("WARNING no CanvasScaler on the controller");
            return;
        }

        Undo.RecordObject(scaler, "Timeline setup");
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        // matchWidthOrHeight is left as authored.
        report.Add("canvas reference 1920x1080");
    }

    private static void SetBackground(GameObject screen, List<string> report)
    {
        Image image = screen.GetComponent<Image>();
        if (image == null)
        {
            report.Add($"WARNING [{screen.name}] has no background Image");
            return;
        }

        if (image.color == (Color)Background)
            return;

        Undo.RecordObject(image, "Timeline setup");
        image.color = Background;
        report.Add($"[{screen.name}] background -> #191919");
    }

    private static Type FindScreenType(string name)
    {
        Type type = TypeCache.GetTypesDerivedFrom<ScreenViewBase>().FirstOrDefault(t => t.Name == name);
        if (type == null)
            throw new InvalidOperationException($"TimelineSceneSetup: no ScreenViewBase subclass named [{name}]. "
                + "Has the App assembly compiled?");
        return type;
    }

    private static Component EnsureComponent(GameObject go, Type type, List<string> report)
    {
        Component existing = go.GetComponent(type);
        if (existing != null)
        {
            report.Add($"reused {type.Name}");
            return existing;
        }

        report.Add($"added {type.Name} to [{go.name}]");
        return Undo.AddComponent(go, type);
    }

    private static void SetScreenType(SerializedObject so, ScreenType value, List<string> report, string owner)
    {
        SerializedProperty prop = so.FindProperty("screenType");
        if (prop == null)
            throw new InvalidOperationException($"TimelineSceneSetup: [{owner}] has no screenType field.");

        if (prop.intValue != (int)value)
            report.Add($"[{owner}] screenType {(ScreenType)prop.intValue} -> {value}");

        prop.intValue = (int)value;
    }

    private static void WireBackButton(SerializedObject so, Transform home, List<string> report)
    {
        SerializedProperty prop = so.FindProperty("backButton");
        if (prop == null || prop.objectReferenceValue != null)
            return; // Already wired: keep the existing reference.

        Transform child = home.Find(BackButtonName);
        Button button = child != null ? child.GetComponent<Button>() : null;
        if (button == null)
        {
            report.Add($"WARNING [{HomeName}] has no [{BackButtonName}] with a Button; backButton left empty");
            return;
        }

        prop.objectReferenceValue = button;
        report.Add("wired Home backButton");
    }

    /// <summary>Fills empty style references only, so Inspector edits survive a re-run.</summary>
    private static void FillStyle(SerializedObject so, List<string> report, string owner)
    {
        Fill(so, "style.boldFont", BoldFont, report, owner);
        Fill(so, "style.regularFont", RegularFont, report, owner);
        Fill(so, "style.frameSprite", FrameSprite, report, owner);
        Fill(so, "style.arrowSprite", ArrowSprite, report, owner);
    }

    private static void Fill(SerializedObject so, string path, AssetRef asset, List<string> report, string owner)
    {
        SerializedProperty prop = so.FindProperty(path);
        if (prop == null)
        {
            report.Add($"WARNING [{owner}] has no {path}");
            return;
        }

        if (prop.objectReferenceValue != null)
            return;

        UnityEngine.Object value = asset.Load();
        if (value == null)
        {
            report.Add($"WARNING asset [{asset.name}] not found for {owner}.{path}");
            return;
        }

        prop.objectReferenceValue = value;
        report.Add($"{owner}.{path} = {asset.name}");
    }

    private static Transform CreateTimelineObject(Transform parent, Transform home, List<string> report)
    {
        GameObject go = new GameObject(TimelineName, typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup),
            typeof(Image));
        Undo.RegisterCreatedObjectUndo(go, "Timeline setup");
        go.transform.SetParent(parent, false);

        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;

        // Same background as Home, so the fade between the two does not flash.
        Image background = go.GetComponent<Image>();
        Image homeBackground = home.GetComponent<Image>();
        if (homeBackground != null)
        {
            background.sprite = homeBackground.sprite;
            background.color = homeBackground.color;
            background.type = homeBackground.type;
        }
        else
        {
            background.color = Background;
        }

        report.Add($"created [{TimelineName}]");
        return go.transform;
    }

    private sealed class AssetRef
    {
        private readonly string _guid;
        public readonly string name;
        private readonly bool _sprite;

        public AssetRef(string guid, string name, bool sprite)
        {
            _guid = guid;
            this.name = name;
            _sprite = sprite;
        }

        public UnityEngine.Object Load()
        {
            string path = AssetDatabase.GUIDToAssetPath(_guid);
            if (string.IsNullOrEmpty(path))
            {
                path = AssetDatabase.FindAssets(name)
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);
            }

            if (string.IsNullOrEmpty(path))
                return null;

            // A sprite is a sub-asset of its texture; a TMP font asset is the main asset of its file.
            return _sprite ? AssetDatabase.LoadAssetAtPath<Sprite>(path) : AssetDatabase.LoadMainAssetAtPath(path);
        }
    }
}
