/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using EnigmaVirtual.Notifications;
using EnigmaVirtual.OneCoreClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace EnigmaVirtual
{
    public class AppDataController : MonoBehaviour
    {
        //public event Action OnDataChangedEvent;
        public event Action OnDataLoadFailedEvent;
        public event Action OnDataLoadedEvent;

        public Translations Translations { get; private set; } = new Translations();
        /// <summary>
        /// The loaded content model, as the framework's base type.
        /// </summary>
        /// <remarks>
        /// Base type, not the project's concrete one: naming the project type here would make the
        /// framework assembly depend on <c>App</c>. <c>MainController</c> exposes these the same way,
        /// and its comment already shows the pattern App code uses:
        /// <code>public static AppData Data =&gt; (AppData)Core.main.AppData;</code>
        /// A project that needs its own fields populated overrides <see cref="AppDataType"/> and
        /// <see cref="AppSettingsType"/>, and authors the subclass in the scene - see the block below.
        /// </remarks>
        public AppDataBase AppData { get; private set; }

        /// <summary>The loaded settings model, as the framework's base type.</summary>
        /// <remarks>Same reasoning as <see cref="AppData"/>.</remarks>
        public AppSettingsBase AppSettings { get; private set; }

        // ---------------------------------------------------------------------------------------
        // Construction hooks. Everything above deserializes into the *Base types, so the framework
        // never names a project type and the App -> framework dependency stays one-way. A branched
        // project that wants its own model populated subclasses this controller and overrides these four
        // - one line each - and nothing else changes: MainController.SetupAppData already discovers the
        // component with GetComponentInChildren, so it finds the subclass for free, and its AddComponent
        // fallback still serves projects that do not subclass.
        //
        //     public class ProjectAppDataController : AppDataController
        //     {
        //         protected override Type AppDataType => typeof(AppData);
        //         protected override Type AppSettingsType => typeof(AppSettings);
        //     }
        //
        // ...and authored under Core in CoreSetup.unity, so SetupAppData discovers it (it is, at
        //     Core/MainController/AppData). THAT STEP IS NOT OPTIONAL: without
        // it the framework deserializes into the base types, the project's own members are never
        // populated, and because MediaWalker is reflective and every consumer takes `object`, nothing
        // fails to compile. AssetManager detects it (the "model cannot see the media the payload contains"
        // LogError) but clears the cache anyway - so on a site with real content the
        // media cache goes. The migration note in overview.md spells this out.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The concrete content model to deserialize into. Override to return the project's type.
        /// </summary>
        /// <remarks>
        /// A <c>Type</c> rather than one virtual method per call site. The first version of this had four
        /// hooks - create, deserialize-appData, deserialize-settings-file, deserialize-settings-block -
        /// and they are all-or-nothing with nothing saying so: a project that
        /// overrode only the creator would write a full settings file (<c>JsonUtility.ToJson</c>
        /// serializes the *runtime* type) and read it back through a base-typed deserializer, so the
        /// object silently reverted to the base and the **next save erased the derived keys from disk**.
        /// Two properties cannot get out of sync with each other.
        /// </remarks>
        protected virtual Type AppDataType => typeof(AppDataBase);

        /// <inheritdoc cref="AppDataType"/>
        protected virtual Type AppSettingsType => typeof(AppSettingsBase);

        /// <summary>
        /// Reports the one failure this design can produce silently: the payload has media and the
        /// model cannot represent it.
        /// </summary>
        /// <remarks>
        /// Almost always means the project's controller was never authored in the scene, so the
        /// framework fell back to the base types. Nothing downstream will say so - every consumer takes
        /// <c>object</c> and <c>MediaWalker</c> is reflective - and the symptom is an app with no
        /// content plus an asset cleanup that treats every cached file as stale. <c>AssetManager</c>
        /// reports it too, but only on the cleanup path, which is off by default.
        /// </remarks>
        private void ReportIfModelCannotSeeTheMedia()
        {
            if (AppDataType != typeof(AppDataBase) || RawMediaValueCount == 0)
                return;

            Development.LogError($"appData declares {RawMediaValueCount} media value(s), but the content "
                + "model is the framework's AppDataBase, which has no media members - so nothing will "
                + "download and the asset cleanup will treat every cached file as stale. Author "
                + "ProjectAppDataController (App/Data/) on a GameObject under Core, or override "
                + "AppDataType on your own AppDataController subclass. (M20)");
        }

        private bool loadingData = false;
        public bool dataLoaded = false;

        /// <summary>
        /// True once <see cref="OnDataLoadedEvent"/> has actually been raised for the current load.
        /// </summary>
        /// <remarks>
        /// <b>Not the same question as <see cref="dataLoaded"/>, which is why this exists.</b> Gating
        /// the failure event on <c>dataLoaded</c> is wrong in both
        /// directions: <c>ParseAppData</c> sets it optimistically *before* parsing and clears it on
        /// failure, so it means "no parse error seen yet", not "success was announced". A throw between
        /// the assignment and the raise - the null <c>jObj</c> case - leaves it true
        /// with no event raised, and the guard would then suppress the failure report and point the
        /// reader away from the fault. Set immediately before each raise instead, and reset per load.
        /// </remarks>
        private bool successAnnounced = false;

        /// <summary>
        /// Seconds to wait before falling back to the local appData after a failed remote fetch.
        /// </summary>
        /// <remarks>
        /// Also a bare <c>5f</c> until v0.49.3. The delay exists so a transient failure is not
        /// retried instantly, and it sits on the boot path - but it does not spend a boot *total*,
        /// because <c>Core.BootWatchdog_IE</c> measures the gap between two <c>_bootStage</c>
        /// changes, not elapsed startup. What it spends is stall budget inside one stage, and the
        /// floor there is <c>BootStallTimeoutSeconds</c> = 90s, raised to
        /// <c>2 x widest + LocalDataFallbackSeconds</c> once that exceeds it - where "widest" is the
        /// widest effective timeout that can be in flight during boot, so <c>AppData.timeout</c> or
        /// either cache timeout. The floor is exceeded above <b>42.5s</b>, and the shipped
        /// configuration gives <b>125s</b>.
        /// Note the budget is derived from <b>both</b> the download timeout and
        /// <c>AppData.timeout</c>, whichever is wider - and this delay is now a term in it rather than
        /// an assumption that it fits, because the stage can issue two bounded requests with this gap
        /// between them. 5s of that is room, not free.
        /// </remarks>
        internal const float LocalDataFallbackSeconds = 5f;

        /// <summary>
        /// Bound on the appSettings read, in seconds. Not settings-driven: see the call site, which
        /// also records why "local read" is only true of Android and not of WebGL.
        /// </summary>
        private const int LocalReadTimeoutSeconds = 10;

        /// <summary>The pending local-data fallback. Held so a second TryLoadData replaces it: with
        /// Invoke the first call's timer stayed pending and fired against the second call.</summary>
        private Coroutine _pendingLocalLoad;
        /// <summary>
        /// The appData did not come from the CMS, so the asset keep-list built from it cannot be
        /// trusted to say what is unreferenced.
        /// </summary>
        /// <remarks>
        /// <b>Not the same predicate as <c>APIResponseModel.fromCache</c>, and deliberately not merged
        /// with it.</b> This one is a *safety* guard: <c>AssetManager</c> reads it before the
        /// recursive delete, so a false positive costs a stale cache and a false negative costs
        /// the site's media. <c>fromCache</c> is a *freshness* marker for whoever renders the content.
        /// They answer different questions, and a single flag would have to pick one meaning.
        /// <para>
        /// They diverge in **both** directions, which is what settles it. A
        /// <c>useLocalAppData: true</c> deployment reaches <c>LoadLocalData</c> directly and
        /// <c>FailRemoteAppData</c> returns early for that config, so this stays <b>false</b> while
        /// <c>fromCache</c> is true - and the cleanup correctly still runs, because that keep-list came
        /// from a real CMS response. Going the other way, a remote fetch that fails outright never
        /// reaches the marking at all - there is no response to mark - so this is <b>true</b> while
        /// <c>fromCache</c> is false. Merge them and the cleanup delete site would read
        /// "the appData came from a file" instead of "the keep-list is untrustworthy", which refuses
        /// cleanup on the deliberate local deployment for no safety gain.
        /// </para>
        /// <para>
        /// What this is <i>not</i>: a claim that the two disagree on the common path. On a remote fetch
        /// that failed, <c>FailRemoteAppData</c> sets this <b>before</b> invoking <c>LoadLocalData</c>,
        /// so both flags are true together <i>once a local copy has been read and marked</i> - which is
        /// now every target, not just desktop. Between the failure and that read, this flag is true and
        /// <c>fromCache</c> is still false, which is the window the paragraph above describes.
        /// </para>
        /// </remarks>
        public bool failedRemoteJson { get; private set; }

        /// <summary>
        /// How many media values the raw appData JSON declares, counted from the text before it is
        /// deserialized - so it is independent of whether the AppData model can represent them.
        ///
        /// This is what lets the asset cleanup tell "this content set genuinely has no media, so
        /// the whole cache is stale" apart from "the model cannot see the media that is there".
        /// Both produce an empty keep-list, and they need opposite answers.
        /// </summary>
        internal int RawMediaValueCount { get; private set; }

        /// <summary>
        /// Counts occurrences of <c>"&lt;mediaKey&gt;": "..."</c> in the raw JSON, for every member
        /// name in <c>AppSettings.mediaPaths</c>. Deliberately textual: the point is to know what
        /// the payload contains without going through the model.
        /// </summary>
        private void CountRawMediaValues(string raw)
        {
            RawMediaValueCount = 0;

            if (string.IsNullOrEmpty(raw))
                return;

            string[] keys = AppSettings?.mediaPaths;
            if (keys == null || keys.Length == 0)
                keys = new[] { "url", "thumb" };

            try
            {
                foreach (string key in keys)
                {
                    // "key" followed by a colon and a non-empty string value.
                    string pattern = "\"" + System.Text.RegularExpressions.Regex.Escape(key) + "\"\\s*:\\s*\"[^\"]+\"";
                    RawMediaValueCount += System.Text.RegularExpressions.Regex.Matches(raw, pattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
                }
            }
            catch (Exception e)
            {
                // Leave the count at 0 rather than guessing - callers treat 0 as "declares none",
                // and the cleanup's other guards still apply.
                Development.LogError($"Could not count media values in raw appData: {e.Message}");
            }
        }
        public virtual void Init()
        {
            Development.LogIf(Core.settings.AppData.doLogs, "AppData initialized");

            // Before the guard, not after: an early return must not leave this null, and the one
            // pre-Init reader (CountRawMediaValues) is null-safe but the rest of the class is not.
            // Not a field initializer either. Not for the reason first written here - derived field
            // initializers actually run BEFORE the base constructor - but because this is a MonoBehaviour:
            // Unity has not deserialized its serialized fields at construction time, so an override that
            // read one would see a pre-deserialization value.
            AppSettings = (AppSettingsBase)Activator.CreateInstance(AppSettingsType);

            if (!Core.IsSet)
                return;

            TryLoadData();

            if (Core.settings.AppData.isActive && !Core.settings.AppData.oneCoreDataModel.isActive)
                LoadSettings();
        }

        // Ctrl+Alt+R manual refresh used to live here, commented out, together with a
        // refreshCooldown/refreshRequest latch it was the only reader of. Both are gone: a latch whose
        // only exit is a delayed callback, kept alive for a caller that does not exist, is what makes
        // the next reader trust it.
        //
        // To restore manual refresh you need re-entry protection, and `loadingData` is NOT it as
        // things stand: it is written in five places and read in none, and its four clear sites are all
        // failure or early-exit paths - the success path falls off the end of LoadRemoteData with it
        // still true. So `if (!loadingData) TryLoadData()` would fire once and never again. Clearing it
        // on the success path is the prerequisite (M117); after that it is the right guard. Do not
        // reintroduce a second latch, and if a debounce is wanted put it on the input, not the load.

        protected void TryLoadData()
        {
            Development.CheckPoint(Core.settings.AppData.doLogs);

            // Per load, not per lifetime: without this a second load starts holding the first load's
            // answer, and a plain connection failure on a refresh takes the suppress branch.
            successAnnounced = false;

            if (Core.settings.AppData.useLocalAppData)
                LoadLocalData();
            else
                StartCoroutine(LoadRemoteData(Core.settings.AppData.dataJsonCompletePath, false, false));
        }

        /// <param name="isLocalFallback">
        /// <c>true</c> when <paramref name="path"/> points at local disk rather than the CMS, which is
        /// how <c>LoadLocalData</c> reaches this method on Android - StreamingAssets lives inside the
        /// package there, so even reading a local file needs <c>UnityWebRequest</c>. It suppresses the
        /// cache write, because a payload that came off disk is not a CMS response and storing it
        /// destroys the placeholder detection described at that call site.
        /// <para><b>Required rather than defaulted on purpose.</b> <c>false</c> is the direction that
        /// writes, so a future call site that simply forgot the argument would re-arm the write with no
        /// compile error - and the write is the defect. Two call sites, both in this file, so a default
        /// buys nothing.</para>
        /// </param>
        private IEnumerator LoadRemoteData(string path, bool isLocalFallback, bool localIsPlaceholder)
        {
            Development.LogIf(Core.settings.AppData.doLogs, "LoadRemoteData: " + path);
            loadingData = true;

            // Kept before the file:// conversion below, because the age has to be read off the file
            // rather than off a URL - and because a path that still carries a scheme after this point
            // is a package entry with no timestamp to read at all.
            string localSourcePath = path;

            if (!Core.IsSet)
            {
                Development.LogError("Core not available!");
                loadingData = false;
                yield break;
            }

            if (string.IsNullOrEmpty(path))
            {
                if (Core.settings.AppData.doLogs)
                    Development.LogWarning("App settings not available!");

                loadingData = false;
                yield break;
            }
            Development.LogIf(Core.settings.AppData.doLogs, path);

#if UNITY_ANDROID || UNITY_IOS
            // file:// whenever the path has no scheme, rather than on a hardcoded prefix. The old tests
            // were `/storage/` on Android and `/Users/` on iOS, and neither matches a real
            // persistentDataPath: Android is only under /storage/ with Write Permission =
            // External(SDCard), and this project ships `ForceSDCardPermission: 0`, so the real path is
            // /data/user/0/<pkg>/files. `/Users/` is a macOS path, not an iOS one.
            //
            // Config seeding arms this from the second launch onward: once a persistent copy exists,
            // AppPaths.Config returns it rather than falling through to the jar:file:// StreamingAssets
            // URL, which already carried a scheme. Nothing in the boot chain waits on appSettings, so
            // getting this wrong fails with one blank error line and default settings.
            path = AppPaths.ToFileUrl(path);
#endif

            // A `using` DECLARATION rather than a block: it disposes at the end of this scope with
            // the same semantics a block would compile to, and it avoids re-indenting a hundred lines
            // of coroutine body - which is the change most likely to introduce a brace error in a
            // method the whole boot chain depends on. Without any disposal the native handle and
            // download buffer leaked once per boot, twice on a boot that falls back, and every
            // `yield break` below is another exit that a hand-placed Dispose would have to remember.
            //
            // Not claimed: a coroutine Unity *stops* rather than lets finish - StopCoroutine,
            // SetActive(false) and a scene unload all abandon the iterator without disposing it. This
            // controller lives under the persistent Core, which is why that is a note and not a second
            // defect. Same wording as LoadRemoteSettings, which took this fix first.
            using UnityWebRequest www = UnityWebRequest.Get(path);
            www.SetRequestHeader("Accept", "application/json");

            // Without this a half-open socket never returns: www.result is never evaluated,
            // FailRemoteAppData never runs, the local fallback never starts, and the boot stops here
            // until the stall watchdog latches. The API policy rather than the file-transfer one -
            // this is a small JSON document, so 0 means "unset" and becomes 30s rather than meaning
            // "no limit". Core.StallBudgetSeconds reads the same key, so widening it widens the
            // watchdog's budget instead of racing it.
            www.SetApiTimeout(Core.settings.AppData.timeout);

            yield return new WaitForEndOfFrame();
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Development.LogError($"AppData request to [{path}] failed [{www.result}]: {www.error}. "
                    + "Body: " + www.downloadHandler?.text);

                FailRemoteAppData("APPDATA ERROR", null);
            }
            else
            {
                if (www.isDone)
                {
                    APIResponseModel response;

                    // Two try blocks, not one. This used to be a single catch around the
                    // deserialization AND OnGetAppData - and OnGetAppData runs ParseAppData, which
                    // raises OnDataLoadedEvent, which reaches MainController and from there
                    // SetupAssetManager and LoadNextScene. So a failure anywhere in the rest of boot
                    // was caught here and reported to the operator as "Error parsing remote appdata
                    // JSON", naming the wrong subsystem with the wrong ex.Message.
                    try
                    {
                        response = JsonUtility.FromJson<APIResponseModel>(www.downloadHandler.text);
                        response.raw = www.downloadHandler.text;

                        // Before OnGetAppData, so every consumer of the model sees the marking rather
                        // than a response that claims to be fresh from the CMS.
                        if (isLocalFallback)
                            MarkServedFromLocalFile(response, localSourcePath, localIsPlaceholder);
                    }
                    catch (Exception ex)
                    {
                        FailRemoteAppData("Error parsing remote appdata JSON", ex);
                        yield break;
                    }

                    try
                    {
                        OnGetAppData(response);
                    }
                    catch (Exception ex)
                    {
                        // Still caught, deliberately. Letting it escape would kill this coroutine with
                        // loadingData stuck true, and the boot chain has no timeout - a permanent stall
                        // is worse than a mislabelled error. What changes is that the label is now
                        // accurate and the recovery still runs.
                        FailRemoteAppData("Appdata parsed, but handling it failed", ex);
                        yield break;
                    }

                    // Only cache a payload the parser accepted. OnGetAppData returns normally for a
                    // response whose `code` is not Success - it logs, raises the failure event and
                    // returns - so control arrived here regardless and WriteAtomic stored the failure
                    // body as the offline cache, to be read back on the next boot as if it were
                    // content. successAnnounced is false on exactly that path.
                    if (!successAnnounced)
                    {
                        Development.LogError("Not caching this response: it was fetched successfully but "
                            + "did not parse into usable appdata - or the server answered with a "
                            + "non-Success code - and caching it would serve the failure back as content "
                            + "on the next offline boot.");

                        loadingData = false;
                        yield break;
                    }

                    // Never when the payload came off local disk. There is nothing to cache - the content
                    // did not come from the CMS - and what it destroys is the placeholder detection that
                    // decides whether the asset keep-list can be trusted. Two shapes reach here, and a
                    // same-file test would only catch the first:
                    //
                    //   - a cache exists, so `path` IS the file about to be written. WriteAtomic replaces
                    //     it, which moves LastWriteTimeUtc, so the length|ticks stamp no longer matches
                    //     and IsShippedPlaceholderCopy stops recognising a seeded stub as a stub.
                    //   - no cache exists, so `path` is the shipped copy and the destination is a
                    //     *different* file - the same-file test passes and this is the worse case. The
                    //     write CREATES the file the seed would have created, and ConfigManager.Apply
                    //     skips a deployed appData.json that already exists, so StampSeededAppData never
                    //     runs at all. There is no stamp to mismatch, and the content comparison that
                    //     would otherwise stand in cannot read a jar:file:// origin - so the stub is
                    //     classified as a real cache permanently.
                    //
                    // Either way failedRemoteJson stays false and an empty keep-list from a stub is
                    // treated as authoritative. Dormant only while cleanUnlistedAssets ships false.
                    if (Core.settings.AppData.Cache.AssetDownload.isActive && !isLocalFallback)
                    {
                        // The same resolver the read uses. This site used to compose its own path -
                        // Path.Combine(PersistentConfig, <folder>) plus a hand-rolled separator - while
                        // downloadedDataJsonPath composed a different one, so the cache was written and
                        // then never found. They agreed only because the folder segment was "" in every
                        // shipped config, which is the same reason that folder is gone as of v0.51.0.
                        //
                        // The resolver is a bool-returning method rather than a property precisely so this
                        // site cannot throw: both path segments come from settings.json, and Mono's
                        // Path.Combine throws on invalid path characters. My first version wrapped only the
                        // write in a try and left the composition outside it, so the comment claimed
                        // a guarantee the code did not give.
                        if (Core.settings.AppData.TryGetDeployedDataJsonPath(out string filePath))
                        {
                            try
                            {
                                string localFolderPath = Path.GetDirectoryName(filePath);

                                if (!string.IsNullOrEmpty(localFolderPath) && !Directory.Exists(localFolderPath))
                                {
                                    Directory.CreateDirectory(localFolderPath);
                                    Development.LogIf(Core.settings.AppData.doLogs, "Creating folder: " + localFolderPath);
                                }

                                ConfigManager.WriteAtomic(filePath, response.raw,
                                    "caching the appData response for the offline fallback");
                            }
                            catch (Exception e)
                            {
                                // The consequence, stated correctly - not "the boot chain
                                // stopped" and "there will be nothing to fall back on" - both wrong.
                                // OnGetAppData has already fired OnDataLoadedEvent synchronously by this
                                // point and nothing follows the write, so boot is unaffected; and there
                                // *is* a fallback, which is the problem - it is the shipped placeholder,
                                // and it reports success.
                                Development.LogError($"could not cache appData to [{filePath}]: {e.Message}. "
                                    + "This run is fine, but if the next fetch fails there is no cache to "
                                    + "fall back on and the shipped placeholder will load instead - which "
                                    + "parses as a success and declares no media.");
                            }
                        }
                    }
                }
                else
                {
                    // Unreachable: www.result == Success implies www.isDone, so this else can never run.
                    // Kept rather than deleted because deleting a branch is a bigger claim than routing
                    // it, and it held a fourth verbatim copy of the recovery block that a later reader
                    // would otherwise have deduped in the wrong direction.
                    Development.LogError($"CALL ERROR {www.responseCode}: {www.error} for "
                        + Core.settings.AppData.remoteSettings.dataJsonCompletePath);

                    FailRemoteAppData("CALL ERROR", null);
                }
            }

            // The success fall-off. Every FAILURE exit already cleared this - four of them, plus
            // FailRemoteAppData - and the success path did not, so after one good load the flag stayed
            // true for the process lifetime. Nothing read it, so nothing broke; what it meant was that
            // the first person to use it as the re-entry guard it looks like would get exactly one load
            // per run.
            //
            // Not a try/finally: this is a coroutine, and a coroutine's finally does not run when Unity
            // STOPS it (StopCoroutine, SetActive(false), enabled = false, destroy). So a cancelled load
            // still leaves this true. That is the honest remaining gap and it belongs in OnDisable,
            // which this class does not have; nothing depends on it today, and adding a lifecycle hook
            // for a flag with no readers would be inventing the coupling this entry exists to remove.
            loadingData = false;
        }

        /// <summary>
        /// True when the file at <paramref name="path"/> is <c>ConfigManager</c>'s seeded copy of the
        /// shipped <c>appData.json</c>, rather than anything the remote pipeline ever wrote.
        /// </summary>
        /// <remarks>
        /// <para><b>The stamp answers first</b> (<c>ConfigManager.seedStampSuffix</c>, written at seed
        /// time), because the obvious test - compare the deployed file with the shipped one - fails
        /// silently on exactly the machines that need it. <c>appData.json</c> is seeded once and never
        /// patched, and every branched project edits its shipped copy sooner or later; after the first
        /// such release, an already-deployed seed no longer matches the file it came from and a content
        /// comparison reports "real cache" over a placeholder, and
        /// it is worse than the defect being fixed, because a detector that has quietly stopped working
        /// reads exactly like one that found nothing.</para>
        ///
        /// <para><b>Content comparison stays as the fallback</b>, for machines seeded before the stamp
        /// existed - the same argument in the other direction. It is routed on the path, not the
        /// platform: inside an Android package the shipped copy is a <c>jar:file://</c> URL that
        /// <c>File</c> cannot open, so no comparison is possible there and this returns <c>false</c>.
        /// The length test is one-sided on purpose: an equality test disagreed with the text compare
        /// exactly where a BOM lands (<c>Apply</c> reads BOM-stripped and <c>WriteAtomic</c> writes
        /// none, so a re-saved shipped file is 3 bytes longer than its own seed), which would have
        /// disabled the whole thing with nothing logged.</para>
        ///
        /// <para><b>Never call this in local mode.</b> There the shipped file *is* the intended content
        /// and the seed is supposed to match it. The caller gates on that; this method cannot, because
        /// "identical to the shipped copy" is all it can honestly measure.</para>
        ///
        /// <para>Reading <c>Core.settings</c> from a static is safe here by precondition rather than by
        /// check: <c>LoadLocalData</c> has already dereferenced <c>downloadedDataJsonPath</c>, and any
        /// <c>localSettings.dataJsonPath</c> that would throw out of <c>Path.Combine</c> already made
        /// <c>TryGetDeployedDataJsonPath</c> return false, which routes to the caller's other branch.</para>
        /// </remarks>
        private static bool IsShippedPlaceholderCopy(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                FileInfo deployed = new FileInfo(path);
                string stampPath = path + ConfigManager.seedStampSuffix;

                if (File.Exists(stampPath))
                {
                    string[] parts = File.ReadAllText(stampPath).Split('|');

                    if (parts.Length == 2
                        && long.TryParse(parts[0], out long length)
                        && long.TryParse(parts[1], out long ticks))
                        return deployed.Length == length && deployed.LastWriteTimeUtc.Ticks == ticks;

                    Development.LogError($"the appData seed stamp [{stampPath}] could not be read as "
                        + "\"length|ticks\", so it cannot say whether the file beside it is a seed or a "
                        + "cache. Delete the stamp to silence this; the shipped-copy comparison below "
                        + "still runs.");
                }

                string shipped = AppPaths.StreamingConfigFile(Core.settings.AppData.localSettings.dataJsonPath);

                // Comparing a file with itself would return a guaranteed true. Reachable only when
                // localSettings.dataJsonPath is rooted inside persistentDataPath, since Path.Combine
                // discards its first argument for a rooted second one - not, as this comment first
                // claimed, because the caller's other branch reported it. That branch requires
                // filePath != deployedPath, which is false here.
                if (shipped.Contains("://") || string.Equals(shipped, path, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!File.Exists(shipped))
                {
                    Development.LogError($"the shipped appData [{shipped}] is missing, so there is "
                        + $"nothing to compare [{path}] against and no stamp beside it either. Whether "
                        + "the local copy is real content or a placeholder cannot be established this "
                        + "run; it is being treated as real.");

                    return false;
                }

                FileInfo origin = new FileInfo(shipped);

                // One-sided, and generous: this exists only to skip reading a multi-megabyte CMS cache,
                // not to decide anything.
                if (deployed.Length > 4 * origin.Length + 8)
                    return false;

                return string.Equals(File.ReadAllText(shipped), File.ReadAllText(path), StringComparison.Ordinal);
            }
            catch (Exception e)
            {
                // A diagnostic, not a load step: whatever went wrong here, the caller still has a file
                // to read. Logged with its frames so it does not become the next silent failure,
                // then treated as "cannot tell", which is the honest answer.
                Development.LogException(e);
                return false;
            }
        }

        private void LoadLocalData()
        {
            Development.CheckPoint(Core.settings.AppData.doLogs);

            string filePath = Core.settings.AppData.downloadedDataJsonPath;

            // The fallback announces itself, at Error, and marks the data untrustworthy.
            //
            // `downloadedDataJsonPath` returns the shipped copy when there is no cache. That stopped the
            // boot hang, but on its own it was worse than the hang it replaced: the shipped
            // appData.json is a placeholder stub whose `"code": 0` is APIResponseCode.Success, so the app
            // boots normally on two languages, one text key and **no media** - a kiosk showing blank
            // screens, indistinguishable from a CMS problem, with nothing above Log level to say why.
            // The old hard failure diagnosed better, so the fallback is only worth keeping if it is loud.
            //
            // failedRemoteJson is the second half, and it is the one that matters most. It is the
            // guard AssetManager checks before deleting unlisted files. Were it set only on the
            // `!useLocalAppData` branches, a local-mode run on the stub would present an **empty
            // keep-list** as authoritative and the cleanup would remove the operator's entire media
            // set - which is why it is set on this branch too.
            // `File.Exists` at the cache path is not evidence of a cache. ConfigManager seeds
            // the shipped appData.json into PersistentConfig, which is the same config root - so
            // on a machine that has never reached the CMS, the seed *is* the cache path, the check
            // below passes, and the placeholder was served as content with nothing said. Worse than
            // nothing said: the age line then reported the seed's own write time, so a first boot
            // announced "0 second(s) ago" over placeholder content.
            // Set by *both* placeholder branches, not just the seeded one: the age line below is
            // wrong in the same way for either, and the no-cache-at-all case needs no detection to
            // know the answer - it is looking straight at the shipped file, whose timestamp is install
            // time. The first version reported "the screen is showing content from
            // that time" about a stub on the one branch that was certain it was a stub.
            bool isPlaceholder = false;

            // TWO outcomes, since downloadedDataJsonPath has two candidates: the appData at the config
            // root, and the file that shipped in the build. Only the second is unconditionally a
            // placeholder.
            //
            // This was a three-way test until v0.51.0, separating a `downloadFolder` cache from a
            // deployed copy. Those were always the same path - the field shipped empty and no branch set
            // it - so the extra branch discriminated nothing. What must NOT be lost with it is why
            // the third candidate was added: testing only "is it the cache?" announced an operator's
            // deployed appData as the shipped placeholder and switched off asset cleanup for it. The
            // surviving test is the permissive one, so that stays fixed.
            AppDataSettings appData = Core.settings.AppData;

            bool isDeployed = appData.TryGetDeployedDataJsonPath(out string deployedPath)
                && string.Equals(filePath, deployedPath, StringComparison.OrdinalIgnoreCase);

            if (!isDeployed)
            {
                isPlaceholder = true;
                failedRemoteJson = true;

                Development.LogError($"no appData at the config root (looked at "
                    + $"[{deployedPath ?? "<unresolvable>"}]), so the shipped placeholder at "
                    + $"[{filePath}] is being used instead. It parses as a success and declares no "
                    + "media, so expect empty content. Asset cleanup is disabled for this run because "
                    + "the keep-list cannot be trusted.");
            }
            // Not in local mode. There the shipped config/appData.json *is* the content the project
            // ships, ConfigManager seeds it, and the seed matching it is the system working - so the
            // check below would fire on every correctly configured offline deployment, call its
            // content a placeholder, and switch off asset cleanup for it. That had to be fixed
            // before the change reached a branched project.
            // The config root is exactly where ConfigManager's seed lands, so the file found there is
            // the one most likely to BE the stub, and the stamp is what tells them apart. Guessing from
            // the path cannot - which is the whole reason the stamp exists, and the reason removing the
            // second branch cost nothing: it never told them apart either.
            else if (!appData.useLocalAppData && IsShippedPlaceholderCopy(filePath))
            {
                isPlaceholder = true;
                failedRemoteJson = true;

                // Only what was actually established: this file is the seed. Not "it declares no
                // media", which RawMediaValueCount would have to answer, and not "the screen will be
                // empty", which depends on what the project ships as its placeholder.
                Development.LogError($"the appData at [{filePath}] is ConfigManager's seeded copy of "
                    + "the shipped placeholder, not anything the CMS ever answered with - so nothing "
                    + "remote has ever been fetched on this machine, and the file's timestamp is when "
                    + "it was seeded. If the shipped copy is the template stub it parses as a success "
                    + "and declares no media, so expect empty content. Asset cleanup is disabled for "
                    + "this run because the keep-list cannot be trusted.");
            }

#if UNITY_ANDROID || UNITY_WEBGL
            // isLocalFallback: filePath is on local disk, so nothing here is a CMS response and the
            // cache write must not run. Routing through LoadRemoteData at all is a platform fact and not
            // a choice - when there is no cache, filePath is the shipped copy inside the package, and a
            // jar:file:// path can only be read by UnityWebRequest.
            StartCoroutine(LoadRemoteData(filePath, true, isPlaceholder));
            return;
#else
            if (File.Exists(filePath))
            {
                Development.LogIf(Core.settings.AppData.doLogs, $"Loading from local data json [{filePath}]");
                string dataAsJson = File.ReadAllText(filePath);
                APIResponseModel response;

                // Api.cs has the same defect and the same fix, but Api.isActive ships false and
                // nothing in this repo calls it - whereas this path runs on every boot that cannot
                // reach the CMS, with AppData.isActive true. Serving a real three-week-old appData
                // cache with nothing naming its age is the version that actually reaches a site.
                //
                // Note the LogError above fires only for the shipped *stub*; a genuine cache took the
                // other branch and said nothing at all.
                // Split for the same reason as the remote path above.
                try
                {
                    response = JsonUtility.FromJson<APIResponseModel>(dataAsJson);
                    response.raw = dataAsJson;

                    MarkServedFromLocalFile(response, filePath, isPlaceholder);
                }
                catch (Exception ex)
                {
                    ReportLoadFailure("Error parsing local appdata JSON", ex);
                    return;
                }

                try
                {
                    OnGetAppData(response);
                }
                catch (Exception ex)
                {
                    ReportLoadFailure("Local appdata parsed, but handling it failed", ex);
                    return;
                }
            }
            else
            {
                Development.LogError($"Local data json [{filePath}] not found!");
                ReportLoadFailure("Local appdata json not found", null);
            }
#endif
        }

        /// <summary>
        /// Marks a response as served from a local file rather than the CMS, and says so once at
        /// <c>LogError</c>.
        /// </summary>
        /// <param name="localPath">
        /// Where it was read from. A path carrying a scheme is inside the application package - the
        /// Android <c>jar:file://</c> case - and cannot be stat'd, so the age is reported as
        /// <b>unknown</b>. That distinction is the reason this takes the path rather than a timestamp:
        /// a <c>default</c> <see cref="DateTime"/> would floor to "0 second(s) ago" and assert
        /// freshness over content of unknown vintage.
        /// </param>
        /// <param name="isPlaceholder">
        /// Whether the file is the shipped stub rather than a real cached response. It switches the
        /// whole tail of the message, not a clause: "this is how old the content on display is" is
        /// exactly as wrong as the age itself when there is no content.
        /// </param>
        /// <remarks>
        /// <para><b>One copy, called from both routes.</b> This used to be inlined in
        /// <c>LoadLocalData</c>'s desktop branch only, and the Android/WebGL branch returns before it -
        /// so on those targets a genuinely weeks-old cache was served with <c>fromCache</c> false,
        /// <c>cacheAge</c> null and not one line in the log. Serving a cache is a success; serving it
        /// silently is the defect.</para>
        ///
        /// <para><b>Ungated <c>LogError</c></b>, for the same reason the Api path uses one: nobody can
        /// act on staleness they cannot see, and this content is on screen either way. `LogWarning`
        /// would vanish on iOS and WebGL with the rest of the verbose channel.</para>
        /// </remarks>
        private void MarkServedFromLocalFile(APIResponseModel response, string localPath,
            bool isPlaceholder)
        {
            DateTime writtenUtc = default;

            // A scheme means the file is inside the package, where there is nothing to stat.
            if (!string.IsNullOrEmpty(localPath) && !localPath.Contains("://"))
            {
                try { writtenUtc = File.GetLastWriteTimeUtc(localPath); }
                catch { writtenUtc = default; }

                // File.GetLastWriteTimeUtc does NOT throw for a missing file - it returns
                // 1601-01-01, which is not default(DateTime), so the `== default` test below would
                // sail past it and the line would read "155000 day(s) ago". No caller can reach that
                // today, but this is a shared helper now and reporting an absurd age as fact is the
                // same class of mistake as reporting a stub as "0 second(s) ago".
                if (writtenUtc.Year < 1900)
                    writtenUtc = default;
            }

            response.fromCache = true;
            response.cacheAge = writtenUtc == default
                ? (TimeSpan?)null
                : DateTime.UtcNow - writtenUtc;

            string age = response.cacheAge == null
                ? "of unknown age (its timestamp could not be read)"
                : $"last written {StringUtil.FormatElapsed(response.cacheAge.Value)} ago";

            // A deployment configured to read appData locally is not failing, and this is the one
            // line that exists to make a genuinely stale cache actionable - so firing it at Error on
            // every healthy boot of a local-mode kiosk would train the operator to scroll past exactly
            // the message it was added for. Local mode gets the same facts at Log level and without the
            // "the remote copy was not used" framing, which is not true there: the local file IS the
            // configured source.
            bool localByDesign = Core.settings?.AppData != null
                && Core.settings.AppData.useLocalAppData;

            if (localByDesign)
            {
                Development.Log($"appData was read from the local file [{localPath}], {age}. This "
                    + "deployment is configured to read appData locally, so that is the intended "
                    + "source rather than a fallback.");
                return;
            }

            string provenance = isPlaceholder
                ? "That timestamp is when the placeholder was put there, not when anything was "
                  + "fetched - there is no content here to be old. See the error above."
                : "The screen is showing content from that time. If the CMS is reachable this "
                  + "is a configuration problem; if it is not, this is how old the content on "
                  + "display is.";

            Development.LogError($"appData is being served from the local file [{localPath}], "
                + $"{age}, because the remote copy was not used. {provenance}");
        }

        /// <summary>
        /// Reports a load failure to the operator and the log, and raises
        /// <see cref="OnDataLoadFailedEvent"/> <b>only if the load has not already been announced as
        /// successful</b>.
        /// </summary>
        /// <remarks>
        /// <c>ParseAppData</c> raises
        /// <see cref="OnDataLoadedEvent"/> as soon as it has a model, and everything after it -
        /// asset manager, texture cache, scene load - runs from that event. A failure downstream used
        /// to raise <see cref="OnDataLoadFailedEvent"/> too, so one boot announced success and then
        /// failure, in that order, and any subscriber tracking state saw a transition no state machine
        /// expects. The signal is <see cref="successAnnounced"/>, set on the line before each raise -
        /// see its remarks for why <c>dataLoaded</c> could not answer this.
        /// <para>
        /// Passing <paramref name="ex"/> as <c>null</c> reports a failure that is not an exception -
        /// a rejected request - and keeps the notification text the operator is used to.
        /// </para>
        /// </remarks>
        /// <remarks>
        /// <b>Every raise of <see cref="OnDataLoadFailedEvent"/> goes through here</b>, including the
        /// appSettings paths, which carry a different payload but the same event. An invariant enforced
        /// at some of its raise sites is worse than none, because the next reader finds it written down
        /// and assumes it holds - the first version of this fix guarded two of six sites.
        /// </remarks>
        private void ReportLoadFailure(string title, Exception ex)
        {
            // ShowNotification logs on its own at the matching level (v0.8.7), so this is the operator
            // surface. The exception goes out separately because ex.Message alone loses the stack, and
            // LogException is always compiled.
            Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error, title,
                ex != null ? ex.Message : "system_request_error");

            if (ex != null)
                Development.LogException(ex);

            if (!successAnnounced)
            {
                OnDataLoadFailedEvent?.Invoke();
                return;
            }

            Development.LogError($"[{title}] happened AFTER OnDataLoadedEvent was already raised, so "
                + "OnDataLoadFailedEvent is NOT being raised - one boot cannot both succeed and fail. "
                + "The failure above is real and something downstream of the appdata parse threw it; "
                + "look there, not at the JSON.");
        }

        /// <summary>
        /// <see cref="ReportLoadFailure"/> plus the remote path's own bookkeeping and its fall-back
        /// to local data - which was written out three times, identically, before v0.31.0.
        /// </summary>
        private void FailRemoteAppData(string title, Exception ex)
        {
            ReportLoadFailure(title, ex);

            loadingData = false;

            if (Core.settings.AppData.useLocalAppData || failedRemoteJson)
                return;

            failedRemoteJson = true;

            // The `if (!Cache.AssetDownload.isActive) return;` that used to sit here is gone, and its
            // removal is deliberate. That gate never guarded the resolver: it required
            // `useLocalAppData && AssetDownload.isActive`, and this method returns early on
            // `useLocalAppData` a few lines above - so on this path the `else` branch always ran and
            // always produced a real path, download flag or not.
            //
            // What it did guard, weakly, is that with downloads off nothing ever writes the cache
            // (see the `AssetDownload.isActive` around the cache write in LoadRemoteData), so the
            // fallback lands on the shipped or seeded copy rather than on fetched content. That is a
            // thinner boot, not a reason to refuse: refusing means the preloader forever. Loading a
            // placeholder and saying so beats hanging - and the saying-so matters, because a seeded
            // copy otherwise reads as a cache.
            //
            // Shipped config has downloads on, so this was latent here and live for any branch that
            // turned them off.

            // "not usable" rather than "the fetch failed": five of the nine callers reach here with a
            // response the server delivered fine - the non-Success code, both "produced no content"
            // sites and both "could not be parsed" ones. The title passed in says which way; this
            // line should not guess.
            Development.LogError("The remote appData was not usable, so the app is falling back to the "
                + "local copy - whatever is cached, or the shipped placeholder if there is no cache. "
                + "The failure above says which way, and the line after this one reports what was "
                + "served and how old it is - on every target.");

            this.CallLater(LocalDataFallbackSeconds, LoadLocalData, ref _pendingLocalLoad);
        }

        protected virtual void OnGetAppData(APIResponseModel data)
        {
            if (Core.settings.AppData.doLogs)
            {
                string singleLineJson = data.raw.Replace("\n", "").Replace("\r", "").Replace("\t", "").Replace("  ", "");

                // Get first and last Core.settings.AppData.appDataMaxLength characters safely
                string firstPart = singleLineJson.Substring(0, Mathf.Min(Core.settings.AppData.appDataMaxLength, singleLineJson.Length));
                string lastPart = singleLineJson.Substring(Mathf.Max(0, singleLineJson.Length - Core.settings.AppData.appDataMaxLength), Mathf.Min(Core.settings.AppData.appDataMaxLength, singleLineJson.Length));

                // Single debug line
                Development.Log("OnGetAppData raw data: " + $"{firstPart} (...) {lastPart}");
            }

            // one core data model

            if (data.code == APIResponseCode.Success)
            {
                if (Core.settings.AppData.oneCoreDataModel.isActive)
                    ParseOneCoreAppData(data);
                else
                    ParseAppData(data);
            }
            else
            {
                // FailRemoteAppData, not ReportLoadFailure. Every other failure on this path falls
                // back to the local copy: the request error, the JSON parse error, the handler throw,
                // the call error, and the two "produced no content" checks. A server answering with an
                // error *code* must not be terminal - ReportLoadFailure only reports,
                // OnDataLoadFailedEvent has no subscribers, so the boot would sit on the preloader
                // until the watchdog called it.
                //
                // Still through a helper rather than raising directly: this branch sits inside the very
                // block whose one-raise invariant the helper enforces, and raising the event here
                // bypassed it. FailRemoteAppData calls ReportLoadFailure first, so that invariant
                // is unchanged - what is added is the recovery every sibling already had.
                //
                // On the *local* path into OnGetAppData this behaves exactly as before:
                // FailRemoteAppData's own `useLocalAppData || failedRemoteJson` guard returns straight
                // after reporting, so there is no second fallback and no loop.

                // The server's own reason. Fixing only the hang would leave an operator with a
                // working fallback and no way to tell a 401 from a 500 from a maintenance page.
                // Ungated, and before the call so it lands above the notification.
                Development.LogError($"The appData endpoint answered with code [{data.code}] and "
                    + $"message [{data.msg ?? "<none>"}], which is not Success, so the payload was not "
                    + "parsed.");

                FailRemoteAppData("Data Json failure retrieving data", null);
            }
        }

        protected virtual void ParseAppData(APIResponseModel data)
        {
            Development.CheckPoint(Core.settings.AppData.doLogs);

            CountRawMediaValues(data.raw);

            JObject jObj = JsonConvert.DeserializeObject<JObject>(data.raw);
            JToken jTokenData = null;

            // Captured before anything is parsed - see the check at the end of this method.
            AppDataBase modelBefore = AppData;
            dataLoaded = true;

            if (jObj.TryGetValue("data", out jTokenData))
            {
                Translations = new Translations();
                Development.LogIf(Core.settings.AppData.doLogs, "Trying to parse translations");
                if (!jTokenData["texts"].IsNullOrEmpty())
                    Translations.SetTranslations(jTokenData["texts"].ToObject<LocalizedField<LocalizedField<string>>>());
                else
                {
                    if (Core.settings.AppData.doLogs)
                        Development.LogWarning("Translations unavailable!");
                }

                if (Translations == null)
                    Translations = new Translations();

                Development.LogIf(Core.settings.AppData.doLogs, "Trying to parse Data");
                try
                {
                    AppData = (AppDataBase)jTokenData.ToObject(AppDataType);
                    ReportIfModelCannotSeeTheMedia();
                }
                catch (Exception e)
                {
                    Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error, "ERROR LOADING APPDATA", e.Message);
                    Development.LogException(e);
                    dataLoaded = false;
                }

                Development.LogIf(Core.settings.AppData.doLogs, "AppData parsing complete!");
            }
            else
            {
                dataLoaded = false;
                Development.LogError("AppData data null or empty!");
            }

            // Did this parse produce a model? `dataLoaded` is set true before any parsing in both
            // parse methods, and the branches that find nothing only log behind doLogs - so without
            // this, a payload that yields no content announces success and the boot runs on nothing.
            //
            // ReferenceEquals against the model we came in with, not a null test: AppData is an
            // instance property that is never reset, and a second parse in the same run IS reachable
            // (a subscriber throwing out of OnDataLoadedEvent lands in LoadRemoteData's catch, which
            // schedules LoadLocalData). A null test would pass on last boot's model and announce
            // success for a parse that produced nothing.
            //
            // FailRemoteAppData, not ReportLoadFailure: recovery, not just a report. Before this fix
            // the missing-content case reached MainController, threw an NRE on AppData.languages, and
            // that NRE was *caught by LoadRemoteData* and turned into the local-appData fallback - so
            // the app booted degraded. Clearing dataLoaded without replacing that recovery would have
            // traded a working-but-ugly boot for a clean hang, since OnDataLoadFailedEvent has no
            // subscribers anywhere. This makes deliberate what used to happen by accident.
            //
            // The failure branch below now calls FailRemoteAppData too, so a parse that produces
            // nothing falls back rather than hanging: a remote appData that cannot be used falls back
            // to the local copy, which is what four other sites in this file already do. What still ends at ReportLoadFailure is the
            // *local* path - and that is correct, because it has no further fallback to reach.
            if (dataLoaded && ReferenceEquals(AppData, modelBefore))
            {
                dataLoaded = false;

                Development.LogError($"The appData payload parsed without error but produced no content "
                    + "model, so there is nothing to boot with. Falling back to the local appData. "
                    + "The payload had a data member that deserialized to nothing - check it against the AppData model's members.");

                FailRemoteAppData("appData payload produced no content", null);
                return;
            }

            if (dataLoaded)
            {
                successAnnounced = true;
                OnDataLoadedEvent?.Invoke();
            }
            else
            {
                // Same argument as the non-Success branch, and the same fix. A payload that arrives
                // whole but has no `data` member, or one whose ToObject throws, is still a remote
                // appData that could not be used - so it falls back like every other remote failure
                // rather than reporting into an event nobody subscribes to. Live on the shipped
                // config.
                //
                // Reached from the local path too, where FailRemoteAppData's own guard returns after
                // reporting - there is no second fallback to schedule, because we are already on it.
                FailRemoteAppData("Appdata could not be parsed", null);
            }

            //Core.Instance.LoadNextScene();
        }

        /// <summary>
        /// Resolves <c>modules[idContent].content</c>, or returns <c>null</c> after saying exactly what
        /// was wrong with the payload.
        /// </summary>
        /// <remarks>
        /// <para><b>Every branch here was previously an exception</b> thrown out of one chained
        /// expression, and the operator saw only the call site's generic message. The shapes are worth
        /// naming individually because they have different causes: a non-array <c>modules</c> is a CMS
        /// contract change, an out-of-range <c>idContent</c> is a configuration mistake, and a module
        /// without a <c>content</c> block is a content-authoring mistake.</para>
        ///
        /// <para><b>Reports at <c>LogError</c> and returns null rather than throwing</b>, so the caller
        /// reaches its own "produced no content" handling - which recovers on the remote path. Returning
        /// null is not a silent failure here: the caller cannot proceed either way, and this is the only
        /// line that says which of four things went wrong.</para>
        /// </remarks>
        protected JToken ResolveOneCoreContent(JToken modules)
        {
            int idContent = Core.settings.AppData.oneCoreDataModel.idContent;

            if (modules == null || modules.Type != JTokenType.Array)
            {
                Development.LogError("appData's `modules` is "
                    + (modules == null ? "missing" : "a " + modules.Type)
                    + ", but AppData.oneCoreDataModel.idContent is a POSITION in it, so it has to be a "
                    + "JSON array - `\"modules\": [ { \"content\": { ... } } ]`. Nothing was parsed.");
                return null;
            }

            JArray entries = (JArray)modules;

            if (idContent < 0 || idContent >= entries.Count)
            {
                Development.LogError($"AppData.oneCoreDataModel.idContent is [{idContent}], but "
                    + $"`modules` has {entries.Count} entr" + (entries.Count == 1 ? "y" : "ies")
                    + " - and it is a POSITION in that array rather than an id, so "
                    + (entries.Count == 0
                        ? "there is no valid value: the array is empty"
                        : $"the valid range is 0 to {entries.Count - 1}")
                    + ". Nothing was parsed.");
                return null;
            }

            JToken entry = entries[idContent];

            if (entry == null || entry.Type != JTokenType.Object)
            {
                Development.LogError($"`modules[{idContent}]` is "
                    + (entry == null ? "missing" : "a " + entry.Type)
                    + ", not an object, so it has no `content` block to read. Nothing was parsed.");
                return null;
            }

            JToken content = entry["content"];

            if (content.IsNullOrEmpty())
            {
                Development.LogError($"`modules[{idContent}]` has no `content` block, so there is "
                    + "nothing to build the app data from. Check that idContent points at the module "
                    + "you meant - it is a position in the array, not an id.");
                return null;
            }

            // The entry being an object does not make `content` one. A `"content": [ ... ]` or
            // `"content": "x"` used to pass every check here and then throw inside ToObject, which is
            // the one exit with the least useful message - so the single shape that leaked through the
            // guard was the one that landed worst.
            if (content.Type != JTokenType.Object)
            {
                Development.LogError($"`modules[{idContent}].content` is a {content.Type}, not an "
                    + "object, so it cannot be read into the app data model. The CMS payload contract "
                    + "may have changed.");
                return null;
            }

            return content;
        }

        protected virtual void ParseOneCoreAppData(APIResponseModel data)
        {
            Development.CheckPoint(Core.settings.AppData.doLogs);

            CountRawMediaValues(data.raw);

            // Captured before anything is parsed - see the check at the end of this method.
            AppDataBase modelBefore = AppData;
            dataLoaded = true;
            JObject jObj = JsonConvert.DeserializeObject<JObject>(data.raw);

            if (jObj.TryGetValue("modules", out JToken jTokenModulesData))
            {
                // Resolved once, where it was written out five times - and defensively, because
                // every shape error in `modules` used to throw out of this expression: an object gives
                // ArgumentException, a short array ArgumentOutOfRangeException, a scalar
                // InvalidOperationException, and a module with no "content" adds an NRE. All of them
                // escaped into the call-site catch, which reports "handling it failed" and names
                // nothing - and on the local path does not recover.
                //
                // Now they come back null instead, which lands on the "produced no content" check at
                // the end of this method: it already explains that idContent is a POSITION rather than
                // an id, and already calls FailRemoteAppData so the remote path falls back. A throw
                // jumped straight over all of that.
                JToken contentToken = ResolveOneCoreContent(jTokenModulesData);

                if (!contentToken.IsNullOrEmpty())
                {
                    JToken jTokenContentData = null;

                    try
                    {
                        jTokenContentData = contentToken.ToObject<JToken>();
                    }
                    catch (Exception e)
                    {
                        Development.LogError(e.Message);
                    }

                    // App data parsing. Two further `["content"]` checks used to nest here, testing
                    // the same expression as the one above with nothing able to change it in
                    // between - so both were always true and both of their "Content unavailable!"
                    // branches were unreachable. One check, one parse.
                    Development.LogIf(Core.settings.AppData.doLogs, "Trying to parse content");

                    // Guarded, where the sibling path in ParseAppData already guards its equivalent.
                    // A content block whose shape does not match AppDataType throws here, and the
                    // operator saw the call site's generic message rather than which key was wrong.
                    try
                    {
                        AppData = (AppDataBase)contentToken.ToObject(AppDataType);
                    }
                    catch (Exception e)
                    {
                        // LogException FIRST. Catching deletes the stack Unity would have printed, and
                        // Newtonsoft's names the member path that failed to map - which is the only
                        // thing here that says WHICH key is wrong. The sibling in ParseAppData does
                        // the same.
                        Development.LogException(e);

                        Development.LogError("the OneCore `content` block could not be read into "
                            + $"{AppDataType.Name}: {e.Message}. Check that the CMS content matches "
                            + "the model this project declares.");

                        // Recover, do not merely return. An earlier version of this guard returned
                        // here, which skipped both the end-of-method check and the else-branch that
                        // calls FailRemoteAppData - so a model mismatch went from booting degraded on
                        // the local copy to sitting on the preloader until the stall watchdog. The
                        // throw this guard replaced DID reach that recovery. Reporting better while
                        // recovering worse is not an improvement.
                        // Deliberately NOT a bare fall-through, which is what the review proposed:
                        // ReportIfModelCannotSeeTheMedia() sits immediately after this try, and the
                        // whole Settings/Translations parse after that, so falling through would run
                        // all of it against a model that just failed to map and report about the
                        // wrong thing. Same destination, reached directly - and the same message the
                        // tail uses, so an operator sees one string for one outcome.
                        dataLoaded = false;
                        FailRemoteAppData("OneCore appdata could not be parsed", e);
                        return;
                    }

                    ReportIfModelCannotSeeTheMedia();

                    if (jTokenContentData != null)
                    {
                        // App data parsing
                        if (!jTokenContentData["Settings"].IsNullOrEmpty())
                        {
                            Dictionary<string, object> settings = null;
                            try
                            {
                                settings = jTokenContentData["Settings"].ToObject<KeyValuePair<string, object>[]>().ToDictionary(x => x.Key, x => x.Value);
                            }
                            catch (Exception e)
                            {
                                Development.LogError(e.Message);
                            }

                            AppSettings = (AppSettingsBase)Activator.CreateInstance(AppSettingsType);

                            if (settings != null)
                            {
                                try
                                {
                                    AppSettings = (AppSettingsBase)JsonConvert.DeserializeObject(JsonConvert.SerializeObject(DictionaryToObject(settings), Formatting.Indented), AppSettingsType);
                                }
                                catch (Exception ex)
                                {
                                    Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error, "ERROR LOADING APP SETTINGS", ex.Message);
                                }
                            }
                            else
                            {
                                if (Core.settings.AppData.doLogs)
                                    Development.LogWarning("Settings unavailable!");
                            }
                        }
                        else
                        {
                            if (Core.settings.AppData.doLogs)
                                Development.LogWarning("Settings unavailable!");
                        }

                        // App text translations parsing.
                        //
                        // This tested ["texts"] and then parsed ["Translations"]. A payload
                        // carrying "Translations" but not "texts" skipped the whole block and left
                        // translations silently empty; one carrying "texts" but not "Translations"
                        // entered it and threw inside the try below, which the catch turned into a
                        // one-line message and the same empty result.
                        //
                        // "Translations" is the OneCore key, and this is settled rather than
                        // guessed: Assets/StreamingAssets/config/appDataOneCore.json carries
                        // /modules[0]/content/Translations as an ARRAY of
                        // {translation_key, translation_text} - field for field what
                        // RawTranslation declares - and no "texts" key at all. So the ORIGINAL
                        // defect was in the check, not the parse: `["texts"]` was copy-pasted from
                        // the non-OneCore path a few hundred lines above, where "texts" is a
                        // different thing entirely.
                        //
                        // The two keys are not interchangeable, which is what makes resolving them
                        // by name alone wrong:
                        //
                        //   "Translations" an ARRAY of {translation_key, translation_text}, read by
                        //                  Helper.ParseTranslations into RawTranslation[]
                        //   "texts"        an OBJECT, read as LocalizedField<LocalizedField<string>>
                        //
                        // An earlier version of this fix preferred "texts" - the one spelling whose
                        // shape ParseTranslations cannot consume - which would have thrown into the
                        // catch below and emptied translations on any payload carrying both. So:
                        // prefer the key this project actually ships, keep the other as a fallback,
                        // and route the PARSE on the token's Type rather than on either name.
                        JToken translationsToken = jTokenContentData["Translations"];
                        string translationsKey = "Translations";

                        if (translationsToken.IsNullOrEmpty())
                        {
                            translationsToken = jTokenContentData["texts"];
                            translationsKey = "texts";

                            // Ungated by doLogs, deliberately. Falling back means the CMS is not
                            // sending the key this project's own sample uses, which is the one event
                            // here worth seeing on a deployed machine - and a doLogs-gated line is
                            // invisible there. Not an error: the fallback works.
                            if (!translationsToken.IsNullOrEmpty())
                                Development.LogWarning("No \"Translations\" key in the OneCore "
                                    + "content; falling back to \"texts\". The payload contract may "
                                    + "have changed.");
                        }

                        if (!translationsToken.IsNullOrEmpty())
                        {
                            Translations = new Translations();

                            try
                            {
                                if (translationsToken.Type == JTokenType.Array)
                                    Translations.SetTranslations(Helper.ParseTranslations(translationsToken.ToObject<object>()));
                                else
                                    Translations.SetTranslations(translationsToken.ToObject<LocalizedField<LocalizedField<string>>>());
                            }
                            catch (Exception e)
                            {
                                // Always compiled, and it names the key and shape: Newtonsoft's own
                                // message says "Cannot deserialize the current JSON object..." and
                                // mentions neither, so on its own it sends the reader nowhere.
                                Development.LogError($"Translations under the \"{translationsKey}\" key "
                                    + $"({translationsToken.Type}) could not be parsed, so the app has no "
                                    + $"translations and every string will render as its raw key: {e.Message}");
                            }

                            if (Translations.IsNullOrEmpty)
                                Development.LogError($"Translations parsed from the "
                                    + $"\"{translationsKey}\" key but came out EMPTY, so every string "
                                    + "will render as its raw key.");

                            if (Translations == null)
                                Translations = new Translations();
                        }
                        else
                        {
                            // LogError, not a doLogs-gated LogWarning: this is the state where every
                            // string in the app renders as its raw key, and the only other signal is
                            // Translations.cs's "Translations not available!" fired once per lookup,
                            // which names no cause and no key. Both keys are named so a CMS rename is
                            // diagnosable rather than absorbed - LogError is always compiled.
                            Development.LogError("No translations in this payload: neither a "
                                + "\"texts\" nor a \"Translations\" key was found under content, so "
                                + "every string will render as its raw key.");
                        }
                    }
                    else
                    {
                        if (Core.settings.AppData.doLogs)
                            Development.LogWarning("Content unavailable!");
                    }
                }
                else
                {
                    if (Core.settings.AppData.doLogs)
                        Development.LogWarning("Content unavailable!");
                }

                Development.LogIf(Core.settings.AppData.doLogs, "AppData parsing complete!");
            }
            else
            {
                dataLoaded = false;
                Development.LogError("AppData null or empty!");
            }

            // Did this parse produce a model? `dataLoaded` is set true before any parsing in both
            // parse methods, and the branches that find nothing only log behind doLogs - so without
            // this, a payload that yields no content announces success and the boot runs on nothing.
            //
            // ReferenceEquals against the model we came in with, not a null test: AppData is an
            // instance property that is never reset, and a second parse in the same run IS reachable
            // (a subscriber throwing out of OnDataLoadedEvent lands in LoadRemoteData's catch, which
            // schedules LoadLocalData). A null test would pass on last boot's model and announce
            // success for a parse that produced nothing.
            //
            // FailRemoteAppData, not ReportLoadFailure: recovery, not just a report. Before this fix
            // the missing-content case reached MainController, threw an NRE on AppData.languages, and
            // that NRE was *caught by LoadRemoteData* and turned into the local-appData fallback - so
            // the app booted degraded. Clearing dataLoaded without replacing that recovery would have
            // traded a working-but-ugly boot for a clean hang, since OnDataLoadFailedEvent has no
            // subscribers anywhere. This makes deliberate what used to happen by accident.
            //
            // The failure branch below now calls FailRemoteAppData too, so a parse that produces
            // nothing falls back rather than hanging: a remote appData that cannot be used falls back
            // to the local copy, which is what four other sites in this file already do. What still ends at ReportLoadFailure is the
            // *local* path - and that is correct, because it has no further fallback to reach.
            if (dataLoaded && ReferenceEquals(AppData, modelBefore))
            {
                dataLoaded = false;

                Development.LogError($"The OneCore payload produced no content model, so there is "
                    + "nothing to boot with. Falling back to the local appData. **If a line above names "
                    + "a reason, that is the cause** - the payload shape checks report specifically and "
                    + "this line is the consequence, not a second diagnosis. Where nothing above says "
                    + "why: AppData.oneCoreDataModel.idContent is "
                    + $"[{Core.settings.AppData.oneCoreDataModel.idContent}], and that is a POSITION in "
                    + "the modules array rather than an id.");

                FailRemoteAppData("OneCore payload produced no content", null);
                return;
            }

            // Gated: unconditional, this path announces SUCCESS even after setting dataLoaded false
            // three lines up - the same "one boot, both outcomes" contradiction the failure side
            // guards against, inverted.
            if (dataLoaded)
            {
                successAnnounced = true;
                OnDataLoadedEvent?.Invoke();
            }
            else
            {
                // As ParseAppData's equivalent above.
                FailRemoteAppData("OneCore appdata could not be parsed", null);
            }

            //Core.Instance.LoadNextScene();
        }

        protected static dynamic DictionaryToObject(IDictionary<String, object> dictionary)
        {
            ExpandoObject expandoObj = new ExpandoObject();
            ICollection<KeyValuePair<String, object>> expandoObjCollection = (ICollection<KeyValuePair<String, object>>)expandoObj;

            foreach (var keyValuePair in dictionary)
                expandoObjCollection.Add(keyValuePair);

            dynamic eoDynamic = expandoObj;
            return eoDynamic;
        }

        /// <summary>
        /// Returns requested translation for "key" in "language" if available.
        /// If parameter "language" not used, will return translation in current set language.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="language"></param>
        /// <returns></returns>
        internal string GetTranslation(string key, string language = null)
        {
            return Translations.GetTranslation(key, language);
        }

        /// <summary>
        /// Loads local AppSettings file if AppData not in One Core data model
        /// </summary>
        private void LoadSettings()
        {
            Development.CheckPoint(Core.settings.AppData.doLogs);

            string filePath;

            filePath = AppPaths.Config(AppSettingsFileName);

            // Android only, unlike the pair in LoadRemoteData: the branch below sends this to
            // UnityWebRequest on Android and WebGL but to File.Exists everywhere else, and File.Exists
            // on a "file://" path is always false - so prefixing on iOS here would break the platform
            // it was meant to help. WebGL's StreamingAssets path already carries a scheme.
            //
            // A scheme test rather than a hardcoded prefix, though. See the note in LoadRemoteData for
            // why `/storage/` never matches a real persistentDataPath, and why config seeding turns
            // that from harmless into a silently defaulted AppSettings.
#if UNITY_ANDROID
            filePath = AppPaths.ToFileUrl(filePath);
#endif
#if UNITY_ANDROID || UNITY_WEBGL
            StartCoroutine(LoadRemoteSettings(filePath));
            return;
#else
            if (File.Exists(filePath))
            {
                try
                {
                    ApplyAppSettings(File.ReadAllText(filePath), filePath);
                }
                catch (Exception ex)
                {
                    ReportLoadFailure("Error parsing appsettings JSON", ex);
                    return;
                }
            }
            else
            {
                Development.LogError($"Local AppSettings json not found at [{filePath}]");
                ReportLoadFailure("AppSettings json not found", null);
            }
#endif
        }

        /// <summary>
        /// Loads local AppSettings as web data (for mobile systems)
        /// </summary>
        private IEnumerator LoadRemoteSettings(string path)
        {
            Development.CheckPoint(Core.settings.AppData.doLogs);

            if (!Core.IsSet)
            {
                yield break;
            }

            if (string.IsNullOrEmpty(path))
            {
                if (Core.settings.AppData.doLogs)
                    Development.LogWarning("AppSettings path not available!");
                yield break;
            }

            // using: the iterator lowers it to try/finally, which is legal around a `yield return` -
            // only a `catch` is not. Without it this request's native handle and download buffer
            // leaked once per call, on the success path as much as the failure one.
            //
            // Not claimed: a coroutine Unity *stops* rather than lets finish. StopCoroutine,
            // SetActive(false) and a scene unload all abandon the iterator without disposing it, so a
            // request in flight at that moment still leaks. AppDataController lives under the
            // persistent Core, which is why that is a note here and not a second defect.
            using (UnityWebRequest www = UnityWebRequest.Get(path))
            {
                www.SetRequestHeader("Accept", "application/json");

                // A const rather than a settings key, because nothing the boot chain waits on is
                // worth an operator knob: a failure here leaves appSettings at defaults and says so,
                // and no caller blocks on it.
                //
                // Two corrections to what an earlier version of this comment claimed, both worth
                // keeping because the premise sounded obvious and was wrong:
                //
                //   - this method is *called* only from an Android/WebGL branch, but it carries no #if
                //     of its own, so it is compiled into every target including Windows.
                //   - it is NOT always a package read. AppPaths.Config falls back to StreamingConfig
                //     when there is no deployed copy, and on WebGL StreamingAssets is served over HTTP
                //     by the web server - so on that one target this bounds a real network fetch over
                //     whatever link the visitor has. 10s with no retry is tight for that, and it is
                //     the reason this is a floor to revisit rather than a settled number. Dormant while
                //     WebGL has never been built.
                //
                // Same shape and value as ConfigManager's package read and FileManager's; three
                // siblings each owning their own bound, worth consolidating one day but not worth
                // refactoring two working classes today.
                www.timeout = LocalReadTimeoutSeconds;

                yield return new WaitForEndOfFrame();
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    // Diagnostic FIRST, notification second, and the order is load-bearing:
                    // notificationManager is null on any branch that deletes or reparents
                    // NotificationsPanel, and an NRE raised while reporting would kill this coroutine
                    // before the line below ever ran - leaving exactly the blank record this fix
                    // exists to remove. Null-conditional for the same reason.
                    //
                    // Both are kept even though ShowNotification logs on its own: what it logs is the
                    // operator-facing string, which names no URL and no cause. This line used to be
                    // `LogError(www.downloadHandler?.text)`, and with no response body that text is
                    // the empty string.
                    //
                    // Not routed through ReportLoadFailure, and not raising OnDataLoadFailedEvent.
                    // This coroutine is started fire-and-forget and nothing waits on it, so
                    // announcing a *data load failure* would claim a fatal outcome that did not
                    // happen. The event has no subscribers today, which is exactly why the wrong
                    // signal would stay invisible until something subscribed.
                    //
                    // "DEFAULT appSettings", not "the ones it already had": Init assigns a fresh
                    // Activator.CreateInstance and this is the only loader on Android and WebGL, so
                    // there is no earlier value to fall back to. Every remotely-configured setting is
                    // absent rather than stale, which is a different thing to go looking for.
                    Development.LogError($"appSettings request to [{path}] failed: {www.result}, "
                        + $"HTTP {www.responseCode}, {www.error}. The app continues on DEFAULT "
                        + "appSettings - nothing was loaded, so every remotely-configured value is "
                        + "absent, not merely stale.");

                    // `!= null`, not `?.`: for a UnityEngine.Object the null-conditional tests the C#
                    // reference, which is still non-null once the object is destroyed - so it would
                    // invoke and throw MissingReferenceException. The operator overload reports a
                    // destroyed object as null, which is the question being asked.
                    if (Core.notificationManager != null)
                    {
                        Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error, "APPSETTINGS ERROR", "system_request_error");
                    }
                }
                else
                {
                    // No `isDone` check: control only reaches here after `yield return
                    // www.SendWebRequest()`, so the request has completed whatever its result. The
                    // check that used to be here was unreachable, and its else branch logged a
                    // "CALL ERROR" that could never fire - which is worth removing rather than
                    // keeping, because reading it suggests a failure mode that does not exist.
                    try
                    {
                        ApplyAppSettings(www.downloadHandler.text, path);
                    }
                    catch (Exception ex)
                    {
                        // LogException first, always compiled, and before anything that could throw
                        // while reporting. The cast to AppSettingsBase raises InvalidCastException as
                        // readily as a malformed payload raises a JSON error, and "unable to cast X to
                        // Y" with no frames names neither the type nor the site.
                        Development.LogException(ex);
                        Development.LogError($"Error parsing appsettings JSON from [{path}]: "
                            + $"{ex.Message}. The app continues on DEFAULT appSettings.");

                        if (Core.notificationManager != null)
                        {
                            Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error, "Error parsing appsettings JSON", ex.Message);
                        }
                        yield break;
                    }
                }
            }
        }

        /// <summary>
        /// Deserializes appSettings and applies it, refusing a null result rather than storing one.
        /// </summary>
        /// <remarks>
        /// <para><c>JsonUtility.FromJson</c> returns <c>null</c> for an <b>empty</b> file and does
        /// <b>not</b> throw - so the <c>try</c> around either read site never fired and the null was
        /// stored. <see cref="AssetManager"/> and <see cref="TextureCacheManager"/> then dereference
        /// <c>AppSettings.isMediaType</c>, so the failure surfaced three files away as "the asset walk
        /// threw" and named the wrong cause.</para>
        /// <para><b>Measured, because the obvious guess is wrong</b>: only the empty string is silent.
        /// <c>"   "</c> and a literal <c>"null"</c> both raise <c>ArgumentException</c>, and a
        /// truncated file is simply invalid JSON - all three already reached the caller's <c>catch</c>.
        /// This guard exists for the zero-byte file alone, which is what an interrupted write leaves
        /// behind.</para>
        /// <para>Keeping the previous instance is the recovery, not a consolation: <c>Init</c> has
        /// already built a usable one, so refusing the null leaves the app on defaults rather than on
        /// nothing. Control flow is deliberately unchanged - the caller carries on exactly as it did
        /// when the parse succeeded, because a settings file that would not load is not a reason to
        /// abandon the rest of the load.</para>
        /// </remarks>
        private void ApplyAppSettings(string json, string source)
        {
            AppSettingsBase parsed = (AppSettingsBase)JsonUtility.FromJson(json, AppSettingsType);

            if (parsed == null)
            {
                // LogError, not LogIf: this is the case that used to be completely silent, and the
                // level has to survive on every target for that to change.
                Development.LogError($"appSettings from [{source}] deserialized to null, so it has "
                    + "been ignored and the previous settings kept. The file is almost certainly "
                    + "zero-length - that is the one input JsonUtility turns into a null without "
                    + "throwing, which is why nothing reported it before.");
                return;
            }

            AppSettings = parsed;

            Development.LogIf(Core.settings.AppData.doLogs, "AppSettings parsing complete!");
        }

        /// <summary>
        /// Saves the current application settings to a JSON file in the configuration directory.
        /// </summary>
        /// <remarks>Always writes to the persistent (writable) config folder, creating it if
        /// needed. It deliberately does not fall back to StreamingAssets: that is read-only on
        /// most platforms, and inside the package on Android. Not thread-safe.</remarks>
        internal void SaveAppSettings()
        {
            Development.CheckPoint(Core.settings.AppData.doLogs);

            // No platform gate. It was `#if UNITY_WINDOWS || UNITY_STANDALONE`, which contradicted this
            // method's own remarks: persistentDataPath is writable on Android and iOS too, and being
            // writable when StreamingAssets is not is the entire reason this targets it. The `#else`
            // reported the refusal through Development.LogWarning, which is [Conditional("DO_LOGS")] and
            // therefore silent on iOS, macOS and Linux.
            //
            // With config seeding active this gate would be an inconsistency: Android gets a deployed
            // appSettings.json it can read and would never save. The try/catch below is what reports a
            // genuinely unwritable location.
            try
            {
                AppPaths.EnsurePersistentConfig();

                // WriteAtomic, not WriteAllText. This is a config file the app reads back, and a
                // truncated appSettings.json deserializes to null WITHOUT throwing. An earlier version
                // of this comment claimed that landed on a "failed, so use a fresh AppSettings()" path;
                // there is no such path, and asserting a safety net that does not exist is why the null
                // went unhandled for so long. ApplyAppSettings is the guard now.
                string savedTo = AppPaths.PersistentConfigFile(AppSettingsFileName);

                bool saved = ConfigManager.WriteAtomic(savedTo,
                    JsonUtility.ToJson(AppSettings),
                    "saving appSettings.json - if this fails the edit is lost, not the stored file");

                // A successful write is not a successful save when nothing will read it back. This
                // method always writes to the persistent folder, while the read goes through
                // AppPaths.Config, which returns the SHIPPED copy unless UsesDeployedConfig - so in
                // the editor without DEBUG_MODE, and on WebGL, a settings panel appears to save and
                // silently reverts on the next run.
                //
                // LogError rather than Log or LogWarning, and it is not a style choice: both of those
                // are stripped on WebGL, which is one of the exact two cases this fires in. Reporting
                // it at a level that vanishes on half the affected platforms would be the same defect
                // wearing a fix. AppPaths.Config already says something similar, but on the NEXT load,
                // only while the file still exists, and at Log level - too late and too quiet to
                // connect to the edit that caused it.
                if (saved && !AppPaths.UsesDeployedConfig)
                {
                    // Worded to match the sibling in FileManager.SaveCoreSettings, and the second
                    // sentence is the one that matters. "Written but not read back" describes the file
                    // as inert, and the action that invites - leave it - is what arms it: the editor
                    // and any player built on this machine share persistentDataPath, so this file
                    // becomes the config that player prefers.
                    //
                    // DEBUG_MODE is scoped to the editor on purpose. It is not a remedy on WebGL, the
                    // other target where UsesDeployedConfig is false: BuildValidator refuses every
                    // build carrying it, and forcing it there would make AppPaths.Config hand an idbfs
                    // path to UnityWebRequest, which 404s into DEFAULT settings - worse than the revert
                    // being reported.
                    Development.LogError($"appSettings.json was written to [{savedTo}], but this "
                        + "build reads the SHIPPED config, so the change will not take effect here. It "
                        + "WILL be preferred by any player built on this machine, which shares the same "
                        + "persistentDataPath - delete that file if you did not mean that. Define "
                        + "DEBUG_MODE to read the deployed copy in-editor; on WebGL the deployed config "
                        + "is not read at all.");
                }
            }
            catch (Exception e)
            {
                // UnityEngine.Debug deliberately, so a failed settings save cannot be silenced by
                // any future change to the logging wrapper.
                UnityEngine.Debug.LogError("SaveAppSettings failed: " + e);
            }
        }

        private const string AppSettingsFileName = "appSettings.json";
    }
}