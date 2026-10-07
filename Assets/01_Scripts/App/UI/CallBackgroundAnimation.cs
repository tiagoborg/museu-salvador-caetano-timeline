using System;
using EnigmaVirtual;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One photo with its vertical line, ring marker and year, placed in reference pixels.</summary>
[Serializable]
public class CallBackgroundGroup
{
    public string year;
    [Tooltip("The photo. Left empty, the group shows its line and year only - never a placeholder.")]
    public Sprite photo;
    [Tooltip("x, y from the screen's top-left; width, height. Reference 1920x1080.")]
    public Rect photoRect;
    public float lineX;
    public float lineTop;
    public float lineBottom;
    public float ringY;
    [Tooltip("Top-left of the two-line year label.")]
    public Vector2 labelPosition;
    [Tooltip("Where this group starts in the drift cycle, in radians, so groups do not move in step.")]
    public float phase;

    public CallBackgroundGroup() { }

    public CallBackgroundGroup(string year, Rect photoRect, float lineX, float lineTop, float lineBottom,
        float ringY, Vector2 labelPosition, float phase)
    {
        this.year = year;
        this.photoRect = photoRect;
        this.lineX = lineX;
        this.lineTop = lineTop;
        this.lineBottom = lineBottom;
        this.ringY = ringY;
        this.labelPosition = labelPosition;
        this.phase = phase;
    }
}

/// <summary>
/// The Call screen's decorative background: dated photos joined to vertical lines, ring markers and
/// years, each group drifting slowly on its own. Built in code behind the screen's authored children,
/// which it never touches. <see cref="CallScreenview"/> calls <see cref="Play"/> on show and
/// <see cref="Stop"/> on hide.
/// </summary>
/// <remarks>
/// Layout comes from the static design reference (Adobe XD, first screen, 1920x1080); the motion is
/// this project's own choice, since the reference does not animate.
/// </remarks>
public class CallBackgroundAnimation : MonoBehaviour
{
    [SerializeField]
    private CallBackgroundGroup[] groups =
    {
        new CallBackgroundGroup("1926", new Rect(580f, 340f, 430f, 570f), 544f, 0f, 930f, 930f, new Vector2(530f, 960f), 0f),
        new CallBackgroundGroup("1946", new Rect(766f, 60f, 245f, 170f), 720f, 0f, 120f, 120f, new Vector2(700f, 150f), 1.3f),
        new CallBackgroundGroup("1971", new Rect(1104f, 60f, 500f, 374f), 1064f, 0f, 1080f, 180f, new Vector2(1054f, 100f), 2.6f),
        new CallBackgroundGroup("1982", new Rect(1230f, 460f, 240f, 168f), 1454f, 734f, 1080f, 734f, new Vector2(1438f, 650f), 3.9f),
        new CallBackgroundGroup("2026", new Rect(1500f, 596f, 360f, 280f), 1772f, 0f, 506f, 506f, new Vector2(1756f, 530f), 5.2f),
    };

    [Header("Look")]
    [SerializeField]
    private TMP_FontAsset yearFont;
    [SerializeField]
    private float yearFontSize = 30f;
    [SerializeField]
    private Color textColor = Color.white;
    [SerializeField]
    private Color lineColor = new Color(0.42f, 0.42f, 0.42f, 1f);
    [SerializeField]
    private Color backgroundColor = new Color32(0x19, 0x19, 0x19, 0xFF);
    [SerializeField]
    private float lineWidth = 2f;
    [SerializeField]
    private float markerSize = 20f;

    [Header("Motion (unscaled time)")]
    [SerializeField, Tooltip("Seconds for one full drift cycle. Larger is slower.")]
    private float periodSeconds = 14f;
    [SerializeField, Tooltip("Furthest a group drifts from its place, in reference pixels. Horizontal keeps "
        + "lines that touch the screen edge attached to it.")]
    private Vector2 travel = new Vector2(12f, 0f);
    [SerializeField, Range(0f, 1f), Tooltip("Lowest opacity of the photos' gentle pulse, on the same cycle "
        + "as the drift. 1 turns the pulse off. Lines, markers and years always stay opaque.")]
    private float photoMinAlpha = 0.65f;

    private RectTransform _root;
    private RectTransform[] _groupRoots;
    // One per group, null where the group has no photo.
    private Image[] _photos;
    private Sprite _ring;
    private float _startTime;
    private bool _playing;

    public bool IsPlaying { get { return _playing; } }

    private void Awake()
    {
        // Idle until Play: the screen decides when this runs. Only when not already playing - Awake order
        // across objects is undefined, and the controller's Awake can show Call, and so Play this, first.
        if (!_playing)
            enabled = false;
    }

    /// <summary>Starts drifting from the initial layout. A second call while playing changes nothing.</summary>
    public void Play()
    {
        if (_playing)
            return;

        if (_root == null)
            Build();

        _startTime = Time.unscaledTime;
        _playing = true;
        enabled = true;
        Tick(0f);
    }

    /// <summary>
    /// Stops, puts every group back in its place and every photo back to full opacity, so the next Play
    /// starts clean.
    /// </summary>
    public void Stop()
    {
        _playing = false;
        enabled = false;

        if (_groupRoots == null)
            return;

        for (int i = 0; i < _groupRoots.Length; i++)
        {
            _groupRoots[i].anchoredPosition = Vector2.zero;
            SetPhotoAlpha(i, 1f);
        }
    }

    private void OnDisable()
    {
        // Covers the screen object being deactivated without a Hide.
        if (_playing)
            Stop();
    }

    private void OnDestroy()
    {
        // Generated in Build, so this component owns it.
        TimelineUi.DestroySprite(_ring);
        _ring = null;
    }

    private void Update()
    {
        if (!_playing)
            return;

        Tick(Time.unscaledTime - _startTime);
    }

    /// <summary>
    /// Positions every group, and sets every photo's opacity, for <paramref name="elapsed"/> seconds into
    /// the cycle.
    /// </summary>
    public void Tick(float elapsed)
    {
        if (_groupRoots == null)
            return;

        for (int i = 0; i < _groupRoots.Length; i++)
        {
            float phase = groups[i].phase;
            _groupRoots[i].anchoredPosition = OffsetAt(elapsed, periodSeconds, travel, phase);
            SetPhotoAlpha(i, AlphaAt(elapsed, periodSeconds, photoMinAlpha, phase));
        }
    }

    private void SetPhotoAlpha(int group, float alpha)
    {
        Image photo = _photos[group];
        if (photo == null)
            return;

        Color color = photo.color;
        color.a = alpha;
        photo.color = color;
    }

    /// <summary>
    /// The photo opacity at a moment: between <paramref name="minAlpha"/> and 1, on the drift's period.
    /// A cosine, so a group at phase 0 starts fully opaque.
    /// </summary>
    public static float AlphaAt(float elapsed, float periodSeconds, float minAlpha, float phase)
    {
        float period = periodSeconds > 0.01f ? periodSeconds : 0.01f;
        float min = Mathf.Clamp01(minAlpha);
        float wave = 0.5f + 0.5f * Mathf.Cos(elapsed * (2f * Mathf.PI / period) + phase);
        return Mathf.Lerp(min, 1f, wave);
    }

    /// <summary>The drift at a moment. Never further than <paramref name="travel"/> on either axis.</summary>
    public static Vector2 OffsetAt(float elapsed, float periodSeconds, Vector2 travel, float phase)
    {
        float period = periodSeconds > 0.01f ? periodSeconds : 0.01f;
        return travel * Mathf.Sin(elapsed * (2f * Mathf.PI / period) + phase);
    }

    private void Build()
    {
        _ring = TimelineUi.MakeCircleSprite(64, 0.2f, backgroundColor, lineColor);

        // Behind every authored child of the screen, and clipped to the screen.
        _root = TimelineUi.Rect(transform, "Call Background");
        TimelineUi.Stretch(_root);
        _root.SetAsFirstSibling();
        _root.gameObject.AddComponent<RectMask2D>();

        _groupRoots = new RectTransform[groups.Length];
        _photos = new Image[groups.Length];
        string missing = string.Empty;

        for (int i = 0; i < groups.Length; i++)
        {
            CallBackgroundGroup g = groups[i];
            RectTransform groupRoot = TimelineUi.Rect(_root, "Group " + g.year);
            TimelineUi.Stretch(groupRoot);
            _groupRoots[i] = groupRoot;

            Image line = TimelineUi.MakeImage(groupRoot, "Line", null, lineColor, false);
            TimelineUi.TopLeft(line.rectTransform, g.lineX - lineWidth * 0.5f, g.lineTop, lineWidth,
                g.lineBottom - g.lineTop);

            // Untinted, so the ring's baked interior covers the line exactly.
            Image ring = TimelineUi.MakeImage(groupRoot, "Marker", _ring, Color.white, false);
            TimelineUi.TopLeft(ring.rectTransform, g.lineX - markerSize * 0.5f, g.ringY - markerSize * 0.5f,
                markerSize, markerSize);

            string year = g.year ?? string.Empty;
            TextMeshProUGUI label = TimelineUi.MakeText(groupRoot, "Year", yearFont, yearFontSize, textColor,
                TextAlignmentOptions.TopLeft);
            TimelineUi.TopLeft(label.rectTransform, g.labelPosition.x, g.labelPosition.y, 80f, yearFontSize * 2.4f);
            label.lineSpacing = -20f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.text = year.Length == 4 ? year.Substring(0, 2) + "\n" + year.Substring(2) : year;

            if (g.photo == null)
            {
                missing += (missing.Length > 0 ? ", " : string.Empty) + year;
                continue;
            }

            Image photo = TimelineUi.MakeImage(groupRoot, "Photo", g.photo, Color.white, false);
            photo.preserveAspect = true;
            TimelineUi.TopLeft(photo.rectTransform, g.photoRect.x, g.photoRect.y, g.photoRect.width, g.photoRect.height);
            _photos[i] = photo;
        }

        if (missing.Length > 0)
            Development.LogWarning($"{name}: CallBackgroundAnimation has no photo for {missing}. Those groups "
                + "show only their line and year. Assign the sprites, or run Gema/Timeline/Setup Scene.");
    }

#if UNITY_EDITOR
    /// <summary>
    /// Lifecycle and motion check, runnable from eval as <c>CallBackgroundAnimation.SelfCheck()</c>
    /// OUTSIDE Play mode, where sprite destruction is immediate and can be asserted. Builds a temporary
    /// object, which is always destroyed.
    /// </summary>
    public static string SelfCheck()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("CallBackgroundAnimation.SelfCheck: exit Play mode first - "
                + "destruction is deferred there, so the ownership check could not be asserted.");

        int checks = 0;
        Action<bool, string> check = (condition, what) =>
        {
            checks++;
            if (!condition)
                throw new Exception("CallBackgroundAnimation self-check FAILED: " + what);
        };

        // Bounded at every sample, for a range of phases.
        Vector2 travel = new Vector2(12f, 5f);
        for (int s = 0; s < 400; s++)
        {
            Vector2 o = OffsetAt(s * 0.137f, 14f, travel, s * 0.71f);
            check(Mathf.Abs(o.x) <= travel.x + 0.0001f && Mathf.Abs(o.y) <= travel.y + 0.0001f, "offset bounded at sample " + s);
        }
        check(OffsetAt(0f, 14f, travel, 0f) == Vector2.zero, "phase 0 starts at rest");
        check((OffsetAt(3.5f, 14f, travel, 0f) - travel).sqrMagnitude < 1e-6f, "quarter period reaches full travel");
        check((OffsetAt(14f, 14f, travel, 0.4f) - OffsetAt(0f, 14f, travel, 0.4f)).sqrMagnitude < 1e-6f, "one period returns to start");
        check(OffsetAt(1f, 0f, travel, 0f).x <= travel.x, "a zero period does not divide by zero");

        // Photo pulse: within [min, 1], full at phase 0, lowest at half a period, back after a period.
        for (int s = 0; s < 400; s++)
        {
            float a = AlphaAt(s * 0.137f, 14f, 0.65f, s * 0.71f);
            check(a >= 0.65f - 0.0001f && a <= 1f + 0.0001f, "alpha bounded at sample " + s);
        }
        check(Mathf.Abs(AlphaAt(0f, 14f, 0.65f, 0f) - 1f) < 1e-5f, "phase 0 starts fully opaque");
        check(Mathf.Abs(AlphaAt(7f, 14f, 0.65f, 0f) - 0.65f) < 1e-5f, "half a period reaches the minimum");
        check(Mathf.Abs(AlphaAt(14f, 14f, 0.65f, 0.4f) - AlphaAt(0f, 14f, 0.65f, 0.4f)) < 1e-5f, "one period returns");
        check(AlphaAt(7f, 14f, 1f, 0f) == 1f, "minimum 1 turns the pulse off");

        GameObject go = new GameObject("CallBackground SelfCheck", typeof(RectTransform));
        Texture2D photoTexture = new Texture2D(2, 2);
        Sprite photoSprite = Sprite.Create(photoTexture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
        try
        {
            // Awake order is undefined, so check both. Outside Play mode Unity sends no Awake, so call it.
            // Normal order: Awake, then Play.
            CallBackgroundAnimation normal = go.AddComponent<CallBackgroundAnimation>();
            normal.Awake();
            check(!normal.enabled && !normal.IsPlaying, "Awake leaves an unplayed animation idle");
            normal.Play();
            check(normal.enabled && normal.IsPlaying, "Play after Awake runs");
            normal.Stop();
            normal.OnDestroy();
            UnityEngine.Object.DestroyImmediate(normal._root.gameObject);
            UnityEngine.Object.DestroyImmediate(normal);

            // Startup order: the screen controller's Awake shows Call, so Play, before this Awake.
            // Group 0 gets a temporary photo; the other four stay empty, which is the missing-photo case.
            CallBackgroundAnimation anim = go.AddComponent<CallBackgroundAnimation>();
            anim.groups[0].photo = photoSprite;
            anim.Play();
            anim.Awake();
            check(anim.enabled && anim.IsPlaying, "a late Awake does not disable an animation already playing");
            anim.Stop();
            anim.Play();
            anim.Play();
            int roots = 0;
            foreach (Transform child in go.transform)
                if (child.name == "Call Background")
                    roots++;
            check(roots == 1, "Play twice builds one background, got " + roots);
            check(go.transform.GetChild(0).name == "Call Background", "background is the first child");
            check(anim._groupRoots.Length == 5, "five groups");
            check(anim._photos[0] != null, "a group with a photo caches its Image");
            for (int i = 1; i < 5; i++)
                check(anim._photos[i] == null && anim._groupRoots[i].Find("Photo") == null,
                    "a group without a photo has no Photo object and no cached Image");

            anim.Tick(3.5f);
            bool moved = false;
            foreach (RectTransform g in anim._groupRoots)
                moved |= g.anchoredPosition != Vector2.zero;
            check(moved, "Tick moves the groups");

            // Group 0 is phase 0, so half a period is its lowest opacity.
            anim.Tick(7f);
            check(Mathf.Abs(anim._photos[0].color.a - anim.photoMinAlpha) < 1e-4f, "Tick pulses the photo to its minimum");
            Transform group0 = anim._groupRoots[0];
            check(group0.Find("Line").GetComponent<Image>().color.a == 1f
                && group0.Find("Marker").GetComponent<Image>().color.a == 1f
                && group0.Find("Year").GetComponent<TextMeshProUGUI>().color.a == 1f,
                "line, marker and year stay opaque while the photo pulses");

            anim.Stop();
            foreach (RectTransform g in anim._groupRoots)
                check(g.anchoredPosition == Vector2.zero, "Stop resets " + g.name);
            check(anim._photos[0].color.a == 1f, "Stop restores the photo to full opacity");
            check(!anim.enabled && !anim.IsPlaying, "Stop disables the component");

            // Outside Play mode Unity does not send OnDestroy to a plain MonoBehaviour, so call it.
            Sprite ring = anim._ring;
            Texture2D ringTexture = ring.texture;
            anim.OnDestroy();
            check(ring == null && ringTexture == null, "OnDestroy destroys the generated ring sprite and its texture");
        }
        finally
        {
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(photoSprite);
            UnityEngine.Object.DestroyImmediate(photoTexture);
        }

        return "CallBackgroundAnimation self-check PASS (" + checks + " checks)";
    }
#endif
}
