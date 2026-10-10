using System;
using System.Collections.Generic;
using EnigmaVirtual;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Lease = TimelineTextureCache.Lease;
using Ticket = TimelineTextureCache.Ticket;

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
    [Tooltip("Galleries up to this many images show one dot each; larger ones show a \"current / total\" counter.")]
    public int maxIndicatorDots = 5;
    public float counterFontSize = 12f;

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
/// <remarks>
/// Images load only while the screen marks the card visible (<see cref="SetVisible"/>), which is
/// separate from <c>enabled</c>: a single-image card is visible without a slideshow. Leaving view
/// cancels loads, clears the layers and releases every texture lease, keeping the slide on screen so
/// re-entry resumes it.
/// </remarks>
public class TimelineCard : MonoBehaviour
{
    /// <summary>The category tag never grows past this share of the card's width.</summary>
    private const float MaxTagWidthFraction = 0.9f;

    private TimelineStyle _style;
    private TimelineCardMetrics _metrics;
    private TimelineTextureCache _cache;
    private float _width;

    private RectTransform _tagRect;
    private TextMeshProUGUI _tagText;
    private TextMeshProUGUI _yearText;
    private TextMeshProUGUI _contentText;
    // Two photo layers for the crossfade; _front is the one showing (or fading in). Each layer holds the
    // lease for the texture it shows.
    private readonly RawImage[] _layers = new RawImage[2];
    private readonly AspectRatioFitter[] _fitters = new AspectRatioFitter[2];
    private readonly float[] _aspects = new float[2];
    private readonly Lease[] _leases = new Lease[2];
    // During a fade to a narrower image: the outgoing image's columns beyond the incoming width. It shows
    // the outgoing layer's texture under that layer's lease.
    private RawImage _strip;
    private int _front;
    private bool _fading;
    private float _fadeStart;
    private RectTransform _dotsRoot;
    // Built once with the card and reused by every Bind: maxIndicatorDots dots and one counter.
    private readonly List<Image> _dots = new List<Image>();
    private int _dotCount;
    private TextMeshProUGUI _counter;
    private bool _wide;
    private bool _imageOnTop;

    private List<string> _gallery = new List<string>();
    private bool _cycles;
    private bool _visible;
    private bool _disabling;
    // The slide last requested; the one on screen can lag behind it while a texture loads. Kept while
    // out of view, so re-entry resumes it.
    private int _slide;
    private int _shownSlide = -1;
    // Generation tokens: only the newest request may change the photo or the prefetch.
    private int _request;
    private int _prefetchRequest;
    private Ticket _ticket;
    private Ticket _prefetchTicket;
    private Lease _prefetchLease;
    private float _interval;
    private float _nextSlide;

    /// <summary>The slide whose image is on screen, or -1 when none is.</summary>
    public int Slide { get { return _shownSlide; } }
    public bool IsFading { get { return _fading; } }
    public bool IsVisible { get { return _visible; } }
    public float Interval { get { return _interval; } }
    /// <summary>The slide to resume on re-entry: the one on screen, else the one last requested.</summary>
    public int RetainedSlide { get { return _shownSlide >= 0 ? _shownSlide : _slide; } }
    /// <summary>The fixed geometry kind; a pooled card is reused only for the same kind.</summary>
    public bool Wide { get { return _wide; } }
    public bool ImageOnTop { get { return _imageOnTop; } }

    public static TimelineCard Create(Transform parent, TimelineStyle style, TimelineCardMetrics metrics,
        TimelineTextureCache cache, float x, float y, bool wide, bool imageOnTop)
    {
        float width = wide ? metrics.wideWidth : metrics.narrowWidth;

        RectTransform root = TimelineUi.Rect(parent, "Card");
        TimelineUi.TopLeft(root, x, y, width, metrics.SlotHeight);

        TimelineCard card = root.gameObject.AddComponent<TimelineCard>();
        card._style = style;
        card._metrics = metrics;
        card._cache = cache;
        card._wide = wide;
        card.Build(width, imageOnTop);
        return card;
    }

    /// <summary>Moves a (pooled) card to a slot of its own geometry kind, in reference pixels.</summary>
    public void MoveTo(float x, float y)
    {
        TimelineUi.TopLeft((RectTransform)transform, x, y, _width, _metrics.SlotHeight);
    }

    private void Build(float width, bool imageOnTop)
    {
        TimelineCardMetrics m = _metrics;
        _width = width;
        _imageOnTop = imageOnTop;

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
        for (int i = 0; i < Mathf.Max(0, m.maxIndicatorDots); i++)
        {
            Image dot = TimelineUi.MakeImage(_dotsRoot, "Dot", _style.discSprite, _style.dotInactiveColor, false);
            dot.gameObject.SetActive(false);
            _dots.Add(dot);
        }
        _counter = TimelineUi.MakeText(_dotsRoot, "Counter", _style.boldFont, m.counterFontSize, _style.textColor,
            TextAlignmentOptions.MidlineRight);
        TimelineUi.Stretch(_counter.rectTransform);
        _counter.textWrappingMode = TextWrappingModes.NoWrap;
        _counter.gameObject.SetActive(false);
        _dotsRoot.gameObject.SetActive(false);

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

        // No slideshow until Bind has a gallery and the screen marks the card visible.
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

    /// <summary>
    /// Binds an event. Safe on a reused card: its requests are cancelled, images cleared and leases
    /// released first, and the indicator reuses the same children. Call while the card is active.
    /// </summary>
    /// <param name="gallery">The category's gallery, shared by every card of that category. Not modified.</param>
    /// <param name="initialSlide">The slide to open on, e.g. the one retained when the event left view;
    /// -1 for the event's own first image.</param>
    public void Bind(TimelineItemVO item, string categoryName, List<string> gallery, float contentFontSize,
        int initialSlide = -1)
    {
        ReleaseImages();
        SetTag((categoryName ?? string.Empty).ToUpperInvariant());
        _yearText.text = item.year.ToString();
        _contentText.fontSize = contentFontSize;
        _contentText.text = TimelineUi.Localized(item.content);

        BindGallery(gallery, initialSlide >= 0 ? initialSlide : TimelineLayout.StartSlide(gallery, item));
    }

    /// <summary>The image half of <see cref="Bind"/>. An out-of-range slide falls back to 0.</summary>
    private void BindGallery(List<string> gallery, int startSlide)
    {
        ReleaseImages();
        _gallery = gallery ?? new List<string>();
        _cycles = TimelineLayout.CyclesSlides(_gallery.Count);
        BuildIndicator(_gallery.Count);

        // Each card samples its own interval, so the slideshows run out of step.
        _interval = TimelineLayout.SlideInterval(UnityEngine.Random.value, _metrics.slideIntervalMin,
            _metrics.slideIntervalMax);
        _slide = startSlide >= 0 && startSlide < _gallery.Count ? startSlide : 0;

        if (_visible)
            Enter();
    }

    /// <summary>
    /// In view (including the screen's preload margin) or not. Entering loads the retained slide and starts
    /// the slideshow; leaving releases everything. Repeated calls with the same value do nothing.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (visible == _visible)
            return;

        _visible = visible;
        if (visible)
            Enter();
        else
            ReleaseImages();
    }

    /// <summary>Out of view for good: before a rebuild destroys the card, or when the screen hides. Idempotent.</summary>
    public void Release()
    {
        _visible = false;
        ReleaseImages();
    }

    private void Enter()
    {
        _nextSlide = Time.unscaledTime + _interval;
        enabled = _cycles;
        RequestSlide(_slide);
    }

    private void Update()
    {
        // A running fade or a load in flight holds the next slide back: the current image stays until its
        // successor is on screen. The overdue advance happens afterwards, once.
        if (_fading)
        {
            ApplyFade(Time.unscaledTime - _fadeStart);
            return;
        }

        if (_ticket != null && !_ticket.IsDone)
            return;

        if (TimelineLayout.SlideDue(Time.unscaledTime, ref _nextSlide, _interval))
            RequestSlide(TimelineLayout.Step(_slide, _gallery.Count, 1));
    }

    private void OnDisable()
    {
        // Deactivated by the screen (hide, out of view) or destroyed: a definite empty state. Visibility is
        // re-established by the screen when the card is active and in view again.
        if (!_disabling)
            Release();
    }

    private void OnDestroy()
    {
        Release();
    }

    /// <summary>Cancels requests, clears both layers and the strip, then releases every lease. Idempotent.</summary>
    private void ReleaseImages()
    {
        if (_shownSlide >= 0)
            _slide = _shownSlide;

        _request++;
        _ticket?.Cancel();
        _ticket = null;
        ClearPrefetch();
        ResetLayers();

        // Stopping the slideshow is not leaving view: keep OnDisable from treating it as deactivation.
        _disabling = true;
        enabled = false;
        _disabling = false;
    }

    private void ClearPrefetch()
    {
        _prefetchRequest++;
        _prefetchTicket?.Cancel();
        _prefetchTicket = null;
        _prefetchLease?.Dispose();
        _prefetchLease = null;
    }

    /// <summary>Both layers empty and transparent, no fade, nothing shown - and only then the leases go.</summary>
    private void ResetLayers()
    {
        _fading = false;
        _shownSlide = -1;
        _front = 0;
        if (_layers[0] == null)
            return; // before Build

        for (int i = 0; i < 2; i++)
        {
            _layers[i].texture = null;
            _layers[i].uvRect = FullUv;
            _layers[i].enabled = false;
            SetAlpha(_layers[i], 0f);
        }
        EndStrip();
        SetLease(0, null);
        SetLease(1, null);
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
        if (_gallery.Count == 0 || _cache == null)
            return;

        // The previous request and prefetch are let go only after the new request is made, so a prefetch
        // of this very slide is joined (or still cached) rather than aborted first.
        Ticket oldTicket = _ticket;
        Ticket oldPrefetch = _prefetchTicket;
        Lease oldPrefetchLease = _prefetchLease;
        _prefetchTicket = null;
        _prefetchLease = null;
        _prefetchRequest++;

        int token = BeginRequest(index);
        string url = _gallery[index];
        _ticket = _cache.Request(url, TimelineTextureCache.Priority.Visible, lease => OnTexture(token, index, url, lease));

        oldTicket?.Cancel();
        oldPrefetch?.Cancel();
        oldPrefetchLease?.Dispose();
    }

    /// <summary>Warms the next slide while this one shows. Its lease is never displayed.</summary>
    private void StartPrefetch()
    {
        ClearPrefetch();
        if (!_visible || !_cycles || _cache == null)
            return;

        int token = _prefetchRequest;
        string url = _gallery[TimelineLayout.Step(_shownSlide, _gallery.Count, 1)];
        _prefetchTicket = _cache.Request(url, TimelineTextureCache.Priority.Prefetch, lease =>
        {
            if (this == null || token != _prefetchRequest)
            {
                lease?.Dispose();
                return;
            }
            _prefetchLease = lease;
        });
    }

    /// <summary>
    /// Applies a loaded texture. The first image appears at once; later ones crossfade over the current
    /// one. Only the newest request counts - any other lease is disposed - and a failed load keeps the
    /// current image.
    /// </summary>
    private void OnTexture(int token, int index, string url, Lease lease)
    {
        // Destroyed, superseded by a newer request, or cancelled by leaving view.
        if (this == null || token != _request)
        {
            lease?.Dispose();
            return;
        }

        Texture2D texture = lease != null ? lease.Texture : null;
        if (texture == null)
        {
            lease?.Dispose();
            Development.LogWarning($"TimelineCard: no texture for [{url}]; keeping the current image.");
            return;
        }

        if (_fading)
            ApplyFade(float.MaxValue);

        float aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
        bool crossfade = _shownSlide >= 0 && _metrics.slideFadeDuration > 0f;

        if (!crossfade)
        {
            // First image, or fades switched off: show it on the front layer and empty the other.
            int other = 1 - _front;
            _layers[other].enabled = false;
            _layers[other].texture = null;
            SetLease(other, null);
            ShowOnLayer(_front, texture, aspect, 1f);
            SetLease(_front, lease);
        }
        else
        {
            // The incoming image goes on the back layer, drawn above the outgoing one, starting clear.
            int incoming = 1 - _front;
            ShowOnLayer(incoming, texture, aspect, 0f);
            SetLease(incoming, lease);
            _layers[incoming].transform.SetAsLastSibling();
            _front = incoming;
            BeginStrip(1 - incoming, aspect);
            _fading = true;
            _fadeStart = Time.unscaledTime;
        }

        _shownSlide = index;
        UpdateIndicator(index);
        StartPrefetch();
    }

    /// <summary>Replaces a layer's lease; call only after the layer stopped showing the old texture.</summary>
    private void SetLease(int layer, Lease lease)
    {
        Lease old = _leases[layer];
        _leases[layer] = lease;
        if (old != null && old != lease)
            old.Dispose();
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
        // Both the outgoing layer and the strip are clear now, so the outgoing texture can be released.
        SetLease(1 - _front, null);
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

    private static void SetAlpha(Graphic image, float alpha)
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

    /// <summary>
    /// Bounded slide indicator: one dot per image up to <c>maxIndicatorDots</c>, a "current / total"
    /// counter past that, nothing for 0 or 1 image. The gallery itself is never truncated.
    /// </summary>
    private void BuildIndicator(int count)
    {
        TimelineCardMetrics m = _metrics;
        // Clamped to the dots built with the card; a larger gallery shows the counter instead.
        _dotCount = Mathf.Min(TimelineLayout.IndicatorDots(count, m.maxIndicatorDots), _dots.Count);
        bool counter = TimelineLayout.CyclesSlides(count) && _dotCount == 0;
        _dotsRoot.gameObject.SetActive(_dotCount > 0 || counter);
        _counter.gameObject.SetActive(counter);

        // Right-aligned as in the design.
        TimelineLayout.DotStrip strip = TimelineLayout.FitDots(_dotCount, _dotsRoot.sizeDelta.x, m.dotSize, m.dotSpacing);
        for (int i = 0; i < _dots.Count; i++)
        {
            bool on = i < _dotCount;
            _dots[i].gameObject.SetActive(on);
            if (!on)
                continue;
            TimelineUi.TopLeft(_dots[i].rectTransform, strip.firstX + i * strip.pitch, (m.dotsHeight - strip.size) * 0.5f,
                strip.size, strip.size);
            _dots[i].color = _style.dotInactiveColor;
        }
    }

    private void UpdateIndicator(int index)
    {
        for (int i = 0; i < _dotCount; i++)
            _dots[i].color = i == index ? _style.dotActiveColor : _style.dotInactiveColor;

        // SetText with numeric arguments does not allocate a string per change.
        if (_counter.gameObject.activeSelf)
            _counter.SetText("{0:0} / {1:0}", index + 1, _gallery.Count);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Crossfade, request-token, lease and visibility check, runnable from eval as
    /// <c>TimelineCard.SelfCheck()</c> OUTSIDE Play mode. Uses a cache with a synchronous loader, and
    /// always destroys the card, the cache and its textures.
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
        // Synchronous loader over two in-memory textures; the cache owns and destroys them.
        TimelineTextureCache cache = new TimelineTextureCache(long.MaxValue,
            (path, done) => { done(path == "a" ? a : path == "b" ? b : null); return null; }, p => p);
        Func<string, Lease> lease = key =>
        {
            Lease got = null;
            cache.Request(key, TimelineTextureCache.Priority.Visible, l => got = l);
            return got;
        };

        try
        {
            TimelineCardMetrics metrics = new TimelineCardMetrics();
            TimelineCard card = Create(parent.transform, new TimelineStyle(), metrics, cache, 0f, 0f, false, false);
            card._gallery = new List<string> { "a", "b" };
            card.ResetLayers();

            // The first image appears at once, on one layer only.
            card.OnTexture(card.BeginRequest(0), 0, "a", lease("a"));
            RawImage front = card._layers[card._front];
            check(front.texture == a && front.color.a == 1f && front.enabled && !card._layers[1 - card._front].enabled
                && !card._fading && card.Slide == 0, "first image is immediate");
            check(Mathf.Abs(card._fitters[card._front].aspectRatio - 2f) < 1e-6f, "layer fitter takes the texture aspect");
            check(cache.Leases("a") == 1, "the shown image holds one lease");

            // A superseded request disposes its lease, and a failed load keeps the current image.
            int stale = card.BeginRequest(1);
            int current = card.BeginRequest(0);
            card.OnTexture(stale, 1, "b", lease("b"));
            check(card._layers[card._front].texture == a && !card._fading && card.Slide == 0 && cache.Leases("b") == 0,
                "stale lease disposed, image unchanged");
            card.OnTexture(current, 0, "a", null);
            check(card._layers[card._front].texture == a && card._layers[card._front].color.a == 1f && card.Slide == 0,
                "failed load keeps the current image");

            // A crossfade: incoming 0 -> 1 over an outgoing layer held at 1; the outgoing clears at the end.
            card.OnTexture(card.BeginRequest(1), 1, "b", lease("b"));
            RawImage incoming = card._layers[card._front];
            RawImage outgoing = card._layers[1 - card._front];
            check(card._fading && incoming.texture == b && incoming.color.a == 0f && outgoing.texture == a
                && outgoing.color.a == 1f && card.Slide == 1, "fade starts with old opaque, new clear");
            check(incoming.transform.GetSiblingIndex() > outgoing.transform.GetSiblingIndex(), "incoming draws above");
            check(cache.Leases("a") == 1 && cache.Leases("b") == 1, "outgoing stays leased during the fade");

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
            check(cache.Leases("a") == 0 && cache.Leases("b") == 1, "outgoing lease released only after it was cleared");

            // A fade to a wider image: no strip, outgoing uncropped.
            card.OnTexture(card.BeginRequest(0), 0, "a", lease("a"));
            int inFlight = card.BeginRequest(1);
            check(card._fading && !card._strip.enabled && card._layers[1 - card._front].uvRect == new Rect(0f, 0f, 1f, 1f),
                "to a wider image: no strip, the incoming-only area fades in at t");

            // Disabling mid-fade clears everything and releases every lease; a late result is disposed.
            card.OnDisable();
            check(!card._fading && !card._layers[0].enabled && !card._layers[1].enabled && card._layers[0].texture == null
                && card._layers[1].texture == null && card.Slide == -1, "OnDisable clears both layers");
            check(cache.Leases("a") == 0 && cache.Leases("b") == 0, "OnDisable releases every lease");
            card.OnTexture(inFlight, 1, "b", lease("b"));
            check(!card._layers[0].enabled && cache.Leases("b") == 0, "a request cancelled by OnDisable is disposed");
            card.OnDisable();
            check(card.Slide == -1, "a second OnDisable is harmless");

            // Visibility: entering loads the retained slide and prefetches the next; leaving releases all.
            card._cycles = true;
            card._slide = 1;
            card.SetVisible(true);
            check(card.IsVisible && card.Slide == 1 && card._layers[card._front].texture == b, "entering shows the retained slide");
            check(cache.Leases("b") == 1 && cache.Leases("a") == 1 && card._prefetchLease != null, "the next slide is prefetched");
            card.SetVisible(true);
            check(cache.Leases("b") == 1, "a repeated SetVisible(true) does nothing");
            card.SetVisible(false);
            check(!card.IsVisible && !card.enabled && card.Slide == -1 && card._slide == 1 && !card._layers[0].enabled
                && cache.Leases("a") == 0 && cache.Leases("b") == 0, "leaving clears, releases, keeps the slide");

            // Prefetch becomes the shown image through the cache, without a second load.
            card.SetVisible(true);
            int loadsBefore = cache.LoadsStarted;
            card.RequestSlide(0);
            check(card._fading && card._layers[card._front].texture == a && cache.LoadsStarted == loadsBefore,
                "advancing to the prefetched slide reuses it");
            card.Release();
            check(cache.Leases("a") == 0 && cache.Leases("b") == 0 && !card.IsVisible, "Release releases everything");

            // Indicator children are built once and reused by every rebind: never multiplied.
            int indicatorChildren = card._dotsRoot.childCount;
            check(indicatorChildren == metrics.maxIndicatorDots + 1, "dots pool and counter built once");
            List<string> hundred = new List<string>(new string[100]);
            for (int r = 0; r < 3; r++)
            {
                card.BindGallery(hundred, 0);
                card.BindGallery(new List<string> { "a", "b", "a2" }, 0);
            }
            check(card._dotsRoot.childCount == indicatorChildren, "rebinding does not add indicator children");
            int activeDots = 0;
            foreach (Image d in card._dots) if (d.gameObject.activeSelf) activeDots++;
            check(activeDots == 3 && card._dotCount == 3 && !card._counter.gameObject.activeSelf, "3 images: 3 dots, no counter");
            card.BindGallery(hundred, 0);
            activeDots = 0;
            foreach (Image d in card._dots) if (d.gameObject.activeSelf) activeDots++;
            check(activeDots == 0 && card._counter.gameObject.activeSelf && card._dotsRoot.gameObject.activeSelf,
                "100 images: counter, no dots; the gallery is not truncated");
            card.BindGallery(new List<string> { "a" }, 0);
            check(!card._dotsRoot.gameObject.activeSelf, "1 image: no indicator");

            // Reuse: a visible card rebound mid-load ignores the old request's late result and disposes it.
            List<string> ab = new List<string> { "a", "b" };
            card.BindGallery(ab, 0);
            card.SetVisible(true);
            int oldToken = card._request;
            card.BindGallery(ab, 1);
            card.OnTexture(oldToken, 0, "a", lease("a"));
            check(card.Slide == 1 && card._layers[card._front].texture == b, "stale result after a rebind ignored");
            check(cache.Leases("a") == 1, "only the new prefetch holds a; the stale lease was disposed");

            // Resume: leaving keeps the shown slide; rebinding with it (as the screen does) shows it again.
            int retained = card.RetainedSlide;
            card.Release();
            check(retained == 1 && card.RetainedSlide == 1 && cache.Leases("a") == 0 && cache.Leases("b") == 0,
                "release keeps the slide and frees every lease");
            card.BindGallery(ab, card.RetainedSlide);
            card.SetVisible(true);
            check(card.Slide == 1 && card._layers[card._front].texture == b, "re-entry resumes the retained slide");
            card.Release();

            // Geometry: a pooled card moves without changing its size or kind.
            card.MoveTo(500f, 104f);
            RectTransform rt = (RectTransform)card.transform;
            check(rt.anchoredPosition == new Vector2(500f, -104f) && rt.sizeDelta == new Vector2(metrics.narrowWidth, metrics.SlotHeight)
                && !card.Wide && !card.ImageOnTop, "MoveTo keeps width, height and kind");

            // Duration 0 swaps without a fade.
            card._gallery = new List<string> { "a", "b" };
            metrics.slideFadeDuration = 0f;
            card.OnTexture(card.BeginRequest(0), 0, "a", lease("a"));
            card.OnTexture(card.BeginRequest(1), 1, "b", lease("b"));
            check(!card._fading && card._layers[card._front].texture == b && card._layers[card._front].color.a == 1f
                && !card._layers[1 - card._front].enabled && cache.Leases("a") == 0, "duration 0 swaps at once and releases");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(parent);
            cache.Dispose();
            if (a != null) UnityEngine.Object.DestroyImmediate(a);
            if (b != null) UnityEngine.Object.DestroyImmediate(b);
        }

        return "TimelineCard self-check PASS (" + checks + " checks)";
    }
#endif
}
