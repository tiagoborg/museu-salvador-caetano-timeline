//#define DO_LOGS // use this on classes to print logs.
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class MultiLangText : MonoBehaviour
    {
        public enum TextCase { None, LowerCase, UpperCase }

        [SerializeField]
        public string key;

        [Header("OUTPUT")]
        [SerializeField]
        private Text _text;
        [SerializeField]
        private TMPro.TextMeshProUGUI _textMesh;

        [Header("FORMATING")]
        [SerializeField]
        private TextCase textCase = TextCase.None;
        [SerializeField, Tooltip("Converts non friendly text tags to Unity friendly.")]
        private bool convertTextTagsForUnity = true;
        [SerializeField]
        private bool _bold = false;
        [SerializeField]
        private bool _italic = false;
        [SerializeField]
        private bool _underline = false;
        [SerializeField]
        private bool _strikethrough = false;

        [Header("OPTIONS")]
        [SerializeField, Tooltip("Refreshs RectTransform on text updated.")]
        private bool forceUIRefresh = false;

        [HideInInspector, SerializeField, Tooltip("Not required if non static field.")]
        public string language;
        [HideInInspector, SerializeField, Tooltip("Text updates with language change or static.")]
        public bool staticText = false;

        private void Reset()
        {
            if (_text == null)
                _text = GetComponent<Text>();
            if (_textMesh == null)
                _textMesh = GetComponent<TMPro.TextMeshProUGUI>();
        }

        // Use this for initialization
        void Start()
        {
            if (!Core.IsSet)
                return;

            if (!staticText)
                Core.LanguageChangedEvent += OnLanguageChanged;

            OnLanguageChanged();
        }

        public void ForceLanguageUpdate()
        {
            OnLanguageChanged();
        }

        public void SetKey(string key, bool forceUpdate = true)
        {
            this.key = key;
            if (forceUpdate)
                ForceLanguageUpdate();
        }

        public string GetKey => key;

        [ContextMenu("Update translation")]
        private void OnLanguageChanged()
        {
            if (_text == null && _textMesh == null)
                return;

            if (!staticText && Core.localizationSettings.currentLanguage.ToString() != language)
                language = Core.localizationSettings.currentLanguage.ToString();

            string langText = $"[{key}]";

            if (!string.IsNullOrEmpty(key))
            {
                try
                {
                    langText = Core.main.AppDataController.Translations.GetTranslation(key, language);
                }
                catch (System.Exception e)
                {
                    Development.LogError(e.Message);
                }
            }
            else
                Development.LogWarning($"Key empty or null [{key}]");

            // Formatting is skipped for an empty string, but the ASSIGNMENT below is not - that is the
            // difference from the old shape, which wrapped both and so left the previous language's text
            // on screen whenever the new one resolved to nothing. Stale content reads as correct; blank
            // reads as missing.
            if (!string.IsNullOrEmpty(langText))
            {
                if (textCase == TextCase.UpperCase)
                    langText = langText.ToUpper();
                else if (textCase == TextCase.LowerCase)
                    langText = langText.ToLower();

                langText = langText.FixDoubleBackSlash();

                if (convertTextTagsForUnity)
                    langText = langText.ConvertTextTagsForUnity();

                if (_bold)
                    langText = langText.Bold();
                if (_italic)
                    langText = langText.Italics();
                if (_underline)
                    langText = langText.Underlined();
                if (_strikethrough)
                    langText = langText.Strikethrough();

            }
            else
                Development.LogWarning($"Translation empty for key [{key}]! Clearing the text rather "
                    + "than leaving the previous language's on screen.");

            if (_text != null)
                _text.text = langText;
            else if (_textMesh != null)
            {
                langText = langText.FixDoubleBackSlash();

                _textMesh.verticalMapping = TMPro.TextureMappingOptions.Character;
                _textMesh.SetText(langText);
            }
            else
                Development.LogWarning($"Text output missing for key [{key}]!");

            if (forceUIRefresh)
                StartCoroutine(ForceRebuild());
        }

        private IEnumerator ForceRebuild()
        {
            yield return new WaitForEndOfFrame();
            LayoutRebuilder.ForceRebuildLayoutImmediate(transform.parent.GetComponent<RectTransform>());
        }

        void OnDestroy()
        {
            // Unconditional. The guard here was `if (!Core.IsSet) return;`, which skips the detach
            // whenever IsSet is false while subscribers still exist - leaving a destroyed component
            // subscribed to a STATIC event that outlives the scene.
            //
            // Be precise about when that happens: Core is
            // DontDestroyOnLoad and _isSet only goes false in Core.OnDestroy, so an ordinary scene
            // change unsubscribed correctly. The reachable route was a DUPLICATE Core clobbering the
            // shared flag, fixed alongside this. So the leak was armed rather than firing - but `-=`
            // needs nothing from Core and is a no-op for a handler never added, so the guard was
            // protecting nothing in the first place.
            Core.LanguageChangedEvent -= OnLanguageChanged;
        }
    }
}
