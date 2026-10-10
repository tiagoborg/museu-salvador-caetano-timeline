#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using EnigmaVirtual;
using EnigmaVirtual.Screens;
using EnigmaVirtual.Screens.Navigation;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

/// <summary>
/// Editor-only measurement of the timeline at scale, driven from eval in Play mode. Reads state and
/// times existing code paths; changes no behaviour except installing the scaling content in memory
/// (<see cref="UseScalingData"/>, undone by <see cref="RestoreShippedData"/>) and, while sampling frames
/// with scroll on, moving the timeline.
/// </summary>
/// <remarks>
/// Private members are reached by reflection so the measured code stays exactly as shipped; a renamed
/// member throws naming itself rather than reporting zeros. Editor numbers include editor overhead:
/// compare runs with each other, not with a player.
/// Order: UseScalingData, OpenTimeline, Snapshot (next eval, after deferred Destroy), StartFrames,
/// FrameReport (after the duration), ToggleFilter, Snapshot.
/// </remarks>
public class TimelineScalingBaseline : MonoBehaviour
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static AppDataBase _shipped;
    private static TimelineScalingBaseline _sampler;
    private static string _lastReport = "no frame sample yet";

    private readonly List<float> _frames = new List<float>(4096);
    private float _duration;
    private float _started;
    private bool _scroll;
    private ScrollRect _scrollRect;
    private int _gcStart;
    private long _allocStart;
    private bool _skipFirst = true;

    // ---------------------------------------------------------------- content

    /// <summary>Loads assets/scaling/appData.scaling.json and installs it as the loaded content.</summary>
    public static string UseScalingData()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "assets", "scaling", "appData.scaling.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("Run Gema > Timeline Scaling > Generate Dataset first.", path);

        AppDataController controller = Controller();
        JToken data = JObject.Parse(File.ReadAllText(path, Encoding.UTF8))["data"];
        AppData scaling = data.ToObject<AppData>();

        if (_shipped == null)
            _shipped = controller.AppData;
        SetAppData(controller, scaling);
        return $"scaling content installed: {scaling.timeline?.Length ?? 0} events, "
            + $"{scaling.categories?.Length ?? 0} categories. Open (or reopen) the timeline to rebuild.";
    }

    public static string RestoreShippedData()
    {
        if (_shipped == null)
            return "nothing to restore";
        SetAppData(Controller(), _shipped);
        _shipped = null;
        return "shipped content restored. Reopen the timeline to rebuild.";
    }

    private static AppDataController Controller()
    {
        if (!Application.isPlaying || Core.main == null || Core.main.AppDataController == null)
            throw new InvalidOperationException("Enter Play mode and let the boot reach Intro first.");
        return Core.main.AppDataController;
    }

    private static void SetAppData(AppDataController controller, AppDataBase data)
    {
        MethodInfo setter = typeof(AppDataController).GetProperty("AppData")?.GetSetMethod(true)
            ?? throw new MissingMemberException(nameof(AppDataController), "AppData setter");
        setter.Invoke(controller, new object[] { data });
    }

    // ---------------------------------------------------------------- timed actions

    /// <summary>
    /// Navigates to the timeline with every category on, timing the synchronous part (build + rebuild).
    /// Layout, mesh generation and texture loads land in the following frames: sample them.
    /// </summary>
    public static string OpenTimeline()
    {
        ScreenController controller = FindFirstObjectByType<ScreenController>()
            ?? throw new InvalidOperationException("No ScreenController in the scene.");
        long gc = GC.CollectionCount(0);
        Stopwatch watch = Stopwatch.StartNew();
        controller.NavigateTo(new NavigationItem(ScreenType.Timeline));
        watch.Stop();
        return $"OpenTimeline syncMs={watch.Elapsed.TotalMilliseconds:F1} gc0={GC.CollectionCount(0) - gc}. "
            + "Call Snapshot() in a later eval: replaced cards are destroyed at end of frame.";
    }

    /// <summary>Times one footer toggle (rebuild included). Call twice to restore the filter.</summary>
    public static string ToggleFilter(int categoryId)
    {
        TimelineScreenview screen = TimelineScreen();
        MethodInfo toggle = Method(typeof(TimelineScreenview), "OnFooterToggle");
        long gc = GC.CollectionCount(0);
        Stopwatch watch = Stopwatch.StartNew();
        toggle.Invoke(screen, new object[] { categoryId });
        watch.Stop();
        return $"ToggleFilter({categoryId}) syncMs={watch.Elapsed.TotalMilliseconds:F1} gc0={GC.CollectionCount(0) - gc}";
    }

    // ---------------------------------------------------------------- state

    public static string Snapshot()
    {
        TimelineScreenview screen = TimelineScreen();
        TimelineCard[] all = screen.GetComponentsInChildren<TimelineCard>(true);
        TimelineCard[] cards = screen.GetComponentsInChildren<TimelineCard>(false);
        int cycling = 0, dots = 0, layers = 0, visible = 0, counters = 0;
        HashSet<Texture> onScreen = new HashSet<Texture>();
        foreach (TimelineCard card in all)
            if (card.IsVisible) visible++;
        foreach (TimelineCard card in cards)
        {
            if (card.enabled) cycling++;
            // Pooled cards keep inactive dot and counter children: count only the shown ones.
            foreach (TMPro.TMP_Text text in card.GetComponentsInChildren<TMPro.TMP_Text>(false))
                if (text.name == "Counter") counters++;
            foreach (Image image in card.GetComponentsInChildren<Image>(false))
                if (image.name == "Dot") dots++;
            foreach (RawImage raw in card.GetComponentsInChildren<RawImage>(false))
            {
                if (!raw.enabled || raw.texture == null) continue;
                layers++;
                onScreen.Add(raw.texture);
            }
        }

        int graphics = screen.GetComponentsInChildren<Graphic>(true).Length;
        int texts = screen.GetComponentsInChildren<TMPro.TMP_Text>(true).Length;
        RectTransform content = (RectTransform)Field(typeof(TimelineScreenview), "_content").GetValue(screen);

        TextureCacheManager cacheManager = Core.main.TextureCacheManager;
        var cache = (Dictionary<string, Texture2D>)Field(typeof(TextureCacheManager), "imageDictionary").GetValue(cacheManager);
        int pending = ((ICollection)Field(typeof(TextureCacheManager), "pendingRequests").GetValue(cacheManager)).Count;
        long cacheBytes = 0, cachePixels = 0;
        foreach (Texture2D texture in cache.Values)
        {
            if (texture == null) continue;
            cacheBytes += Profiler.GetRuntimeMemorySizeLong(texture);
            cachePixels += (long)texture.width * texture.height;
        }

        TimelineTextureCache own = screen.TextureCache;
        string timelineCache = own == null ? "timelineCache=none " :
            $"timelineCache entries={own.Entries} readyMiB={own.ReadyBytes / 1048576.0:F1} pinnedMiB={own.PinnedBytes / 1048576.0:F1} "
            + $"highWaterMiB={own.HighWaterBytes / 1048576.0:F1} budgetMiB={own.BudgetBytes / 1048576.0:F0} active={own.Active} "
            + $"queued={own.Queued} maxActiveSeen={own.MaxActiveSeen} loads={own.LoadsStarted} coalesced={own.Coalesced} "
            + $"evictions={own.Evictions} prefetchDropped={own.PrefetchDropped} failures={own.Failures} "
            + $"overBudgetEvents={own.OverBudgetEvents} ";

        return $"Snapshot layoutEventCount={screen.LayoutEventCount} activeViews={screen.ActiveCardCount} "
            + $"pooledViews={screen.PooledCardCount} cardsTotal={all.Length} cardsActive={cards.Length} cardsVisible={visible} cycling={cycling} "
            + $"dots={dots} counters={counters} enabledPhotoLayers={layers} " + timelineCache
            + $"distinctOnScreenTextures={onScreen.Count} graphics={graphics} tmpTexts={texts} "
            + $"contentWidth={content.sizeDelta.x:F0} frameworkCacheEntries={cache.Count} cachePixels={cachePixels} "
            + $"frameworkCacheRuntimeMiB={cacheBytes / 1048576.0:F1} cacheRgba32MiB={cachePixels * 4 / 1048576.0:F1} pendingRequests={pending} "
            + $"allocatedMiB={Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F1} "
            + $"reservedMiB={Profiler.GetTotalReservedMemoryLong() / 1048576.0:F1} "
            + $"monoUsedMiB={Profiler.GetMonoUsedSizeLong() / 1048576.0:F1} gc0Total={GC.CollectionCount(0)}";
    }

    // ---------------------------------------------------------------- frames

    /// <summary>
    /// Records unscaled frame times for <paramref name="seconds"/>. With <paramref name="scroll"/>, drives
    /// the timeline from start to end and back over the duration. Read with <see cref="FrameReport"/>.
    /// </summary>
    public static string StartFrames(float seconds, bool scroll)
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Frame sampling needs Play mode.");
        if (_sampler != null)
            Destroy(_sampler.gameObject);

        _sampler = new GameObject("TimelineScalingBaseline").AddComponent<TimelineScalingBaseline>();
        _sampler._duration = Mathf.Max(0.5f, seconds);
        _sampler._scroll = scroll;
        _sampler._scrollRect = scroll ? TimelineScreen().GetComponentInChildren<ScrollRect>() : null;
        _lastReport = "running";
        return $"sampling {_sampler._duration:F1}s scroll={scroll}";
    }

    public static string FrameReport()
    {
        return _lastReport;
    }

    private void Update()
    {
        // The frame that ran StartFrames carries the eval's own cost; start counting after it.
        if (_skipFirst)
        {
            _skipFirst = false;
            _started = Time.unscaledTime;
            _gcStart = GC.CollectionCount(0);
            _allocStart = Profiler.GetTotalAllocatedMemoryLong();
            return;
        }

        _frames.Add(Time.unscaledDeltaTime * 1000f);
        float elapsed = Time.unscaledTime - _started;

        if (_scrollRect != null)
            _scrollRect.horizontalNormalizedPosition = Mathf.PingPong(elapsed * 2f / _duration, 1f);

        if (elapsed < _duration)
            return;

        _lastReport = Report();
        _sampler = null;
        Destroy(gameObject);
    }

    private string Report()
    {
        _frames.Sort();
        int n = _frames.Count;
        float sum = 0f;
        int over16 = 0, over33 = 0;
        foreach (float ms in _frames)
        {
            sum += ms;
            if (ms > 16.7f) over16++;
            if (ms > 33.4f) over33++;
        }

        Func<float, float> pct = p => n == 0 ? 0f : _frames[Mathf.Clamp(Mathf.CeilToInt(p * n) - 1, 0, n - 1)];
        return $"FrameReport scroll={_scroll} frames={n} meanMs={(n == 0 ? 0f : sum / n):F2} p50={pct(0.5f):F2} "
            + $"p95={pct(0.95f):F2} p99={pct(0.99f):F2} maxMs={(n == 0 ? 0f : _frames[n - 1]):F2} "
            + $"over16_7={over16} over33_4={over33} gc0={GC.CollectionCount(0) - _gcStart} "
            + $"allocatedDeltaMiB={(Profiler.GetTotalAllocatedMemoryLong() - _allocStart) / 1048576.0:F1} | {Snapshot()}";
    }

    // ---------------------------------------------------------------- reflection

    private static TimelineScreenview TimelineScreen()
    {
        return FindFirstObjectByType<TimelineScreenview>(FindObjectsInactive.Include)
            ?? throw new InvalidOperationException("No TimelineScreenview in the scene.");
    }

    private static FieldInfo Field(Type type, string name)
    {
        return type.GetField(name, Any) ?? throw new MissingFieldException(type.Name, name);
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name, Any) ?? throw new MissingMethodException(type.Name, name);
    }
}
#endif
