using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace EnigmaVirtual
{
    public class TextureCacheManager : MonoBehaviour
    {
        public event Action<int> onTotalAssetsEvent;
        //public event Action<int, string, int> onAssetVerificationEvent;
        public event Action<int, string, int> onTextureCachedEvent;
        public event Action onAssetVerificationCompleteEvent;

        public static TextureCacheManager Instance { get; private set; }

        /// <summary>
        /// Floor for how long the cache pass may make *no progress at all* before it gives up and lets
        /// boot continue. This is a stall timeout, not a total budget - a large first-run cache is
        /// legitimately slow, and cutting it off early would fire "complete" while downloads continued in
        /// the background.
        /// </summary>
        /// <remarks>
        /// A floor rather than the whole answer, for the same reason <c>Core</c>'s boot watchdog uses one:
        /// no progress mark can arrive faster than one request, so a budget below the request timeout
        /// fires on a healthy pass. 60s flat was wrong the moment <c>TextureCache.timeout</c> could exceed
        /// it - and it can be up to an hour - which made a single slow image end the pass early and
        /// complete the cache half-empty. See <see cref="CacheStallBudgetSeconds"/>.
        /// </remarks>
        private const float CacheStallTimeoutSeconds = 60f;

        /// <summary>
        /// The stall budget actually applied: the floor, or twice one request's effective timeout,
        /// whichever is larger.
        /// </summary>
        private static float CacheStallBudgetSeconds()
        {
            TextureCacheSettings textureCache = Core.settings?.AppData?.Cache?.TextureCache;

            // The floor when there is nothing to read, and NOT via `?? 0`. On this block 0 means "no
            // limit", so collapsing an absent block to 0 would resolve it to the ceiling and hand this
            // watchdog a two-hour budget for a configuration that never stated one.
            if (textureCache == null)
                return CacheStallTimeoutSeconds;

            return Mathf.Max(CacheStallTimeoutSeconds,
                WebRequestExtensions.EffectiveFileTransferTimeout(textureCache.timeout) * 2f);
        }

        private Dictionary<string, Texture2D> imageDictionary = new Dictionary<string, Texture2D>();
        private List<string> mediaAssets = new List<string>();

        [SerializeField, ReadOnly]
        private int imageDictionaryCount = 0;

        /// <summary>Chunk coroutines finished. The completion wait keys off this, not a per-item count.</summary>
        private int chunkValidated = 0;
        private int chunkTotal = 0;

        /// <summary>
        /// Shared placeholder loaded from Resources. Held so it is looked up once, and so
        /// <see cref="ClearCache"/> never destroys it - destroying a Resources asset corrupts it
        /// for the rest of the session, not just for this cache.
        /// </summary>
        private static Texture2D errorTexture;

        private static Texture2D ErrorTexture
        {
            get
            {
                if (errorTexture == null)
                {
                    Sprite sprite = Resources.Load<Sprite>(AppResources.ImageDownloadErrorSprite);
                    if (sprite == null)
                    {
                        Development.LogError($"Resources/{AppResources.ImageDownloadErrorSprite} is missing - no placeholder available.");
                        return null;
                    }
                    errorTexture = sprite.texture;
                }
                return errorTexture;
            }
        }

        void Awake()
        {
            // No throw here: this component is created by MainController.SetupTextureCacheManager
            // via AddComponent, which runs Awake synchronously - so throwing would abort the boot
            // sequence from inside Init().
            if (Instance != null && Instance != this)
            {
                Development.LogWarning("A second TextureCacheManager was created; destroying the duplicate.");
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            // notifyPending: false. Teardown is the one clear where completing the callbacks is wrong -
            // the consumers are being destroyed in the same pass, so every one would log
            // "lost its RawImage before [url] arrived" on the way out. Nothing is waiting for an answer
            // when the scene is going away.
            ClearCacheInternal(notifyPending: false);
        }

        public void CacheAssets(object data, bool clearCurrentCache = false)
        {
            if (clearCurrentCache)
                ClearCache();

            StartCoroutine(ValidateAssets_IE(data));
        }

        /// <summary>
        /// Every value <c>MediaWalker</c> offered this pass, and how many were dropped for being the
        /// wrong file type. Kept so an empty <c>mediaAssets</c> can say *why* it is empty: "there
        /// is no media" and "there is media but none of it is a .png/.jpg/.jpeg" need opposite responses,
        /// and both used to produce the same silence.
        /// </summary>
        private int walkedValues;
        private int rejectedNotImage;
        private int rejectedOtherMedia;
        private string firstRejectedValue;

        /// <summary>
        /// Whether this pass has already announced completion. The boot chain keys off
        /// <c>onAssetVerificationCompleteEvent</c> - <c>MainController</c>'s handler is what calls
        /// <c>LoadNextScene</c> - and it is raised from five places now, including two error paths that
        /// exist precisely because something unexpected happened. Making the raise idempotent per pass
        /// means those paths can fire it without any of them having to know whether one of the others
        /// already did.
        /// </summary>
        private bool verificationCompleteRaised;

        private void RaiseVerificationComplete()
        {
            if (verificationCompleteRaised)
                return;

            verificationCompleteRaised = true;
            onAssetVerificationCompleteEvent?.Invoke();
        }

        private IEnumerator ValidateAssets_IE(object data)
        {
            mediaAssets = new List<string>();
            walkedValues = 0;
            rejectedNotImage = 0;
            rejectedOtherMedia = 0;
            firstRejectedValue = null;
            verificationCompleteRaised = false;

            yield return new WaitForEndOfFrame();

            if (data != null)
            {
                // Two guards, not one, because the walk and what follows it fail for different reasons
                // and a single catch could only name one of them. Passing null as the callback keeps
                // AssetSorting to just the MediaWalker pass; OnAssetsSorted is invoked separately below.
                //
                // Either one throwing used to kill this coroutine outright: onAssetVerificationCompleteEvent
                // never fired, the app sat on the preloader, and the only report was a generic BOOT STALLED
                // two minutes after Unity had already logged the real exception.
                //
                // LogException first, every time. Catching the exception removes the stack trace Unity
                // would otherwise have printed, so without this the fix trades a hang for a boot that
                // continues with no way to find the cause - a worse deal on a path nobody is watching.
                bool walked = false;

                try
                {
                    AssetSorting(data, null);
                    walked = true;
                }
                catch (Exception e)
                {
                    Development.LogException(e);
                    Development.LogError($"The walk over the content model threw ({e.GetType().Name}), so "
                        + "no textures can be pre-cached. Continuing the boot without a texture cache.");

                    RaiseVerificationComplete();
                }

                if (walked)
                {
                    // Scoped separately so its message can be honest about its own reach: this covers
                    // OnAssetsSorted, the RawMediaValueCount read inside it, onTotalAssetsEvent's
                    // subscribers, and - on the empty-list branch - the completion event's subscribers
                    // too, which is as far as LoadNextScene. "The walk threw" would have been a confident
                    // wrong answer for any of those.
                    try
                    {
                        OnAssetsSorted();
                    }
                    catch (Exception e)
                    {
                        Development.LogException(e);
                        Development.LogError($"Sorting the walked textures, or a handler it notified, threw "
                            + $"({e.GetType().Name}). The texture cache may be incomplete; the boot chain is "
                            + "being released so the app does not hang on it.");

                        RaiseVerificationComplete();
                    }
                }
            }
            else
            {
                // Signal, then return. This branch used to log and fall out of the coroutine, which is the
                // same never-signals defect as the branch below - and worse, because the boot chain
                // waits on this event: MainController.TextureCacheManager_onAssetVerificationCompleteEvent
                // is what calls LoadNextScene. Without it the app sits on the preloader until
                // Core.BootWatchdog_IE reports BOOT STALLED ~125s later and latches _bootFailed, after
                // which LoadNextScene refuses permanently - so a null AppData cost a hung boot whose only
                // report was a generic stall two minutes after the actual cause was logged.
                //
                // AssetManager tolerates a null AppData (MediaWalker just returns), so it hands off
                // cleanly and this is exactly where a boot with no content used to die.
                Development.LogError("Presented Data null or empty, so no textures can be pre-cached. "
                    + "Continuing the boot without a texture cache.");

                RaiseVerificationComplete();
            }

            yield return null;
        }

        private void OnAssetsSorted()
        {
            Development.LogIf(Core.settings.AppData.Cache.TextureCache.doLogs, "Total items sorted: " + mediaAssets.Count);

            onTotalAssetsEvent?.Invoke(mediaAssets.Count);

            if (mediaAssets.IsNullOrEmpty())
            {
                // Say why. This branch used to fire the completion event and return in silence, so a
                // texture cache that cached nothing was indistinguishable from one with nothing to do -
                // and the only report was the Assert above, which is doLogs-gated (ships false) and
                // stripped outside DO_LOGS besides.
                //
                // Three causes, not two, and they need different answers. `walkedValues` alone cannot
                // separate the first two: this walk uses the same MediaWalker and the same
                // AppSettings.isMediaType predicate AssetManager uses, so a wrong mediaPaths yields zero
                // walked values on a payload that is full of media - and calling that "text-only" would
                // be exactly the misdiagnosis this guards against. RawMediaValueCount is counted from
                // the raw JSON before deserialization, so it sees the payload the walker cannot. This
                // discriminator is complementary to AssetManager's, not better than it.
                //
                // The third cause is this file's own, because mediaAssets is the *image subset*:
                // AddMediaAsset drops anything whose extension is not in ImageExtensions, so a .webp
                // content set caches zero textures and, without this, says nothing.
                int declaredMedia = Core.main.AppDataController.RawMediaValueCount;

                if (walkedValues > 0 && rejectedOtherMedia == rejectedNotImage)
                {
                    // Every reject is a video or audio file this build does play. Nothing is wrong: there
                    // are simply no images to pre-cache. Warning, not Error.
                    Development.LogWarning($"Texture cache: all {walkedValues} media value(s) are video or "
                        + "audio, so there are no images to pre-cache. Normal for a video-only content set.");
                }
                else if (walkedValues > 0)
                {
                    // Every walked value was rejected on type: a duplicate needs a prior add, and there
                    // were none, so walkedValues == rejectedNotImage here. One number, printed once.
                    Development.LogError($"Texture cache: walked {walkedValues} media value(s) and "
                        + "recognised none of them as an image, so nothing will be pre-cached. The accepted "
                        + $"extensions are [{string.Join(", ", ImageExtensions)}], "
                        + $"{rejectedNotImage - rejectedOtherMedia} of the values are not a recognised "
                        + $"video or audio type either, and the first one dropped was "
                        + $"[{firstRejectedValue}]. Either the content set uses a format this build does "
                        + "not accept, or the paths are not files.");
                }
                else if (declaredMedia > 0)
                {
                    Development.LogError($"Texture cache: the appData JSON declares {declaredMedia} media "
                        + "value(s), but the walk over the presented data found none, so nothing will be "
                        + "pre-cached. The AppData model cannot see the media the payload contains - the "
                        + "same fault AssetManager reports, and the same place to look: member names "
                        + "against JSON keys, and AppSettings.mediaPaths.");
                }
                else
                {
                    // LogWarning is [Conditional("DO_LOGS")], so unlike the two LogErrors above this one
                    // is stripped outside Standalone and Android. Deliberate: it is the benign case, and
                    // a legitimate configuration should not be an error.
                    Development.LogWarning("Texture cache: the presented data contains no media at all, "
                        + "so there is nothing to pre-cache. Expected for a text-only content set.");
                }

                RaiseVerificationComplete();
                return;
            }

            StartCoroutine(OnAssetsSorted_IE());
        }

        private IEnumerator OnAssetsSorted_IE()
        {
            chunkValidated = 0;
            imageDictionaryCount = 0;

            // Threads can be 0 in settings.json, which made Ceiling(n / 0.0) infinite, the cast
            // undefined, and Split yield no chunks at all - so nothing downloaded and the old
            // per-item wait was satisfied trivially by 0 == 0.
            int configuredThreads = Core.settings.AppData.Cache.AssetDownload.downloadThreads;

            if (configuredThreads < 1)
            {
                // See AssetManager: the clamp is silent unless the log lives at the clamp site.
                Development.LogError($"AppData.Cache.AssetDownload.downloadThreads is {configuredThreads} "
                    + "in settings.json, which is not a usable thread count. Using 1 for texture caching.");
            }

            int threads = Mathf.Max(1, configuredThreads);
            int chunkSize = Mathf.Max(1, (int)Math.Ceiling(mediaAssets.Count / (double)threads));

            List<List<string>> divUrls = mediaAssets.Split(chunkSize)
                .Select(chunk => chunk.ToList())
                .ToList();

            chunkTotal = divUrls.Count;

            for (int i = 0; i < divUrls.Count; i++)
                StartCoroutine(DownloadImages_IE(divUrls[i]));

            // Wait on chunks completing, not on a per-item counter. The old condition
            // (imageDictionaryCount == mediaAssets.Count) could never be met, because
            // DownloadImages_IE skips the increment for external and already-cached URLs - one
            // absolute URL in appData hung startup permanently.
            //
            // The watchdog measures *stall*, not total duration: a legitimate first-run cache of
            // several hundred assets can take far longer than any fixed budget, so the deadline
            // resets every time an item completes. It only fires when nothing has progressed.
            int lastProgress = -1;
            float stallBudget = CacheStallBudgetSeconds();
            float stallDeadline = Time.realtimeSinceStartup + stallBudget;

            while (chunkValidated < chunkTotal)
            {
                if (imageDictionaryCount != lastProgress)
                {
                    lastProgress = imageDictionaryCount;
                    stallDeadline = Time.realtimeSinceStartup + stallBudget;
                }
                else if (Time.realtimeSinceStartup > stallDeadline)
                {
                    Development.LogError($"Texture cache stalled for {stallBudget}s " +
                                         $"({chunkValidated}/{chunkTotal} batches, {imageDictionaryCount}/{mediaAssets.Count} textures). Continuing without it.");
                    break;
                }

                yield return null;
            }

            RaiseVerificationComplete();
        }

        /// <summary>
        /// Collects every media path in <paramref name="data"/> into <c>mediaAssets</c>.
        /// <see cref="AddMediaAsset"/> applies the image-extension filter - that filter is the one
        /// place this differed from AssetManager's copy of the same walk, so it stays at the call
        /// site rather than inside the shared walker.
        /// </summary>
        private void AssetSorting(System.Object data, Action callback = null)
        {
            MediaWalker.Collect(data, Core.main.AppDataController.AppSettings.isMediaType, AddMediaAsset);

            callback?.Invoke();
        }

        private void AddMediaAsset(string value)
        {
            walkedValues++;

            if (!isValidImageFileType(value))
            {
                // Not a duplicate - the wrong file type, which the single `else` below used to report as
                // "Duplicate [x] not added" whatever the reason. That wording is how a .webp content set
                // would hide even with doLogs on: the one line that mentioned it named the wrong
                // cause. The counters feed the empty-list diagnosis in OnAssetsSorted.
                rejectedNotImage++;

                // Classify the reject, because "not an image" is not the same as "not media". mediaPaths
                // ships {"url","thumb"} and those carry video and audio urls too, so a video-only content
                // set walks fine and rejects every value here - and reporting that as a defect is how an
                // operator learns to ignore errors. MediaFile is the single source of truth for the other
                // two extension sets. It used to allocate a HashSet per call, which is what this only
                // running for an already-rejected value was originally justified by; that allocation is
                // gone as of v0.49.4, so the narrow placement is now just the natural one rather
                // than a cost dodge - measured at ~2 us and 192 B saved per call, which is under 1% of
                // what the callers around it spend.
                // IsNullOrEmpty first: MediaFile's helpers call GetFileType() unguarded, and that throws
                // ArgumentException on null or empty - unlike the local isValidImageFileType above, which
                // guards. MediaWalker does not emit empties today, so this is belt-and-braces, but without
                // it this classification would turn a value the old code silently skipped into a throw.
                if (!string.IsNullOrEmpty(value)
                    && (MediaFile.isValidVideoFileType(value) || MediaFile.isValidAudioFileType(value)))
                    rejectedOtherMedia++;

                if (firstRejectedValue == null)
                    firstRejectedValue = value;

                Development.LogIf(Core.settings.AppData.Cache.TextureCache.doLogs, string.Format(@"Not a cacheable image type, skipping [{0}].", value));
                return;
            }

            if (mediaAssets.Contains(value))
            {
                Development.LogIf(Core.settings.AppData.Cache.TextureCache.doLogs, string.Format(@"Duplicate [{0}] not added to list.", value));
                return;
            }

            mediaAssets.Add(value);
            Development.LogIf(Core.settings.AppData.Cache.TextureCache.doLogs, string.Format(@"Added [{0}] to list.", value));
        }

        // static readonly: this was rebuilding the set once per media asset during sorting.
        //
        // .webp is absent DELIBERATELY - do not add it. The register carried its
        // absence as a defect from v0.4.0, but Unity cannot decode webp: Texture2D.LoadImage returns
        // false for one, measured here against a PNG control of the same image. Admitting it would let
        // a file past this gate to a decoder that rejects it, replacing the diagnostic below - which
        // names the rejected extension and this set - with a blank texture. Supporting webp means
        // shipping a decoder, not editing this line. MediaFile.ImageExtensions carries the same set and
        // the same note; keep them in step.
        private static readonly HashSet<string> ImageExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

        private bool isValidImageFileType(string url)
        {
            return !string.IsNullOrEmpty(url) && ImageExtensions.Contains(url.GetFileType());
        }

        private IEnumerator DownloadImages_IE(List<string> imageUrls)
        {
            foreach (string url in imageUrls)
            {
                // Every skip below still counts as processed. The old code left imageDictionaryCount
                // behind for skipped entries, which is what made the completion wait unreachable.
                if (string.IsNullOrEmpty(url))
                {
                    MarkProcessed(url);
                    continue;
                }

                if (url.IsValidUrl())
                {
                    Development.LogError("External URL detected! Currently not supported for local download! " + url);
                    MarkProcessed(url);
                    continue;
                }

                if (imageDictionary.ContainsKey(url))
                {
                    Development.LogIf(Core.settings.AppData.Cache.TextureCache.doLogs, "Already cached, skipping: " + url);
                    MarkProcessed(url);
                    continue;
                }

                // ToFileUrl, or a local file goes to UnityWebRequest with no scheme. This site had no
                // prefix handling at all, so a grep for broken *attempts* rather than for the failure
                // does not find it. The pre-cache pass is
                // dormant behind TextureCache.isActive; its twin in GetOrSetTexture2D is not.
                string fileURL = AppPaths.ToFileUrl(Core.settings.AppData.GetAssetPath(url));

                // using: UnityWebRequest holds a native handle and its download buffer until disposed.
                //
                // nonReadable: true - see the note on GetOrSetTexture2D's own request below. The single-
                // argument overload defaults it to false, which is what made every cached texture carry a
                // second full-size copy in system memory.
                using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(fileURL, nonReadable: true))
                {
                    www.SetFileTransferTimeout(Core.settings.AppData.Cache.TextureCache.timeout);
                    yield return www.SendWebRequest();

                    SetTexture(www, url);
                }
            }

            chunkValidated++;
        }

        /// <summary>
        /// Drops every cached texture. Textures are native objects, so clearing the dictionary
        /// alone frees nothing - the old version leaked the entire cache on every refresh.
        /// </summary>
        /// <remarks>
        /// Destroying is the point, but it means any RawImage still pointing at a cached texture
        /// goes blank rather than merely stale. Safe at boot (the cache is empty) and at teardown.
        /// A mid-session re-cache needs consumers to reload afterwards, and there is currently no
        /// notification for that.
        /// </remarks>
        public void ClearCache()
        {
            ClearCacheInternal(notifyPending: true);
        }

        /// <summary>
        /// The whole of <see cref="ClearCache"/>, plus the decision about in-flight requests.
        /// </summary>
        /// <remarks>
        /// <c>StopAllCoroutines</c> is what makes this necessary. It kills any
        /// <c>DownloadTextureCoroutine</c> mid-request, and before v0.35.2 that callback simply never
        /// arrived: the consumer's <c>RawImage</c> stayed blank for the rest of the session, with no
        /// exception and no log. Unlike almost everything else in this file that path has **no**
        /// <c>isActive</c> gate in front of it - <c>GetOrSetTexture2D</c> is reachable whatever
        /// <c>TextureCache.isActive</c> says - so it was live in every build.
        /// <para>
        /// Order matters. The cache is emptied *before* the callbacks are completed, because a consumer
        /// is free to request another texture from inside its own callback: notifying first would let it
        /// cache a fresh texture that the destroy loop below then destroys. Notifying last means a
        /// re-entrant request starts against an empty cache and behaves like any other miss.
        /// </para>
        /// </remarks>
        private void ClearCacheInternal(bool notifyPending)
        {
            // Snapshot, detach and mark Done BEFORE stopping anything - which is what actually makes this
            // correct, and is not the belt-and-braces it was written as.
            //
            // **A stopped coroutine does not run its finally block.** Measured in the editor: a
            // coroutine's finally runs on normal completion and on a throw, but Unity
            // abandons a *stopped* iterator without disposing it - StopCoroutine, StopAllCoroutines,
            // SetActive(false), enabled = false and DestroyImmediate all leave the finally unrun.
            //
            // So the finally below is no safety net for cancellation, and marking Done here is the only
            // thing completing these requests. The ordering this method describes is still right; the
            // reason given for it was not. The finally in DownloadTextureCoroutine remains valid for the
            // path it was really written for - a *throw* between registration and the first Complete.
            PendingRequest[] cancelled = pendingRequests.ToArray();
            pendingRequests.Clear();

            for (int i = 0; i < cancelled.Length; i++)
                cancelled[i].Done = true;

            // The same StopAllCoroutines also kills an in-flight *pre-cache* pass, and the boot chain
            // waits on onAssetVerificationCompleteEvent - MainController's handler is what calls
            // LoadNextScene. Killing the pass without firing it is the identical never-signals defect,
            // one method above the one this fix is about. Not reachable in this repo, where CacheAssets
            // has a single caller that clears before any pass exists, but the remarks above explicitly
            // invite re-entrant calls, so it is guarded rather than argued away.
            bool passWasInFlight = chunkTotal > 0 && chunkValidated < chunkTotal;

            StopAllCoroutines();

            chunkValidated = 0;
            chunkTotal = 0;
            imageDictionaryCount = 0;

            foreach (KeyValuePair<string, Texture2D> entry in imageDictionary)
            {
                // Never destroy the shared Resources placeholder: it is one asset instance reused
                // across every failed load, and destroying it breaks it for the whole session.
                if (entry.Value != null && entry.Value != errorTexture)
                    Destroy(entry.Value);
            }

            imageDictionary.Clear();
            mediaAssets.Clear();

            // Above the notifyPending gate on purpose. That flag is about per-request consumer
            // callbacks, which are pointless at teardown; the boot chain is a different waiter, and
            // destroying the manager mid-pass has to release it either way or the app hangs on a
            // component that no longer exists.
            //
            // Guarded on the flag rather than announced unconditionally, because after the stall break in
            // OnAssetsSorted_IE the completion event has already been raised while chunkValidated is still
            // below chunkTotal - so passWasInFlight stays true for the rest of the session, and claiming
            // "raising it anyway" there would be describing something that did not happen.
            if (passWasInFlight && !verificationCompleteRaised)
            {
                Development.LogError("Texture cache cleared while a pre-cache pass was still running. "
                    + "The pass is abandoned; the completion event is being raised anyway so the boot "
                    + "chain is not left waiting on it.");

                RaiseVerificationComplete();
            }

            if (!notifyPending || cancelled.Length == 0)
                return;

            // One aggregate line, not one per request, and at LogError because it explains the burst of
            // consumer-side errors that follows it - TextureCacheLoader reports each blanked image
            // itself. Without this the cause of that burst is invisible.
            Development.LogError($"Texture cache cleared with {cancelled.Length} request(s) still in "
                + "flight. Each has been completed with no texture so nothing waits forever, but those "
                + "images are blank until their consumers ask again.");

            for (int i = 0; i < cancelled.Length; i++)
            {
                // Per-callback try/catch, and it is not defensive padding. Every request here is already
                // Done and detached, so a throw part-way through this loop would leave the remaining ones
                // permanently uncompletable. A destroyed consumer
                // is the concrete trigger: a delegate targeting a destroyed MonoBehaviour is still a live
                // delegate as far as C# is concerned, and touching a Unity member inside it throws
                // MissingReferenceException rather than returning fake-null.
                //
                // null, NOT Placeholder(). A cancellation is not a failed image, and with
                // showErrorImage: true the placeholder is non-null - so the consumer would take its
                // SUCCESS branch, show the broken-image icon, resize the rect to it, fade it in, and log
                // nothing at all. That turns a cancelled download into a silent, visibly wrong success,
                // which is worse than the defect this whole entry is about. null makes every consumer
                // take the path that reports.
                try
                {
                    cancelled[i].Callback?.Invoke(null);
                }
                catch (Exception e)
                {
                    Development.LogError("A texture callback threw while the cache was being cleared: "
                        + e.Message + ". The remaining cancelled requests were still completed.");
                }
            }
        }

        // Callback delegate
        public delegate void TextureDownloadedCallback(Texture2D texture);

        /// <summary>
        /// One outstanding <see cref="GetOrSetTexture2D"/> request. Exists so a cache clear can complete
        /// what it cancels, and so "completed" is a fact about the request rather than about which
        /// code path ran - <c>Done</c> is what makes the callback fire at most once even though both the
        /// coroutine and <see cref="ClearCacheInternal"/> can finish it.
        /// </summary>
        private sealed class PendingRequest
        {
            public TextureDownloadedCallback Callback;
            public bool Done;
        }

        private readonly List<PendingRequest> pendingRequests = new List<PendingRequest>();

        /// <summary>Delivers a request's single answer, whoever produced it.</summary>
        private void Complete(PendingRequest request, Texture2D texture)
        {
            if (request.Done)
                return;

            request.Done = true;
            pendingRequests.Remove(request);
            request.Callback?.Invoke(texture);
        }

        /// <summary>
        /// Returns the cached texture for <paramref name="url"/>, downloading it first if needed.
        /// The callback is invoked exactly once, with null when nothing could be produced.
        ///
        /// **Exactly once as of v0.35.2.** It used to be "at most once": <see cref="ClearCache"/> calls
        /// StopAllCoroutines, and a download in flight was killed without ever reporting back, so the
        /// consumer waited forever with nothing logged. A clear now completes what it cancels,
        /// always with <c>null</c> - never the placeholder, because with <c>showErrorImage</c> on the
        /// placeholder is a real texture and a consumer would render a cancellation as a successful load
        /// of a broken-image icon, silently.
        ///
        /// With <paramref name="cacheIfNotAvailable"/> false the returned texture is *not* owned by
        /// this manager and nothing will ever destroy it - the caller takes ownership. Prefer true.
        /// </summary>
        public void GetOrSetTexture2D(string url, TextureDownloadedCallback callback, bool cacheIfNotAvailable = false)
        {
            if (string.IsNullOrEmpty(url))
            {
                // LogError, ungated: this was behind `doLogs` (ships false) at LogWarning (which is
                // [Conditional("DO_LOGS")] as well), so on the shipped config an empty url produced no
                // report here at all - and the consumer's own message names three causes, none of which
                // is this one. An empty url is a content or binding fault, not a trace line.
                Development.LogError("Texture requested with a null or empty url. Nothing to load; "
                    + "returning the placeholder.");

                callback?.Invoke(Placeholder());
                return;
            }

            if (imageDictionary.TryGetValue(url, out Texture2D cachedTexture))
            {
                callback?.Invoke(cachedTexture != null ? cachedTexture : Placeholder());
                return;
            }

            if (Core.settings.AppData.Cache.TextureCache.doLogs)
                Development.LogWarning($"Texture not available on cache for url [{url}]");

            // StartCoroutine on an inactive GameObject does not throw - it logs "Coroutine couldn't be
            // started" and returns null - so registering first would leave a request that nothing can ever
            // complete, sitting in pendingRequests for the session. Answer here instead. MainController
            // documents the same hazard for this component.
            if (!gameObject.activeInHierarchy)
            {
                Development.LogError($"Texture requested for [{url}] while the TextureCacheManager's "
                    + "GameObject is inactive, so no download can start. Returning the placeholder.");

                callback?.Invoke(Placeholder());
                return;
            }

            // Only the coroutine path is tracked. The returns above have already answered, so they have
            // nothing outstanding to cancel.
            PendingRequest request = new PendingRequest { Callback = callback };
            pendingRequests.Add(request);

            StartCoroutine(DownloadTextureCoroutine(url, request, cacheIfNotAvailable));
        }

        /// <summary>Placeholder if the setting allows one, otherwise null. Never a second callback.</summary>
        private static Texture2D Placeholder()
        {
            return Core.settings.AppData.Cache.TextureCache.showErrorImage ? ErrorTexture : null;
        }

        private IEnumerator DownloadTextureCoroutine(string url, PendingRequest request, bool cacheIfNotAvailable = false)
        {
            // try/finally, no catch - C# forbids catch around a yield, but finally is legal and is what
            // this needs. Complete is idempotent, so on every normal path this is a no-op; it exists for
            // the paths that cannot answer for themselves. A throw anywhere between registration and the
            // first Complete - GetAssetPath handing null to GetTexture, a settings NRE - would otherwise
            // kill the coroutine with the request still pending. Unity
            // would log the exception and the image would never arrive.
            //
            // Disposal runs this too, which is why ClearCacheInternal marks every request Done before it
            // calls StopAllCoroutines.
            try
            {
            if (url.IsValidUrl())
            {
                if (Core.settings.AppData.Cache.TextureCache.doLogs)
                    Development.LogWarning("External URL detected! Currently not supported for local download! " + url);

                // Previously this logged, optionally invoked the callback, then fell through and
                // downloaded anyway - invoking the callback a second time.
                Complete(request, Placeholder());
                yield break;
            }

            // As the pre-cache pass above. This one is **live** whatever TextureCache.isActive
            // says: the flag gates the boot pre-cache, not the manager, and GetOrSetTexture2D has no gate
            // of its own.
            string fileURL = AppPaths.ToFileUrl(Core.settings.AppData.GetAssetPath(url));

            // nonReadable: true. The one-argument overload hard-codes false - confirmed by disassembling
            // UnityEngine.UnityWebRequestTextureModule.dll, where GetTexture(string) is literally
            // `ldc.i4.0; call GetTexture(string, bool)` - so every cached texture kept a CPU-side pixel
            // array alongside its GPU copy, for pixels nothing here reads.
            //
            // The saving is ~43%, not half: Unity's decoder produces RGB24 for a JPG or an alpha-less PNG,
            // so the CPU copy is 3 bytes per pixel while the GPU side is still 4. A 1920x1080 JPG goes from
            // 14.5 MB resident to 8.3 - about 6 MB an image, ~1.2 GB across a 200-image cache. The bool
            // overload touches only the Readable bit, so mipmap behaviour, decode time and upload are all
            // unchanged; what goes away is the staging copy that used to be kept after upload, which also
            // lowers peak memory during the download itself.
            //
            // It was only ever required by an Apply() this class no longer makes. TextureCacheLoader reads width and
            // height and assigns rawImage.texture - it resizes the RECT, never the texture. Assigning a
            // non-readable texture there is known to render, because Placeholder() already serves the
            // Resources error image and its meta has isReadable: 0. That says nothing about the download
            // path, only that the display side is fine.
            //
            // THE BREAK SURFACE IS THE TWO PUBLIC WAYS OUT, not this class: GetTexture2D below, and
            // GetOrSetTexture2D called with cacheIfNotAvailable: false, which hands the texture to an
            // arbitrary caller. A project reading pixels from either should NOT flip this back - that pays
            // doubled memory on every image to read one. Use TextureExtensions.ReadableClone, and Destroy
            // the result when done. That method was itself broken until v0.32.0 - Graphics.CopyTexture is a
            // GPU-side copy that never populated the destination's CPU array, so it returned a plausible
            // texture full of noise with no exception - and is Blit + ReadPixels now.
            using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(fileURL, nonReadable: true))
            {
                www.SetFileTransferTimeout(Core.settings.AppData.Cache.TextureCache.timeout);

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Development.LogError($"Failed to download texture from {url}: {www.error}");
                    Complete(request, Placeholder());
                    yield break;
                }

                if (cacheIfNotAvailable)
                {
                    // Hand back the cached instance rather than a second copy of the same bytes -
                    // GetContent allocates a new Texture2D per call, and the extra one would leak.
                    SetTexture(www, url);
                    Complete(request, imageDictionary.TryGetValue(url, out Texture2D cached) ? cached : Placeholder());
                }
                else
                {
                    Complete(request, DownloadHandlerTexture.GetContent(www));
                }
            }
            }
            finally
            {
                Complete(request, Placeholder());
            }
        }

        public Texture2D GetTexture2D(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            if (imageDictionary.TryGetValue(url, out Texture2D cachedTexture) && cachedTexture != null)
                return cachedTexture;

            Development.LogError($"Texture not available in cache for url [{url}]");

            return Placeholder();
        }

        private void SetTexture(UnityWebRequest www, string url)
        {
            if (imageDictionary.ContainsKey(url))
            {
                Development.LogIf(Core.settings.AppData.Cache.TextureCache.doLogs, $"Texture ignored! [{url}] already exists in cache!");
                MarkProcessed(url);
                return;
            }

            // No throw-away `new Texture2D(128,128)` here: on the success path it was orphaned
            // immediately, leaking one texture per cached asset.
            Texture2D texture;

            if (www.result == UnityWebRequest.Result.Success)
            {
                texture = DownloadHandlerTexture.GetContent(www);

                Development.LogIf(Core.settings.AppData.Cache.TextureCache.doLogs, $"Set Texture to cache from [{www.url}]");
            }
            else
            {
                // The placeholder is the shared Resources instance, and ClearCache knows not to
                // destroy it. A null entry is a valid "known bad" marker.
                texture = Placeholder();

                Development.LogError($"Failed to cache image from {www.url}: {www.error}");
            }

            imageDictionary.Add(url, texture);
            MarkProcessed(url);
        }

        /// <summary>One item processed, whatever the outcome - counted and announced together.</summary>
        /// <remarks>
        /// <para><b>Every outcome, not just success.</b> <c>onTextureCachedEvent</c> used to be raised only
        /// on <c>Result.Success</c>, while the failure branch and the three skips advanced the count
        /// silently. That made the boot watchdog's per-image progress marks a report on the HEALTHY path
        /// only - and failure is the slow outcome, not success. Three consecutive timing-out images cost
        /// three full timeouts with no mark, which out-runs the stall budget at any timeout value; the
        /// watchdog latches, and <c>LoadNextScene</c> then refuses permanently. A boot that fails closed
        /// because nothing said it was still working.</para>
        /// <para>The count is post-increment on purpose, so the sequence runs 1..N and the last item marks
        /// <c>N/N</c>. Reading <c>imageDictionary.Count</c> before the <c>Add</c> ran 0..N-1 and never
        /// reached the total, which also understated the figure printed in <c>BOOT STALLED</c>.</para>
        /// </remarks>
        private void MarkProcessed(string url)
        {
            imageDictionaryCount++;

            onTextureCachedEvent?.Invoke(imageDictionaryCount, url, mediaAssets.Count);
        }
    }
}
