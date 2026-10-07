using System;
using System.Collections.Generic;
using EnigmaVirtual;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Card geometry in reference pixels (1920x1080). Every card has the same total height.</summary>
[Serializable]
public class TimelineCardMetrics
{
    [Header("Widths - the only two sizes a card can have")]
    public float narrowWidth = 260f;
    public float wideWidth = 380f;

    [Header("Category tag above the card")]
    [Tooltip("Minimum tag width. The tag grows with the category name up to 90% of the card's width.")]
    public float tagWidth = 160f;
    public float tagHeight = 30f;
    public float tagGap = 10f;
    public float tagFontSize = 11f;

    [Header("Card body - the image takes whatever height the text section leaves")]
    public float cardHeight = 340f;
    public float padding = 14f;
    public float yearHeight = 26f;
    public float yearFontSize = 19f;
    [Tooltip("Height the event text must fit in. Decides narrow vs wide.")]
    public float textAreaHeight = 118f;
    public float contentFontSize = 14f;
    [Tooltip("Wide cards may shrink their text down to this size before the text is reported as not fitting.")]
    public float minContentFontSize = 11f;
    public float dotsHeight = 22f;
    public float dotSize = 6f;
    public float dotSpacing = 6f;

    [Header("Image swipe")]
    [Tooltip("Horizontal travel, in reference pixels, a drag on the image needs to change slide.")]
    public float swipeThreshold = 60f;

    public float SlotHeight { get { return tagHeight + tagGap + cardHeight; } }
    public float TextSectionHeight { get { return padding * 2f + yearHeight + textAreaHeight + dotsHeight; } }
    public float ImageHeight { get { return Mathf.Max(0f, cardHeight - TextSectionHeight); } }
    public float TextWidth(bool wide) { return (wide ? wideWidth : narrowWidth) - padding * 2f; }
}

/// <summary>
/// One timeline event: category tag, year, text, and the category's image gallery.
/// Built entirely in code by <see cref="Create"/>; there is no prefab.
/// </summary>
public class TimelineCard : MonoBehaviour
{
    /// <summary>The category tag never grows past this share of the card's width.</summary>
    private const float MaxTagWidthFraction = 0.9f;

    private TimelineStyle _style;
    private TimelineCardMetrics _metrics;
    private float _width;

    private RectTransform _tagRect;
    private TextMeshProUGUI _tagText;
    private TextMeshProUGUI _yearText;
    private TextMeshProUGUI _contentText;
    private RawImage _photo;
    private AspectRatioFitter _photoFitter;
    private RectTransform _dotsRoot;
    private readonly List<Image> _dots = new List<Image>();
    private TimelineImageSwipe _swipe;

    private List<string> _gallery;
    private int _slide;

    public int Slide { get { return _slide; } }

    public static TimelineCard Create(Transform parent, TimelineStyle style, TimelineCardMetrics metrics,
        float x, float y, bool wide, bool imageOnTop)
    {
        float width = wide ? metrics.wideWidth : metrics.narrowWidth;

        RectTransform root = TimelineUi.Rect(parent, "Card");
        TimelineUi.TopLeft(root, x, y, width, metrics.SlotHeight);

        TimelineCard card = root.gameObject.AddComponent<TimelineCard>();
        card._style = style;
        card._metrics = metrics;
        card.Build(width, imageOnTop);
        return card;
    }

    private void Build(float width, bool imageOnTop)
    {
        TimelineCardMetrics m = _metrics;
        _width = width;

        // Solid background fills, so the axis lines behind a tag or text section do not show through.
        // The tag's width here is provisional; Bind sizes it to the category name.
        Image tag = TimelineUi.MakeImage(transform, "Tag", null, _style.backgroundColor, false);
        _tagRect = tag.rectTransform;
        TimelineUi.TopLeft(_tagRect, 0f, 0f, m.tagWidth, m.tagHeight);
        _tagText = TimelineUi.MakeText(tag.transform, "Label", _style.boldFont, m.tagFontSize, _style.textColor,
            TextAlignmentOptions.MidlineLeft);
        TimelineUi.Stretch(_tagText.rectTransform);
        _tagText.margin = new Vector4(10f, 0f, 6f, 0f);
        _tagText.textWrappingMode = TextWrappingModes.NoWrap;
        _tagText.overflowMode = TextOverflowModes.Ellipsis;
        _tagText.characterSpacing = 8f;
        AddFrame(tag.transform);

        // Invisible but a raycast target over the whole card, so a drag outside the photo reaches the
        // timeline's ScrollRect. It has no fill: only the text section is filled, so nothing sits behind
        // the photo.
        Image body = TimelineUi.MakeImage(transform, "Body", null, Color.clear, true);
        TimelineUi.TopLeft(body.rectTransform, 0f, m.tagHeight + m.tagGap, width, m.cardHeight);

        float textTop = imageOnTop ? m.ImageHeight : 0f;
        float imageTop = imageOnTop ? 0f : m.cardHeight - m.ImageHeight;
        float innerWidth = width - m.padding * 2f;

        // The text section: background fill, year, text, dots, and the frame last so it draws on top.
        // The frame surrounds this section only, never the photo.
        Image textSection = TimelineUi.MakeImage(body.transform, "Text", null, _style.backgroundColor, false);
        TimelineUi.TopLeft(textSection.rectTransform, 0f, textTop, width, m.TextSectionHeight);

        _yearText = TimelineUi.MakeText(textSection.transform, "Year", _style.boldFont, m.yearFontSize,
            _style.textColor, TextAlignmentOptions.TopLeft);
        TimelineUi.TopLeft(_yearText.rectTransform, m.padding, m.padding, innerWidth, m.yearHeight);

        _contentText = TimelineUi.MakeText(textSection.transform, "Content", _style.regularFont, m.contentFontSize,
            _style.textColor, TextAlignmentOptions.TopLeft);
        TimelineUi.TopLeft(_contentText.rectTransform, m.padding, m.padding + m.yearHeight, innerWidth,
            m.textAreaHeight);
        // Ellipsis rather than silent clipping: if text ever exceeds even the wide card, it shows.
        _contentText.overflowMode = TextOverflowModes.Ellipsis;

        _dotsRoot = TimelineUi.Rect(textSection.transform, "Dots");
        TimelineUi.TopLeft(_dotsRoot, m.padding, m.padding + m.yearHeight + m.textAreaHeight, innerWidth,
            m.dotsHeight);

        AddFrame(textSection.transform);

        // The photo area: a transparent mask and a RawImage fitted to the area's full height at the
        // source aspect, left aligned. A wide image is clipped on the right; a narrow one leaves the area
        // empty on the right, showing whatever is behind the card. Never centred, never cropped
        // vertically. The transparent Image is the raycast target that delivers swipes.
        Image photoArea = TimelineUi.MakeImage(body.transform, "Photo", null, Color.clear, true);
        TimelineUi.TopLeft(photoArea.rectTransform, 0f, imageTop, width, m.ImageHeight);
        photoArea.gameObject.AddComponent<RectMask2D>();

        _photo = TimelineUi.Rect(photoArea.transform, "Texture").gameObject.AddComponent<RawImage>();
        // Stretched vertically and pinned to the left edge; the fitter sets only the width, from the
        // height, so it follows the holder if the holder is resized.
        RectTransform photoRt = _photo.rectTransform;
        photoRt.anchorMin = new Vector2(0f, 0f);
        photoRt.anchorMax = new Vector2(0f, 1f);
        photoRt.pivot = new Vector2(0f, 0.5f);
        photoRt.anchoredPosition = Vector2.zero;
        photoRt.sizeDelta = Vector2.zero;
        _photo.raycastTarget = false;
        _photo.enabled = false;
        _photoFitter = _photo.gameObject.AddComponent<AspectRatioFitter>();
        _photoFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;

        _swipe = photoArea.gameObject.AddComponent<TimelineImageSwipe>();
        _swipe.threshold = m.swipeThreshold;
        _swipe.onSwipe = OnSwipe;
    }

    /// <summary>
    /// The frame outline over the whole of <paramref name="parent"/>. Must be its last child to draw on
    /// top. Not a raycast target, so drags still reach the body (pan) below it, and no centre fill, so it
    /// never hides what it frames.
    /// </summary>
    private void AddFrame(Transform parent)
    {
        // Only a sliced sprite honours fillCenter = false. Without borders the Image would be Simple and
        // paint the whole rect over the text, so leave the unframed fill instead.
        if (_style.frameSprite == null || _style.frameSprite.border == Vector4.zero)
            return;

        Image frame = TimelineUi.MakeImage(parent, "Frame", _style.frameSprite, _style.frameColor, false,
            _style.framePixelsPerUnitMultiplier);
        frame.fillCenter = false;
        TimelineUi.Stretch(frame.rectTransform);
    }

    /// <param name="gallery">The category's gallery, shared by every card of that category. Not modified.</param>
    public void Bind(TimelineItemVO item, string categoryName, List<string> gallery, float contentFontSize)
    {
        SetTag((categoryName ?? string.Empty).ToUpperInvariant());
        _yearText.text = item.year.ToString();
        _contentText.fontSize = contentFontSize;
        _contentText.text = TimelineUi.Localized(item.content);

        _gallery = gallery ?? new List<string>();
        BuildDots(_gallery.Count);

        // Below two slides there is nothing to cycle, so the image stops claiming drags and they pan
        // the timeline instead.
        _swipe.enabled = _gallery.Count > 1;

        ShowSlide(TimelineLayout.StartSlide(_gallery, item));
    }

    /// <summary>
    /// Sizes the tag to its one-line label: at least <c>tagWidth</c>, at most 90% of the card. A label
    /// wider than that keeps the maximum width and shrinks its font until it fits on one line, so a
    /// category name is never cut.
    /// </summary>
    private void SetTag(string label)
    {
        TimelineCardMetrics m = _metrics;

        // Measured at the design size. TMP's preferred width already includes the margins and the
        // character spacing.
        _tagText.enableAutoSizing = false;
        _tagText.fontSize = m.tagFontSize;
        float preferred = _tagText.GetPreferredValues(label).x;

        float maxWidth = _width * MaxTagWidthFraction;
        float tagWidth = Mathf.Clamp(preferred, Mathf.Min(m.tagWidth, maxWidth), maxWidth);
        _tagRect.sizeDelta = new Vector2(tagWidth, m.tagHeight);

        if (preferred > maxWidth)
        {
            // TMP picks the largest size that fits the width, between these bounds.
            _tagText.fontSizeMax = m.tagFontSize;
            _tagText.fontSizeMin = 1f;
            _tagText.enableAutoSizing = true;
        }

        _tagText.text = label;
    }

    private void BuildDots(int count)
    {
        // One dot is kept, zero hides the row.
        _dotsRoot.gameObject.SetActive(count > 0);

        // Right-aligned as in the design, and shrunk to fit when the category gallery is large, so every
        // indicator stays inside the card.
        TimelineCardMetrics m = _metrics;
        TimelineLayout.DotStrip strip = TimelineLayout.FitDots(count, _dotsRoot.sizeDelta.x, m.dotSize, m.dotSpacing);

        for (int i = 0; i < count; i++)
        {
            Image dot = TimelineUi.MakeImage(_dotsRoot, "Dot", _style.discSprite, _style.dotInactiveColor, false);
            TimelineUi.TopLeft(dot.rectTransform, strip.firstX + i * strip.pitch, (m.dotsHeight - strip.size) * 0.5f,
                strip.size, strip.size);
            _dots.Add(dot);
        }
    }

    private void OnSwipe(int direction)
    {
        ShowSlide(TimelineLayout.Step(_slide, _gallery.Count, direction));
    }

    private void ShowSlide(int index)
    {
        _slide = index;

        for (int i = 0; i < _dots.Count; i++)
            _dots[i].color = i == _slide ? _style.dotActiveColor : _style.dotInactiveColor;

        if (_gallery.Count == 0)
        {
            _photo.enabled = false;
            return;
        }

        TextureCacheManager cache = Core.main != null ? Core.main.TextureCacheManager : null;
        if (cache == null)
        {
            Development.LogError("TimelineCard: Core.main.TextureCacheManager is not available, so no "
                + "timeline image can be shown.");
            return;
        }

        int requested = _slide;
        string url = _gallery[requested];

        // The manager owns and keeps the texture, so swiping back to a slide does not reload it.
        cache.GetOrSetTexture2D(url, texture => OnTexture(requested, url, texture), true);
    }

    private void OnTexture(int requested, string url, Texture2D texture)
    {
        // The card can be destroyed by a rebuild, or swiped on, before the texture arrives.
        if (this == null || requested != _slide)
            return;

        if (texture == null)
        {
            _photo.enabled = false;
            Development.LogWarning($"TimelineCard: no texture for [{url}].");
            return;
        }

        _photo.texture = texture;
        _photoFitter.aspectRatio = texture.height > 0 ? (float)texture.width / texture.height : 1f;
        _photo.enabled = true;
    }
}

/// <summary>
/// Swipe on a card's image. Implementing the drag handlers is what makes the image claim the drag:
/// the EventSystem delivers a drag to the first handler above the pointer, so the timeline's
/// ScrollRect never sees a drag that starts here. Disable this component and drags pass through.
/// </summary>
public class TimelineImageSwipe : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public float threshold = 60f;
    public Action<int> onSwipe;

    private Canvas _canvas;

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData) { }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_canvas == null)
        {
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            _canvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
        }

        // Screen pixels to reference pixels, so the threshold means the same at any resolution.
        float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
        int direction = TimelineLayout.SwipeDirection((eventData.position.x - eventData.pressPosition.x) / scale,
            threshold);

        if (direction != 0 && onSwipe != null)
            onSwipe(direction);
    }
}
