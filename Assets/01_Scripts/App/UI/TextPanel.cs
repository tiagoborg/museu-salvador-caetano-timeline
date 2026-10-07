using EnigmaVirtual;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TextPanel : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI titleText;
    [SerializeField]
    private TextMeshProUGUI subTitleText;
    [SerializeField]
    private TextMeshProUGUI contentText;
    [SerializeField]
    private RectTransform contentHolder;
    [SerializeField]
    private ScrollRect scrollRect;
    [SerializeField]
    private Scrollbar scrollbar;

    private LocalizedField<string> titleLocalized;
    private LocalizedField<string> subTitleLocalized;
    private LocalizedField<string> contentLocalized;
    private Coroutine forceRebuildCoroutine;

    private void Start()
    {
        if (!Core.IsSet)
            return;

        Core.LanguageChangedEvent += OnLanguageChanged;
    }

    private void OnDestroy()
    {
        // The comment that used to sit here said "Always unsubscribe to avoid leaking handlers even if
        // Core.IsSet changed" - directly above a `if (!Core.IsSet) return;` that did the opposite. The
        // comment had it right. Unlike MultiLangText, a stale instance of this one does not fail
        // quietly: its LocalizedField caches are plain objects that survive destruction, so the handler
        // runs on to `gameObject.activeInHierarchy` and throws MissingReferenceException - which aborts
        // the rest of the delegate chain, so every subscriber after it keeps the old language.
        Core.LanguageChangedEvent -= OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        string title;
        string subTitle;
        string content;

        // The bool used to be discarded at all three sites, so a panel with no content for the current
        // language rendered blank with nothing logged - the same shape as the gallery label.
        if (!TryGetLocalized(titleLocalized, out title))
            Development.LogWarning($"TextPanel [{name}]: no title for the current language.");

        if (!TryGetLocalized(subTitleLocalized, out subTitle))
            Development.LogWarning($"TextPanel [{name}]: no subtitle for the current language.");

        if (!TryGetLocalized(contentLocalized, out content))
            Development.LogWarning($"TextPanel [{name}]: no content for the current language.");

        SetData(title, subTitle, content);
    }

    public void SetData(LocalizedField<string> titleLocalized, LocalizedField<string> subTitleLocalized, LocalizedField<string> contentLocalized)
    {
        this.titleLocalized = titleLocalized;
        this.subTitleLocalized = subTitleLocalized;
        this.contentLocalized = contentLocalized;

        OnLanguageChanged();
    }

    private bool TryGetLocalized(LocalizedField<string> localizedField, out string text)
    {
        text = null;
        if (localizedField == null)
            return false;

        text = localizedField.GetLocalized();
        return !string.IsNullOrEmpty(text);
    }

    public void SetData(string title, string subtitle, string content)
    {
        ResetPosition();

        if (titleText != null)
            titleText.text = !string.IsNullOrEmpty(title) ? title : string.Empty;
        if (subTitleText != null)
            subTitleText.text = !string.IsNullOrEmpty(subtitle) ? subtitle : string.Empty;
        if (contentText != null)
            contentText.text = !string.IsNullOrEmpty(content) ? content : string.Empty;

        // Avoid starting multiple concurrent rebuild coroutines
        if (forceRebuildCoroutine != null)
            StopCoroutine(forceRebuildCoroutine);

        if (gameObject.activeInHierarchy) // Only start the coroutine if the GameObject is active
            forceRebuildCoroutine = StartCoroutine(ForceRebuild());
    }

    public void ResetPosition()
    {
        if (contentHolder != null)
            contentHolder.anchoredPosition = Vector2.zero;
    }

    private IEnumerator ForceRebuild()
    {
        yield return new WaitForEndOfFrame();

        if (contentHolder != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentHolder);

        yield return new WaitForEndOfFrame();

        if (scrollRect != null && scrollbar != null)
            scrollRect.vertical = scrollbar.isActiveAndEnabled;

        forceRebuildCoroutine = null;
    }
}
