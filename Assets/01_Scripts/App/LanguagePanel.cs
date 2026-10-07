using EnigmaVirtual;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(ToggleGroup))]
public class LanguagePanel : PanelBase
{
    [SerializeField]
    private ToggleGroup toggleGroup;
    [SerializeField]
    private Transform content;
    [SerializeField]
    private GameObject languageTogglePrefab;

    private List<LanguageToggle> languageToggles;

    protected override void Reset()
    {
        base.Reset();

        // Guarded like base.Reset, and that matters now rather than before: Reset only ever ran
        // from the inspector until the empty Awake override was deleted, so an unconditional
        // reassignment here now runs at RUNTIME and would silently rebind an authored reference to
        // a child's ToggleGroup back onto this object.
        if (toggleGroup == null)
            toggleGroup = GetComponent<ToggleGroup>();
    }

    protected override void Awake()
    {
    }

    // `protected override void Awake() { }` used to sit here and is deleted. An empty
    // override does not "skip some setup" - it discards PanelBase.Awake wholesale, so Reset() never
    // ran and the panel never initialised to alpha 0 / non-interactable / non-blocking. It started
    // however the scene happened to leave it, which on a prefab saved in the visible state means a
    // language picker on screen and clickable before anything shows it.
    //
    // Nothing here overrides Awake now, so PanelBase's runs. It LogErrors when the object has no
    // CanvasGroup, but PanelBase already carries [RequireComponent(typeof(CanvasGroup))] and this
    // class inherits it, so that path needs someone to have removed the component by hand.

    void Start()
    {
        if (!Core.IsSet)
            return;

        if (toggleGroup == null || content == null || languageTogglePrefab == null)
        {
            Debug.LogWarning($"{nameof(LanguagePanel)}: Missing serialized references (toggleGroup/content/languageTogglePrefab).");
            return;
        }

        if (Core.localizationSettings == null)
        {
            Debug.LogWarning($"{nameof(LanguagePanel)}: Core.localizationSettings is null.");
            return;
        }

        SetButtons();
    }

    private void SetButtons()
    {
        Langs[] languages = Core.localizationSettings.languages;
        languageToggles = new List<LanguageToggle>();

        if (languages == null || languages.Length < 2)
            return;

        // remove existing children to avoid duplicates when re-opening panel
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Transform child = content.GetChild(i);
#if UNITY_EDITOR
            if (Application.isPlaying)
                Object.Destroy(child.gameObject);
            else
                Object.DestroyImmediate(child.gameObject);
#else
            Object.Destroy(child.gameObject);
#endif
        }

        foreach (var lang in languages)
        {
            GameObject go = Instantiate(languageTogglePrefab, content, false);
            LanguageToggle lt = go.GetComponent<LanguageToggle>();
            if (lt == null)
            {
                Debug.LogWarning($"{nameof(LanguagePanel)}: Instantiated prefab is missing LanguageToggle component.");
                Destroy(go);
                continue;
            }

            lt.SetData(lang, toggleGroup, Core.localizationSettings.currentLanguage == lang);
            languageToggles.Add(lt);
        }

        foreach (LanguageToggle lt in languageToggles)
        {
            if (lt.language == Core.localizationSettings.currentLanguage)
            {
                lt.SetToggleActive();
                break;
            }
        }
    }
}
