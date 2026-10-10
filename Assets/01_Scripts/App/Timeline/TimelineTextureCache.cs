using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using EnigmaVirtual;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Profiling;

/// <summary>
/// The timeline's own texture cache, owned by <see cref="TimelineScreenview"/>. Loads are queued by
/// priority, coalesced per normalized file, and at most <see cref="MaxActive"/> run at once. A caller
/// gets a <see cref="Lease"/>; a leased texture is pinned and never destroyed. Unpinned textures stay
/// cached, least recently used first out, while the total stays under the budget.
/// </summary>
/// <remarks>
/// The budget is SOFT for what is on screen: unpinned textures are evicted first, then those pinned only
/// by prefetch leases, but a texture pinned by a visible lease is never destroyed. If the visible working
/// set alone exceeds the budget it is kept and reported once per crossing (<see cref="OverBudgetEvents"/>).
/// Prefetch never pushes past it: a prefetch is not started while the cache is at or over budget.
/// Rules a caller must keep:
/// - Every lease passed to a callback belongs to the callee, including one it no longer wants: dispose
///   it. A cancelled ticket's callback is never invoked.
/// - Clear any RawImage showing a lease's texture BEFORE disposing the lease.
/// - A prefetch lease may be revoked under memory pressure; its <see cref="Lease.Texture"/> then reads
///   null. Never display a prefetch lease's texture - request it again at Visible priority.
/// </remarks>
public sealed class TimelineTextureCache
{
    public enum Priority { Visible = 0, Prefetch = 1 }

    /// <summary>Starts loading <c>path</c>. Must call <c>done</c> exactly once, with null on failure or
    /// abort, and may return an abort action.</summary>
    public delegate Action Loader(string path, Action<Texture2D> done);

    public const int MaxActive = 2;
    private const int LoadTimeoutSeconds = 30;

    public sealed class Ticket
    {
        internal Entry entry;
        internal Action<Lease> callback;
        internal Priority priority;
        // Set by Cancel even while a delivery batch is running; a cancelled ticket is never called back.
        internal bool cancelled;

        /// <summary>Delivered or cancelled.</summary>
        public bool IsDone { get; internal set; }

        public void Cancel()
        {
            if (IsDone || cancelled)
                return;
            cancelled = true;
            IsDone = true;
            entry?.cache.CancelTicket(this);
        }
    }

    public sealed class Lease : IDisposable
    {
        internal Entry entry;
        internal bool prefetch;
        private bool _disposed;

        /// <summary>Null once disposed, or when a prefetch lease was revoked under memory pressure.</summary>
        public Texture2D Texture { get { return _disposed || entry.evicted ? null : entry.texture; } }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            entry.cache.ReleaseLease(this);
        }
    }

    internal sealed class Entry
    {
        public TimelineTextureCache cache;
        public string key;
        public Texture2D texture;
        public long bytes;
        public bool loading, ready, evicted;
        public Action abort;
        public int leases, prefetchLeases;
        public long order; // FIFO while queued, LRU stamp once ready
        public readonly List<Ticket> waiting = new List<Ticket>(2);
    }

    private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private readonly List<Entry> _queue = new List<Entry>();
    private readonly HashSet<UnityWebRequest> _live = new HashSet<UnityWebRequest>();
    private readonly Loader _loader;
    private readonly Func<string, string> _normalize;
    private readonly MonoBehaviour _host;
    private int _active;
    private long _clock;
    private bool _disposed;
    private bool _overBudgetReported;
    // Self-checks provoke failures on purpose; this keeps them out of the console.
    internal bool quiet;
    // Bytes charged per texture, captured once at load. Checks fix it: the profiler's figure for a fresh
    // editor texture is not stable enough to build budget arithmetic on.
    internal Func<Texture2D, long> measure = EstimateBytes;

    public long BudgetBytes;
    public long ReadyBytes { get; private set; }
    public long HighWaterBytes { get; private set; }
    public int Active { get { return _active; } }
    public int Queued { get { return _queue.Count; } }
    public int Entries { get { return _entries.Count; } }
    public int MaxActiveSeen { get; private set; }
    public int LoadsStarted { get; private set; }
    public int Coalesced { get; private set; }
    public int Evictions { get; private set; }
    public int PrefetchDropped { get; private set; }
    public int Failures { get; private set; }
    public int OverBudgetEvents { get; private set; }

    /// <summary>Production cache: loads files under the asset root with coroutines on <paramref name="host"/>.</summary>
    public TimelineTextureCache(MonoBehaviour host, long budgetBytes)
    {
        _host = host;
        _loader = LoadFile;
        _normalize = ResolvePath;
        BudgetBytes = budgetBytes;
    }

    /// <summary>Cache with an injected loader, for checks.</summary>
    internal TimelineTextureCache(long budgetBytes, Loader loader, Func<string, string> normalize)
    {
        _loader = loader;
        _normalize = normalize;
        BudgetBytes = budgetBytes;
    }

    public long PinnedBytes
    {
        get
        {
            long pinned = 0;
            foreach (Entry e in _entries.Values)
                if (e.ready && e.leases > 0) pinned += e.bytes;
            return pinned;
        }
    }

    /// <summary>
    /// Asks for <paramref name="path"/>. The callback gets a lease, or null on failure, when a prefetch
    /// is dropped, or after <see cref="Dispose"/>. It runs synchronously when the texture is cached.
    /// </summary>
    public Ticket Request(string path, Priority priority, Action<Lease> callback)
    {
        Ticket ticket = new Ticket { callback = callback, priority = priority };

        if (_disposed || string.IsNullOrEmpty(path))
        {
            Deliver(ticket, null);
            return ticket;
        }

        // A malformed CMS path must fail this one request, not the caller's loop over every card.
        string key;
        try
        {
            key = _normalize(path);
        }
        catch (Exception e)
        {
            Report(e, $"TimelineTextureCache: cannot resolve [{path}]; the image is skipped.");
            key = null;
        }

        if (string.IsNullOrEmpty(key))
        {
            Failures++;
            Deliver(ticket, null);
            return ticket;
        }

        if (!_entries.TryGetValue(key, out Entry entry))
        {
            entry = new Entry { cache = this, key = key };
            _entries[key] = entry;
        }
        else if (!entry.ready)
        {
            Coalesced++;
        }

        ticket.entry = entry;

        if (entry.ready)
        {
            entry.order = ++_clock;
            Deliver(ticket, Grant(entry, priority));
            return ticket;
        }

        entry.waiting.Add(ticket);
        if (!entry.loading && !_queue.Contains(entry))
        {
            entry.order = ++_clock;
            _queue.Add(entry);
        }

        Pump();
        return ticket;
    }

    private Lease Grant(Entry entry, Priority priority)
    {
        bool prefetch = priority == Priority.Prefetch;
        entry.leases++;
        if (prefetch)
            entry.prefetchLeases++;
        return new Lease { entry = entry, prefetch = prefetch };
    }

    private void CancelTicket(Ticket ticket)
    {
        // Not waiting: already taken into a delivery batch, which skips it and disposes its grant.
        Entry entry = ticket.entry;
        if (!entry.waiting.Remove(ticket))
            return;

        // Only the last subscriber leaving abandons the load.
        if (entry.waiting.Count > 0 || entry.ready || entry.evicted)
            return;

        _entries.Remove(entry.key);
        entry.evicted = true;
        if (!_queue.Remove(entry) && entry.loading)
            entry.abort?.Invoke(); // the slot stays taken until the loader reports back
    }

    private void ReleaseLease(Lease lease)
    {
        Entry entry = lease.entry;
        if (entry.evicted)
            return; // revoked or disposed: nothing is counted any more

        entry.leases--;
        if (lease.prefetch)
            entry.prefetchLeases--;
        if (entry.leases == 0)
            entry.order = ++_clock;
        Trim();
    }

    private void Pump()
    {
        while (!_disposed && _active < MaxActive && _queue.Count > 0)
        {
            // Visible before prefetch; oldest first within a priority. A visible ticket joining a queued
            // prefetch therefore promotes it.
            Entry best = null;
            Priority bestPriority = Priority.Prefetch;
            foreach (Entry e in _queue)
            {
                Priority p = BestPriority(e);
                if (best == null || p < bestPriority || (p == bestPriority && e.order < best.order))
                {
                    best = e;
                    bestPriority = p;
                }
            }

            _queue.Remove(best);

            if (bestPriority == Priority.Prefetch && ReadyBytes >= BudgetBytes)
            {
                // Under pressure a prefetch is not worth a load: answer it with nothing.
                PrefetchDropped++;
                _entries.Remove(best.key);
                best.evicted = true;
                Notify(best, false);
                continue;
            }

            best.loading = true;
            _active++;
            LoadsStarted++;
            MaxActiveSeen = Math.Max(MaxActiveSeen, _active);

            // Completes at most once, whatever the loader does: the slot is freed exactly once, and a texture
            // reported a second time is destroyed rather than leaked.
            Entry started = best;
            bool finished = false;
            Action<Texture2D> done = texture =>
            {
                if (finished)
                {
                    DestroyTexture(texture);
                    return;
                }
                finished = true;
                OnLoaded(started, texture);
            };

            try
            {
                Action abort = _loader(started.key, done);
                if (!finished)
                    started.abort = abort;
            }
            catch (Exception e)
            {
                Report(e, $"TimelineTextureCache: the loader threw for [{started.key}].");
                done(null);
            }
        }
    }

    private static Priority BestPriority(Entry entry)
    {
        Priority best = Priority.Prefetch;
        foreach (Ticket t in entry.waiting)
            if (t.priority < best) best = t.priority;
        return best;
    }

    private void OnLoaded(Entry entry, Texture2D texture)
    {
        _active--;
        entry.loading = false;
        entry.abort = null;

        if (_disposed || entry.evicted)
        {
            // Abandoned while loading: nobody is waiting, so the result is not kept.
            DestroyTexture(texture);
            Pump();
            return;
        }

        if (texture == null)
        {
            Failures++;
            _entries.Remove(entry.key);
            entry.evicted = true;
            Notify(entry, false);
            Pump();
            return;
        }

        entry.texture = texture;
        entry.bytes = measure(texture);
        entry.ready = true;
        entry.order = ++_clock;
        ReadyBytes += entry.bytes;
        HighWaterBytes = Math.Max(HighWaterBytes, ReadyBytes);

        // Leases first, so the new texture is pinned before anything is trimmed.
        Notify(entry, true);
        Trim();
        Pump();
    }

    private void Notify(Entry entry, bool success)
    {
        Ticket[] waiting = entry.waiting.ToArray();
        entry.waiting.Clear();

        // Every subscriber is pinned before any callback runs: a first consumer disposing its lease must
        // not let Trim evict the texture the later ones are about to receive.
        Lease[] grants = new Lease[waiting.Length];
        if (success)
            for (int i = 0; i < waiting.Length; i++)
                grants[i] = Grant(entry, waiting[i].priority);

        for (int i = 0; i < waiting.Length; i++)
            Deliver(waiting[i], grants[i]);
    }

    /// <summary>
    /// The single way a lease reaches a caller. A ticket cancelled meanwhile - including by an earlier
    /// callback in the same batch - is skipped and its grant released; a callback that throws is reported
    /// and its grant released, and the remaining subscribers are still served.
    /// </summary>
    private void Deliver(Ticket ticket, Lease lease)
    {
        if (ticket.cancelled)
        {
            lease?.Dispose();
            return;
        }

        ticket.IsDone = true;
        try
        {
            ticket.callback?.Invoke(lease);
        }
        catch (Exception e)
        {
            Report(e, "TimelineTextureCache: a texture callback threw; its lease is released.");
            lease?.Dispose();
        }
    }

    private void Report(Exception e, string message)
    {
        if (quiet)
            return;
        // Exception first: catching it removed the stack trace Unity would have printed.
        Development.LogException(e);
        Development.LogError(message);
    }

    /// <summary>
    /// Brings the total under budget: unpinned textures first, oldest first; then textures pinned only by
    /// prefetch leases. What is left over budget is the pinned working set, which is never destroyed -
    /// it is reported once each time the cache goes over.
    /// </summary>
    private void Trim()
    {
        for (int pass = 0; pass < 2 && ReadyBytes > BudgetBytes; pass++)
        {
            while (ReadyBytes > BudgetBytes)
            {
                Entry victim = null;
                foreach (Entry e in _entries.Values)
                {
                    bool candidate = e.ready && (pass == 0 ? e.leases == 0 : e.leases > 0 && e.leases == e.prefetchLeases);
                    if (candidate && (victim == null || e.order < victim.order))
                        victim = e;
                }

                if (victim == null)
                    break;
                if (pass == 1)
                    PrefetchDropped++;
                Evict(victim);
            }
        }

        if (ReadyBytes <= BudgetBytes)
        {
            _overBudgetReported = false;
            return;
        }

        if (_overBudgetReported)
            return;
        _overBudgetReported = true;
        OverBudgetEvents++;
        if (!quiet)
            Development.LogError($"TimelineTextureCache: on-screen images need {ReadyBytes / 1048576.0:F1} MiB, over the "
                + $"{BudgetBytes / 1048576.0:F0} MiB budget. Nothing visible is dropped; raise the budget or lower "
                + "the visibility margin if this repeats.");
    }

    /// <summary>
    /// Re-measures every cached texture, corrects the accounted total and trims against it. A texture's
    /// size is first taken when it loads; the runtime size may change after that first sampling, so the
    /// load-time total can undercount what is resident. Called about
    /// once a second by the screen; call it before asserting on <see cref="ReadyBytes"/>. Visible pins are
    /// never dropped - an excess they cause is reported as usual. Returns how many sizes changed.
    /// </summary>
    public int ReconcileBytes()
    {
        if (_disposed)
            return 0;

        long total = 0;
        int changed = 0;
        foreach (Entry e in _entries.Values)
        {
            if (!e.ready)
                continue;

            // A texture destroyed behind the cache's back holds nothing any more.
            long now = e.texture != null ? measure(e.texture) : 0;
            if (now != e.bytes)
            {
                e.bytes = now;
                changed++;
            }
            total += e.bytes;
        }

        ReadyBytes = total;
        HighWaterBytes = Math.Max(HighWaterBytes, ReadyBytes);
        Trim();
        return changed;
    }

    private void Evict(Entry entry)
    {
        _entries.Remove(entry.key);
        entry.evicted = true;
        entry.ready = false;
        ReadyBytes -= entry.bytes;
        Evictions++;
        DestroyTexture(entry.texture);
        entry.texture = null;
    }

    /// <summary>
    /// Destroys every cached texture and aborts every load. Call only after every consumer has cleared
    /// its images; leases disposed afterwards are no-ops, and later requests answer null.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (Entry e in _entries.Values)
        {
            e.evicted = true;
            foreach (Ticket t in e.waiting)
                t.IsDone = true;
            e.waiting.Clear();
            DestroyTexture(e.texture);
            e.texture = null;
        }

        // A stopped coroutine runs no finally, so the requests are disposed here, not by their routines.
        foreach (UnityWebRequest www in _live)
        {
            www.Abort();
            www.Dispose();
        }

        _live.Clear();
        _entries.Clear();
        _queue.Clear();
        ReadyBytes = 0;
    }

    /// <summary>
    /// Bytes counted against the budget: the profiler's figure, but never less than RGBA32 with a full
    /// mip chain. The profiler reports 0 in non-development players, and the decoder's format is not ours.
    /// </summary>
    internal static long EstimateBytes(Texture2D texture)
    {
        long estimate = (long)texture.width * texture.height * 4;
        if (texture.mipmapCount > 1)
            estimate = estimate * 4 / 3;
        return Math.Max(Profiler.GetRuntimeMemorySizeLong(texture), estimate);
    }

    internal int Leases(string path)
    {
        return _entries.TryGetValue(_normalize(path), out Entry e) ? e.leases : 0;
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (texture == null)
            return;
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(texture);
        else
            UnityEngine.Object.DestroyImmediate(texture);
    }

    // ---------------------------------------------------------------- production loader

    /// <summary>One key per file: the full asset path, forward slashes.</summary>
    private static string ResolvePath(string url)
    {
        if (url.Contains("://") || !Core.IsSet)
            return url;
        return Path.GetFullPath(Core.settings.AppData.GetAssetPath(url)).Replace('\\', '/');
    }

    private Action LoadFile(string path, Action<Texture2D> done)
    {
        // Remote urls were never served to the timeline; downloads are a separate, validated step.
        if (path.Contains("://"))
        {
            Development.LogError($"TimelineTextureCache: [{path}] is a remote url; only local asset files are loaded.");
            done(null);
            return null;
        }

        // StartCoroutine on an inactive object logs and returns null, which would hold a slot forever.
        if (_host == null || !_host.gameObject.activeInHierarchy)
        {
            done(null);
            return null;
        }

        UnityWebRequest www = UnityWebRequestTexture.GetTexture(AppPaths.ToFileUrl(path), true);
        www.timeout = LoadTimeoutSeconds;
        _live.Add(www);
        _host.StartCoroutine(LoadRoutine(www, path, done));
        return www.Abort;
    }

    private IEnumerator LoadRoutine(UnityWebRequest www, string path, Action<Texture2D> done)
    {
        // finally, not catch: C# forbids catch around a yield. A throw from SendWebRequest or GetContent
        // still disposes the request and completes the load (with null) exactly once; the exception then
        // reaches Unity's log with its stack. A coroutine Unity STOPS runs no finally - Dispose covers that.
        Texture2D texture = null;
        try
        {
            yield return www.SendWebRequest();

            // Disposed while waiting: the request is already aborted and disposed, so do not touch it.
            if (!_disposed)
            {
                if (www.result == UnityWebRequest.Result.Success)
                    texture = DownloadHandlerTexture.GetContent(www);
                else if (www.error != "Request aborted")
                    Development.LogError($"TimelineTextureCache: could not load [{path}]: {www.error}");
            }
        }
        finally
        {
            _live.Remove(www);
            www.Dispose();
            done(texture);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Queue, coalescing, max-2, cancel/abort, failure, leases, eviction, prefetch drop and dispose,
    /// against a manual loader. Runnable from eval as <c>TimelineTextureCache.SelfCheck()</c>.
    /// </summary>
    public static string SelfCheck()
    {
        int checks = 0;
        Action<bool, string> check = (condition, what) =>
        {
            checks++;
            if (!condition)
                throw new Exception("TimelineTextureCache self-check FAILED: " + what);
        };

        Dictionary<string, Action<Texture2D>> pending = new Dictionary<string, Action<Texture2D>>();
        List<string> aborted = new List<string>();
        List<Texture2D> made = new List<Texture2D>();
        Func<Texture2D> tex = () => { Texture2D t = new Texture2D(4, 4, TextureFormat.RGBA32, false); made.Add(t); return t; };
        Action<string, Texture2D> complete = (key, t) => { Action<Texture2D> d = pending[key]; pending.Remove(key); d(t); };
        Loader manual = (p, done) => { pending[p] = done; return () => aborted.Add(p); };

        // Every texture counts exactly one unit, so the budget arithmetic below is deterministic.
        const long unit = 100;
        Func<Texture2D, long> fixedSize = t => unit;
        TimelineTextureCache cache = new TimelineTextureCache(unit * 5 / 2, manual, p => p.Replace('\\', '/'));
        cache.quiet = true;
        cache.measure = fixedSize;
        // Everything is over this budget from the first texture, so every check below runs under pressure.
        TimelineTextureCache tight = new TimelineTextureCache(unit / 2, manual, p =>
        {
            if (p.Contains("|")) throw new ArgumentException("bad path");
            return p;
        });
        tight.quiet = true;
        tight.measure = fixedSize;

        try
        {
            Lease a1 = null, a2 = null;
            Ticket t1 = cache.Request("x/a", Priority.Visible, l => a1 = l);
            cache.Request("x\\a", Priority.Visible, l => a2 = l);
            cache.Request("b", Priority.Visible, l => { });
            int cCalls = 0;
            Lease cLease = null;
            cache.Request("c", Priority.Prefetch, l => cCalls++);
            cache.Request("d", Priority.Visible, l => l?.Dispose());
            check(cache.LoadsStarted == 2 && cache.Active == 2 && cache.Queued == 2, "at most two loads run");
            check(cache.Coalesced == 1 && pending.ContainsKey("x/a") && !pending.ContainsKey("x\\a"), "same file coalesced after normalizing");

            complete("x/a", tex());
            check(a1 != null && a2 != null && a1.Texture == a2.Texture && cache.Leases("x/a") == 2, "both subscribers leased one texture");
            check(t1.IsDone && pending.ContainsKey("d") && !pending.ContainsKey("c"), "visible starts before an older prefetch");

            cache.Request("c", Priority.Visible, l => cLease = l);
            complete("b", tex());
            check(pending.ContainsKey("c"), "a visible request promotes the queued prefetch");
            complete("c", tex());
            check(cCalls == 1 && cLease != null && cache.Leases("c") == 2, "promoted load answers both subscribers");
            check(cache.MaxActiveSeen == 2, "never more than two at once");

            // a, b and c are pinned (b by a lease its callback ignored, c by both its tickets): 3 units
            // against 2.5. d's callback disposes its lease, so d is unpinned and goes at once, although
            // it is the newest; the pinned set stays and is reported once.
            complete("d", tex());
            check(cache.Evictions == 1 && cache.Entries == 3 && cache.Leases("b") == 1 && cache.OverBudgetEvents == 1,
                "unpinned evicted first; pinned working set kept and reported once (evictions " + cache.Evictions
                + ", entries " + cache.Entries + ", leases b " + cache.Leases("b") + ", overBudgetEvents "
                + cache.OverBudgetEvents + ", ready " + cache.ReadyBytes + "/" + cache.BudgetBytes + ")");
            check(a1.Texture != null, "a pinned texture is never destroyed");

            // Cancel while queued: never started. Cancel while loading: aborted, result discarded.
            Ticket g = cache.Request("g", Priority.Visible, l => check(false, "cancelled callback ran"));
            Ticket h = cache.Request("h", Priority.Visible, l => check(false, "cancelled callback ran"));
            Ticket i = cache.Request("i", Priority.Visible, l => check(false, "cancelled callback ran"));
            check(pending.ContainsKey("g") && pending.ContainsKey("h") && cache.Queued == 1, "i is queued");
            i.Cancel();
            check(cache.Queued == 0 && !pending.ContainsKey("i"), "a queued cancel never loads");
            h.Cancel();
            check(aborted.Contains("h") && cache.Active == 2, "the last subscriber leaving aborts; the slot is held until it ends");
            Texture2D late = tex();
            complete("h", late);
            check(late == null && cache.Active == 1, "an aborted load's late texture is destroyed");
            Lease g2 = null;
            Ticket gOther = cache.Request("g", Priority.Visible, l => g2 = l);
            g.Cancel();
            check(!aborted.Contains("g"), "cancelling one of two subscribers does not abort");
            complete("g", tex());
            check(g2 != null && cache.Leases("g") == 1, "the remaining subscriber is served");

            // Failure: answered with null, not cached, retried by the next request.
            int failCalls = 0;
            cache.Request("bad", Priority.Visible, l => { failCalls++; check(l == null, "failure gives null"); });
            complete("bad", null);
            check(failCalls == 1 && cache.Failures == 1, "failure reported once");
            cache.Request("bad", Priority.Visible, l => l?.Dispose());
            check(pending.ContainsKey("bad"), "a failed file is retried");
            complete("bad", null);

            // Still over (a, b, c, g pinned). Releasing: c keeps only its ignored prefetch lease, so it is
            // revoked; g is then unpinned and evicted; a and b remain.
            check(cache.OverBudgetEvents == 1, "staying over budget is not re-reported");
            a2.Dispose();
            a2.Dispose();
            check(cache.Leases("x/a") == 1, "disposing twice releases once");
            cLease.Dispose();
            check(cache.Leases("c") == 0 && cache.Entries == 3, "a texture pinned only by prefetch is revoked under pressure");
            g2.Dispose();
            check(cache.ReadyBytes <= cache.BudgetBytes && cache.Entries == 2, "released textures make room");

            // A prefetch-only pin is revoked under pressure; its lease then reads null.
            Lease pre = null;
            cache.Request("p", Priority.Prefetch, l => pre = l);
            complete("p", tex());
            Lease v1 = null, v2 = null;
            cache.Request("v1", Priority.Visible, l => v1 = l);
            cache.Request("v2", Priority.Visible, l => v2 = l);
            complete("v1", tex());
            complete("v2", tex());
            check(pre != null && pre.Texture == null && v1.Texture != null && v2.Texture != null, "prefetch pin revoked before visible");
            pre.Dispose();

            // Over budget with pinned textures: a new prefetch is dropped without loading.
            int dropped = cache.PrefetchDropped;
            Lease none = new Lease();
            cache.Request("q", Priority.Prefetch, l => none = l);
            check(none == null && cache.PrefetchDropped == dropped + 1 && !pending.ContainsKey("q"), "prefetch dropped under pressure");

            // Dispose destroys cached textures; late completions and leases are harmless.
            Texture2D shown = v1.Texture;
            bool called = false;
            cache.Request("z", Priority.Visible, l => called = true);
            cache.Dispose();
            check(shown == null && v1.Texture == null, "dispose destroys cached textures");
            Texture2D after = tex();
            complete("z", after);
            check(!called && after == null, "a load finishing after dispose is discarded");
            v1.Dispose();
            Lease postDispose = new Lease();
            cache.Request("y", Priority.Visible, l => postDispose = l);
            check(postDispose == null && cache.HighWaterBytes > 0, "requests after dispose answer null");

            // ---- delivery under pressure, on the tight cache (budget half a texture).
            pending.Clear();

            // The first subscriber disposing at once must not evict the texture before the second gets it.
            bool secondHadTexture = false;
            tight.Request("k", Priority.Visible, l => l.Dispose());
            tight.Request("k", Priority.Visible, l => { secondHadTexture = l.Texture != null; l.Dispose(); });
            complete("k", tex());
            check(secondHadTexture, "all subscribers are pinned before the first callback runs");
            check(tight.Entries == 0, "unpinned after both, so the over-budget texture is evicted");

            // A sibling cancelled by an earlier callback in the batch is skipped, and its grant released.
            Ticket sibling = null;
            int siblingCalls = 0;
            Lease m1 = null, m3 = null;
            tight.Request("m", Priority.Visible, l => { m1 = l; sibling.Cancel(); });
            sibling = tight.Request("m", Priority.Visible, l => siblingCalls++);
            tight.Request("m", Priority.Visible, l => m3 = l);
            complete("m", tex());
            check(siblingCalls == 0 && sibling.IsDone && m1 != null && m3 != null && tight.Leases("m") == 2,
                "cancelled mid-batch: no callback, grant released, the others served");
            m1.Dispose();
            m3.Dispose();

            // A throwing callback has its grant released, uncached and cached, and the others are served.
            Lease t2 = null;
            tight.Request("t", Priority.Visible, l => { throw new InvalidOperationException("consumer"); });
            tight.Request("t", Priority.Visible, l => t2 = l);
            complete("t", tex());
            check(t2 != null && t2.Texture != null && tight.Leases("t") == 1, "uncached throw: grant released, sibling served");
            tight.Request("t", Priority.Visible, l => { throw new InvalidOperationException("consumer"); });
            check(tight.Leases("t") == 1, "cached throw: grant released");
            t2.Dispose();

            // A path that cannot be normalized fails that request only.
            int badCalls = 0;
            int failuresBefore = tight.Failures;
            tight.Request("bad|path", Priority.Visible, l => { badCalls++; check(l == null, "bad path gives null"); });
            check(badCalls == 1 && tight.Failures == failuresBefore + 1 && tight.Active == 0, "normalize failure answered, nothing started");

            // A loader that throws frees its slot and fails its subscribers; the queue moves on.
            TimelineTextureCache throwing = new TimelineTextureCache(unit * 10,
                (p, done) => p == "boom" ? throw new IOException("loader") : manual(p, done), p => p);
            throwing.quiet = true;
            throwing.measure = fixedSize;
            Lease boomLease = new Lease();
            throwing.Request("boom", Priority.Visible, l => boomLease = l);
            throwing.Request("n1", Priority.Visible, l => l?.Dispose());
            throwing.Request("n2", Priority.Visible, l => l?.Dispose());
            check(boomLease == null && throwing.Failures == 1 && throwing.Active == 2 && pending.ContainsKey("n1")
                && pending.ContainsKey("n2"), "loader throw: slot freed, subscribers failed, queue progressed");

            // Reported twice (a loader that completes and also aborts late): counted once, extra texture destroyed.
            Texture2D extra = tex();
            Action<Texture2D> n1Done = pending["n1"];
            complete("n1", tex());
            n1Done(extra);
            check(extra == null && throwing.Active == 1, "a second completion is ignored and its texture destroyed");
            complete("n2", tex());
            throwing.Dispose();

            // Size growth after load: reconcile corrects the total, evicts unpinned LRU, keeps the pin.
            TimelineTextureCache growing = new TimelineTextureCache(unit * 5 / 2, manual, p => p);
            growing.quiet = true;
            Texture2D grown = null;
            long grownSize = unit;
            growing.measure = t => t == grown ? grownSize : unit;
            try
            {
                Lease pinned = null;
                growing.Request("g1", Priority.Visible, l => pinned = l);
                grown = tex();
                complete("g1", grown);
                growing.Request("g2", Priority.Visible, l => l.Dispose());
                complete("g2", tex());
                check(growing.ReadyBytes == 2 * unit && growing.Entries == 2, "two textures accounted at load size");
                check(growing.ReconcileBytes() == 0, "nothing changed, nothing to correct");

                grownSize = 2 * unit;
                check(growing.ReconcileBytes() == 1 && growing.Evictions == 1 && growing.Entries == 1
                    && growing.ReadyBytes == 2 * unit && pinned.Texture == grown,
                    "growth reconciled: unpinned evicted, pinned kept (ready " + growing.ReadyBytes + ", entries "
                    + growing.Entries + ")");

                grownSize = 4 * unit;
                growing.ReconcileBytes();
                check(pinned.Texture == grown && growing.ReadyBytes == 4 * unit && growing.OverBudgetEvents == 1
                    && growing.HighWaterBytes == 4 * unit, "a pinned excess is kept and reported; high water follows");

                // A texture destroyed behind the cache counts zero and does not throw.
                // Pinned, so the cache keeps the entry while the texture itself is destroyed externally.
                Lease doomedLease = null;
                growing.Request("g3", Priority.Visible, l => doomedLease = l);
                Texture2D doomed = tex();
                complete("g3", doomed);
                UnityEngine.Object.DestroyImmediate(doomed);
                growing.ReconcileBytes();
                check(growing.ReadyBytes == 4 * unit, "destroyed texture reconciled to zero bytes");
                doomedLease.Dispose();
                pinned.Dispose();
            }
            finally
            {
                growing.Dispose();
            }
        }
        finally
        {
            tight.Dispose();
            cache.Dispose();
            foreach (Texture2D t in made)
                if (t != null) UnityEngine.Object.DestroyImmediate(t);
        }

        return "TimelineTextureCache self-check PASS (" + checks + " checks)";
    }
#endif
}
