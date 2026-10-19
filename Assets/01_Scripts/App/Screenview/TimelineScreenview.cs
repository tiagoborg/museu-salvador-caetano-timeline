using System.Collections.Generic;
using EnigmaVirtual;
using EnigmaVirtual.Screens;
using EnigmaVirtual.Screens.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The horizontal timeline. Every decade from 1920 to the last one in the complete content is on
/// the axis; decades with no event in an active category collapse to a narrow strip.
/// </summary>
/// <remarks>
/// All of the screen's UI is built in code on the first <see cref="Show"/>, from the style and
/// geometry fields below, so the scene object needs only this component, a CanvasGroup and a
/// RectTransform stretched over the canvas. Navigation data is an <c>int[]</c> of the category ids to
/// start with; anything else starts with every category on.
/// </remarks>
public class TimelineScreenview : ScreenView
{
    private const string ChronologyKey = "chronology";

    [SerializeField]
    private TimelineStyle style = new TimelineStyle();
    [SerializeField]
    private TimelineCardMetrics card = new TimelineCardMetrics();

    [Header("Axis (reference pixels from the top-left of the screen)")]
    [SerializeField]
    private float contentLeft = 60f;
    [SerializeField]
    private float contentRightPadding = 200f;
    [SerializeField]
    private float firstRowTop = 104f;
    [SerializeField]
    private float rowPitch = 398f;
    [SerializeField]
    private float axisY = 860f;
    [SerializeField]
    private float scrollAreaHeight = 960f;
    [SerializeField]
    private float lineWidth = 2f;
    [SerializeField]
    private float markerSize = 20f;
    [SerializeField]
    private float decadeLabelFontSize = 30f;

    [Header("Decades")]
    [SerializeField]
    private float emptyDecadeWidth = 50f;
    [SerializeField]
    private float decadePaddingLeft = 20f;
    [SerializeField]
    private float columnGap = 18f;
    [SerializeField]
    private float decadePaddingRight = 18f;

    [Header("Header")]
    [SerializeField]
    private float headerY = 60f;
    [SerializeField]
    private float headerFontSize = 16f;
    [SerializeField, Tooltip("Decorative scroll hint, top right. Not a button. When empty, it is drawn as two "
        + "copies of the style's arrow sprite pointing apart.")]
    private Sprite scrollHintSprite;
    [SerializeField]
    private Vector2 scrollHintSize = new Vector2(56f, 24f);

    [Header("Footer category filter")]
    [SerializeField]
    private float footerY = 1012f;
    [SerializeField]
    private float footerFontSize = 13f;
    [SerializeField]
    private float footerSpacing = 14f;
    [SerializeField, Range(0f, 1f), Tooltip("Opacity of a switched-off category in the footer. Selected ones "
        + "are fully opaque; both keep the accent colour.")]
    private float footerUnselectedAlpha = 0.35f;

    [Header("Images")]
    [SerializeField, Range(128, 256), Tooltip("Decoded texture budget. Off-screen images are released first; "
        + "images on screen are never dropped, and exceeding the budget with them alone is logged.")]
    private int textureBudgetMiB = 256;
    [SerializeField, Min(0f), Tooltip("Cards this far outside the viewport (reference px) already load, so "
        + "images are ready when they scroll in.")]
    private float visibilityMargin = 480f;

    [Header("Back button - built at the Home button's position when left empty")]
    [SerializeField]
    private Button backButton;

    private readonly HashSet<int> _activeCategories = new HashSet<int>();
    private readonly Dictionary<int, TextMeshProUGUI> _footerLabels = new Dictionary<int, TextMeshProUGUI>();
    private int[] _pendingCategories;
    private bool _built;
    private bool _listening;
    // Installed while shown, alongside the listeners; null when no event system was found.
    private SingleTouchInput _singleTouch;
    private bool _contentValidated;
    private readonly HashSet<string> _reportedOverflow = new HashSet<string>();

    private ScrollRect _scroll;
    private RectTransform _content;
    private RectTransform _lines;
    private RectTransform _cards;
    private RectTransform _footer;
    private TextMeshProUGUI _header;
    private TextMeshProUGUI _measure;

    private TimelineTextureCache _textureCache;
    private float _nextReconcile;
    /// <summary>
    /// One laid-out event. The layout covers every event; a card view exists only while the event is in
    /// view (plus the margin), and goes back to the pool when it leaves.
    /// </summary>
    private sealed class EventSlot
    {
        public TimelineItemVO item;
        public string categoryName;
        public List<string> gallery;
        public float x, y, fontSize;
        public bool wide, imageOnTop;
        // The slide on screen when the view last left, so re-entry resumes it; -1 means the event's own.
        public int retainedSlide = -1;
        public TimelineCard view;
        public float Right(TimelineCardMetrics m) { return x + (wide ? m.wideWidth : m.narrowWidth); }
    }

    private readonly List<EventSlot> _slots = new List<EventSlot>();
    // Released views, inactive and holding no texture, by geometry kind: (wide, image on top).
    private readonly List<TimelineCard>[] _pool = { new List<TimelineCard>(), new List<TimelineCard>(),
        new List<TimelineCard>(), new List<TimelineCard>() };

    /// <summary>For measurement tools.</summary>
    public TimelineTextureCache TextureCache { get { return _textureCache; } }
    public int LayoutEventCount { get { return _slots.Count; } }

    public int ActiveCardCount
    {
        get
        {
            int n = 0;
            foreach (EventSlot slot in _slots)
                if (slot.view != null) n++;
            return n;
        }
    }

    public int PooledCardCount
    {
        get
        {
            int n = 0;
            foreach (List<TimelineCard> pool in _pool)
                n += pool.Count;
            return n;
        }
    }

    public override void SetData<T>(T data)
    {
        _pendingCategories = data as int[];
    }

    public override void Show()
    {
        if (!_built)
            BuildChrome();

        // A navigation without data starts with everything on; Home always sends data.
        _activeCategories.Clear();
        if (_pendingCategories != null)
            _activeCategories.UnionWith(_pendingCategories);
        else
            _activeCategories.UnionWith(AllCategoryIds());
        _pendingCategories = null;

        if (!_listening)
        {
            backButton.onClick.AddListener(OnBackButtonClicked);
            Core.LanguageChangedEvent += OnLanguageChanged;
            // Simultaneous touches are not supported here: only the first finger reaches the UI.
            _singleTouch = SingleTouchInput.Install();
            _listening = true;
        }

        RefreshLabels();

        // Active before the rebuild, not after: cards measure their tag text with TMP when bound, which
        // needs an active hierarchy.
        _cards.gameObject.SetActive(true);
        Rebuild(true);
        base.Show();
    }

    public override void Hide()
    {
        StopListening();

        // Hide only fades the screen, so without this every card's slideshow kept running - and loading
        // textures - while invisible. Releasing first cancels queued loads and frees every lease.
        ReleaseCards();
        if (_cards != null)
            _cards.gameObject.SetActive(false);

        base.Hide();
    }

    private void OnDisable()
    {
        // Deactivated without Hide: cancel every request and free every lease. On reactivation LateUpdate
        // marks the cards in view again; Show re-adds the listeners.
        StopListening();
        ReleaseCards(false);
    }

    private void OnDestroy()
    {
        StopListening();

        // Consumers first, so no RawImage still shows a texture the cache is about to destroy.
        ReleaseCards(false);
        _textureCache?.Dispose();

        // Generated in BuildChrome, so this screen owns them.
        TimelineUi.DestroySprite(style.discSprite);
        TimelineUi.DestroySprite(style.ringSprite);
        style.discSprite = style.ringSprite = null;
    }

    private void StopListening()
    {
        if (!_listening)
            return;

        if (backButton != null)
            backButton.onClick.RemoveListener(OnBackButtonClicked);
        Core.LanguageChangedEvent -= OnLanguageChanged;
        if (_singleTouch != null)
            _singleTouch.Uninstall();
        _singleTouch = null;
        _listening = false;
    }

    private void OnBackButtonClicked()
    {
        Controller.NavigateTo(new NavigationItem(ScreenType.Home));
    }

    private void OnLanguageChanged()
    {
        // Card widths depend on the rendered text, so a language change is a relayout.
        RefreshLabels();
        Rebuild(false);
    }

    private static IEnumerable<int> AllCategoryIds()
    {
        AppData data = AppData.Current;
        if (data == null || data.categories == null)
            yield break;

        foreach (CategoryVO category in data.categories)
        {
            if (category != null)
                yield return category.id;
        }
    }

    // ---------------------------------------------------------------- static chrome

    private void BuildChrome()
    {
        TimelineUi.ReportMissingStyle(this, style);

        // Circles for the slide dots and the axis markers. No circle sprite ships with the project.
        style.discSprite = TimelineUi.MakeCircleSprite(64, 0f);
        // Both colours baked in, and the marker drawn untinted, so the interior is exactly the background.
        style.ringSprite = TimelineUi.MakeCircleSprite(64, 0.2f, style.backgroundColor, style.lineColor);
        // Loads run as coroutines on the persistent MainController, not on this screen: deactivating the
        // screen would stop them mid-flight, and a stopped load never frees its slot. This screen is the
        // host only where no Core is running (isolated editor checks).
        MonoBehaviour loadHost = Core.main != null ? (MonoBehaviour)Core.main : this;
        _textureCache = new TimelineTextureCache(loadHost, textureBudgetMiB * 1024L * 1024L);

        // Scroll area across the full width, from the top of the screen to below the decade labels.
        RectTransform scrollRt = TimelineUi.Rect(transform, "Timeline Scroll");
        scrollRt.anchorMin = new Vector2(0f, 1f);
        scrollRt.anchorMax = new Vector2(1f, 1f);
        scrollRt.pivot = new Vector2(0f, 1f);
        scrollRt.anchoredPosition = Vector2.zero;
        scrollRt.sizeDelta = new Vector2(0f, scrollAreaHeight);

        // Transparent but raycastable, so a drag on empty space pans the timeline.
        Image viewport = TimelineUi.MakeImage(scrollRt, "Viewport", null, new Color(0f, 0f, 0f, 0f), true);
        TimelineUi.Stretch(viewport.rectTransform);
        viewport.gameObject.AddComponent<RectMask2D>();

        _content = TimelineUi.Rect(viewport.transform, "Content");
        TimelineUi.TopLeft(_content, 0f, 0f, 0f, scrollAreaHeight);
        _lines = TimelineUi.Rect(_content, "Lines");
        TimelineUi.TopLeft(_lines, 0f, 0f, 0f, 0f);
        _cards = TimelineUi.Rect(_content, "Cards");
        TimelineUi.TopLeft(_cards, 0f, 0f, 0f, 0f);

        _scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        _scroll.viewport = viewport.rectTransform;
        _scroll.content = _content;
        _scroll.horizontal = true;
        _scroll.vertical = false;
        _scroll.movementType = ScrollRect.MovementType.Elastic;
        _scroll.inertia = true;

        // Header: a short rule from the left edge, then the title. Drawn over the scroll area.
        Image rule = TimelineUi.MakeImage(transform, "Header Rule", null, style.lineColor, false);
        TimelineUi.TopLeft(rule.rectTransform, 0f, headerY - lineWidth * 0.5f, 110f, lineWidth);
        _header = TimelineUi.MakeText(transform, "Header", style.boldFont, headerFontSize, style.textColor,
            TextAlignmentOptions.MidlineLeft);
        TimelineUi.TopLeft(_header.rectTransform, 130f, headerY - 20f, 400f, 40f);
        _header.characterSpacing = 8f;

        BuildScrollHint();

        // Footer: a centred row of category toggles.
        _footer = TimelineUi.Rect(transform, "Footer");
        _footer.anchorMin = _footer.anchorMax = new Vector2(0.5f, 1f);
        _footer.pivot = new Vector2(0.5f, 0.5f);
        _footer.anchoredPosition = new Vector2(0f, -footerY);
        HorizontalLayoutGroup row = _footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = footerSpacing;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        ContentSizeFitter fitter = _footer.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        BuildFooter();

        // A button outside this screen fades out with its own screen and is never seen here.
        if (backButton != null && !backButton.transform.IsChildOf(transform))
        {
            Development.LogError($"{name}: backButton [{backButton.name}] is not part of this screen, so it "
                + "would be hidden with whichever screen owns it. Building this screen's own back button instead.");
            backButton = null;
        }

        if (backButton == null)
            backButton = BuildBackButton();

        // Measures text with the same font and size as a card, without being part of the timeline.
        _measure = TimelineUi.MakeText(transform, "Text Measure", style.regularFont, card.contentFontSize,
            new Color(0f, 0f, 0f, 0f), TextAlignmentOptions.TopLeft);
        TimelineUi.TopLeft(_measure.rectTransform, 0f, 0f, 1f, 1f);

        _built = true;
    }

    /// <summary>The decorative double arrow, top right. Not interactive.</summary>
    private void BuildScrollHint()
    {
        RectTransform hint = TimelineUi.Rect(transform, "Scroll Hint");
        hint.anchorMin = hint.anchorMax = hint.pivot = new Vector2(1f, 1f);
        hint.anchoredPosition = new Vector2(-60f, -(headerY - scrollHintSize.y * 0.5f));
        hint.sizeDelta = scrollHintSize;

        if (scrollHintSprite != null)
        {
            Image image = TimelineUi.MakeImage(hint, "Image", scrollHintSprite, style.textColor, false);
            image.preserveAspect = true;
            TimelineUi.Stretch(image.rectTransform);
            return;
        }

        if (style.arrowSprite == null)
            return; // ReportMissingStyle has already said so.

        // Two copies of the left-pointing arrow, the right one mirrored, meeting in the middle.
        for (int side = 0; side < 2; side++)
        {
            Image arrow = TimelineUi.MakeImage(hint, side == 0 ? "Left" : "Right", style.arrowSprite, style.textColor, false);
            arrow.preserveAspect = true;
            RectTransform rt = arrow.rectTransform;
            rt.anchorMin = new Vector2(side * 0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f + side * 0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            if (side == 1)
                rt.localScale = new Vector3(-1f, 1f, 1f);
        }
    }

    private void BuildFooter()
    {
        AppData data = AppData.Current;
        if (data == null || data.categories == null)
            return;

        bool first = true;
        foreach (CategoryVO category in data.categories)
        {
            if (category == null)
                continue;

            if (!first)
            {
                TextMeshProUGUI separator = TimelineUi.MakeText(_footer, "Separator", style.boldFont, footerFontSize,
                    style.textColor, TextAlignmentOptions.Center);
                separator.text = "/";
                separator.color = "#1FABE3".ToUnityColor();
            }
            first = false;

            int id = category.id;
            TextMeshProUGUI label = TimelineUi.MakeText(_footer, "Category " + id, style.boldFont, footerFontSize,
                style.accentColor, TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.characterSpacing = 8f;
            // A finger-sized hit area around small text.
            label.raycastTarget = true;
            label.raycastPadding = new Vector4(-8f, -24f, -8f, -24f);
            label.color = "#1FABE3".ToUnityColor();

            Button toggle = label.gameObject.AddComponent<Button>();
            toggle.transition = Selectable.Transition.ColorTint;
            toggle.onClick.AddListener(() => OnFooterToggle(id));

            _footerLabels[id] = label;
        }
    }

    private Button BuildBackButton()
    {
        // Transparent hit area, enlarged beyond the 54x50 visual for touch. The outline is four plain
        // 1 px edges rather than the sliced frame sprite, which did not render on this runtime-built button.
        Image frame = TimelineUi.MakeImage(transform, "Back Button", null, new Color(0f, 0f, 0f, 0f), true);
        frame.raycastPadding = new Vector4(-24f, -24f, -24f, -24f);
        RectTransform rt = frame.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(111f, 60f);
        rt.sizeDelta = new Vector2(54f, 50f);

        for (int edge = 0; edge < 4; edge++)
        {
            Image line = TimelineUi.MakeImage(frame.transform, "Edge", null, style.textColor, false);
            RectTransform lineRt = line.rectTransform;
            bool horizontal = edge < 2;
            float side = edge % 2; // 0 = bottom/left, 1 = top/right
            lineRt.anchorMin = horizontal ? new Vector2(0f, side) : new Vector2(side, 0f);
            lineRt.anchorMax = horizontal ? new Vector2(1f, side) : new Vector2(side, 1f);
            lineRt.pivot = horizontal ? new Vector2(0.5f, side) : new Vector2(side, 0.5f);
            lineRt.anchoredPosition = Vector2.zero;
            lineRt.sizeDelta = horizontal ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
        }

        if (style.arrowSprite != null)
        {
            Image arrow = TimelineUi.MakeImage(frame.transform, "Arrow", style.arrowSprite, style.textColor, false);
            arrow.preserveAspect = true;
            RectTransform arrowRt = arrow.rectTransform;
            arrowRt.anchorMin = arrowRt.anchorMax = arrowRt.pivot = new Vector2(0.5f, 0.5f);
            arrowRt.sizeDelta = new Vector2(20f, 20f);
        }

        Button button = frame.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = frame;
        return button;
    }

    private void OnFooterToggle(int categoryId)
    {
        // Switching the last category off is allowed: every decade collapses and the footer stays usable.
        if (!_activeCategories.Remove(categoryId))
            _activeCategories.Add(categoryId);

        RefreshLabels();
        Rebuild(true);
    }

    private void RefreshLabels()
    {
        if (_header != null)
            _header.text = TimelineUi.Translate(ChronologyKey).ToUpperInvariant();

        Dictionary<int, CategoryVO> categories = CategoriesById();
        foreach (KeyValuePair<int, TextMeshProUGUI> pair in _footerLabels)
        {
            string categoryName = categories.TryGetValue(pair.Key, out CategoryVO category)
                ? TimelineUi.Localized(category.name) : string.Empty;
            pair.Value.text = categoryName.ToUpperInvariant();
            // Off is shown by opacity only, never by a line through or over the name.
            Color color = style.accentColor;
            color.a = _activeCategories.Contains(pair.Key) ? 1f : footerUnselectedAlpha;
            pair.Value.color = color;
            pair.Value.fontStyle = FontStyles.Normal;
        }
    }

    private static Dictionary<int, CategoryVO> CategoriesById()
    {
        Dictionary<int, CategoryVO> result = new Dictionary<int, CategoryVO>();
        AppData data = AppData.Current;
        if (data == null || data.categories == null)
            return result;

        foreach (CategoryVO category in data.categories)
        {
            if (category != null)
                result[category.id] = category;
        }

        return result;
    }

    // ---------------------------------------------------------------- timeline

    private void Rebuild(bool resetScroll)
    {
        AppData data = AppData.Current;
        if (data == null)
            return; // AppData.Current reports the misconfiguration itself.

        // Card views go back to the pool - released, so no lease or queued load survives - and are reused,
        // never destroyed here. The lines are a few dozen plain objects and are rebuilt.
        ReleaseCards();
        _slots.Clear();
        for (int i = _lines.childCount - 1; i >= 0; i--)
            Destroy(_lines.GetChild(i).gameObject);

        TimelineItemVO[] all = data.timeline ?? new TimelineItemVO[0];
        Dictionary<int, CategoryVO> categories = CategoriesById();
        int lastDecade = TimelineLayout.LastDecade(all);

        // Content does not change after boot, so it is checked once rather than on every relayout.
        if (!_contentValidated)
        {
            ReportHiddenEvents(all, categories);
            _contentValidated = true;
        }

        // Horizontal axis through the marker centres. First child of Lines, so the markers (whose opaque
        // interior hides it inside each ring), vertical lines and cards all draw over it. Its width is set
        // once the content width is known, below.
        Image baseline = TimelineUi.MakeImage(_lines, "Baseline", null, style.lineColor, false);

        float x = contentLeft;
        List<float> columnWidths = new List<float>();

        // Every event is laid out (its text is measured for the card width); only those in view get a card.
        for (int decade = TimelineLayout.FirstDecade; decade <= lastDecade; decade += 10)
        {
            DrawBoundary(x, decade);

            List<TimelineItemVO> items = TimelineLayout.ItemsInDecade(all, decade, _activeCategories);
            List<TimelineLayout.CardFit> fits = new List<TimelineLayout.CardFit>(items.Count);
            columnWidths.Clear();

            for (int i = 0; i < items.Count; i++)
            {
                TimelineLayout.CardFit fit = Fit(items[i]);
                fits.Add(fit);

                float width = fit.wide ? card.wideWidth : card.narrowWidth;
                int column = TimelineLayout.ColumnOf(i);
                if (column == columnWidths.Count)
                    columnWidths.Add(width);
                else
                    columnWidths[column] = Mathf.Max(columnWidths[column], width);
            }

            float columnX = x + decadePaddingLeft;
            for (int column = 0, i = 0; column < columnWidths.Count; column++)
            {
                for (; i < items.Count && TimelineLayout.ColumnOf(i) == column; i++)
                {
                    TimelineItemVO item = items[i];
                    float y = firstRowTop + TimelineLayout.RowOf(i) * rowPitch;

                    _slots.Add(new EventSlot
                    {
                        item = item,
                        categoryName = categories.TryGetValue(item.categoryId, out CategoryVO category)
                            ? TimelineUi.Localized(category.name) : string.Empty,
                        gallery = TimelineLayout.Gallery(item),
                        x = columnX,
                        y = y,
                        fontSize = fits[i].fontSize,
                        wide = fits[i].wide,
                        imageOnTop = TimelineLayout.ImageOnTop(column),
                    });
                }

                columnX += columnWidths[column] + columnGap;
            }

            x += TimelineLayout.DecadeWidth(columnWidths, emptyDecadeWidth, decadePaddingLeft, columnGap,
                decadePaddingRight);
        }

        // Closing reference line, labelled with the next decade. It has no strip of its own.
        DrawBoundary(x, lastDecade + 10);

        _content.sizeDelta = new Vector2(x + contentRightPadding, scrollAreaHeight);

        // From the content's left edge to its end, and at least across the viewport, so the axis still
        // reaches the right of the screen when every decade is collapsed.
        float baselineWidth = Mathf.Max(_content.sizeDelta.x, _scroll.viewport.rect.width);
        TimelineUi.TopLeft(baseline.rectTransform, 0f, axisY - lineWidth * 0.5f, baselineWidth, lineWidth);

        if (resetScroll)
        {
            _scroll.StopMovement();
            _content.anchoredPosition = Vector2.zero;
        }

        UpdateVisibility();
    }

    private void LateUpdate()
    {
        // Hidden: Hide released the cards and they stay out of view until the next Show rebuilds them.
        if (_cards != null && _cards.gameObject.activeSelf && _slots.Count > 0)
            UpdateVisibility();

        // Also while hidden but active: the retained unpinned textures must fit the budget too. A texture's
        // measured size can grow after it loads, so re-measure at 1 Hz, not every frame. An inactive screen
        // gets no LateUpdate; its entries keep their sizes and are reconciled once it is active again.
        if (Time.unscaledTime >= _nextReconcile)
        {
            _nextReconcile = Time.unscaledTime + 1f;
            _textureCache?.ReconcileBytes();
        }
    }

    /// <summary>
    /// Gives a card to every event in view - with <c>visibilityMargin</c> on both sides as a preload zone -
    /// and takes it back from every event that left, from the layout spans and the scroll offset. Leaving
    /// views are returned first, so the arriving ones reuse them in the same frame and the pool never
    /// grows past the most cards of one kind ever in view together. No allocation once the pool is warm.
    /// </summary>
    private void UpdateVisibility()
    {
        float offset = _content.anchoredPosition.x;
        float left = -visibilityMargin;
        float right = _scroll.viewport.rect.width + visibilityMargin;

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (EventSlot slot in _slots)
            {
                bool visible = slot.Right(card) + offset > left && slot.x + offset < right;
                if (pass == 0 && !visible && slot.view != null)
                    ReturnView(slot);
                else if (pass == 1 && visible)
                {
                    if (slot.view == null)
                        TakeView(slot);
                    else
                        slot.view.SetVisible(true); // no-op unless released in place by OnDisable
                }
            }
        }
    }

    private static int Kind(bool wide, bool imageOnTop)
    {
        return (wide ? 2 : 0) + (imageOnTop ? 1 : 0);
    }

    private void TakeView(EventSlot slot)
    {
        List<TimelineCard> pool = _pool[Kind(slot.wide, slot.imageOnTop)];
        TimelineCard view = null;
        while (view == null && pool.Count > 0)
        {
            view = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
        }

        if (view == null)
            view = TimelineCard.Create(_cards, style, card, _textureCache, slot.x, slot.y, slot.wide, slot.imageOnTop);

        // Active before Bind: the tag is measured with TMP. Bind releases anything the view still held.
        view.gameObject.SetActive(true);
        view.MoveTo(slot.x, slot.y);
        view.Bind(slot.item, slot.categoryName, slot.gallery, slot.fontSize, slot.retainedSlide);
        view.SetVisible(true);
        slot.view = view;
    }

    private void ReturnView(EventSlot slot)
    {
        TimelineCard view = slot.view;
        slot.view = null;
        if (view == null)
            return; // destroyed with the scene

        slot.retainedSlide = view.RetainedSlide;
        view.Release();
        view.gameObject.SetActive(false);
        _pool[Kind(view.Wide, view.ImageOnTop)].Add(view);
    }

    /// <summary>
    /// Cancels every view's requests, clears its images and releases its leases. Idempotent. With
    /// <paramref name="toPool"/> the views also go back to the pool. Without it they stay assigned and
    /// active - Unity refuses SetActive while the screen itself is being deactivated or destroyed - and
    /// UpdateVisibility makes them load again on reactivation.
    /// </summary>
    private void ReleaseCards(bool toPool = true)
    {
        foreach (EventSlot slot in _slots)
        {
            if (slot.view == null)
                continue;
            if (toPool)
            {
                ReturnView(slot);
                continue;
            }

            slot.retainedSlide = slot.view.RetainedSlide;
            slot.view.Release();
        }
    }

    private TimelineLayout.CardFit Fit(TimelineItemVO item)
    {
        string text = TimelineUi.Localized(item.content);

        TimelineLayout.CardFit fit = TimelineLayout.FitCard(
            (width, size) =>
            {
                _measure.fontSize = size;
                return _measure.GetPreferredValues(text, width, 0f).y;
            },
            card.TextWidth(false), card.TextWidth(true), card.textAreaHeight,
            card.contentFontSize, card.minContentFontSize, 0.5f);

        _measure.fontSize = card.contentFontSize;

        // LogError, which is compiled on every target: there is no detail view, so text cut by the ellipsis
        // cannot be read anywhere else. Once per event and language, not on every relayout.
        string language = Core.IsSet ? Core.localizationSettings.currentLanguage.ToString() : string.Empty;
        if (!fit.fits && _reportedOverflow.Add(item.id + "/" + language))
            Development.LogError($"Timeline: the {language} text of event {item.id} ({item.year}) does not fit a "
                + $"wide card even at {card.minContentFontSize}pt, so it ends in an ellipsis and the rest cannot "
                + "be read anywhere. Shorten it in appData.json.");

        return fit;
    }

    /// <summary>
    /// Reports every event the timeline can never show, whatever the filter: an unknown category is
    /// in no filter, not even "all", and a year before the axis has no decade to sit in.
    /// LogError, which is compiled on every target, because otherwise the event just disappears.
    /// </summary>
    private static void ReportHiddenEvents(TimelineItemVO[] all, Dictionary<int, CategoryVO> categories)
    {
        foreach (TimelineItemVO item in all)
        {
            if (item == null)
            {
                Development.LogError("Timeline: appData.json has a null entry in timeline; it is skipped.");
                continue;
            }

            if (!categories.ContainsKey(item.categoryId))
                Development.LogError($"Timeline: event {item.id} ({item.year}) has categoryId {item.categoryId}, "
                    + "which is not in categories, so it is never shown - not even with all categories on.");

            if (!TimelineLayout.IsSupportedYear(item.year))
                Development.LogError($"Timeline: event {item.id} has year {item.year}, outside the supported "
                    + $"range {TimelineLayout.FirstDecade}-{TimelineLayout.LastSupportedYear}, so it is never shown.");

            if (item.images == null)
                continue;

            foreach (MediaVO image in item.images)
            {
                string url = image != null ? image.url : null;
                if (!string.IsNullOrEmpty(url) && !TimelineLayout.IsImageUrl(url))
                    Development.LogError($"Timeline: event {item.id} ({item.year}) lists \"{url}\", which is not a "
                        + "supported image type (png, jpg, jpeg), so it is skipped and never loaded.");
            }
        }
    }

    private void DrawBoundary(float x, int decade)
    {
        // The vertical line stops at the top of the ring; the ring's opaque interior hides the baseline
        // inside it, so neither line crosses the marker.
        Image line = TimelineUi.MakeImage(_lines, "Line " + decade, null, style.lineColor, false);
        TimelineUi.TopLeft(line.rectTransform, x - lineWidth * 0.5f, 0f, lineWidth, axisY - markerSize * 0.5f);

        Image marker = TimelineUi.MakeImage(_lines, "Marker " + decade, style.ringSprite, Color.white, false);
        TimelineUi.TopLeft(marker.rectTransform, x - markerSize * 0.5f, axisY - markerSize * 0.5f, markerSize,
            markerSize);

        // "19" over "20", left edge just left of the line, as in the design.
        string year = decade.ToString();
        TextMeshProUGUI label = TimelineUi.MakeText(_lines, "Label " + decade, style.boldFont, decadeLabelFontSize,
            style.textColor, TextAlignmentOptions.TopLeft);
        TimelineUi.TopLeft(label.rectTransform, x - 10f, axisY + markerSize, 80f, decadeLabelFontSize * 2.4f);
        label.lineSpacing = -20f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        // Split only a four-digit year; the closing label after 9990 is 10000 and stays whole.
        label.text = year.Length == 4 ? year.Substring(0, 2) + "\n" + year.Substring(2) : year;
    }
}
