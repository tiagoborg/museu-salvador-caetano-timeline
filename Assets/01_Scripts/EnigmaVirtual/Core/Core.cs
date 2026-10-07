/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using EnigmaVirtual.Notifications;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnigmaVirtual
{
    public class Core : MonoBehaviour
    {
        public event Action onCoreSetEvent;

        [SerializeField]
        private CoreSystemUI systemUI;

        public static FileManager file { get; private set; }
        public static CoreSettings settings { get; private set; }
        public static LocalizationSettings localizationSettings { get; private set; } = new LocalizationSettings();
        public static MainController main { get; private set; }
        public static NotificationManager notificationManager { get; private set; }
        public static event Action LanguageChangedEvent;

        [Header("Next Scene Options")]
        [Tooltip("TRUE : fade out AppInfo and wait for anim onComplete before jumping to next scene.\nFALSE : Doesn't show app info and jumps to next scene immediately.")]
        public bool UseAppInfo = true;
        [Tooltip("TRUE : jumps to StartScene after streamingAssts jsons are loaded.\nFALSE : Requires external call to jump to any scene.")]
        public bool AutoLoadNextScene = true;
        [Tooltip("Next scene to start after load.")]
        public string StartScene = "Intro";

        [SerializeField]
        private bool _isUsingPlayerPrefs = false;

        //private bool _waitToLoadFiles;
        //private int _jsonsLoaded = 0;
        //private int _filedToLoad = 0;
        private static Core _instance;
        private CoreSystemUI _coreUI;
        [Space]
        private static bool _isSet = false;

        /// <summary>
        /// How long boot may make **no progress at all** before it is declared failed.
        ///
        /// A stall budget, not a total one. A legitimate first run downloading several hundred
        /// assets takes far longer than any fixed total, and cutting that off would be worse than
        /// the hang it is meant to catch - so the deadline resets every time a stage is marked.
        /// <c>TextureCacheManager</c>'s cache watchdog works the same way.
        ///
        /// A const, not a setting: this exists to catch a failed settings parse, so it cannot
        /// depend on settings having parsed. It is the **floor** rather than the whole answer -
        /// <see cref="StallBudgetSeconds"/> raises it once a request timeout is known, and returns
        /// this verbatim until then.
        /// </summary>
        private const float BootStallTimeoutSeconds = 90f;

        private static string _bootStage = "Core.Awake";
        private static bool _bootComplete;
        private static bool _bootFailed;

        /// <summary>
        /// Whether the operator asked to skip the asset validation/download phase.
        ///
        /// Sticky and one-way: pressing the key before the phase starts has to work as well as
        /// pressing it during, because the operator cannot tell which part of boot is slow. Lives
        /// on Core rather than on AssetManager because AssetManager is created at runtime and may
        /// not exist yet when the key is pressed.
        ///
        /// Deliberately not platform-gated even though the only trigger today is a Windows-only
        /// shortcut: the mechanism is cross-platform, so another target can add its own trigger
        /// without touching the asset phase.
        /// </summary>
        internal static bool SkipAssetValidation { get; private set; }

        /// <summary>
        /// Records the operator's request. Idempotent, so holding the key does not spam the log.
        /// </summary>
        internal static void RequestSkipAssetValidation()
        {
            // Acknowledge EVERY press, including repeats, and do it before the idempotence check.
            // The operator gets no other feedback: the in-flight request has to finish first - up
            // to the configured timeout, 60s as shipped - so for a minute nothing visibly changes
            // and a second press would otherwise produce complete silence. ShowNotification logs on
            // its own, so this reaches the log file too.
            if (notificationManager != null)
            {
                notificationManager.ShowNotification(NotificationManager.NotificationType.Warning,
                    "SKIP REQUESTED", "Asset validation will stop after the current request.");
            }

            if (SkipAssetValidation)
                return;

            SkipAssetValidation = true;

            // Only claim the consequence when there is actually a phase left to skip. Pressed after
            // boot finished, or with downloads switched off entirely, the old unconditional message
            // asserted a degradation that never happened - and sent the next reader of the log
            // hunting a content bug that does not exist.
            bool downloadsPending = !_bootComplete
                && (settings?.AppData?.Cache?.AssetDownload?.isActive ?? false);

            if (downloadsPending)
            {
                // Error, not Log. An installation that quietly ran on stale or missing media would
                // be worse than the wait this avoids, and whoever reads the log afterwards has to
                // see that a human intervened.
                Development.LogError("Asset validation SKIPPED by operator key. The app will start "
                    + "on whatever media is already on disk; anything missing or outdated stays "
                    + "that way until the next launch without the key.");

                // Named so the boot trace does not read like a clean run. Without this the stage
                // history is indistinguishable from a normal boot.
                MarkBootStage("asset validation SKIPPED by operator");
            }
            else
            {
                Development.LogWarning("Skip asset validation requested, but there is nothing to "
                    + "skip - asset downloading is either already finished or switched off. No "
                    + "media was abandoned.");
            }
        }

        /// <summary>
        /// Records that boot reached a new stage, which resets the watchdog deadline and gives the
        /// failure message somewhere concrete to point. Every hop in the chain calls this - the
        /// chain is a sequence of one-shot events with no shared state, so there is nothing else
        /// an observer could watch.
        /// </summary>
        /// <param name="log">
        /// Whether to also write a line. Only the per-asset progress marks pass <c>false</c>, and
        /// only to keep a 200-asset pass from filling the log with one line per file.
        ///
        /// <b>The deadline reset is not optional and callers cannot opt out of it.</b> Gating a whole
        /// <c>MarkBootStage</c> call on a <c>doLogs</c> flag reads like a logging decision but starves
        /// the watchdog: the asset pass is the longest phase of startup, so a slow-but-healthy
        /// download set then trips the stall timeout with no defect behind it. The two concerns are
        /// therefore separate parameters rather than one call site's judgement.
        /// </param>
        internal static void MarkBootStage(string stage, bool log = true)
        {
            if (_bootComplete)
                return;

            _bootStage = stage;

            if (log)
                Development.Log("Boot stage: " + stage);
        }

        void Awake()
        {
            //PlayerPrefs.DeleteAll();

#if UNITY_EDITOR && DEBUG_MODE
            Debug.LogWarning("PLAY WARNING: DEBUG_MODE is enabled! Make sure this is intentional.");
#endif

            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);

                // Reset first: these are statics, and with "Enter Play Mode Options" disabling
                // domain reload they survive from the previous Editor session, which would leave
                // _bootComplete true and the watchdog exiting immediately on every run after the
                // first - the guard silently absent exactly where it is developed.
                _bootComplete = false;
                _bootStage = "Core.Awake";
                SkipAssetValidation = false;

                // LanguageChangedEvent is the one static here that accumulates references to destroyed
                // MonoBehaviours, so it belongs in this block more than the flags do: without domain
                // reload it arrives holding last session's subscribers, all pointing at objects that no
                // longer exist. It is also now an `event`, so this is the only place that CAN clear it -
                // outside code gets += and -= only.
                LanguageChangedEvent = null;

                // Started before anything else, and deliberately before the settings parse below:
                // a parse failure returns early out of Awake, leaving IsSet false and the app on
                // the preloader forever with nothing surfaced. That is the single most common way
                // this framework fails, and it happens above every other hop.
                StartCoroutine(BootWatchdog_IE());

                LeanTween.init();

                main = GetComponentInChildren<MainController>();
                _coreUI = GetComponentInChildren<CoreSystemUI>(true);
                notificationManager = GetComponentInChildren<NotificationManager>(true);

                // It lives on Core > SystemUI > NotificationsPanel in CoreSetup.unity, which is the
                // only place it has ever lived - so a derived project that deletes or reparents it gets
                // null here. That matters more than a missing toast: twelve catch blocks in this
                // file and AppDataController report failures *only* through this reference, so a
                // null would turn each of them into a NullReferenceException raised while reporting
                // the real error, and the original exception would be lost.
                // Fail loudly at boot instead of mysteriously inside a catch later.
                if (notificationManager == null)
                {
                    Development.LogError("no NotificationManager found under Core. Expected it "
                        + "on Core > SystemUI > NotificationsPanel. On-screen notifications are "
                        + "disabled; error paths that report through it will still be logged.");
                }

                file = new FileManager(this);

                if (PlayerPrefs.HasKey("Settings"))
                {
#if UNITY_ANDROID || UNITY_IOS
                    Development.Log("Settings loaded from PlayerPrefs, which is the expected source on "
                        + "this platform - the settings panel writes there rather than to a file.");
#else
                    // LogError, not LogWarning: LogWarning is [Conditional("DO_LOGS")] and would vanish
                    // on macOS, Linux and WebGL - iOS takes the branch above. The state itself
                    // is legitimate, but on a desktop
                    // install it means every edit to settings.json is being ignored - and the file read
                    // is gated out entirely below - which is an operator-facing error however it arose
                    // (an older build, or a branched project writing that key itself).
                    Development.LogError("Settings were loaded from PlayerPrefs, NOT from settings.json, "
                        + "so every edit to that file is being IGNORED. Clear it with Reset Player Prefs "
                        + "in the settings panel to go back to reading the file. (M53)");
#endif
                    if (!ApplySettingsJson(PlayerPrefs.GetString("Settings"), "PlayerPrefs[\"Settings\"]"))
                        return;

                    _isUsingPlayerPrefs = true;
                }

                // One boot order on every target, decided by what CoreSettings() returns rather than by
                // which platform is compiling. null means a fetch is in flight and OnJsonLoaded will
                // finish the boot when it arrives; anything else is the text that was read, and is
                // parsed here.
                //
                // This used to be two #if branches, and the cost was two boot orders on Android chosen
                // by whether a deployed config existed yet: launch 1 fetched StreamingAssets (a jar://
                // URL, genuinely asynchronous) and completed through the event, launch 2 read the seeded
                // file and completed synchronously inside this method. Same platform, same build, two
                // orders - which is the hardest kind of startup bug to reproduce.
                file.OnLoadJson.AddListener(OnJsonLoaded);

                if (_isUsingPlayerPrefs)
                {
                    CoreSetupComplete();
                    return;
                }

                string settingsJson = file.CoreSettings();

                // A fetch is running, and OnJsonLoaded will finish the boot. Returning here is what
                // keeps CoreSetupComplete from being called once now and again from the event.
                //
                // Deliberately `== null` and NOT IsNullOrEmpty. An empty string is a different
                // outcome: the read was attempted and produced nothing, so no event is ever coming,
                // and ApplySettingsJson below has to report it instead of waiting forever. Conflating
                // the two turns a missing or zero-byte settings.json into a blank preloader with
                // nothing in the log until the watchdog fires.
                if (settingsJson == null)
                    return;

                if (!ApplySettingsJson(settingsJson, "settings.json"))
                    return;

                CoreSetupComplete();
            }
            else
            {
                //prevent duplicates
                DestroyImmediate(gameObject);
            }
        }

        void Start()
        {
            if (!UseAppInfo && _coreUI)
                _coreUI.Hide(true);
        }

        private void OnDestroy()
        {
            // Identity check, because this runs on DUPLICATES too. Awake destroys a second Core with
            // DestroyImmediate, which invokes this method on the duplicate - and _isSet is static, so
            // the duplicate was switching the REAL Core off. Nothing sets it back: CoreSetupComplete
            // runs once, on the original. Every `if (!Core.IsSet) return;` in the app then bails
            // forever, silently, while Core itself keeps running.
            if (_instance == this)
                _isSet = false;
        }

        void CoreSetupComplete()
        {
            Development.CheckPoint();
#if !(UNITY_ANDROID || UNITY_IOS)
            Cursor.visible = settings.System.mouseVisibility;
#endif
            _isSet = true;

            // Here because Core owns `settings` and already owns boot-time failure reporting - the
            // stall watchdog is a few lines below. Not because MainController.Init would be too late:
            // Init runs unconditionally, and it is only module creation inside it that is gated.
            ReportClampedTimeouts();

            MarkBootStage("settings parsed, starting modules");
            onCoreSetEvent?.Invoke();
            //_waitToLoadFiles = false;
            main.Init();
            systemUI.Init();
            //LoadNextScene();
        }

        /// <summary>
        /// Seconds without a stage change that <see cref="BootWatchdog_IE"/> tolerates before it
        /// reports a stall.
        /// </summary>
        /// <remarks>
        /// The budget must clear the longest gap a *healthy* boot can leave between two stage marks,
        /// and that gap is one network request: asset progress is marked per asset, so a single HEAD
        /// or download can sit silent for as long as the request timeout in force. Too small a budget
        /// makes this watchdog fire on a working install, and it latches - a false fire refuses to
        /// boot at all.
        ///
        /// <para>Called on every deadline reset rather than once, because the watchdog starts before
        /// <c>settings.json</c> is parsed and there is nothing to read until it has. Until then the
        /// floor is the right answer anyway: no request can be in flight yet.</para>
        ///
        /// <para>It scales off the <b>effective</b> timeout, not the configured one. A mistyped 86400
        /// is capped before any request is made, so using the raw field would hand this watchdog a
        /// 48-hour deadline for a request that cannot exceed an hour. The reverse also holds and is
        /// the reason a <c>timeout</c> of 0 buys a large budget: 0 means "no limit" on that block and
        /// resolves to the ceiling, so a request really can take that long, and reporting a stall
        /// sooner would be reporting a healthy boot as a failure.</para>
        ///
        /// <para>One budget covers every stage, including the ones that make no request at all. That
        /// is coarse - a settings parse does not need an hour - but per-stage budgets would need the
        /// stage strings to become an enum, and a late report is a far better failure than a false
        /// one.</para>
        /// </remarks>
        private static float StallBudgetSeconds()
        {
            // Null-conditional throughout: settings may never have parsed, which is the case this
            // whole watchdog exists for.
            if (settings == null)
                return BootStallTimeoutSeconds;

            // Only a request that can actually be in flight during boot may widen the budget. A
            // project with downloading switched off never leaves a gap that size, and widening it
            // there would blunt the watchdog for nothing.
            if (settings.AppData == null || !settings.AppData.isActive)
                return BootStallTimeoutSeconds;

            // The appData fetch, and it is read before the Cache block rather than inside it: this
            // timeout lives on AppData itself, so a project with no Cache section at all still issues
            // the request. The doubling below covers the two requests one appData stage can make - the
            // remote attempt, then the local fallback after LocalDataFallbackSeconds - because
            // "loading appData" is one stage string and the watchdog only resets when that changes.
            // Deliberately NOT gated on useLocalAppData. With that on, a desktop boot reads the
            // file synchronously and issues no request at all, so this over-estimates the budget for
            // that one configuration - but on Android and WebGL the same config still routes through
            // UnityWebRequest, and a budget too WIDE only reports late while one too narrow latches
            // and refuses to boot. Only observable above a 42.5s timeout, where the floor stops
            // dominating.
            float widest = WebRequestExtensions.EffectiveApiTimeout(settings.AppData.timeout);

            CacheSettings cache = settings.AppData.Cache;

            if (cache == null)
                return StageBudgetFor(widest);

            // Mathf.Max, not assignment: `widest` already carries the appData timeout, and a plain
            // assignment here would discard it whenever downloading is on - which is the shipped
            // config, so the bug would be live rather than theoretical.
            if (cache.AssetDownload != null && cache.AssetDownload.isActive)
            {
                widest = Mathf.Max(widest,
                    WebRequestExtensions.EffectiveFileTransferTimeout(cache.AssetDownload.timeout));
            }

            // The pre-cache is gated separately from downloading, so a texture-cache-only project
            // would otherwise sit on the floor.
            //
            // As of v0.52.4 the pass marks per image - `textures {done}/{total}`, once per item
            // PROCESSED, so a failure or a skip resets the deadline exactly like a success does. That
            // is what makes twice one request timeout an honest budget here rather than a lower bound:
            // the gap this has to cover is now one item, not the whole pass. Before that it marked two
            // stages for every image in the project and this branch was knowingly too small.
            //
            // The load-bearing half is "processed", not "cached". If a future change moves the mark
            // back onto the success path, this budget silently becomes wrong again - failure is the
            // slow outcome, so marking only successes reports progress on the one path that never
            // stalls.
            if (cache.TextureCache != null && cache.TextureCache.isActive)
            {
                widest = Mathf.Max(widest,
                    WebRequestExtensions.EffectiveFileTransferTimeout(cache.TextureCache.timeout));
            }

            return StageBudgetFor(widest);
        }

        /// <summary>
        /// Turns the widest request timeout that can be in flight into a stall budget for the stage
        /// that issues it.
        /// </summary>
        /// <remarks>
        /// <para><b>Doubled, because one stage can issue two bounded requests.</b> "loading appData" is
        /// a single <c>_bootStage</c> string and the watchdog only resets its deadline when that string
        /// changes - and on Android and WebGL that stage makes the remote attempt and then re-enters
        /// the same method for the local fallback. <c>failedRemoteJson</c> caps it at two, so there is
        /// no third.</para>
        ///
        /// <para><b>Plus the fallback delay as a term, not as an assumption that it fits inside the
        /// doubling.</b> It does not always fit: with <c>AppData.timeout</c> at 60 and downloading off,
        /// twice 60 is 120 against a 125s honest worst case. The shipped config is not affected because
        /// the 60s asset timeout dominates, which is exactly why the gap would have gone unnoticed -
        /// and a budget short in the <b>small</b> direction latches and refuses to boot, which is worse
        /// than reporting late.</para>
        /// </remarks>
        private static float StageBudgetFor(float widestEffectiveSeconds)
        {
            return Mathf.Max(BootStallTimeoutSeconds,
                widestEffectiveSeconds * 2f + AppDataController.LocalDataFallbackSeconds);
        }

        /// <summary>
        /// <see cref="StallBudgetSeconds"/>, logged whenever it changes.
        /// </summary>
        /// <remarks>
        /// The budget is *derived*, so nothing else in the log states it, and it moves for a reason no
        /// other line reports: a timeout well inside its ceiling is never clamped, so
        /// <see cref="ReportClampedTimeouts"/> stays silent about it, yet
        /// <c>AssetDownload.timeout: 1800</c> quietly takes the stall budget to 3600s. An operator who
        /// widened a timeout for a slow CMS would otherwise have no way to discover that the loudest
        /// boot diagnostic had been widened with it.
        ///
        /// <para><b>Deliberately not capped.</b> Bounding the budget at some comfortable figure would
        /// make a legitimately long request trip the watchdog - and it latches, so that is a refusal
        /// to boot at all. This codebase has already established which way that trade goes: a late
        /// report beats a false one. So the budget follows the configuration honestly and says what it
        /// became.</para>
        ///
        /// <para>Plain <c>Development.Log</c>, not <c>LogIf</c>: this runs before the boot chain
        /// completes, and <c>LogIf</c>'s argument would dereference <c>Core.settings.System.doLogs</c>
        /// on a path where settings may never have parsed. Stripped on iOS and WebGL with the rest of
        /// the verbose channel, which is the one gap left here.</para>
        /// </remarks>
        private static float AnnouncedStallBudget(float current)
        {
            float next = StallBudgetSeconds();

            if (next == current)
                return current;

            Development.Log($"Boot stall budget is now {next}s (was {current}s) - derived from the "
                + "widest effective request timeout that can be in flight during boot, not configured "
                + "directly.");

            return next;
        }

        /// <summary>
        /// Reports every timeout in <c>settings.json</c> that this build will not use verbatim.
        /// </summary>
        /// <remarks>
        /// Once, at boot, rather than where the value is applied. At the request it would fire per
        /// asset - two hundred lines for two hundred assets - and every cheaper channel is
        /// unavailable regardless: <c>Development.LogWarning</c> and <c>LogIf</c> are
        /// <c>[Conditional("DO_LOGS")]</c> so they are stripped on iOS and WebGL, and both download
        /// blocks ship <c>doLogs: false</c>, which gates a <c>LogIf</c> off on every target that does
        /// compile it. <c>LogError</c> is always compiled, and a number the operator typed which the
        /// app then quietly replaces has earned one.
        ///
        /// <para>Nothing fires on the shipped config - <c>AppData.timeout</c> 30 and the two cache
        /// timeouts at 60 are all inside their ceilings - so a line here always means somebody edited a
        /// timeout and did not get what they asked for.</para>
        /// </remarks>
        private static void ReportClampedTimeouts()
        {
            if (settings == null)
                return;

            if (settings.Api != null && settings.Api.isActive)
            {
                ReportClampedTimeout("Api.timeout", settings.Api.timeout,
                    WebRequestExtensions.EffectiveApiTimeout(settings.Api.timeout),
                    WebRequestExtensions.ApiCeilingSeconds, "unset");
            }

            // Before the Cache block, because this key lives on AppData itself - a project with no
            // Cache section still issues the appData request. Gated on AppData.isActive, which is what
            // decides whether the request happens at all.
            if (settings.AppData != null && settings.AppData.isActive)
            {
                ReportClampedTimeout("AppData.timeout", settings.AppData.timeout,
                    WebRequestExtensions.EffectiveApiTimeout(settings.AppData.timeout),
                    WebRequestExtensions.ApiCeilingSeconds, "unset");
            }

            CacheSettings cache = settings.AppData?.Cache;

            if (cache == null)
                return;

            // Gated on AppData.isActive as well as its own flag: with AppData off, SetupAppData is
            // never called and no asset download is ever issued.
            if (settings.AppData != null && settings.AppData.isActive
                && cache.AssetDownload != null && cache.AssetDownload.isActive)
            {
                ReportClampedTimeout("AppData.Cache.AssetDownload.timeout", cache.AssetDownload.timeout,
                    WebRequestExtensions.EffectiveFileTransferTimeout(cache.AssetDownload.timeout),
                    WebRequestExtensions.FileTransferCeilingSeconds, "no limit");
            }

            // Gated on NEITHER flag, and that is the whole point. TextureCache.isActive gates the
            // boot pre-cache pass, not TextureCacheLoader - and MainController.SetupTextureCacheManager
            // is called outside every isActive check, including AppData's, so the loader can download,
            // cache and serve with both flags off. A clamp on this block is live in every
            // configuration, so gating the report on either flag would hide the half that runs.
            if (cache.TextureCache != null)
            {
                ReportClampedTimeout("AppData.Cache.TextureCache.timeout", cache.TextureCache.timeout,
                    WebRequestExtensions.EffectiveFileTransferTimeout(cache.TextureCache.timeout),
                    WebRequestExtensions.FileTransferCeilingSeconds, "no limit");
            }
        }

        private static void ReportClampedTimeout(string key, long configured, int effective,
            int ceiling, string zeroMeaning)
        {
            if (configured == effective)
                return;

            // Three cases, not two. `Api.timeout` is a signed int, so a typed -1 is reachable and
            // would otherwise be explained as though it were 0. The download blocks are `uint`, where
            // only the first two branches can occur.
            string why = configured < 0
                ? "a negative timeout is not a duration"
                : configured == 0
                    ? $"0 means \"{zeroMeaning}\" on this block, which resolves to {effective}"
                    : $"the ceiling for this kind of request is {ceiling}";

            Development.LogError($"settings.json: {key} is {configured}, but requests will use "
                + $"{effective}s - {why}. Set the number of seconds you actually need: the value is "
                + "not honoured verbatim, and no other log line will tell you so.");
        }

        /// <summary>
        /// Fails boot loudly instead of leaving it hanging. See <see cref="StallBudgetSeconds"/>
        /// for why this measures stall rather than total time.
        /// </summary>
        private System.Collections.IEnumerator BootWatchdog_IE()
        {
            // Budget read fresh on every deadline reset, not once up here. That is not tidiness:
            // this coroutine is started *before* the settings parse, deliberately, and everything
            // before a coroutine's first `yield` runs synchronously inside StartCoroutine - so a
            // single read at the top always saw a null Core.settings and always produced the floor.
            string lastStage = _bootStage;
            float stallBudget = StallBudgetSeconds();
            float deadline = Time.realtimeSinceStartup + stallBudget;

            while (!_bootComplete)
            {
                if (!string.Equals(lastStage, _bootStage))
                {
                    lastStage = _bootStage;
                    stallBudget = AnnouncedStallBudget(stallBudget);
                    deadline = Time.realtimeSinceStartup + stallBudget;
                }
                else if (Time.realtimeSinceStartup > deadline)
                {
                    // Latched, and then the loop KEEPS RUNNING. The first version reported and then
                    // yield-broke, which meant two things that were both wrong: it claimed "the app
                    // will not start" while having no power to prevent that, and it disarmed itself,
                    // so a second stall later in the same boot produced nothing at all.
                    _bootFailed = true;

                    Development.LogError($"BOOT STALLED: no progress for {stallBudget}s. "
                        + $"Last stage reached was [{_bootStage}]. Startup will not be allowed to "
                        + "continue past this point. This is almost always a settings.json or "
                        + "appData.json parse failure, or a network hop with no route - the cause is "
                        + "logged above this line.");

                    // ShowNotification logs on its own, so this reaches the log even where the
                    // UI is unavailable. Null-guarded because a boot this broken may not have one.
                    if (notificationManager != null)
                    {
                        notificationManager.ShowNotification(
                            NotificationManager.NotificationType.Error, "STARTUP FAILED", _bootStage);
                    }

                    // Re-arm so a *later* stall is reported too, rather than the first one being the
                    // only thing this ever says.
                    deadline = Time.realtimeSinceStartup + stallBudget;
                }

                yield return null;
            }
        }

        internal void LoadNextScene()
        {
            // NOT the terminus, which is why _bootComplete is not set here. With the shipped
            // UseAppInfo = true the actual SceneManager.LoadScene happens several callbacks later -
            // _coreUI.Show() -> AppInfo -> a LeanTween completion -> onHiddenEvent -> AppInfoHidden -
            // and every one of those links is unguarded. Marking boot finished on entry disarmed the
            // watchdog immediately before the one hop it most needed to watch: a stuck tween, or a
            // CoreSystemUI.Show() that silently does nothing because _appInfo is null, left the app
            // on the splash forever with a log that ended in "Boot complete."
            if (_bootFailed)
            {
                Development.LogError("Refusing to load the start scene: boot already failed "
                    + $"(stalled at [{_bootStage}]). Starting with incomplete data would replace one "
                    + "diagnosable hang with a storm of NullReferenceExceptions across every screen.");
                return;
            }

            MarkBootStage("loading the start scene");

            if (!SceneManager.GetActiveScene().name.Equals(StartScene) && AutoLoadNextScene)
            {
                if (_coreUI)
                {
                    if (UseAppInfo)
                    {

                        _coreUI.onHiddenEvent.AddListener(AppInfoHidden);
                        _coreUI.Show();
                    }
                    else
                    {
                        MarkBootComplete();
                        SceneManager.LoadScene(StartScene);
                    }
                }
                else
                {
                    MarkBootComplete();
                    SceneManager.LoadScene(StartScene);
                }
            }
        }

        public static void ChangeCurrentLang(Langs lang, bool refresh = false)
        {
            if (!refresh && localizationSettings.currentLanguage == lang)
            {
                // Logged, because this return was the one outcome with no evidence at all. The line
                // below reports a change and its listener count; this one reported nothing, so "the
                // language button does nothing" looked identical to "the event fired into an empty
                // list". Both are visible, and only this one is normal.
                Development.Log($"Language is already [{lang}]; nothing to notify. "
                    + "Pass refresh: true to broadcast anyway.");
                return;
            }

            localizationSettings.SetCurrentLang(lang);

            // "The language button does nothing" had three indistinguishable causes and no evidence for
            // any of them: the early return above, an empty subscriber list, or a subscriber throwing
            // part-way through the chain. The count separates the first two; a missing follow-up line
            // separates the third.
            //
            // Development.Log deliberately, not LogError: this is routine telemetry rather than a fault,
            // so it is [Conditional("DO_LOGS")] and absent on iOS and WebGL. On those targets the
            // screen is the instrument, not the log, so the on-screen [key] fallback carries this
            // rather than the log level being raised.
            Development.Log($"Language changed to [{lang}], notifying "
                + $"{LanguageChangedEvent?.GetInvocationList().Length ?? 0} listeners.");

            LanguageChangedEvent.SafeInvoke();
        }

        /// <summary>
        /// Called immediately before the start scene actually loads - from all three routes, since
        /// <see cref="LoadNextScene"/> only *begins* the transition. This is the honest terminus.
        /// </summary>
        private static void MarkBootComplete()
        {
            _bootComplete = true;
            Development.Log("Boot complete.");
        }

        void AppInfoHidden()
        {
            _coreUI.onHiddenEvent.RemoveListener(AppInfoHidden);
            MarkBootComplete();
            _coreUI.Hide(true);
            SceneManager.LoadScene(StartScene);
        }

        private void OnJsonLoaded(string filename, string data)
        {
            Development.Log("Load: " + filename);
            switch (filename)
            {
                case "settings.json":
                    if (!ApplySettingsJson(data, filename))
                        return;
                    break;

                default:
                    // Only settings.json completes the boot. FileManager.LoadJsonWeb is public and
                    // this subscription is no longer platform-gated, so any other file fetched
                    // through it would otherwise run the boot chain early here - and again when the
                    // settings themselves arrive.
                    Development.Log("Load: " + filename + " carries no core settings; ignored.");
                    return;
            }

            Development.Log("Load: " + filename + " COMPLETE");

            CoreSetupComplete();
        }

        /// <summary>
        /// Parses a settings payload into <see cref="settings"/>, applies the frame rate, and reports
        /// every way it can fail. Returns <c>false</c> when the caller must abandon the boot.
        /// </summary>
        /// <param name="source">Where the payload came from, for the operator-facing message.</param>
        /// <remarks>
        /// <para>One method for what used to be three copies - the PlayerPrefs read, the file read in
        /// <c>Awake</c> and this event callback - and they had drifted: two reported
        /// <c>ex.Message</c> while the third reported <c>ex.ToString()</c>, two dereferenced a
        /// <see cref="notificationManager"/> that <c>Awake</c> explicitly tolerates being null, and
        /// none of them handled an empty payload.</para>
        ///
        /// <para><b>An empty payload cannot be left to the catch.</b> Measured:
        /// <c>JsonUtility.FromJson&lt;CoreSettings&gt;("")</c> returns <b>null without throwing</b>,
        /// while a whitespace-only string throws <c>ArgumentException</c>. So an empty
        /// <c>settings.json</c> used to reach the <c>settings.System</c> dereference and surface as a
        /// bare <c>NullReferenceException</c> - and on the event path that dereference sat outside the
        /// <c>try</c>, so the exception escaped the coroutine and the boot hung. A zero-byte or
        /// BOM-only file is the case that matters: it passes <c>File.Exists</c>,
        /// <c>File.ReadAllText</c> does not throw, so nothing upstream logs anything either.</para>
        /// </remarks>
        private bool ApplySettingsJson(string json, string source)
        {
            if (string.IsNullOrEmpty(json))
            {
                ReportSettingsFailure(source, "it is empty, so there are no settings to parse");
                return false;
            }

            try
            {
                settings = JsonUtility.FromJson<CoreSettings>(json);
            }
            catch (Exception ex)
            {
                ReportSettingsFailure(source, ex.Message);
                return false;
            }

            if (settings == null)
            {
                ReportSettingsFailure(source, "it parsed to nothing");
                return false;
            }

            RereadOneCoreWithNewtonsoft(json, source);

            Application.targetFrameRate = settings.System.frameRate;
            return true;
        }

        /// <summary>
        /// Re-populates <c>settings.OneCore</c> with Newtonsoft, so its command payloads survive.
        /// </summary>
        /// <remarks>
        /// <para><b>Why one block gets a second parser.</b>
        /// <c>OneCoreCommand.command_data</c> and <c>event_data</c> are <c>object</c>, because real
        /// project data puts a JSON <i>object</i> in them - and <c>JsonUtility</c> skips <c>object</c>
        /// entirely, so anything an operator wrote there was dropped in both directions with nothing
        /// said. No single type is both <c>JsonUtility</c>-visible and able to hold a JSON object, so
        /// the type cannot be the fix; the parser has to be. <c>appData.json</c> already goes through
        /// Newtonsoft for the same reason, so this is the pipeline's existing answer applied to the one
        /// settings block that needs it.</para>
        ///
        /// <para><b><c>PopulateObject</c>, not <c>DeserializeObject</c>.</b> The instance handed in was
        /// built by <c>JsonUtility</c> with every C# initializer applied, and populating it in place
        /// keeps a key that is absent from the JSON on its initializer - which is what the rest of this
        /// config pipeline promises. Deserializing a fresh object would silently reset every OneCore
        /// field the operator did not mention.</para>
        ///
        /// <para><b>Not fatal.</b> By this point the <c>JsonUtility</c> parse has succeeded, so the rest
        /// of the configuration is usable and only the command payloads are missing. A malformed OneCore
        /// block is reported at <c>LogError</c> - always compiled - and boot continues, rather than
        /// holding the app on the preloader over a block whose module ships disabled.</para>
        ///
        /// <para>Deliberately not gated on <c>OneCore.isActive</c>: the settings object should describe
        /// what the file says whether or not the module is created, and the cost is one parse of one
        /// block at boot.</para>
        /// </remarks>
        private static void RereadOneCoreWithNewtonsoft(string json, string source)
        {
            if (settings?.OneCore == null)
                return;

            try
            {
                // ConfigManager.ParseJson rather than JObject.Parse: the latter defaults to
                // DateParseHandling.DateTime and would rewrite any date-shaped string value.
                JObject root = ConfigManager.ParseJson(json);
                JToken block = root?["OneCore"];

                // An absent block is not a failure - every field keeps what JsonUtility gave it.
                if (block == null)
                    return;

                // A present-but-wrong OneCore block is an operator typo worth naming. Returning
                // silently here would leave the whole block on its C# defaults with nothing said.
                if (block.Type != JTokenType.Object)
                {
                    Development.LogError($"the OneCore block in [{source}] is a {block.Type}, not an "
                        + "object, so none of it was applied and every OneCore setting is on its "
                        + "built-in default. Check the braces around it.");
                    return;
                }

                // NullValueHandling.Ignore: PopulateObject defaults to Include, so an explicit
                // "Authentication": null in the file would null a field whose C# initialiser exists
                // precisely to stop the settings panel NRE-ing on it - JsonUtility never does that.
                // ObjectCreationHandling.Replace: arrays are replaced either way today, but a branch
                // that changes OneCoreCommand[] to a List would otherwise get the JSON entries
                // APPENDED to what JsonUtility already put there rather than replacing them.
                JsonConvert.PopulateObject(block.ToString(), settings.OneCore, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                });
            }
            catch (Exception ex)
            {
                Development.LogError($"the OneCore block in [{source}] could not be re-read for its "
                    + $"command payloads: {ex.Message}. Everything else in the settings applied "
                    + "normally, but OneCoreCommand.command_data and event_data will be null, so any "
                    + "command or event carrying one is skipped with its own line.");
            }
        }

        /// <summary>Reports a settings failure to the operator, on screen and in the log.</summary>
        private void ReportSettingsFailure(string source, string reason)
        {
            // Paired on purpose: ShowNotification logs on its own, but this is the failure people grep
            // for and an explicit line here survives any later change to the notifier. Null-checked
            // because Core carries on when the NotificationManager is missing, having said so at boot -
            // unguarded, the NRE raised while reporting would replace the error being reported.
            if (notificationManager != null)
                notificationManager.ShowNotification(NotificationManager.NotificationType.Error,
                    "Error parsing core settings JSON", reason);

            Development.LogError($"Could not read core settings from [{source}]: {reason}. "
                + "Core.IsSet stays false, so the app will hold on the preloader.");
        }

        [ContextMenu("ClearPlayerPrefs")]
        private void ClearPlayerPrefs()
        {
            PlayerPrefs.DeleteKey("Settings");
        }

        public static Core Instance { get { return _instance; } }
        public static bool IsSet { get { return _isSet; } }
    }
}