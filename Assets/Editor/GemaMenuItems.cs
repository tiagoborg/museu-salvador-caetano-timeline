using System;
using EnigmaVirtual;
using EnigmaVirtual.Components.UIElements;
using EnigmaVirtual.Screens;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class GemaMenuItems : MonoBehaviour
{
    /// <summary>
    /// The project's own concrete subclass of a framework base, found at edit time.
    /// </summary>
    /// <remarks>
    /// These menu items build project screens, but this assembly must not name a project type:
    /// everything under <c>App/</c> is redefined by each branched project, and one missing name
    /// fails the whole editor assembly - <c>BuildValidator</c> and the version sync with it.
    /// Resolving by base class instead keeps the menu working whatever the subclass is called.
    ///
    /// Returns the first concrete subclass, and reports when there is more than one:
    /// <c>TypeCache</c> does not guarantee an order, so an ambiguous project would otherwise get a
    /// component that can change between domain reloads with nothing said about it.
    /// </remarks>
    private static Type ConcreteSubclassOf<T>() where T : MonoBehaviour
    {
        Type found = null;
        int concreteCount = 0;

        foreach (Type t in TypeCache.GetTypesDerivedFrom<T>())
        {
            // A generic type definition reports IsAbstract false, and AddComponent returns null for it.
            if (t.IsAbstract || t.IsGenericTypeDefinition)
                continue;

            concreteCount++;
            if (found == null)
                found = t;
        }

        if (concreteCount > 1)
            Development.LogError("Ambiguous " + typeof(T).Name + ": this project has " + concreteCount
                                 + " concrete subclasses. Using " + found.FullName
                                 + ", which may differ after the next domain reload.");

        return found;
    }

    [MenuItem("GameObject/Gema/Text MultiLang", false, 10)]
    static void CreateMultiLangTextGO(MenuCommand menuCommand)
    {
        // Create a custom game object
        GameObject go = new GameObject("Text MultiLang");
        RectTransform rt = go.AddComponent(typeof(RectTransform)) as RectTransform;
        rt.anchorMin = new Vector2(.5f, .5f);
        rt.anchorMax = new Vector2(.5f, .5f);
        rt.pivot = new Vector2(.5f, .5f);
        rt.SetWidth(200);
        rt.SetHeight(30);

        go.AddComponent(typeof(Text));
        go.AddComponent(typeof(MultiLangText));

        // Ensure it gets reparented if this was a context click (otherwise does nothing)
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        // Register the creation in the undo system
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }

    [MenuItem("GameObject/Gema/TextMeshPro MultiLang", false, 10)]
    static void CreateMultiLangTextMeshProGO(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("TextMeshPro MultiLang");
        go.AddComponent(typeof(TMPro.TextMeshProUGUI));
        go.AddComponent(typeof(MultiLangText));
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }

    // Add a menu item to create custom GameObjects.
    // Priority 1 ensures it is grouped with the other menu items of the same kind
    // and propagated to the hierarchy dropdown and hierarchy context menus.
    [MenuItem("GameObject/Gema/Image Downloadable", false, 10)]
    static void CreateDownloadableImageGO(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("Image Downloadable");
        go.AddComponent(typeof(RectTransform));
        go.AddComponent(typeof(Image));
        go.AddComponent(typeof(Mask));
        Mask m = go.GetComponent<Mask>();
        m.showMaskGraphic = false;
        go.AddComponent(typeof(DownloadableImage));
        DownloadableImage di = go.GetComponent<DownloadableImage>();

        GameObject gosec = new GameObject("Image");
        gosec.AddComponent(typeof(Image));
        //gosec.AddComponent(typeof(DownloadableImage));

        di.SetImage(gosec.GetComponent<Image>());

        GameObjectUtility.SetParentAndAlign(gosec, go);

        gosec.AddComponent(typeof(ImageResizer));

        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }

    [MenuItem("GameObject/Gema/RawImage Downloadable", false, 10)]
    static void CreateDownloadableRawImageGO(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("RawImage Downloadable");
        go.AddComponent(typeof(RectTransform));
        go.AddComponent(typeof(Image));
        go.AddComponent(typeof(Mask));
        Mask m = go.GetComponent<Mask>();
        m.showMaskGraphic = false;
        go.AddComponent(typeof(DownloadableImage));
        DownloadableImage di = go.GetComponent<DownloadableImage>();

        GameObject gosec = new GameObject("RawImage");
        gosec.AddComponent(typeof(RawImage));
        RawImage ri = gosec.GetComponent<RawImage>();
        //gosec.AddComponent(typeof(DownloadableImage));

        di.SetRawImage(ri);

        GameObjectUtility.SetParentAndAlign(gosec, go);

        gosec.AddComponent(typeof(ImageResizer));

        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }

    [MenuItem("GameObject/Gema/Canvas ScreenController", false, 10)]
    static void CreateScreenControllerCanvasGO(MenuCommand menuCommand)
    {
        // Both lookups happen before anything is created: a failure half way through would leave an
        // unparented canvas in the scene with no Undo entry to remove it.
        Type screenControllerType = ConcreteSubclassOf<ScreenControllerBase>();
        if (screenControllerType == null)
        {
            Development.LogError("Canvas ScreenController not created: this project has no "
                                 + "concrete ScreenControllerBase subclass.");
            return;
        }

        if (ConcreteSubclassOf<ScreenViewBase>() == null)
        {
            Development.LogError("Canvas ScreenController not created: it needs a ScreenView, and this "
                                 + "project has no concrete ScreenViewBase subclass.");
            return;
        }

        GameObject go = new GameObject("Canvas ScreenController");
        Canvas cv = go.AddComponent(typeof(Canvas)) as Canvas;
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        ApplicationDescription appDes = AssetDatabase.LoadAssetAtPath<ApplicationDescription>(AppVersionSync.DescriptorPath);
        CanvasScaler cvs = go.AddComponent(typeof(CanvasScaler)) as CanvasScaler;
        cvs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        if (appDes != null)
            cvs.referenceResolution = new Vector2(appDes.DesignWidth, appDes.DesignHeight);
        cvs.matchWidthOrHeight = 1;
        go.AddComponent(typeof(GraphicRaycaster));
        go.AddComponent(screenControllerType);

        GameObject gosec = CreateScreenViewGO(new MenuCommand(null, 0) { context = go });
        if (gosec == null)
            return;

        gosec.GetComponent<ScreenViewBase>().SetScreenType(ScreenType.Home);

        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }

    [MenuItem("GameObject/Gema/Panel ScreenView", false, 10)]
    static void CreateScreenViewPanelGO(MenuCommand menuCommand)
    {
        GameObject go = CreateScreenViewGO(menuCommand);
        if (go == null)
            return;

        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }

    [MenuItem("GameObject/Gema/Text Panel", false, 10)]
    static void CreateTextPanelGO(MenuCommand menuCommand)
    {
        GameObject go = Instantiate(Resources.Load("Prefabs/TextPanel") as GameObject);
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }

    [MenuItem("GameObject/Gema/Image Gallery Panel", false, 10)]
    static void CreateImageGalleryPanelGO(MenuCommand menuCommand)
    {
        GameObject go = Instantiate(Resources.Load("Prefabs/ImageGalleryPanel") as GameObject);
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }

    [MenuItem("GameObject/Gema/Panel HorizontalScrollSnap", false, 10)]
    static void CreateVerticalScrollSnapPanelGO(MenuCommand menuCommand)
    {
        GameObject go = CreatePanelGO(menuCommand, "Panel HorizontalScrollSnap");
        Mask m = go.AddComponent(typeof(Mask)) as Mask;
        m.showMaskGraphic = false;
        // Content panel
        GameObject content = CreatePanelGO(go, "Content", false);
        content.AddComponent(typeof(HorizontalLayoutGroup));
        ContentSizeFitter csf = content.AddComponent(typeof(ContentSizeFitter)) as ContentSizeFitter;
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        // Pagination
        GameObject pag = CreatePanelGO(go, "Pagination");
        RectTransform pagRt = pag.GetComponent<RectTransform>();
        pagRt.anchorMin = new Vector2(0, 0);
        pagRt.anchorMax = new Vector2(1, 0);
        pagRt.pivot = new Vector2(0.5f, 0.5f);
        pagRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, go.GetComponent<RectTransform>().rect.width);
        pagRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 50);
        pagRt.anchoredPosition = new Vector2(0, 25);
        HorizontalLayoutGroup paghl = pag.AddComponent(typeof(HorizontalLayoutGroup)) as HorizontalLayoutGroup;
        paghl.childAlignment = TextAnchor.MiddleCenter;
        paghl.childForceExpandWidth = false;
        paghl.spacing = 10;
        // Navigation
        GameObject nav = CreatePanelGO(go, "Navigation", false);
        GameObject nextbtt = (GameObject)Instantiate(Resources.Load("Prefabs/HorizonalScrollSnap/NavNextButtonPrefab") as GameObject);
        GameObjectUtility.SetParentAndAlign(nextbtt, nav);
        GameObject prevbtt = (GameObject)Instantiate(Resources.Load("Prefabs/HorizonalScrollSnap/NavPreviousButtonPrefab") as GameObject);
        GameObjectUtility.SetParentAndAlign(prevbtt, nav);

        // Config
        ScrollRect sr = go.AddComponent(typeof(ScrollRect)) as ScrollRect;
        sr.vertical = false;
        sr.content = content.GetComponent<RectTransform>();
        HorizontalScrollSnap hss = go.AddComponent(typeof(HorizontalScrollSnap)) as HorizontalScrollSnap;
        hss.UseFastSwipe = true;
        hss.Pagination = pag;
        hss.PaginationItemPrefab = Resources.Load("Prefabs/HorizonalScrollSnap/PaginationItemPrefab") as GameObject;
        hss.PrevButton = prevbtt;
        hss.NextButton = nextbtt;

        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Selection.activeObject = go;
    }

    private static GameObject CreateScreenViewGO(MenuCommand menuCommand, string name = "Panel ScreenView")
    {
        Type screenViewType = ConcreteSubclassOf<ScreenViewBase>();
        if (screenViewType == null)
        {
            Development.LogError("Panel ScreenView not created: this project has no concrete "
                                 + "ScreenViewBase subclass.");
            return null;
        }

        GameObject go = CreatePanelGO(menuCommand, name);
        go.AddComponent(screenViewType);
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        return go;
    }

    private static GameObject CreatePanelGO(MenuCommand menuCommand, string name = "Panel", bool addImage = true)
    {
        return CreatePanelGO(menuCommand.context as GameObject, name, addImage);
    }

    private static GameObject CreatePanelGO(GameObject parentGameObject, string name = "Panel", bool addImage = true)
    {
        GameObject go = new GameObject(name);
        RectTransform rt = go.AddComponent(typeof(RectTransform)) as RectTransform;
        rt.anchorMax = new Vector2(1, 1);
        rt.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Bottom, 0, 0);
        rt.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 0, 0);
        rt.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 0, 0);
        rt.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 0, 0);
        rt.anchorMin = Vector2.zero;
        if (addImage)
        {
            Color imgColor = Color.white;
            imgColor.a = 0.39f;
            Image img = go.AddComponent(typeof(Image)) as Image;
            img.color = imgColor;
        }
        GameObjectUtility.SetParentAndAlign(go, parentGameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        return go;
    }
}

//// Add a menu item named "Do Something" to MyMenu in the menu bar.
//[MenuItem("MyMenu/Do Something")]
//static void DoSomething()
//{
//    Debug.Log("Doing Something...");
//}

//// Validated menu item.
//// Add a menu item named "Log Selected Transform Name" to MyMenu in the menu bar.
//// We use a second function to validate the menu item
//// so it will only be enabled if we have a transform selected.
//[MenuItem("MyMenu/Log Selected Transform Name")]
//static void LogSelectedTransformName()
//{
//    Debug.Log("Selected Transform is on " + Selection.activeTransform.gameObject.name + ".");
//}

//// Validate the menu item defined by the function above.
//// The menu item will be disabled if this function returns false.
//[MenuItem("MyMenu/Log Selected Transform Name", true)]
//static bool ValidateLogSelectedTransformName()
//{
//    // Return false if no transform is selected.
//    return Selection.activeTransform != null;
//}

//// Add a menu item named "Do Something with a Shortcut Key" to MyMenu in the menu bar
//// and give it a shortcut (ctrl-g on Windows, cmd-g on macOS).
//[MenuItem("MyMenu/Do Something with a Shortcut Key %g")]
//static void DoSomethingWithAShortcutKey()
//{
//    Debug.Log("Doing something with a Shortcut Key...");
//}

//// Add a menu item called "Double Mass" to a Rigidbody's context menu.
//[MenuItem("CONTEXT/Rigidbody/Double Mass")]
//static void DoubleMass(MenuCommand command)
//{
//    Rigidbody body = (Rigidbody)command.context;
//    body.mass = body.mass * 2;
//    Debug.Log("Doubled Rigidbody's Mass to " + body.mass + " from Context Menu.");
//}