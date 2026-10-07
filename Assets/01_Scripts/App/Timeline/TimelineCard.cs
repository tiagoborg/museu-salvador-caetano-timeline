using System;
using System.Collections.Generic;
using EnigmaVirtual;
using TMPro;
using UnityEngine;
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

    [Header("Image slideshow (unscaled seconds)")]
    [Tooltip("Each card picks its own interval between these two when it is bound, so cards advance out "
        + "of step. Applies only to galleries with two or more images.")]
    public float slideIntervalMin = 4f;
    public float slideIntervalMax = 8f;
    [Tooltip("Crossfade between two images. 0 swaps at once. The next slide waits for a fade to finish.")]
    public float slideFadeDuration = 0.5f;

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
    // Two photo layers for the crossfade; _front is the one showing (or fading in).
    private readonly RawImage[] _layers = new RawImage[2];
    private readonly AspectRatioFitter[] _fitters = new AspectRatioFitter[2];
    private readonly float[] _aspects = new float[2];
    // During a fade to a narrower image: the outgoing image's columns beyond the incoming width.
    private RawImage _strip;
    private int _front;
    private bool _fading;
    private float _fadeStart;
    private RectTransform _dotsRoot;
    private readonly List<Image> _dots = new List<Image>();

    private List<string> _gallery;
    // The slide last requested; the one on screen can lag behind it while a texture loads.
    private int _slide;
    private int _shownSlide = -1;
    // Generation token: only the newest texture request may change the photo.
    private int _request;
    private float _interval;
    private float _nextSlide;

    /// <summary>The slide whose image is on screen, or -1 before the first one arrives.</summary>
    public int Slide { get { return _shownSlide; } }
    public bool IsFading { get { return _fading; } }
    public float Interval { get { return _interval; } }

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

        // The photo area: a transparent mask holding two RawImages, so one image can crossfade into the
        // next. Each is fitted to the area's full height at its own source aspect, left aligned. A wide
        // image is clipped on the right; a narrow one leaves the area empty on the right, showing
        // whatever is behind the card. Never centred, never cropped vertically. Not interactive: the area
        // has no graphic of its own, so a drag here falls through to the Body and pans the timeline.
        RectTransform photoArea = TimelineUi.Rect(body.transform, "Photo");
        TimelineUi.TopLeft(photoArea, 0f, imageTop, width, m.ImageHeight);
        photoArea.gameObject.AddComponent<RectMask2D>();

        for (int i = 0; i < 2; i++)
        {
            RawImage layer = TimelineUi.Rect(photoArea, "Texture " + i).gameObject.AddComponent<RawImage>();
            // Stretched vertically and pinned to the left edge; the fitter sets only the width, from the
            // height, so it follows the holder if the holder is resized.
            RectTransform layerRt = layer.rectTransform;
            layerRt.anchorMin = new Vector2(0f, 0f);
            layerRt.anchorMax = new Vector2(0f, 1f);
            layerRt.pivot = new Vector2(0f, 0.5f);
            layerRt.anchoredPosition = Vector2.zero;
            layerRt.sizeDelta = Vector2.zero;
            layer.raycastTarget = false;
            layer.enabled = false;

            AspectRatioFitter fitter = layer.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;

            _layers[i] = layer;
            _fitters[i] = fitter;
        }

        // Positioned per fade by BeginStrip; full height, left anchored, no fitter of its own.
        _strip = TimelineUi.Rect(photoArea, "Texture Strip").gameObject.AddComponent<RawImage>();
        RectTransform stripRt = _strip.rectTransform;
        stripRt.anchorMin = new Vector2(0f, 0f);
        stripRt.anchorMax = new Vector2(0f, 1f);
        stripRt.pivot = new Vector2(0f, 0.5f);
        stripRt.sizeDelta = Vector2.zero;
        _strip.raycastTarget = false;
        _strip.enabled = false;

        // No slideshow until Bind has a gallery.
        enabled = false;
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
        bool cycles = TimelineLayout.CyclesSlides(_gallery.Count);
        BuildDots(cycles ? _gallery.Count : 0);

        // Each card samples its own interval, so the slideshows run out of step. Update only runs while
        // there is something to cycle; Unity stops it when the card is inactive or destroyed.
        _interval = TimelineLayout.SlideInterval(UnityEngine.Random.value, _metrics.slideIntervalMin,
            _metrics.slideIntervalMax);
        _nextSlide = Time.unscaledTime + _interval;
        enabled = cycles;

        ResetLayers();
        RequestSlide(TimelineLayout.StartSlide(_gallery, item));
    }

    private void Update()
    {
        // A running fade holds the next slide back; the overdue advance happens once it ends.
        if (_fading)
        {
            ApplyFade(Time.unscaledTime - _fadeStart);
            return;
        }

        if (TimelineLayout.SlideDue(Time.unscaledTime, ref _nextSlide, _interval))
            RequestSlide(TimelineLayout.Step(_slide, _gallery.Count, 1));
    }

    private void OnDisable()
    {
        // Deactivated - the Timeline screen hides by disabling its cards - so settle on a definite state:
        // finish any fade, and cancel any request still in flight so it cannot start one while hidden.
        // Guarded for a disable before Build has created the layers.
        if (_fading && _layers[0] != null)
            ApplyFade(float.MaxValue);
        _request++;
    }

    /// <summary>Both layers empty and transparent, no fade, nothing shown.</summary>
    private void ResetLayers()
    {
        _fading = false;
        _shownSlide = -1;
        _front = 0;
        for (int i = 0; i < 2; i++)
        {
            _layers[i].texture = null;
            _layers[i].uvRect = FullUv;
            _layers[i].enabled = false;
            SetAlpha(_layers[i], 0f);
        }
        EndStrip();
    }

    private static readonly Rect FullUv = new Rect(0f, 0f, 1f, 1f);

    /// <summary>Makes <paramref name="index"/> the target slide and returns the token its texture must carry.</summary>
    private int BeginRequest(int index)
    {
        _slide = index;
        return ++_request;
    }

    private void RequestSlide(int index)
    {
        if (_gallery.Count == 0)
            return;

        TextureCacheManager cache = Core.main != null ? Core.main.TextureCacheManager : null;
        if (cache == null)
        {
            Development.LogError("TimelineCard: Core.main.TextureCacheManager is not available, so no "
                + "timeline image can be shown.");
            return;
        }

        int token = BeginRequest(index);
        string url = _gallery[index];

        // The manager owns and keeps the texture, so cycling back to a slide does not reload it.
        cache.GetOrSetTexture2D(url, texture => OnTexture(token, index, url, texture), true);
    }

    /// <summary>
    /// Applies a loaded texture. The first image appears at once; later ones crossfade over the current
    /// one. Only the newest request counts, and a failed load keeps the current image.
    /// </summary>
    private void OnTexture(int token, int index, string url, Texture2D texture)
    {
        // Destroyed by a rebuild, superseded by a newer request, or cancelled by OnDisable.
        if (this == null || token != _request)
            return;

        if (texture == null)
        {
            Development.LogWarning($"TimelineCard: no texture for [{url}]; keeping the current image.");
            return;
        }

        float aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
        bool crossfade = _shownSlide >= 0 && _metrics.slideFadeDuration > 0f;

        if (!crossfade)
        {
            // First image, or fades switched off: show it on the front layer and hide the other.
            ShowOnLayer(_front, texture, aspect, 1f);
            _layers[1 - _front].enabled = false;
            _layers[1 - _front].texture = null;
        }
        else
        {
            // The incoming image goes on the back layer, drawn above the outgoing one, starting clear.
            int incoming = 1 - _front;
            ShowOnLayer(incoming, texture, aspect, 0f);
            _layers[incoming].transform.SetAsLastSibling();
            _front = incoming;
            BeginStrip(1 - incoming, aspect);
            _fading = true;
            _fadeStart = Time.unscaledTime;
        }

        _shownSlide = index;
        for (int i = 0; i < _dots.Count; i++)
            _dots[i].color = i == index ? _style.dotActiveColor : _style.dotInactiveColor;
    }

    /// <summary>
    /// Sets the crossfade <paramref name="elapsed"/> seconds in; ends it at full progress. Per pixel
    /// this is a lerp from the outgoing to the incoming image, where each is transparent outside its
    /// own width:
    /// - Where both images are, the incoming one fades in at t over the outgoing one held at 1.
    ///   UI blending is source-over, so this gives t·in + (1 - t)·out with full coverage. Fading the
    ///   outgoing layer too would cover only 1 - t(1 - t), dipping toward the background.
    /// - Where only the outgoing image is (it is wider), the strip shows those columns at 1 - t.
    /// - Where only the incoming image is, it fades in at t.
    /// </summary>
    private void ApplyFade(float elapsed)
    {
        float t = TimelineLayout.FadeProgress(elapsed, _metrics.slideFadeDuration);
        RawImage incoming = _layers[_front];
        RawImage outgoing = _layers[1 - _front];
        SetAlpha(incoming, t);
        SetAlpha(outgoing, 1f);
        if (_strip.enabled)
            SetAlpha(_strip, 1f - t);

        if (t < 1f)
            return;

        _fading = false;
        outgoing.enabled = false;
        outgoing.texture = null;
        outgoing.uvRect = FullUv;
        EndStrip();
    }

    /// <summary>
    /// When the outgoing image is wider than the incoming one, crops the outgoing layer to the incoming
    /// width - same height, so the same scale - and shows its remaining columns on the strip.
    /// </summary>
    private void BeginStrip(int outgoing, float incomingAspect)
    {
        float outgoingAspect = _aspects[outgoing];
        if (outgoingAspect <= incomingAspect || outgoingAspect <= 0f)
            return;

        float split = incomingAspect / outgoingAspect;
        float height = _layers[outgoing].rectTransform.rect.height;

        _layers[outgoing].uvRect = new Rect(0f, 0f, split, 1f);
        _fitters[outgoing].aspectRatio = incomingAspect;

        _strip.texture = _layers[outgoing].texture;
        _strip.uvRect = new Rect(split, 0f, 1f - split, 1f);
        _strip.rectTransform.anchoredPosition = new Vector2(height * incomingAspect, 0f);
        _strip.rectTransform.sizeDelta = new Vector2(height * (outgoingAspect - incomingAspect), 0f);
        SetAlpha(_strip, 1f);
        _strip.enabled = true;
    }

    private void EndStrip()
    {
        _strip.enabled = false;
        _strip.texture = null;
    }

    private void ShowOnLayer(int layer, Texture2D texture, float aspect, float alpha)
    {
        _layers[layer].texture = texture;
        _layers[layer].uvRect = FullUv;
        _aspects[layer] = aspect;
        _fitters[layer].aspectRatio = aspect;
        SetAlpha(_layers[layer], alpha);
        _layers[layer].enabled = true;
    }

    private static void SetAlpha(RawImage image, float alpha)
    {
        Color color = image.color;
        color.a = alpha;
        image.color = color;
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
        // Called with 0 for a single image or an empty gallery, which hides the row.
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

#if UNITY_EDITOR
    /// <summary>
    /// Crossfade and request-token check, runnable from eval as <c>TimelineCard.SelfCheck()</c> OUTSIDE
    /// Play mode. Drives a temporary card's texture callbacks directly, without the texture cache, and
    /// always destroys the card and its textures.
    /// </summary>
    public static string SelfCheck()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("TimelineCard.SelfCheck: exit Play mode first.");

        int checks = 0;
        Action<bool, string> check = (condition, what) =>
        {
            checks++;
            if (!condition)
                throw new Exception("TimelineCard self-check FAILED: " + what);
        };

        // Fade progress: endpoints, midpoint, clamping, and a zero duration finishing at once.
        check(TimelineLayout.FadeProgress(0f, 0.5f) == 0f && TimelineLayout.FadeProgress(0.5f, 0.5f) == 1f, "fade endpoints");
        check(Mathf.Abs(TimelineLayout.FadeProgress(0.25f, 0.5f) - 0.5f) < 1e-6f, "fade midpoint");
        check(TimelineLayout.FadeProgress(-1f, 0.5f) == 0f && TimelineLayout.FadeProgress(9f, 0.5f) == 1f, "fade clamps");
        check(TimelineLayout.FadeProgress(0f, 0f) == 1f, "duration 0 is an instant swap");

        GameObject parent = new GameObject("TimelineCard SelfCheck", typeof(RectTransform));
        Texture2D a = new Texture2D(4, 2);
        Texture2D b = new Texture2D(2, 4);
        try
        {
            TimelineCardMetrics metrics = new TimelineCardMetrics();
            TimelineCard card = Create(parent.transform, new TimelineStyle(), metrics, 0f, 0f, false, false);
            card._gallery = new List<string> { "a", "b" };
            card.ResetLayers();

            // The first image appears at once, on one layer only.
            card.OnTexture(card.BeginRequest(0), 0, "a", a);
            RawImage front = card._layers[card._front];
            check(front.texture == a && front.color.a == 1f && front.enabled && !card._layers[1 - card._front].enabled
                && !card._fading && card.Slide == 0, "first image is immediate");
            check(Mathf.Abs(card._fitters[card._front].aspectRatio - 2f) < 1e-6f, "layer fitter takes the texture aspect");

            // A superseded request is ignored, and a failed load keeps the current image.
            int stale = card.BeginRequest(1);
            int current = card.BeginRequest(0);
            card.OnTexture(stale, 1, "b", b);
            check(card._layers[card._front].texture == a && !card._fading && card.Slide == 0, "stale texture ignored");
            card.OnTexture(current, 0, "a", null);
            check(card._layers[card._front].texture == a && card._layers[card._front].color.a == 1f && card.Slide == 0,
                "failed load keeps the current image");

            // A crossfade: incoming 0 -> 1 over an outgoing layer held at 1; the outgoing clears at the end.
            card.OnTexture(card.BeginRequest(1), 1, "b", b);
            RawImage incoming = card._layers[card._front];
            RawImage outgoing = card._layers[1 - card._front];
            check(card._fading && incoming.texture == b && incoming.color.a == 0f && outgoing.texture == a
                && outgoing.color.a == 1f && card.Slide == 1, "fade starts with old opaque, new clear");
            check(incoming.transform.GetSiblingIndex() > outgoing.transform.GetSiblingIndex(), "incoming draws above");

            // a (aspect 2) is wider than b (aspect 0.5): the outgoing layer is cropped to b's width at the
            // same scale, and the strip shows the rest of a's columns. Holder height is 146, so the split
            // is at 146 * 0.5 = 73 and a ends at 146 * 2 = 292.
            float h = outgoing.rectTransform.rect.height;
            RawImage strip = card._strip;
            check(Mathf.Abs(h - metrics.ImageHeight) < 1e-3f && Mathf.Abs(strip.rectTransform.rect.height - h) < 1e-3f,
                "layers and strip share the holder height");
            check(outgoing.uvRect == new Rect(0f, 0f, 0.25f, 1f) && Mathf.Abs(card._fitters[1 - card._front].aspectRatio - 0.5f) < 1e-6f,
                "outgoing cropped to the incoming width at the same scale");
            check(strip.enabled && strip.texture == a && strip.uvRect == new Rect(0.25f, 0f, 0.75f, 1f)
                && Mathf.Abs(strip.rectTransform.anchoredPosition.x - h * 0.5f) < 1e-3f
                && Mathf.Abs(strip.rectTransform.sizeDelta.x - h * 1.5f) < 1e-3f && strip.color.a == 1f,
                "strip shows the outgoing-only columns, left-aligned after the split");

            card.ApplyFade(metrics.slideFadeDuration * 0.5f);
            check(Mathf.Abs(incoming.color.a - 0.5f) < 1e-5f && outgoing.color.a == 1f && Mathf.Abs(strip.color.a - 0.5f) < 1e-5f,
                "fade midpoint: incoming 0.5 over outgoing 1, outgoing-only strip 0.5");
            for (int s = 0; s < 10; s++)
            {
                // Where both images are, source-over coverage stays 1 (no dip); where only the outgoing
                // one is, it fades 1 -> 0 with the strip; where only the incoming one is, it fades in at t.
                float t = s / 10f;
                card.ApplyFade(metrics.slideFadeDuration * t);
                float composite = incoming.color.a + (1f - incoming.color.a) * outgoing.color.a;
                check(Mathf.Abs(composite - 1f) < 1e-5f, "overlap coverage 1 at step " + s);
                check(Mathf.Abs(strip.color.a - (1f - t)) < 1e-5f, "outgoing-only area at 1 - t at step " + s);
            }
            card.ApplyFade(metrics.slideFadeDuration);
            check(!card._fading && incoming.color.a == 1f && !outgoing.enabled && outgoing.texture == null
                && outgoing.uvRect == new Rect(0f, 0f, 1f, 1f) && !strip.enabled && strip.texture == null, "fade ends clean");

            // Disabling mid-fade settles it and cancels the request in flight.
            card.OnTexture(card.BeginRequest(0), 0, "a", a);
            int inFlight = card.BeginRequest(1);
            check(card._fading, "a second fade is running");
            check(!card._strip.enabled && card._layers[1 - card._front].uvRect == new Rect(0f, 0f, 1f, 1f),
                "to a wider image: no strip, outgoing uncropped, the incoming-only area fades in at t");
            card.OnDisable();
            check(!card._fading && card._layers[card._front].color.a == 1f && !card._layers[1 - card._front].enabled,
                "OnDisable finishes the fade");
            card.OnTexture(inFlight, 1, "b", b);
            check(card._layers[card._front].texture == a && card.Slide == 0, "a request cancelled by OnDisable is ignored");

            // Duration 0 swaps without a fade.
            metrics.slideFadeDuration = 0f;
            card.OnTexture(card.BeginRequest(1), 1, "b", b);
            check(!card._fading && card._layers[card._front].texture == b && card._layers[card._front].color.a == 1f
                && !card._layers[1 - card._front].enabled, "duration 0 swaps at once");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(parent);
            UnityEngine.Object.DestroyImmediate(a);
            UnityEngine.Object.DestroyImmediate(b);
        }

        return "TimelineCard self-check PASS (" + checks + " checks)";
    }
#endif
}
