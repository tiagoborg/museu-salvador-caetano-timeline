using System.Collections.Generic;
using EnigmaVirtual;
using EnigmaVirtual.Screens;
using EnigmaVirtual.Screens.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Category picker. One link per category opens the timeline with only that category on; the
/// "all categories" link opens it with every category on.
/// </summary>
/// <remarks>
/// The links are built in code from <c>AppData.categories</c>, inside <see cref="categoryArea"/> when
/// one is assigned, otherwise in a rect created at the design position.
/// </remarks>
public class HomeScreenview : ScreenView
{
    private const string AllCategoriesKey = "allCategories";

    [SerializeField]
    private Button backButton;

    [Header("Category links")]
    [SerializeField]
    private TimelineStyle style = new TimelineStyle();
    [SerializeField, Tooltip("Optional. Links flow left to right and wrap inside its width.")]
    private RectTransform categoryArea;
    [SerializeField]
    private Vector2 categoryAreaPosition = new Vector2(60f, 470f);
    [SerializeField]
    private float categoryAreaWidth = 1800f;
    [SerializeField]
    private float categoryFontSize = 28f;
    [SerializeField]
    private float categoryRowHeight = 80f;
    [SerializeField]
    private float categoryGap = 46f;
    [SerializeField]
    private float allCategoriesY = 786f;
    [SerializeField]
    private float allCategoriesFontSize = 36f;
    [SerializeField]
    private float arrowSize = 18f;
    [SerializeField]
    private float arrowGap = 14f;

    private RectTransform _links;
    private bool _listening;

    public override void Show()
    {
        base.Show();
        backButton.onClick.AddListener(OnBackButtonClicked);

        if (!_listening)
        {
            Core.LanguageChangedEvent += BuildLinks;
            _listening = true;
        }

        BuildLinks();
    }

    public override void Hide()
    {
        backButton.onClick.RemoveListener(OnBackButtonClicked);
        StopListening();
        base.Hide();
    }

    private void OnDestroy()
    {
        StopListening();
    }

    private void StopListening()
    {
        if (!_listening)
            return;

        Core.LanguageChangedEvent -= BuildLinks;
        _listening = false;
    }

    private void OnBackButtonClicked()
    {
        Controller.NavigateTo(new EnigmaVirtual.Screens.Navigation.NavigationItem(EnigmaVirtual.Screens.ScreenType.Call));
    }

    private void OpenTimeline(int[] categoryIds)
    {
        Controller.NavigateTo(new NavigationItem(ScreenType.Timeline, categoryIds));
    }

    /// <summary>Rebuilt on every show and language change, since link widths follow the text.</summary>
    private void BuildLinks()
    {
        AppData data = AppData.Current;
        if (data == null)
            return;

        if (_links == null)
        {
            TimelineUi.ReportMissingStyle(this, style);

            _links = TimelineUi.Rect(categoryArea != null ? categoryArea : transform, "Category Links");
            if (categoryArea != null)
                TimelineUi.TopLeft(_links, 0f, 0f, categoryArea.rect.width, 0f);
            else
                TimelineUi.TopLeft(_links, categoryAreaPosition.x, categoryAreaPosition.y, categoryAreaWidth, 0f);
        }

        for (int i = _links.childCount - 1; i >= 0; i--)
            Destroy(_links.GetChild(i).gameObject);

        float width = _links.sizeDelta.x;
        float x = 0f;
        float y = 0f;
        List<int> allIds = new List<int>();

        if (data.categories != null)
        {
            foreach (CategoryVO category in data.categories)
            {
                if (category == null)
                    continue;

                int id = category.id;
                allIds.Add(id);

                RectTransform link = BuildLink(_links, TimelineUi.Localized(category.name), categoryFontSize,
                    () => OpenTimeline(new[] { id }));

                float linkWidth = link.sizeDelta.x;
                if (x > 0f && x + linkWidth > width)
                {
                    x = 0f;
                    y += categoryRowHeight;
                }

                TimelineUi.TopLeft(link, x, y, linkWidth, link.sizeDelta.y);
                x += linkWidth + categoryGap;
            }
        }

        int[] everything = allIds.ToArray();
        RectTransform all = BuildLink(_links, TimelineUi.Translate(AllCategoriesKey), allCategoriesFontSize,
            () => OpenTimeline(everything));
        // Centred on the screen horizontally, at its own design height.
        float areaLeft = categoryArea != null ? 0f : categoryAreaPosition.x;
        float screenCentre = categoryArea != null ? width * 0.5f : 960f - areaLeft;
        float allTop = categoryArea != null ? y + categoryRowHeight * 2f : allCategoriesY - categoryAreaPosition.y;
        TimelineUi.TopLeft(all, screenCentre - all.sizeDelta.x * 0.5f, allTop, all.sizeDelta.x, all.sizeDelta.y);
    }

    /// <summary>A text link with an up-right arrow, sized to its text. Returns it unpositioned.</summary>
    private RectTransform BuildLink(Transform parent, string label, float fontSize, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform link = TimelineUi.Rect(parent, "Link");

        TextMeshProUGUI text = TimelineUi.MakeText(link, "Label", style.boldFont, fontSize, style.textColor,
            TextAlignmentOptions.MidlineLeft);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = label ?? string.Empty;
        Vector2 size = text.GetPreferredValues(text.text);
        float height = Mathf.Max(size.y, arrowSize) + 16f;
        TimelineUi.TopLeft(text.rectTransform, 0f, 0f, size.x, height);

        float linkWidth = size.x;
        if (style.arrowSprite != null)
        {
            Image arrow = TimelineUi.MakeImage(link, "Arrow", style.arrowSprite, style.textColor, false);
            arrow.preserveAspect = true;
            TimelineUi.TopLeft(arrow.rectTransform, size.x + arrowGap, (height - arrowSize) * 0.5f, arrowSize, arrowSize);
            // The arrow sprite points left; turned 135 degrees clockwise it points up and to the right.
            arrow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            arrow.rectTransform.anchoredPosition += new Vector2(arrowSize * 0.5f, -arrowSize * 0.5f);
            arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, -135f);
            linkWidth += arrowGap + arrowSize;
        }

        // Transparent hit area over the whole link, so the touch target is not just the glyphs.
        Image hit = link.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        Button button = link.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.onClick.AddListener(onClick);

        link.sizeDelta = new Vector2(linkWidth, height);
        return link;
    }
}
