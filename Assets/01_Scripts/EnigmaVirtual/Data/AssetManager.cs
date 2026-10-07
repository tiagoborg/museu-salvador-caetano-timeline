using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace EnigmaVirtual
{
    public class AssetManager : MonoBehaviour
    {
        public event Action<int> onTotalAssetsEvent;
        public event Action<int, string, int> onAssetVerificationEvent;
        public event Action<int, string, int> onAssetDownloadEvent;
        public event Action onAssetVerificationCompleteEvent;
        public event Action<int, int> onAssetCleanedEvent;
        public event Action onAssetCleaningCompleteEvent;

        /// <summary>
        /// Extra attempts per asset before it is given up on.
        /// </summary>
        /// <remarks>
        /// Serialized as of v0.49.3. It is load-bearing arithmetic rather than a
        /// preference: a failing asset costs <c>(1 + maxRetryCount) x timeout</c>, which at the
        /// shipped 60s timeout is 4 x 60s, and that product is what the consecutive-failure
        /// limit was measured against. Raising it lengthens a dead-host boot proportionally.
        /// </remarks>
        /// <remarks>
        /// <c>[Range]</c> rather than a bare int because exposing this to the inspector exposes
        /// both ends: 0 is legitimate (retries off) but 50 is not - at the shipped 60s timeout a
        /// single dead asset would take 51 x 60s, about **51 minutes**, and the cap holds that to
        /// 11. Note it does <b>not</b> stall the boot: <c>onAssetDownloadEvent</c> carries the
        /// <i>raw</i> <c>validatedAssets</c>, so each
        /// retry moves the stage string and resets <c>BootWatchdog_IE</c>'s deadline. The
        /// failure mode is a boot nobody waits out, not a hang - which is worse to diagnose, not
        /// better.
        /// </remarks>
        [SerializeField]
        [Range(0, 10)]
        [Tooltip("Extra attempts per asset. Cost of a failing asset is (1 + this) x timeout, "
            + "so raising it lengthens a dead-host boot proportionally.")]
        private int maxRetryCount = 3;

        /// <summary>
        /// User-Agent sent with asset requests.
        /// </summary>
        /// <remarks>
        /// A literal at the call site until v0.49.3, and worth naming rather than
        /// tidying away: it claims to be <b>Chrome 72</b>, released in 2019. Unity's default
        /// agent is rejected or served differently by some CDNs and WAFs, which is why one is
        /// set at all - but a version this old is itself a filtering signal now. Left as-is
        /// because changing what a deployed site sends is a behaviour change for every branch;
        /// named so the next person can see what they are choosing to change.
        /// </remarks>
        private const string DesktopUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
            + "Chrome/72.0.3626.121 Safari/537.36";

        private List<string> mediaAssets;

        /// <summary>Membership index for <see cref="mediaAssets"/> - same contents, O(1) lookup.</summary>
        /// <remarks>The <see cref="List{T}"/> stays because order matters downstream; this exists only
        /// so <c>AddMediaAsset</c> stops asking it <c>Contains</c>. That was O(n^2) across the walk, in
        /// one frame on the boot path: measured 259ms at n=5000 against 5ms here. Keep the two in step -
        /// every add goes through <c>AddMediaAsset</c>, which is the only writer.</remarks>
        private HashSet<string> mediaAssetIndex;
        /// <summary>
        /// Assets this run could not be downloaded and are absent from disk.
        /// </summary>
        /// <remarks>
        /// A property rather than a parameter on <c>onAssetCleaningCompleteEvent</c>, deliberately:
        /// changing that event's signature would be a breaking change for every branched project, and
        /// the only thing the UI needs is the number. Additive instead.
        ///
        /// <b>Accumulated across chunks, not taken from one.</b> The per-chunk tally is a local inside
        /// <c>ValidateAndDownloadMedia_IE</c>, and a run is split into <c>downloadThreads</c> chunks
        /// that run concurrently - so a field that simply replaced the local would report whichever
        /// chunk finished last. The locals stay for the per-chunk summary, which is a different
        /// question from the run total.
        /// </remarks>
        public int MissingAssetCount { get; private set; }

        /// <summary>
        /// Assets this run were REFUSED for resolving outside the asset root, and so were not written.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="MissingAssetCount"/> for the same reason the per-chunk tallies
        /// are: both end with nothing on disk, but "the server did not have it" and "the appData asked
        /// to write outside the app" want different responses, and a single number cannot say which.
        /// </remarks>
        public int RefusedAssetCount { get; private set; }

        private int chunkValidated = 0;
        private int validatedAssets = 0;

        /// <summary>
        /// How many of <c>mediaAssets</c> the verification loop will actually reach - the count
        /// excluding empty and external URLs, which <c>continue</c> before the progress event.
        /// </summary>
        /// <remarks>
        /// This is the denominator the readout needs. Using <c>mediaAssets.Count</c> advertised a
        /// total the loop provably could not reach, so a completely successful run ended showing
        /// something like "48/50" and looked like it had stopped early - and because
        /// <c>PREPARING n FILES</c> goes on screen before the first file, the run announced that
        /// unreachable total up front.
        ///
        /// The alternative - raising the event before the <c>continue</c> - is a trap: this
        /// event feeds <c>MainController</c>'s boot-stage string, and the boot watchdog only resets
        /// its deadline when that string changes.
        ///
        /// <c>mediaAssets</c> itself is deliberately NOT filtered. It is the keep-list behind the
        /// recursive asset cleanup; narrowing a delete set to fix a progress label is not
        /// a trade worth making.
        /// </remarks>
        private int verifiableAssets = 0;

        /// <summary>
        /// Whether <c>ValidateAndDownloadMedia_IE</c> will actually reach <paramref name="url"/>, or
        /// <c>continue</c> past it before the progress event fires.
        /// </summary>
        /// <remarks>
        /// One method, called both by the count that builds <see cref="verifiableAssets"/> and by the
        /// loop itself, so the denominator mirrors the loop by construction rather than by two
        /// spellings agreeing. Adding a new <c>continue</c> ahead of the progress event without
        /// adding it here is how the two spellings drift apart again.
        /// </remarks>
        private static bool IsVerifiableLocally(string url)
        {
            return !string.IsNullOrEmpty(url) && !url.IsValidUrl();
        }

        private int totalRetryCount = 0;

        public void ValidateAssets()
        {
            StartCoroutine(ValidateAssets_IE());
        }

        /// <summary>
        /// Whether this pass has already announced that asset handling is finished.
        /// <c>onAssetCleaningCompleteEvent</c> is what <c>MainController</c> waits on to reach
        /// <c>LoadNextScene</c>, and it is raised from nine places - several of them refusal paths that
        /// exist because something already went wrong. Idempotence per pass means none of them has to
        /// know whether another already fired, and matches what <c>TextureCacheManager</c> does.
        /// </summary>
        /// <remarks>
        /// Today a second raise would be harmless anyway, because both subscribers unsubscribe as
        /// their first act - but that invariant lives in two other files, and a branched project's
        /// handler is under no obligation to keep it.
        /// </remarks>
        private bool cleaningCompleteRaised;

        private void RaiseCleaningComplete()
        {
            if (cleaningCompleteRaised)
                return;

            // Set before the invoke, so a subscriber that re-enters cannot drive a second raise.
            cleaningCompleteRaised = true;
            onAssetCleaningCompleteEvent?.Invoke();
        }

        private IEnumerator ValidateAssets_IE()
        {
            onAssetVerificationCompleteEvent += AssetManager_onAssetVerificationCompleteEvent;

            cleaningCompleteRaised = false;
            mediaAssets = new List<string>();
            mediaAssetIndex = new HashSet<string>();

            yield return new WaitForEndOfFrame();

            // Every path here must signal. onAssetCleaningCompleteEvent is what MainController waits
            // on to reach LoadNextScene, so anything that leaves this method without raising it hangs
            // the boot until the watchdog reports a generic BOOT STALLED - which names the phase,
            // not the cause. TextureCacheManager had the same defect.
            //
            // The whole thing is inside the try, including the AppData read: Core.main and
            // AppDataController are both set by the time SetupAssetManager runs, but a dereference
            // sitting outside the guard is the one line that could still fail unsignalled.
            //
            // Two catches rather than one, because the walk and what follows it fail differently and
            // a single message could only name one of them. LogException first in both: catching the
            // exception removes the stack trace Unity would have printed, so the fix would otherwise
            // trade a hang for a boot that continues with nothing to diagnose.
            bool walked = false;

            try
            {
                if (Core.main.AppDataController.AppData != null)
                {
                    AssetSorting(Core.main.AppDataController.AppData, null);
                    walked = true;
                }
                else
                {
                    // Deliberately NOT ValidateExistingMedia. This is *not* the completed-walk case:
                    // there, a walk that found nothing genuinely means nothing is referenced, and
                    // clearing the cache is correct. Here the walk never ran, so there is no keep-list
                    // at all - and running the cleanup against one that was never built is how a
                    // site's media gets deleted.
                    Development.LogError("AppData is null, so no assets can be validated or downloaded. "
                        + "Continuing the boot with whatever media is already on disk. The asset cleanup "
                        + "is skipped: the walk never ran, so there is no keep-list to trust.");

                    RaiseCleaningComplete();
                }
            }
            catch (Exception e)
            {
                Development.LogException(e);
                Development.LogError($"The walk over the content model threw ({e.GetType().Name}), so no "
                    + "assets can be validated or downloaded. Continuing the boot with whatever media is "
                    + "already on disk; the cleanup is skipped for the same reason as above.");

                RaiseCleaningComplete();
            }

            if (walked)
            {
                // Separate scope, separate message. This one reaches OnAssetsSorted, ValidateExistingMedia
                // and every subscriber they notify - which on the empty-media branch is as far as
                // LoadNextScene itself. Labelling a scene that is missing from the build settings as
                // "the asset walk threw" would be a confident wrong answer.
                try
                {
                    OnAssetsSorted();
                }
                catch (Exception e)
                {
                    Development.LogException(e);
                    Development.LogError($"Sorting the walked assets, or a handler it notified, threw "
                        + $"({e.GetType().Name}). Asset handling may be incomplete; releasing the boot "
                        + "chain so the app does not hang on it.");

                    RaiseCleaningComplete();
                }
            }

            yield return null;
        }

        private void AssetManager_onAssetVerificationCompleteEvent()
        {
            onAssetVerificationCompleteEvent -= AssetManager_onAssetVerificationCompleteEvent;

            //mediaAssets = null;
        }

        private void OnAssetsSorted()
        {
            // Reset the run totals HERE, not inside OnAssetsSorted_IE, because the empty-media branch
            // below returns before that coroutine ever starts - so a re-run whose walk finds no media
            // used to report the previous run's counts on the preloader. Dormant today (nothing calls
            // this twice), live for any branch that re-runs on an appData refresh.
            MissingAssetCount = 0;
            RefusedAssetCount = 0;
            Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs, "Total items sorted: " + mediaAssets.Count);

            verifiableAssets = 0;
            int emptyUrls = 0;
            int externalUrls = 0;
            for (int i = 0; i < mediaAssets.Count; i++)
            {
                if (IsVerifiableLocally(mediaAssets[i]))
                    verifiableAssets++;
                else if (string.IsNullOrEmpty(mediaAssets[i]))
                    emptyUrls++;    // dead in practice - MediaWalker only emits non-empty strings -
                else                // but the loop guards it, so the count matches the loop.
                    externalUrls++;
            }

            // Said once here rather than once per asset inside the loop: the count is what explains
            // a progress total smaller than "Total items sorted" above.
            //
            // Split by severity deliberately. An EXTERNAL url means an asset that will be absent from
            // disk at runtime - a real failure, and LogError so it survives on the four targets
            // without DO_LOGS. An EMPTY url is an optional media field an editor left blank,
            // which is legitimate content; reporting that as an error would put a line on the one
            // channel reserved for missing/refused/cleanup-refused on every boot of every project.
            if (externalUrls > 0)
                Development.LogError($"{externalUrls} of {mediaAssets.Count} sorted asset(s) are "
                    + "EXTERNAL urls. They are not downloaded, so those assets will not be on disk at "
                    + "runtime, and they are excluded from the progress total because the verification "
                    + $"loop skips them. ({emptyUrls} further entr(y/ies) were empty.)");
            else if (emptyUrls > 0)
                Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs,
                    $"{emptyUrls} of {mediaAssets.Count} sorted asset(s) are empty and excluded from "
                    + "the progress total. An empty media field is normal content, not a fault.");

            onTotalAssetsEvent?.Invoke(verifiableAssets);

            if (mediaAssets.IsNullOrEmpty())
            {
                // Say it - and say WHICH of the two it is, because they need opposite responses and
                // both used to produce the same nothing: this branch raised no progress event, so the
                // preloader label never appeared and the run completed looking exactly like a healthy
                // one. The only existing report was the Assert above, which is doLogs-gated (ships
                // false) and stripped outside DO_LOGS on top.
                //
                // RawMediaValueCount is counted from the raw JSON text before deserialization, so it
                // describes the payload independently of the model - the same discriminator
                // ValidateExistingMedia uses at the cleanup site below. That one is the ONLY other
                // report of this, and it sits under the cleanUnlistedAssets gate, which ships false;
                // so without the LogError here a model that cannot see its own media produces a
                // media-less kiosk and one warning line.
                //
                // Deliberately not the same level for both: a text-only project is a legitimate
                // configuration and gets a Warning, while a payload full of media the model cannot
                // represent is a defect and gets an Error.
                int declaredMedia = Core.main.AppDataController.RawMediaValueCount;

                if (declaredMedia > 0)
                {
                    string[] mediaNames = Core.main.AppDataController.AppSettings?.mediaPaths;
                    string knownNames = mediaNames == null || mediaNames.Length == 0
                        ? "<none>"
                        : string.Join(", ", mediaNames);

                    Development.LogError($"The appData JSON declares {declaredMedia} media value(s), but "
                        + "the collected asset list is empty, so nothing will be downloaded and the app "
                        + "will run with no media. The AppData model cannot see the media the payload "
                        + "contains. Check that every media-bearing member exists on your AppData classes "
                        + "with the same name as the JSON key, and that the members holding paths are "
                        + $"named in mediaPaths [{knownNames}].");
                }
                else
                {
                    Development.LogWarning("This appData declares no media at all, so nothing will be "
                        + "downloaded. That is expected for a text-only content set; if it is not, the "
                        + "payload is the place to look - the list arrived empty, it was not emptied "
                        + "here.");
                }

                // Nothing to download - but the cache may still need pruning. Switching to a content
                // set that uses none of the previous files is a legitimate reason for every cached
                // file to be stale, so cleanup still runs. ValidateExistingMedia decides for itself
                // whether an empty keep-list is trustworthy here. This used to return in
                // silence behind a "TODO: files might need cleaning" comment.
                ValidateExistingMedia();
                onAssetVerificationCompleteEvent?.Invoke();
                return;
            }

            StartCoroutine(OnAssetsSorted_IE());
        }

        private IEnumerator OnAssetsSorted_IE()
        {
            chunkValidated = 0;
            validatedAssets = 0;
            totalRetryCount = 0;

            // Both clamps matter. downloadThreads is 0 in a hand-edited settings.json more often
            // than you would think (SettingsParse.Int only clamps the settings *panel*), and
            // Ceiling(n / 0.0) is Infinity, whose cast to int is int.MinValue - a negative chunk
            // size, from which Split yielded zero chunks and the WaitUntil below completed
            // immediately, having downloaded nothing at all. TextureCacheManager already
            // guards this the same way.
            int configuredThreads = Core.settings.AppData.Cache.AssetDownload.downloadThreads;

            if (configuredThreads < 1)
            {
                // Logged here rather than inside Split, because with both callers clamping, Split's
                // own guard can never fire - so the operator would still never learn the setting is
                // invalid. LogError because it is always compiled; Split keeps its guard as a
                // backstop for a future caller.
                Development.LogError($"AppData.Cache.AssetDownload.downloadThreads is {configuredThreads} "
                    + "in settings.json, which is not a usable thread count. Using 1. Downloads will "
                    + "run single-file until this is corrected.");
            }

            int threads = Mathf.Max(1, configuredThreads);
            int chunkSize = Mathf.Max(1, (int)Math.Ceiling(mediaAssets.Count / (double)threads));

            IEnumerable<IEnumerable<string>> splitMedia = mediaAssets.Split(chunkSize);
            List<List<string>> divUrls = splitMedia.Select(innerEnumerable => innerEnumerable.Select(obj => obj.ToString()).ToList()).ToList();

            for (int i = 0; i < divUrls.Count; i++)
                StartCoroutine(ValidateAndDownloadMedia_IE(divUrls[i].ToArray()));

            // >=, not ==: if a previous run's chunk coroutines are still alive when this one
            // resets the counter, their increments overshoot the target and an == comparison is
            // never true again - a permanent, silent hang.
            yield return new WaitUntil(() => chunkValidated >= divUrls.Count);

            ValidateExistingMedia();

            onAssetVerificationCompleteEvent?.Invoke();
        }

        /// <summary>
        /// Removes local files that the current appData no longer references. This RECURSIVELY
        /// DELETES from a computed root, so every exit below fires
        /// <see cref="onAssetCleaningCompleteEvent"/> - bailing without it used to hang the boot
        /// chain on the same run that had already half-deleted the media set.
        /// </summary>
        private void ValidateExistingMedia()
        {
            // First, before the cleanUnlistedAssets gate. Cleanup would *probably* be safe - the
            // keep-list comes from appData rather than from what downloaded, and the corroboration
            // check would refuse anyway once few files are present - but "probably safe" plus
            // "deletes files" plus "the machine is already misbehaving enough that a human hit the
            // skip key" is not a combination worth reasoning about at a site.
            if (Core.SkipAssetValidation)
            {
                // LogError, not LogWarning: LogWarning is [Conditional("DO_LOGS")] and therefore
                // absent on any target that is not Android or Standalone, and skipping a step that
                // DELETES FILES is a state change, not chatter.
                Development.LogError("Asset cleanup skipped: validation was skipped by operator, so "
                    + "the on-disk set is deliberately incomplete and the keep-list cannot be judged "
                    + "against it.");
                RaiseCleaningComplete();
                return;
            }

            if (!Core.settings.AppData.Cache.AssetDownload.cleanUnlistedAssets)
            {
                Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs,
                    "Asset cleanup disabled (Cache.AssetDownload.cleanUnlistedAssets).");
                RaiseCleaningComplete();
                return;
            }

            if (Core.main.AppDataController.failedRemoteJson)
            {
                // The keep-list comes from appData. If that failed to load, the list is incomplete
                // and everything not in it would be deleted.
                Development.LogWarning("Asset cleanup skipped: remote AppData was unavailable, so the keep-list is untrustworthy.");
                RaiseCleaningComplete();
                return;
            }

            string root = Core.settings.AppData.assetsPath;

            if (!IsSafeCleanupRoot(root))
            {
                RaiseCleaningComplete();
                return;
            }

            // Build the keep-set once, canonicalised. The old code compared raw strings with a
            // case-sensitive ==, inside an O(n*m) loop that ran GetAssetPath - and therefore a
            // Regex - on every single comparison.
            //
            // Entries are composed with Path.Combine against the root rather than through
            // GetAssetPath. GetAssetPath does string.Concat(assetsPath, value) with no separator,
            // so an assetsPath without a trailing slash produced ".../assetsvideo.mp4" - a keep-set
            // that matches nothing, against which every file looks unreferenced. It also returns
            // rooted values as-is, so a CMS emitting "/video.mp4" yielded "C:\video.mp4" and the
            // real file was deleted.
            string rootFull = Path.GetFullPath(root);
            HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int unresolved = 0;

            for (int i = 0; i < mediaAssets.Count; i++)
            {
                string value = mediaAssets[i];

                if (string.IsNullOrWhiteSpace(value) || value.IsValidUrl())
                    continue;

                try
                {
                    // TrimStart: a leading slash would make Path.Combine discard the root.
                    string candidate = Path.GetFullPath(Path.Combine(root, value.TrimStart('/', '\\')));

                    if (IsUnder(candidate, rootFull))
                        keep.Add(candidate);
                    else
                        unresolved++;
                }
                catch (Exception e)
                {
                    unresolved++;
                    Development.LogError($"Asset cleanup: could not resolve [{value}]: {e.Message}");
                }
            }

            // An empty keep-list means nothing under the root is referenced, so everything there is
            // stale and gets removed. This is deliberate: the cache belongs to whichever appData is
            // current, and swapping content sets is a normal reason for all of it to go.
            //
            // It is still worth knowing *why* the list came out empty, so the two cases are
            // reported differently. RawMediaValueCount is counted from the raw JSON text before
            // deserialization, so it describes the payload independently of the model.
            if (keep.Count == 0)
            {
                int declared = Core.main.AppDataController.RawMediaValueCount;

                if (declared > 0)
                {
                    string[] names = Core.main.AppDataController.AppSettings?.mediaPaths;
                    string known = names == null || names.Length == 0 ? "<none>" : string.Join(", ", names);

                    // Loud, because this is very likely a model bug rather than genuinely empty
                    // content - the payload has media the model could not represent. The cache is
                    // cleared anyway by decision; expect the app to have no media until the
                    // model is fixed, since the same blindness also stops the download.
                    Development.LogError($"Asset cleanup: the appData JSON declares {declared} media value(s), "
                        + $"but none reached the keep-list ({mediaAssets.Count} collected, {unresolved} "
                        + $"unresolvable under [{rootFull}]). The AppData model cannot see the media the payload "
                        + "contains. CLEARING THE CACHE ANYWAY - nothing is referenced. Check that every "
                        + "media-bearing member exists on your AppData classes with the same name as the JSON "
                        + $"key, and that the members holding paths are named in mediaPaths [{known}].");
                }
                else
                {
                    Development.LogWarning("Asset cleanup: this appData declares no media at all, so every "
                        + $"cached file under [{rootFull}] is stale and will be removed.");
                }
            }
            else if (unresolved > 0)
            {
                Development.LogWarning($"Asset cleanup: {unresolved} referenced asset(s) did not resolve under "
                    + "the root and were ignored for keep purposes.");
            }

            string[] files;
            try
            {
                // GetFiles, not GetFileSystemEntries: the latter returns directories too.
                files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                Development.LogError($"Asset cleanup: could not enumerate [{root}]: {e.Message}");
                RaiseCleaningComplete();
                return;
            }

            Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs,
                $"Asset cleanup: {files.Length} local file(s) against {keep.Count} referenced.");

            StartCoroutine(ProcessFiles_IE(files, keep, rootFull));
        }

        /// <summary>
        /// Deletes directories the file pass emptied, deepest first. The root itself is always kept.
        /// Without this a content swap left the entire previous folder tree behind - empty, but
        /// present, which reads as "cleanup did nothing".
        /// </summary>
        private static int RemoveEmptyFolders(string rootFull)
        {
            int removed = 0;

            try
            {
                // Deepest first, so a parent emptied by its own children is caught in the same pass.
                string[] directories = Directory.GetDirectories(rootFull, "*", SearchOption.AllDirectories);
                Array.Sort(directories, (a, b) => b.Length.CompareTo(a.Length));

                foreach (string directory in directories)
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(directory).Length != 0)
                            continue;

                        // Belt and braces: never step outside the verified root.
                        if (!IsUnder(Path.GetFullPath(directory), rootFull))
                            continue;

                        Directory.Delete(directory, false);
                        removed++;
                    }
                    catch (Exception e)
                    {
                        Development.LogError($"Asset cleanup: could not remove folder [{directory}]: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Development.LogError($"Asset cleanup: could not enumerate folders under [{rootFull}]: {e.Message}");
            }

            return removed;
        }

        /// <summary>
        /// Whether a composed local asset path stays inside the configured asset root.
        /// </summary>
        /// <remarks>
        /// The download had no containment check at all, while the cleanup pass above has had one
        /// since the containment guard landed - so the app refused to *delete* a file outside the
        /// root while happily *creating*
        /// one there. Two ways out, both from CMS-controlled values:
        /// <c>AppDataSettings.GetAssetPath</c> returns a rooted value **verbatim**
        /// (<c>if (Path.IsPathRooted(value)) return value;</c>), so <c>C:\Windows\…</c> or
        /// <c>//host/share/x</c> arrives unchanged; and a relative value is concatenated without
        /// normalising, so <c>../../</c> segments walk out of the root before anything resolves them.
        /// <para>
        /// The guard is **here and not in <c>GetAssetPath</c>**, which is the decision that kept this
        /// open. That method has six callers and only this one writes - the other five read (video,
        /// downloadable image, texture cache x2, media file), and for a read a rooted path is a
        /// legitimate configuration: an operator pointing at a local media folder or a shared drive.
        /// Guarding the resolver would have changed five read sites to fix one write.
        /// </para>
        /// <para>
        /// The ceiling: <c>Path.GetFullPath</c> does not resolve reparse points, so a junction or
        /// symlink already inside the root passes containment and writes wherever it points. Planting
        /// one needs local filesystem access, which already beats this guard, and an operator who maps
        /// <c>&lt;root&gt;/media</c> onto another drive has chosen that. Containment here is against the
        /// CMS, not against someone who is already on the machine.
        /// </para>
        /// <para>
        /// Refusal is per asset, matching every other refusal in this file: one bad CMS entry costs
        /// that asset, not the boot.
        /// </para>
        /// </remarks>
        private static bool IsInsideAssetRoot(string localFilePath, out string reason)
        {
            reason = null;

            string root = Core.settings.AppData.assetsPath;

            if (string.IsNullOrWhiteSpace(root))
            {
                reason = "assetsPath is empty, so there is no root for the download to stay inside";
                return false;
            }

            string rootFull;
            string candidate;

            try
            {
                // GetFullPath is what collapses `..` - the composed string can look contained and
                // resolve somewhere else entirely, which is the whole point of checking.
                rootFull = Path.GetFullPath(root);
                candidate = Path.GetFullPath(localFilePath);
            }
            catch (Exception e)
            {
                reason = $"the path could not be resolved ({e.GetType().Name}: {e.Message})";
                return false;
            }

            if (!IsUnder(candidate, rootFull))
            {
                reason = $"it resolves to [{candidate}], which is outside the asset root [{rootFull}]";
                return false;
            }

            // Containment alone is not enough, because the root can collapse *upwards*. Both halves of
            // assetsPath are operator-editable from the settings panel with no validation, and with
            // both empty the property yields <persistentDataPath>/StreamingAssets - the PARENT of the
            // config folder. A CMS value of "config/settings.json" is then genuinely "inside the root",
            // and the download would replace the operator's settings.json: panel password, hostUrl,
            // cleanUnlistedAssets. IsSafeCleanupRoot already refuses that collapse for deletes and says
            // why; this is the same rule for writes.
            string configFull;

            try
            {
                configFull = Path.GetFullPath(AppPaths.PersistentConfig);
            }
            catch (Exception e)
            {
                reason = $"the config folder could not be resolved to compare against ({e.Message})";
                return false;
            }

            string configTrimmed = configFull.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            if (IsUnder(candidate, configFull) || candidate.Equals(configTrimmed,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = $"it resolves to [{candidate}], which is inside the config folder "
                    + $"[{configFull}] - never a download destination, whatever assetsPath is set to";
                return false;
            }

            return true;
        }

        /// <summary>True if <paramref name="candidate"/> is strictly inside <paramref name="baseFull"/>.</summary>
        /// <remarks>
        /// The comparison follows the filesystem rather than the language. Case-insensitive is right on
        /// Windows and wrong everywhere else: on Android and Linux a root of <c>…/assets</c> and a
        /// candidate of <c>…/ASSETS/x.jpg</c> are **different directories**, and treating them as the
        /// same lets a <c>../ASSETS/…</c> value out of the root it is meant to be confined to. This is
        /// the case where a compile-time platform test is the right tool - the question is what the
        /// filesystem does, not what is happening at runtime.
        /// </remarks>
        private static bool IsUnder(string candidate, string baseFull)
        {
            string b = baseFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
#else
            const StringComparison comparison = StringComparison.Ordinal;
#endif

            return candidate.StartsWith(b + Path.DirectorySeparatorChar, comparison);
        }

        /// <summary>
        /// Guards the recursive delete. The root is computed from settings, so it can collapse
        /// upward: an empty <c>assetsPath</c> made it streamingAssets or persistentData itself,
        /// which is where settings.json and the operator's config overrides live.
        /// </summary>
        private static bool IsSafeCleanupRoot(string root)
        {
            // Deliberately no editor refusal. One was added to stop the editor deleting project
            // source, but the persistentDataPath-only rule below already guarantees that - and
            // being checked first, it blocked every legitimate download-cache cleanup in the
            // editor, which is where this actually gets tested.
            if (string.IsNullOrWhiteSpace(root))
            {
                Development.LogError("Asset cleanup REFUSED: assetsPath is empty.");
                return false;
            }

            if (!Directory.Exists(root))
            {
                Development.LogError($"Asset cleanup skipped: [{root}] does not exist.");
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception e)
            {
                Development.LogError($"Asset cleanup REFUSED: [{root}] is not a valid path: {e.Message}");
                return false;
            }

            // GetFullPath does not resolve junctions or symlinks, but GetFiles(AllDirectories)
            // follows them - so a `mklink /D assets D:/Media` (a normal way to keep large media off
            // the system drive) would land every delete outside the guarded root.
            try
            {
                if ((new DirectoryInfo(full).Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Development.LogError($"Asset cleanup REFUSED: [{full}] is a symlink or junction, so "
                        + "containment cannot be verified.");
                    return false;
                }
            }
            catch (Exception e)
            {
                Development.LogError($"Asset cleanup REFUSED: could not inspect [{full}]: {e.Message}");
                return false;
            }

            // persistentDataPath ONLY. Cleanup means "prune the download cache"; StreamingAssets
            // media is shipped inside the build and cannot be re-downloaded, so deleting there is
            // never a cache operation.
            string cacheBase = Path.GetFullPath(Application.persistentDataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (full.Equals(cacheBase, StringComparison.OrdinalIgnoreCase))
            {
                Development.LogError($"Asset cleanup REFUSED: [{full}] is persistentDataPath itself, not a "
                    + "subfolder. As written this would delete the operator's config overrides.");
                return false;
            }

            if (!IsUnder(full, cacheBase))
            {
                Development.LogError($"Asset cleanup REFUSED: [{full}] is not under persistentDataPath. Only the "
                    + "download cache may be pruned - shipped StreamingAssets media is not re-downloadable.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Fraction of the keep-list that must actually exist on disk for the list to be trusted.
        ///
        /// This replaced a "refuse if deleting more than half the folder" cap, which could not tell
        /// a legitimate full content swap - point the app at a different appData and every previous
        /// file is correctly unreferenced - from a broken keep-set. By volume the two are identical,
        /// so that cap blocked the normal workflow and had to be commented out to get any work done.
        ///
        /// Corroboration separates them. Cleanup runs after the download phase, so the assets
        /// appData names should be on disk by now. If they are, the keep-list is real and deleting
        /// everything else is correct at any volume. If none of them are, the keep-list does not
        /// describe this folder and the delete would be indiscriminate.
        /// </summary>
        private const float MinKeepCorroboration = 0.5f;

        private IEnumerator ProcessFiles_IE(string[] files, HashSet<string> keep, string rootForFolders)
        {
            int corroborated = 0;
            foreach (string wanted in keep)
            {
                if (File.Exists(wanted))
                    corroborated++;
            }

            // keep.Count == 0 has already been vetted against the raw payload in
            // ValidateExistingMedia, so treat it as corroborated rather than re-refusing it.
            float ratio = keep.Count == 0 ? 1f : (float)corroborated / keep.Count;

            if (ratio < MinKeepCorroboration)
            {
                Development.LogError($"Asset cleanup REFUSED: only {corroborated} of {keep.Count} referenced "
                    + $"asset(s) are present on disk ({ratio:P0}, need {MinKeepCorroboration:P0}). The keep-list "
                    + "does not match this folder, so deleting against it would be indiscriminate. Check "
                    + "AppData assetsPath, and whether the downloads actually succeeded.");
                RaiseCleaningComplete();
                yield break;
            }

            Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs,
                $"Asset cleanup: keep-list corroborated ({corroborated}/{keep.Count} present on disk).");

            List<string> doomed = new List<string>();

            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    if (!keep.Contains(Path.GetFullPath(files[i])))
                        doomed.Add(files[i]);
                }
                catch (Exception e)
                {
                    // Unreadable path: keep the file. Deleting on uncertainty is the wrong default.
                    Development.LogError($"Asset cleanup: skipping [{files[i]}]: {e.Message}");
                }

                if ((i & 63) == 0)
                    yield return null;
            }

            int deleted = 0;

            for (int i = 0; i < doomed.Count; i++)
            {
                onAssetCleanedEvent?.Invoke(i, doomed.Count);

                {
                    try
                    {
                        File.Delete(doomed[i]);
                        deleted++;

                        Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs,
                            $"Asset cleanup: deleted [{doomed[i]}]");
                    }
                    catch (Exception e)
                    {
                        // Must not abort the loop: one locked or read-only file used to kill this
                        // coroutine, so the completion event never fired and boot hung.
                        Development.LogError($"Asset cleanup: could not delete [{doomed[i]}]: {e.Message}");
                    }
                }

                // One yield per 64 files rather than a nested coroutine per file.
                if ((i & 63) == 0)
                    yield return null;
            }

            int folders = RemoveEmptyFolders(rootForFolders);

            Development.LogWarning($"Asset cleanup complete: removed {deleted} of {files.Length} file(s)"
                + (folders > 0 ? $" and {folders} emptied folder(s)." : "."));

            RaiseCleaningComplete();
        }

        /// <summary>
        /// Collects every media path in <paramref name="data"/> into <c>mediaAssets</c>.
        /// Delegates to <see cref="MediaWalker"/> - this used to be a local properties-only walk
        /// duplicated in TextureCacheManager, which is why a model built from fields (the
        /// documented convention) collected nothing at all.
        /// </summary>
        private void AssetSorting(System.Object data, Action callback = null)
        {
            MediaWalker.Collect(data, Core.main.AppDataController.AppSettings.isMediaType, AddMediaAsset);

            callback?.Invoke();
        }
        /// <summary>
        /// Adds media asset url to mediaAssets List if list doesnt already contains a duplicate.
        /// </summary>
        /// <param name="value"></param>
        private void AddMediaAsset(string value)
        {
            // The index answers membership; the list keeps order. HashSet.Add returns false when the
            // value was already present, so one call does both jobs and the two cannot drift.
            bool added = mediaAssetIndex.Add(value);

            if (added)
                mediaAssets.Add(value);

            // Hoisted rather than passed to LogIf: LogIf is [Conditional("DO_LOGS")], and DO_LOGS IS
            // defined for Standalone and Android - so the string.Format ran on every walked value on
            // exactly the platforms that ship. Measured 635 ns and 192 B per value, ~3ms at n=5000.
            if (Core.settings.AppData.Cache.AssetDownload.doLogs)
            {
                Development.Log(added
                    ? "Added [" + value + "] to list."
                    : "Duplicate [" + value + "] not added to list.");
            }
        }

        /// <summary>
        /// Validates and downloads all media requested to persistentData folder.
        /// </summary>
        /// <param name="urls">Array of urls to validate/download.</param>
        /// <returns></returns>
        /// <summary>
        /// Deletes a partial/failed download. Never throws: this runs on the failure path of a
        /// coroutine whose final statement increments the counter OnAssetsSorted_IE waits on, so
        /// an exception here would not merely lose a file - it hangs the boot chain permanently.
        /// </summary>
        private static void TryDeletePartialFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                // Locked by an AV scanner, read-only attribute, handle not yet released.
                Development.LogError($"Could not remove the partial download at [{path}]: {e.Message}. "
                    + "It will be re-downloaded rather than trusted, but may need deleting by hand.");
            }
        }

        private IEnumerator ValidateAndDownloadMedia_IE(string[] urls)
        {
            int retryCount = 0;
            int unparseableHeaders = 0;
            int missingAssets = 0;

            // Counted apart from missingAssets on purpose. Both end with nothing downloaded, but "the
            // server did not have it" and "the appData tried to write outside the app" want different
            // responses from whoever reads the end-of-chunk summary, and one tally cannot say which
            // happened. It is also not safe to claim a refused asset is missing at runtime: the five
            // READ callers of GetAssetPath still resolve a rooted value verbatim, so an operator whose
            // media genuinely lives on another drive sees it render perfectly.
            int refusedAssets = 0;
            int staleAssets = 0;

            // Three CONSECUTIVE connection failures, not a share of the catalogue.
            //
            // History, because both earlier versions were wrong in instructive ways. The original
            // `(10 * count) / 100` is arithmetically identical to `count / 10`, so it was 0 for any
            // set under 10 assets - and since it is tested *after* the increment, `>= 0` cancelled
            // everything on the first failure while reporting "reached 10% errors". Replacing
            // it with `Max(3, count / 10)` made it reachable but useless at scale: a failure costs
            // (1 + maxRetryCount) x timeout = 4 x 60s as shipped, so a 225-asset set needed 22
            // failures - 88 minutes - before it would give up, which is the hang this limit exists
            // to stop. Measured from a real run against an unroutable host, not reasoned about.
            //
            // Consecutive is also the better *diagnosis*. A dead host produces an unbroken run of
            // connection failures; scattered 404s from CMS drift do not, and a single flaky asset
            // does not. So this both fires in bounded time - 3 x 4 x timeout, independent of how
            // large the content set is - and means what the log message says it means.
            const int ConsecutiveFailureLimit = 3;
            int consecutiveFailures = 0;

            // try/finally around the whole loop, and the `finally` is the entire point: three
            // `yield break` exits below leave this method early, and they all used to skip the fold-in
            // that follows the loop. With downloadThreads: 1 as shipped there is exactly one chunk, so
            // the dead-host abort - the classic site failure, and the one with the most missing media -
            // left MissingAssetCount at 0 and the preloader cleared the screen exactly as it did before
            // this fix. A `finally` covers every exit at once, including any added later; folding in at
            // each `yield break` would have to be remembered next time.
            //
            // `yield return` inside a try/finally is legal - only a `catch` is not (CS1626) - and Unity
            // running the iterator to completion or `yield break`ing both run the finally. A coroutine
            // Unity *stops* does not (M95), which is why the boot-critical signal stays where it is.
            try
            {
            for (int i = 0; i < urls.Length; i++)
            {
                // Checked per asset rather than per chunk, so the skip takes effect at the top of
                // the next iteration rather than after the whole chunk - worst case one in-flight
                // request, which on a slow host is the difference between a minute and an hour.
                if (Core.SkipAssetValidation)
                {
                    // The abandoned tail is NOT added to missingAssets. Those URLs were never
                    // checked, so whether they are on disk is unknown - and folding an unknown into
                    // a count labelled "will be MISSING" is the exact over-claim removed from the
                    // consecutive-failure abort below. A fully cached kiosk skipped at asset 3 of 225 would
                    // otherwise report 222 missing assets that are all present.
                    //
                    // Reports the same three tallies as the abort path, because the yield break skips
                    // the end-of-chunk summary and these would otherwise go nowhere - the per-asset
                    // detail is Assert/LogWarning, both stripped without DO_LOGS.
                    Development.LogError($"Asset validation SKIPPED by operator: abandoning "
                        + $"{urls.Length - i} remaining asset(s) in this chunk - those were not "
                        + $"checked, so some may be absent. {missingAssets} asset(s) will be MISSING "
                        + $"at runtime and {staleAssets} could not be refreshed but are on disk; "
                        + $"{unparseableHeaders} had no usable Last-Modified.");

                    // Incremented before the bail, or OnAssetsSorted_IE's WaitUntil never completes
                    // and the skip would hang the boot it was meant to rescue.
                    chunkValidated++;
                    yield break;
                }

                // Both skips go through the same predicate as the denominator. Keep it that
                // way: a `continue` here that IsVerifiableLocally does not know about puts the
                // progress readout back out of reach of its total.
                if (!IsVerifiableLocally(urls[i]))
                {
                    if (!string.IsNullOrEmpty(urls[i]))
                        Development.LogError("External URL detected! Currently not supported for local download! " + urls[i]);

                    continue;
                }

                Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs, "Validating [" + urls[i] + "]");

                onAssetVerificationEvent?.Invoke(++validatedAssets - totalRetryCount, urls[i], verifiableAssets);

                string localFilePath;
                string localFolderPath;
                string fileURL;

                try
                {
                    // GetAssetPath calls Path.IsPathRooted, which throws ArgumentException on a
                    // value containing invalid path characters - and these values come from a CMS.
                    // Unguarded, that exception escapes the coroutine, the chunkValidated++ at the
                    // end never runs, and OnAssetsSorted_IE's WaitUntil hangs forever.
                    localFilePath = Core.settings.AppData.GetAssetPath(urls[i]);
                    localFolderPath = StringUtil.RemoveFileNameFromPath(localFilePath);
                    // if using local appData.json will still download from remote host
                    fileURL = string.Concat(Core.settings.AppData.remoteSettings.hostUrl, (string.IsNullOrEmpty(Core.settings.AppData.remoteSettings.assetsPath) ? "" : Core.settings.AppData.remoteSettings.assetsPath), urls[i]);
                }
                catch (Exception e)
                {
                    missingAssets++;
                    Development.LogError($"Cannot build a local path for the asset [{urls[i]}]: {e.Message}. "
                        + "Check the appData for invalid characters in this value. Skipping it - it will "
                        + "be missing at runtime.");
                    continue;
                }

                // Outside the try on purpose: this is a refusal, not a failure to compute, and the two
                // want different words in the log. Everything downstream - the .part sidecar, the
                // File.Exists freshness check, the DownloadHandlerFile write - derives from
                // localFilePath, so containing it here contains all of them.
                if (!IsInsideAssetRoot(localFilePath, out string containmentRefusal))
                {
                    refusedAssets++;

                    // Deliberately no File.Exists on the refused path. Checking would turn this guard
                    // into an existence oracle for any path the CMS names, which is most of what the
                    // guard exists to prevent - so the log says what was refused, not what is there.
                    Development.LogError($"Asset download REFUSED for [{urls[i]}]: {containmentRefusal}. "
                        + "Nothing was written and nothing was read. This value came from the appData, "
                        + "so if it is not an attempt to write outside the app it is a misconfiguration "
                        + "- check AppData.localSettings.assetsPath and AppData.remoteSettings.assetsPath "
                        + "in settings.json against the paths the CMS is serving. The asset is not "
                        + "downloaded; whether it still displays depends on whether it is readable where "
                        + "it points.");
                    continue;
                }

                // verify if file already exists and size checks, if it does move to next file in queue (if exists)
                if (File.Exists(localFilePath))
                {
                    bool skipThisFile = false;

                    // `using` rather than a bare request: the iterator lowers it to try/finally, so
                    // the handle is released on the `yield break` cancel path below too, which the
                    // original leaked - one native handle per file checked.
                    using (UnityWebRequest wwwHead = UnityWebRequest.Head(fileURL))
                    {
                        wwwHead.SetFileTransferTimeout(Core.settings.AppData.Cache.AssetDownload.timeout);

                        yield return wwwHead.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                        if (wwwHead.result != UnityWebRequest.Result.Success)
#else
                        if (wwwHead.isNetworkError || wwwHead.isHttpError)
#endif
                        {
                            // Only a *connection* failure counts toward the abort. A ProtocolError
                            // is the server answering - a 404 for an asset the CMS dropped is
                            // ordinary content drift, and counting it meant three stale references
                            // aborted the rest of the chunk and abandoned brand-new assets, while
                            // the log blamed an unreachable host. Connection errors and
                            // timeouts are the signal that the host is actually gone.
#if UNITY_2020_1_OR_NEWER
                            bool hostUnreachable = wwwHead.result == UnityWebRequest.Result.ConnectionError;
#else
                            bool hostUnreachable = wwwHead.isNetworkError;
#endif
                            // STALE, not missing. This whole branch is inside `if (File.Exists(...))`,
                            // and skipThisFile below keeps the local copy - so the asset is on disk and
                            // will render fine; all that failed is the freshness check. Counting it as
                            // missing made a fully-cached kiosk against a dead host report "225 will be
                            // MISSING at runtime" in the same breath as "will start with whatever media
                            // is already on disk", which is the kind of contradiction that teaches an
                            // operator to stop reading the error line. (An earlier version of this fix
                            // counted it nowhere at all, which was the opposite error - the log then
                            // said "0 will be MISSING" right after cancelling for three failures.)
                            staleAssets++;

                            if (hostUnreachable)
                            {
                                consecutiveFailures++;
                            }
                            else
                            {
                                // The server answered, so it is up. A 404 is content drift.
                                consecutiveFailures = 0;
                            }

                            Development.LogError("Error checking remote [" + fileURL + "]: " + wwwHead.error);

                            if (consecutiveFailures >= ConsecutiveFailureLimit)
                            {
                                // The `failedRemoteJson &&` precondition was removed here.
                                // With it, a fresh install - no local files, so this HEAD branch
                                // never runs - or a good appData against a dead asset host could
                                // never reach the abort at all, and every URL then cost
                                // (1 + maxRetryCount) x timeout = 4 x 60s. A hundred assets is
                                // about six and a half hours on the preloader, which looks exactly
                                // like a hang and which the boot watchdog cannot catch, because the
                                // phase is progressing - just uselessly.
                                //
                                // Booting with missing media beats not booting: the app runs, the
                                // gaps are visible, and this line says why.
                                // Everything left in this chunk is abandoned, so count it before
                                // bailing - the yield break skips the end-of-chunk summary, and
                                // without this the abandoned assets were reported nowhere at all.
                                // The abandoned tail is deliberately NOT added to missingAssets:
                                // those URLs were never checked, so whether they are on disk is
                                // unknown, and folding an unknown into a count labelled "will be
                                // MISSING" is the same over-claim this round of fixes removed.
                                // It is reported separately below.
                                //
                                // Everything the summary would have said is said here too: the
                                // `yield break` skips it, and unparseableHeaders is otherwise
                                // reported only by an Assert, stripped without DO_LOGS.
                                Development.LogError($"Cancelling asset download: {consecutiveFailures} "
                                    + "consecutive connection failure(s), so the host is very likely "
                                    + $"unreachable. Abandoning {urls.Length - i - 1} remaining asset(s) in "
                                    + $"this chunk - those were not checked, so some may be absent. "
                                    + $"{missingAssets} asset(s) will be MISSING at runtime and {staleAssets} "
                                    + $"could not be refreshed but are on disk; {unparseableHeaders} had no "
                                    + "usable Last-Modified. The app will start with the media it has.");
                                chunkValidated++;
                                yield break;
                            }

                            skipThisFile = true;
                        }
                        else
                        {
                            // TryParseExact, not Parse: "r" is the RFC1123 pattern Last-Modified
                            // uses, InvariantCulture makes its English day/month names locale-proof
                            // (Parse used CurrentCulture and could throw FormatException on a pt-PT
                            // machine), and AssumeUniversal|AdjustToUniversal pins the result to UTC
                            // to compare like-for-like against LastWriteTimeUtc. TryParse cannot
                            // throw, so a missing or malformed header no longer kills the chunk.
                            //
                            // The lenient second attempt matters: "r" accepts strict RFC1123 only,
                            // so a proxy that rewrites the header, or a server emitting RFC-850 or
                            // "UTC" instead of "GMT", would otherwise re-download every asset on
                            // every boot forever.
                            const DateTimeStyles utcStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
                            string lastModified = wwwHead.GetResponseHeader("Last-Modified");

                            if (!DateTime.TryParseExact(lastModified, "r", CultureInfo.InvariantCulture, utcStyles, out DateTime remoteUtc)
                                && !DateTime.TryParse(lastModified, CultureInfo.InvariantCulture, utcStyles, out remoteUtc))
                            {
                                // Unknown freshness re-downloads rather than skips: a stale asset on
                                // an unattended kiosk costs more than one wasted transfer. Counted
                                // rather than logged per asset - see the summary at the end, since
                                // LogWarning is stripped without DO_LOGS and this is the shape of
                                // "boot gets slower forever, cause invisible".
                                unparseableHeaders++;
                                Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs,
                                    $"No usable Last-Modified for [{fileURL}] (got [{lastModified ?? "<none>"}]).");
                                remoteUtc = DateTime.MaxValue;
                            }

                            DateTime localUtc = DateTime.MinValue;
                            long localLength = 0;

                            try
                            {
                                // One FileInfo for both values. Exception, not IOException:
                                // UnauthorizedAccessException here would escape the coroutine and
                                // hang the WaitUntil, and no timestamp read is worth that.
                                FileInfo info = new FileInfo(localFilePath);
                                localUtc = info.LastWriteTimeUtc;
                                localLength = info.Length;
                            }
                            catch (Exception e)
                            {
                                Development.LogError($"Could not read local file info for [{localFilePath}]: "
                                    + $"{e.Message}. Treating it as stale and re-downloading.");
                            }

                            // localLength > 0 is not belt-and-braces. If an earlier run failed to
                            // delete a partial download, a 0-byte file is left whose mtime is *now* -
                            // newer than any remote timestamp - so the size-blind check skipped it as
                            // "available and updated" and shipped an empty asset silently.
                            if (localLength > 0 && remoteUtc <= localUtc)
                            {
                                Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs, "File available and updated! Moving to next file!");
                                skipThisFile = true;
                            }

                            // A successful HEAD is proof the host is answering, whether or not the
                            // file needed re-downloading.
                            consecutiveFailures = 0;
                        }
                    }

                    if (skipThisFile)
                        continue;
                }

                Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs, "File unavailable or outdated!");

                if (!localFolderPath.EndsWith("/"))
                    localFolderPath += "/";

                localFolderPath = localFolderPath.Replace("//", "/");

                onAssetDownloadEvent?.Invoke(validatedAssets, fileURL, verifiableAssets);

                // CreateDirectory throws on a path the OS rejects - too long, invalid characters,
                // no write permission. C# forbids `yield` inside a try that has a catch, so the loop
                // body cannot be wrapped as a whole; the throwing calls are guarded individually.
                try
                {
                    if (!Directory.Exists(localFolderPath))
                    {
                        Directory.CreateDirectory(localFolderPath);
                        Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs, "Creating folder: " + localFolderPath);
                    }
                }
                catch (Exception e)
                {
                    missingAssets++;
                    Development.LogError($"Cannot create the download folder [{localFolderPath}]: {e.Message}. "
                        + $"Skipping [{urls[i]}] - it will be missing at runtime.");
                    continue;
                }

                bool downloadFailed;
                bool downloadHostUnreachable;

                // Download to a sidecar, never straight over the live file. DownloadHandlerFile
                // truncates its target the moment it is constructed, before a single byte arrives,
                // so pointing it at localFilePath meant a refresh that then failed destroyed media
                // that was working perfectly - a host blip during a site reboot was enough, and
                // after maxRetryCount the asset was simply gone. The good file is now not
                // touched until a complete download exists to replace it.
                string partPath = localFilePath + ".part";

                using (UnityWebRequest www = UnityWebRequest.Get(fileURL))
                {
                    // Constructed inside the using and assigned immediately, so the request owns it
                    // and disposes it when the block closes. Built before the request, it leaked an
                    // open write handle for the process lifetime if anything in between threw.
                    try
                    {
                        www.downloadHandler = new DownloadHandlerFile(partPath);
                    }
                    catch (Exception e)
                    {
                        missingAssets++;
                        Development.LogError($"Cannot open [{partPath}] for writing: {e.Message}. "
                            + $"Skipping [{urls[i]}] - it will be missing at runtime.");
                        continue;
                    }

                    www.SetFileTransferTimeout(Core.settings.AppData.Cache.AssetDownload.timeout);

                    // for websites blocking the user-agent used by urllib
                    www.SetRequestHeader("User-agent", DesktopUserAgent);

                    Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs, "Requesting download of " + www.url);

                    yield return www.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
                    downloadFailed = www.result != UnityWebRequest.Result.Success;
                    // Same classification the HEAD branch above makes, and it has to be made here
                    // too: this is the ONLY path a fresh install takes (no local files means the
                    // File.Exists branch never runs), so treating a 404 as a connection failure let
                    // three dead CMS references abort the chunk while the log blamed the network -
                    // and the assets it then abandoned were the new ones nobody had seen yet.
                    downloadHostUnreachable = www.result == UnityWebRequest.Result.ConnectionError;
#else
                    downloadFailed = www.isNetworkError || www.isHttpError;
                    downloadHostUnreachable = www.isNetworkError;
#endif
                    // The attempt number and the ceiling belong on this line rather than on a
                    // second one next to the retry, because this already fires - unconditionally, at a
                    // level that survives every target - for every failed attempt. What was missing was
                    // never the failure, it was that a retry was coming and how many were left, so the
                    // 60-second gaps between identical error lines read as a hang.
                    if (downloadFailed)
                        Development.LogError($"Download failed [{urls[i]}], attempt {retryCount + 1} "
                            + $"of {maxRetryCount + 1}: {www.error}");
                }

                // The delete happens AFTER the using block closes, and that ordering is the point:
                // DownloadHandlerFile holds an open write handle to localFilePath until the request
                // is disposed, so File.Delete on it threw IOException from inside a coroutine,
                // unhandled. That killed the chunk, the chunkValidated++ below never ran, the
                // WaitUntil in OnAssetsSorted_IE never completed, and the app sat on the preloader
                // forever.
                if (downloadFailed)
                {
                    // Only the sidecar is removed. Whatever was at localFilePath is still there and
                    // still good, which is the entire point of the .part swap.
                    TryDeletePartialFile(partPath);

                    if (retryCount < maxRetryCount)
                    {
                        // Deliberately no log here. This used to be a LogWarning behind
                        // `if (doLogs)` that said "Retrying 0/3" and nothing else - doubly invisible,
                        // since doLogs ships false and LogWarning is [Conditional("DO_LOGS")] as well.
                        // The obvious repair was an ungated LogError, but that put two lines on every
                        // failed attempt: the one above already names the url and the error. The attempt
                        // counter went there instead.
                        retryCount++;
                        totalRetryCount++;
                        i--;
                    }
                    else
                    {
                        // The .part swap means a failed download never destroys the previous copy, so "is it
                        // on disk?" is a real question here rather than always no.
                        bool haveUsableCopy = File.Exists(localFilePath);

                        if (haveUsableCopy)
                            staleAssets++;
                        else
                            missingAssets++;

                        if (downloadHostUnreachable)
                            consecutiveFailures++;
                        else
                            consecutiveFailures = 0;

                        Development.LogError($"Giving up on [{urls[i]}] after {maxRetryCount} retries. "
                            + (haveUsableCopy
                                ? "The previous copy is still on disk and will be used."
                                : "It will be missing at runtime."));
                        retryCount = 0;

                        // The abort has to be tested here too. On a fresh install there are no local
                        // files, so the File.Exists HEAD branch above never runs and its check could
                        // never fire - and without this test every URL still costs
                        // (1 + maxRetryCount) x timeout, about six and a half hours for 100 assets.
                        if (consecutiveFailures >= ConsecutiveFailureLimit)
                        {
                            Development.LogError($"Cancelling asset download: {consecutiveFailures} "
                                + "consecutive connection failure(s), so the host is very likely "
                                + $"unreachable. Abandoning {urls.Length - i - 1} remaining asset(s) in "
                                + $"this chunk - those were not checked, so some may be absent. "
                                + $"{missingAssets} asset(s) will be MISSING at runtime and {staleAssets} "
                                + $"could not be refreshed but are on disk; {unparseableHeaders} had no "
                                + "usable Last-Modified.");
                            chunkValidated++;
                            yield break;
                        }

                        continue;
                    }
                }
                else
                {
                    // Reset BEFORE the swap, not after it. The bytes arrived, which is the only
                    // evidence this counter is about; a swap that then fails on an AV lock or a
                    // read-only target would otherwise leave the streak intact and let a later
                    // failure declare a demonstrably answering host unreachable.
                    consecutiveFailures = 0;

                    // Swap the completed download in. File.Replace when the target exists so the
                    // switch is a single operation rather than delete-then-move with a window in
                    // which the asset exists nowhere.
                    try
                    {
                        if (File.Exists(localFilePath))
                            File.Replace(partPath, localFilePath, null);
                        else
                            File.Move(partPath, localFilePath);

                        Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs, "Success downloading to " + localFilePath);
                    }
                    catch (Exception e)
                    {
                        // The previous copy is untouched by a failed swap - that is the whole point
                        // of it - so this is stale-but-usable when one exists, missing when it
                        // does not - the same distinction the give-up path makes.
                        if (File.Exists(localFilePath))
                            staleAssets++;
                        else
                            missingAssets++;

                        Development.LogError($"Downloaded [{urls[i]}] but could not move it into place at "
                            + $"[{localFilePath}]: {e.Message}. The previous copy, if any, is untouched; "
                            + $"the download is left at [{partPath}].");
                    }

                    retryCount = 0;
                }
            }

            // One summary at Error level, because the per-asset detail above is either Assert or
            // LogWarning and both vanish without DO_LOGS. Without this, a chunk that silently lost
            // half its media looked identical to a clean run.
            }
            finally
            {
                MissingAssetCount += missingAssets;
                RefusedAssetCount += refusedAssets;
            }

            if (missingAssets > 0 || staleAssets > 0 || unparseableHeaders > 0 || refusedAssets > 0)
            {
                Development.LogError($"Asset chunk finished with problems: {missingAssets} asset(s) will be "
                    + $"MISSING at runtime, {staleAssets} could not be refreshed but are on disk, "
                    + $"{unparseableHeaders} had no usable Last-Modified header so they will re-download on "
                    + $"every boot until the server sends one, and {refusedAssets} were REFUSED for "
                    + "pointing outside the asset root - a non-zero count there is worth reading the "
                    + "individual lines for, because it means the appData asked to write somewhere it "
                    + "should not.");
            }
            else
            {
                Development.LogIf(Core.settings.AppData.Cache.AssetDownload.doLogs, "Media validadion complete!");
            }

            chunkValidated++;
            yield return null;
        }
    }
}