/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class CoreSystemUI : MonoBehaviour
    {
        public UnityEvent onHiddenEvent = new UnityEvent();

        [SerializeField]
        private float hideTime = 1f;
        [SerializeField]
        private float hideDelay = 0.5f;
        [SerializeField]
        private DetectLongClick LongTouchZone;
        [SerializeField]
        private RectTransform Background;
        [SerializeField]
        private EditorSettingsLockScreen loginPanel;
        [SerializeField]
        private Text assetManagerOutputText;
        [SerializeField]
        private AssetManager assetManager;
        [SerializeField]
        private TextureCacheManager textureCacheManager;

        private AppInfo _appInfo;

        /// <summary>The pending hidden-notification, so a second Hide replaces it.</summary>
        private Coroutine _pendingHidden;

        /// <summary>
        /// Attempts seen per download url, so a repeat can be reported as a retry rather than as
        /// progress. Keyed rather than a single "previous url" field because `AssetManager` runs one
        /// `ValidateAndDownloadMedia_IE` per `downloadThreads` (`AssetManager.cs:109`), and with more
        /// than one chunk in flight the events interleave (`AssetManager.cs:121`) - a comparison against
        /// the previous url would
        /// then be reset by the other chunk and the retry would render as progress, which is the exact
        /// thing this exists to prevent. The shipped config is 1 thread; a branched project changing it
        /// must not silently break the readout.
        /// </summary>
        private readonly Dictionary<string, int> downloadAttempts = new Dictionary<string, int>();

        /// <summary>
        /// Extra attempts seen so far, so the download label can subtract them from a payload that
        /// counts them.
        /// </summary>
        /// <remarks>
        /// <c>onAssetDownloadEvent</c> carries <c>validatedAssets</c> RAW, and a retry re-enters the
        /// loop iteration, so the number includes retries. That has to stay raw - it is the only
        /// count that keeps moving during a retry, which is what resets the boot watchdog. The
        /// old label clamped with <c>Min(arg1, total)</c> against <c>mediaAssets.Count</c>, so
        /// saturation needed as many retries as there were excluded assets. The total is now the
        /// smaller verifiable count, so a single retry could pin the screen at "n/n FILES" while
        /// downloads carried on. Correct it here, in the handler that displays it - never in the
        /// event payload, which the watchdog reads.
        /// </remarks>
        private int downloadRetries;

        void Awake()
        {
            _appInfo = GetComponentInChildren<AppInfo>();
            if (_appInfo)
            {
                _appInfo.onHiddenEvent.AddListener(OnAppinfoHidden);
                LongTouchZone.onLongClickEvent.AddListener(OnLongTouchClicked);
                _appInfo.Init();
            }

            // FindAnyObjectByType, not FindFirstObjectByType: both of these are single-instance
            // managers, so there is no "first" to be deterministic about, and Any is allowed to stop at
            // the first hit rather than sorting the results. Replaces FindObjectOfType(Type), obsolete
            // since 2023.1. Still a scene scan, but only as a fallback when the serialized
            // reference is empty.
            if (!assetManager)
                assetManager = FindAnyObjectByType<AssetManager>();

            if (!textureCacheManager)
                textureCacheManager = FindAnyObjectByType<TextureCacheManager>();
        }

        public void Init()
        {

            if (Core.settings.EditorSettings.isActive)
                loginPanel.onValidLoginEvent += LoginPanel_onValidLoginEvent;

            // Re-resolved here, not just in Awake, because MainController CREATES this one in its own
            // Init - and Core calls main.Init() before systemUI.Init(), so this is the first moment it
            // can exist. `CoreSystemUI.Awake` runs before `MainController.Init`, so on a scene that
            // carries no TextureCacheManager the Awake lookup necessarily found nothing; without this
            // the check below would report "not available" about a manager that exists by now, and the
            // whole texture readout would be silently absent.
            if (textureCacheManager == null && Core.main != null)
                textureCacheManager = Core.main.TextureCacheManager;

            // The asset manager needs the SAME re-resolve, and then a fallback on top of it, because
            // it can be created either side of this method:
            //
            //   - With `useLocalAppData` on Standalone, `AppDataController.Init` reads the file and
            //     raises `OnDataLoadedEvent` SYNCHRONOUSLY, all inside `MainController.SetupAppData`
            //     and therefore inside `main.Init()` - which `Core` runs *before* `systemUI.Init()`.
            //     So the manager already exists here, and the event has already fired and will never
            //     fire again. Waiting for it would strand the readout with nothing logged.
            //   - On the remote path the load is a coroutine, so nothing exists yet and the event is
            //     still to come. That is what `WaitForAssetManager` is for.
            //
            // Resolve first, wait second. Getting this the wrong way round is worse than the defect
            // it replaced, because the old code at least reported.
            AdoptOwnedAssetManager();

            // Every one of the eight handlers below returns silently when this is null, so a scene
            // that drops the Text loses the entire preloader readout - and every future addition to
            // it - with nothing in the log. Reported once here rather than per handler.
            //
            // Gated on the phases that can actually reach a handler, so this cannot cry about a
            // config where nothing would have been drawn anyway. Both readouts sit downstream of the
            // appData load: the asset phase starts from `AppDataController_OnDataLoadedEvent`, and the
            // texture pre-cache starts from `AssetManager_onAssetCleaningCompleteEvent` - so it is
            // reachable only when the ASSET phase ran, which is why `TextureCache.isActive` needs no
            // term of its own here. If the texture phase is ever given its own entry point, this gate
            // has to gain one too.
            if (assetManagerOutputText == null
                && Core.settings.AppData.isActive
                && Core.settings.AppData.Cache.AssetDownload.isActive)
            {
                Development.LogError("CoreSystemUI has no assetManagerOutputText assigned, so the "
                    + "entire preloader readout is disabled - no asset or texture progress will "
                    + "appear on screen, and the boot will look like it is doing nothing. Bind the "
                    + "Text on Core > SystemUI.");
            }

            if (Core.settings.AppData.Cache.AssetDownload.isActive)
            {
                // Gated on the OWNER, not on the local field, and that distinction is the whole of
                // M76's remaining half. On the shipped config appData loads remotely, so
                // MainController has not resolved its AssetManager yet when this runs - which means
                // AdoptOwnedAssetManager above had nothing to compare against and whatever Awake found
                // is bound on trust. Gating on `assetManager != null` took that on trust permanently:
                // it subscribed here and never called WaitForAssetManager, so the appData-loaded
                // handler - the first moment the owner exists, and the only place a wrong binding can
                // be corrected - was unreachable on the path everything actually ships with.
                //
                // The two branches stay mutually exclusive, so nothing can subscribe twice or strand
                // handlers on an instance it has moved away from.
                if (Core.main != null && Core.main.AssetManager != null)
                    SetupAssetManagerListenners();
                else
                    WaitForAssetManager();
            }

            if (Core.settings.AppData.Cache.TextureCache.isActive)
            {
                if (textureCacheManager != null)
                    SetupTextureCacheManagerListenners();
                else
                    Development.LogError("no TextureCacheManager found, so the texture-cache readout "
                        + "will not appear on screen. MainController.Init creates one, and this runs "
                        + "after it, so reaching this means creation failed too - check the log above.");
            }
        }

        /// <summary>
        /// Waits for the appData load and wires the asset readout then, because the manager does not
        /// exist yet.
        /// </summary>
        /// <remarks>
        /// <para><b>Only reached when the manager does not exist yet</b> - <see cref="Init"/> resolves
        /// from <c>Core.main.AssetManager</c> first, because on the local-appData path the load
        /// completes synchronously inside <c>MainController.Init</c> and the event has already fired
        /// by the time this class is initialised. This method is the remote path, where the load is a
        /// coroutine and the manager is genuinely still to come.</para>
        ///
        /// <para><b>Subscription order is what makes it work, and it is not luck.</b>
        /// <c>MainController</c> subscribes to the same event during its own <c>Init</c>, which
        /// <c>Core</c> runs before <c>systemUI.Init()</c> - so its handler, the one that creates the
        /// manager, is always registered first and always runs first. A C# event invokes in
        /// subscription order.</para>
        ///
        /// <para>Unsubscribes on first fire, so a later appData refresh does not re-wire the
        /// listeners. <see cref="OnDisable"/> covers the case where this component dies first.</para>
        /// </remarks>
        private void WaitForAssetManager()
        {
            AppDataController controller = Core.main != null ? Core.main.AppDataController : null;

            if (controller == null)
            {
                // AppData.isActive, for the same reason the readout-Text check carries it: with the
                // appData module off, MainController never calls SetupAppData, so there is no
                // controller AND no asset phase - nothing to report a missing readout for.
                // Only when nothing is bound at all. This is now also reached with a manager already
                // resolved by Awake - waiting for the owner to appear so the binding can be confirmed -
                // and there a missing controller costs the re-check, not the readout.
                if (Core.settings.AppData.isActive && assetManager == null)
                {
                    Development.LogError("no AssetManager and no AppDataController either, so the "
                        + "download readout cannot be wired up now or later. Expected an AssetManager "
                        + "on Core > MainController, or serialized on Core > SystemUI.");
                }

                return;
            }

            _awaitingAssetManager = true;
            controller.OnDataLoadedEvent += AppData_onDataLoadedForAssetManager;
        }

        private void AppData_onDataLoadedForAssetManager()
        {
            StopWaitingForAssetManager();

            AdoptOwnedAssetManager();

            if (assetManager != null)
            {
                SetupAssetManagerListenners();
                return;
            }

            // Silent when the operator skipped validation: MainController's handler goes straight to
            // LoadNextScene in that case and never creates a manager, so there is no readout to miss
            // and nothing is wrong. Any other route here means the phase is running with no readout.
            if (!Core.SkipAssetValidation)
            {
                Development.LogError("the appData loaded but no AssetManager exists, so download and "
                    + "verification progress will not appear on screen. Expected MainController to "
                    + "have created one - check the log above for why it did not.");
            }
        }

        /// <summary>
        /// True while subscribed to <c>OnDataLoadedEvent</c>, so <see cref="OnDisable"/> only
        /// unsubscribes when there is something to unsubscribe.
        /// </summary>
        private bool _awaitingAssetManager;

        private void StopWaitingForAssetManager()
        {
            if (!_awaitingAssetManager)
                return;

            _awaitingAssetManager = false;

            // Null-checked because teardown order between this and Core is undefined.
            if (Core.main != null && Core.main.AppDataController != null)
                Core.main.AppDataController.OnDataLoadedEvent -= AppData_onDataLoadedForAssetManager;
        }

        /// <summary>
        /// <c>OnDisable</c>, not <c>OnDestroy</c>: it covers deactivation as well, and a delegate
        /// left on the controller's event while this component is inactive still fires into it.
        /// </summary>
        /// <remarks>
        /// <c>protected virtual</c>, matching <c>ScreenControllerBase</c> and
        /// <c>VideoPlayerControllerBase</c>, because Unity dispatches a message to the most-derived
        /// declaration only: a subclass declaring its own <c>OnDisable</c> would otherwise stop this
        /// one running and leak the subscription, with nothing to say so. Override and call
        /// <c>base.OnDisable()</c>.
        /// </remarks>
        protected virtual void OnDisable()
        {
            StopWaitingForAssetManager();
        }

        /// <summary>
        /// Binds to the <see cref="AssetManager"/> that <c>MainController</c> actually drives, in
        /// preference to whatever <c>Awake</c> resolved.
        /// </summary>
        /// <remarks>
        /// <para><b>Prefer, not fall back.</b> This used to read
        /// <c>if (assetManager == null) assetManager = Core.main.AssetManager;</c> - so it only helped
        /// when nothing had been found, and a non-null wrong answer won permanently. <c>Awake</c>
        /// resolves from a serialized reference or a scene-wide <c>FindAnyObjectByType</c>, while
        /// <c>MainController</c> resolves from its own subtree and creates one if that misses, so the
        /// two can land on different objects. When they do, this component drives a manager nobody
        /// runs: every preloader label stops updating, no error, boot completes.</para>
        ///
        /// <para>Called before any listener is attached on both paths, so rebinding here cannot strand
        /// handlers on the instance being replaced.</para>
        ///
        /// <para>Null-safe by design: <c>Core.main.AssetManager</c> does not exist yet on the remote
        /// appData path - that is what <c>WaitForAssetManager</c> is for - so a null owner leaves
        /// whatever was already resolved in place rather than clearing it.</para>
        /// </remarks>
        private void AdoptOwnedAssetManager()
        {
            AssetManager owned = Core.main != null ? Core.main.AssetManager : null;

            if (owned == null || assetManager == owned)
                return;

            // Only when it is REPLACING something, and at Error: a preloader that silently stops
            // updating is exactly the failure this codebase keeps producing, and the operator sees no
            // other symptom. Reaching this means the scene has two AssetManagers.
            if (assetManager != null)
            {
                Development.LogError("CoreSystemUI resolved a different AssetManager than the one "
                    + "MainController drives, so the scene has two. Rebinding to MainController's - "
                    + "without this the preloader readout would stay bound to the idle one and simply "
                    + "stop updating. Check whether an AssetManager was moved out of MainController's "
                    + "transform, or authored twice.");
            }

            assetManager = owned;
        }

        private void SetupAssetManagerListenners()
        {
            assetManager.onTotalAssetsEvent += AssetManager_onTotalAssetsEvent;
            assetManager.onAssetVerificationEvent += AssetManager_onAssetVerificationEvent;
            assetManager.onAssetDownloadEvent += AssetManager_onAssetDownloadEvent;
            // assetManager.onAssetVerificationCompleteEvent += AssetManager_onAssetVerificationCompleteEvent;
            assetManager.onAssetCleanedEvent += AssetManager_onAssetCleaningEvent;
            assetManager.onAssetCleaningCompleteEvent += AssetManager_onAssetCleaningCompleteEvent;
        }

        private void SetupTextureCacheManagerListenners()
        {
            textureCacheManager.onTotalAssetsEvent += TextureCache_onTotalAssetsEvent;
            textureCacheManager.onTextureCachedEvent += TextureCache_onTextureCachedEvent;
            textureCacheManager.onAssetVerificationCompleteEvent += TextureCache_onAssetVerificationCompleteEvent;
        }

        /// <summary>
        /// The texture-cache twin of <see cref="AssetManager_onTotalAssetsEvent"/>.
        /// </summary>
        /// <remarks>
        /// <c>onTotalAssetsEvent</c> was declared and invoked on <c>TextureCacheManager</c> with
        /// nothing subscribed to it. Dormant
        /// until now only because this is the bulk pre-cache stage, which <c>TextureCache.isActive</c>
        /// really does gate - unlike <c>GetOrSetTexture2D</c>, which has no gate at all.
        /// <para>
        /// The zero case is never rendered, for the same reason the asset one is not:
        /// <c>OnAssetsSorted</c> raises this and then <c>onAssetVerificationCompleteEvent</c> in one
        /// synchronous call stack, and that hides the label. The durable report is the log pair in
        /// <c>TextureCacheManager</c>.
        /// </para>
        /// </remarks>
        private void TextureCache_onTotalAssetsEvent(int total)
        {
            if (assetManagerOutputText == null)
                return;

            assetManagerOutputText.text = total > 0
                ? string.Format("PREPARING {0} TEXTURES", total)
                : "NO TEXTURES TO CACHE";

            assetManagerOutputText.gameObject.SetActive(true);
        }

        private void TextureCache_onTextureCachedEvent(int arg1, string arg2, int arg3)
        {
            if (assetManagerOutputText != null)
            {
                assetManagerOutputText.text = string.Format("BUILDING TEXTURE CACHE {0}%", (arg1 * 100 / arg3));
                assetManagerOutputText.gameObject.SetActive(true);
            }
        }

        private void TextureCache_onAssetVerificationCompleteEvent()
        {
            HideOrReportAssetProblems();

            textureCacheManager.onTotalAssetsEvent -= TextureCache_onTotalAssetsEvent;
            textureCacheManager.onTextureCachedEvent -= TextureCache_onTextureCachedEvent;
            textureCacheManager.onAssetVerificationCompleteEvent -= TextureCache_onAssetVerificationCompleteEvent;
        }

        /// <summary>
        /// Clears the asset readout, or leaves it up saying what was lost.
        /// </summary>
        /// <remarks>
        /// The readout used to be hidden on completion <b>regardless of outcome</b>, so a run that gave
        /// up on assets after its retries cleared the screen exactly like a clean one. The give-up path
        /// does log at <c>LogError</c>, but a log is not where someone standing at a kiosk looks - and
        /// on an unattended install nobody looks at all until the content is visibly wrong.
        ///
        /// <para>Reads <c>AssetManager</c>'s run totals rather than taking them from the event, because
        /// adding a parameter to <c>onAssetCleaningCompleteEvent</c> would break every branched
        /// project's handler.</para>
        ///
        /// <para>It stays on screen deliberately. The texture pre-cache overwrites this label when it
        /// runs, so the message survives only until then - and with <c>TextureCache.isActive</c> false,
        /// as shipped, it survives to the end of boot. That asymmetry is the honest behaviour rather
        /// than a bug: the pre-cache having something to say is more current than this.</para>
        /// </remarks>
        private void HideOrReportAssetProblems()
        {
            if (assetManagerOutputText == null)
                return;

            int missing = assetManager != null ? assetManager.MissingAssetCount : 0;
            int refused = assetManager != null ? assetManager.RefusedAssetCount : 0;

            if (missing <= 0 && refused <= 0)
            {
                assetManagerOutputText.gameObject.SetActive(false);
                return;
            }

            // Refused is named separately when it happens, because it means the content asked to write
            // outside the asset root - a different problem from a server that did not have the file,
            // and the one worth reading the log over.
            // Each clause only appears when its count is non-zero. "0 ASSET(S) MISSING, 4 REFUSED"
            // reads as though the 0 were the finding, and a readout an operator sees for the rest of
            // the session should not lead with a number that means nothing happened.
            if (missing > 0 && refused > 0)
            {
                assetManagerOutputText.text =
                    string.Format("{0} ASSET(S) MISSING, {1} REFUSED - SEE LOG", missing, refused);
            }
            else if (refused > 0)
            {
                assetManagerOutputText.text = string.Format("{0} ASSET(S) REFUSED - SEE LOG", refused);
            }
            else
            {
                assetManagerOutputText.text = string.Format("{0} ASSET(S) MISSING - SEE LOG", missing);
            }

            assetManagerOutputText.gameObject.SetActive(true);
        }

        private void AssetManager_onAssetCleaningEvent(int arg1, int arg2)
        {
            if (assetManagerOutputText != null)
            {
                assetManagerOutputText.text = string.Format("CLEANING ASSETS {0}%", (arg1 * 100 / arg2));
                assetManagerOutputText.gameObject.SetActive(true);
            }
        }

        private void AssetManager_onAssetCleaningCompleteEvent()
        {
            HideOrReportAssetProblems();

            downloadAttempts.Clear();
            downloadRetries = 0;

            assetManager.onTotalAssetsEvent -= AssetManager_onTotalAssetsEvent;
            assetManager.onAssetVerificationEvent -= AssetManager_onAssetVerificationEvent;
            assetManager.onAssetDownloadEvent -= AssetManager_onAssetDownloadEvent;
            //assetManager.onAssetVerificationCompleteEvent -= AssetManager_onAssetVerificationCompleteEvent;
            assetManager.onAssetCleanedEvent -= AssetManager_onAssetCleaningEvent;
            assetManager.onAssetCleaningCompleteEvent -= AssetManager_onAssetCleaningCompleteEvent;
        }

        /// <summary>
        /// Puts the size of the job on screen before the first file is touched.
        /// </summary>
        /// <remarks>
        /// <c>onTotalAssetsEvent</c> had no subscriber anywhere in the project. It carries the one
        /// number the preloader could never show: the
        /// total, known at <c>AssetManager.OnAssetsSorted</c> before any request is made. Without it the
        /// first thing on screen is <c>VERIFYING 1/225</c>, so the scale of the wait only becomes clear
        /// once it is already under way.
        /// <para>
        /// Nothing else subscribes this event, so unlike the two progress events it feeds no watchdog
        /// and carries no watchdog constraint - it is free to say whatever reads best.
        /// </para>
        /// </remarks>
        private void AssetManager_onTotalAssetsEvent(int total)
        {
            if (assetManagerOutputText == null)
                return;

            // The zero case is renderable now. It used to be unreachable because zero only arrived
            // via OnAssetsSorted's IsNullOrEmpty branch, which reaches onAssetCleaningCompleteEvent -
            // the thing that hides this label - in the same synchronous call stack with no yield
            // between. The total is now the count of *verifiable*
            // assets, so a content set full of external urls sends zero down the normal path,
            // which yields, and this string really does go on screen.
            //
            // Which is why it no longer says "NO MEDIA IN THIS CONTENT SET". Zero here means "nothing
            // to prepare", and that is true both for an empty content set and for one where every
            // entry was excluded as empty or external - opposite problems, and claiming the first
            // when it is the second sends an installer to look at the CMS instead of at the url
            // scheme. Which one it is, is named in AssetManager's log.
            assetManagerOutputText.text = total > 0
                ? string.Format("PREPARING {0} FILES", total)
                : "NO FILES TO PREPARE";

            assetManagerOutputText.gameObject.SetActive(true);
        }

        private void AssetManager_onAssetVerificationEvent(int arg1, string arg2, int arg3)
        {
            if (assetManagerOutputText != null)
            {
                assetManagerOutputText.text = string.Format("VERIFYING {0}/{2} FILES\n{1}", arg1, arg2, arg3);
                assetManagerOutputText.gameObject.SetActive(true);
            }
            //assetManagerOutputText.text = string.Format("VERIFYING {0}%\n{1}", (arg1 * 100 / arg3), arg2);
        }

        /// <summary>
        /// Shows which file is actually being fetched, so a long download is distinguishable from a hang.
        /// </summary>
        /// <remarks>
        /// Both events are raised from the same per-asset loop in <c>AssetManager</c>, so verification
        /// already refreshed this label once per file. What it could not show is the wait *inside* one
        /// file: <c>AssetDownload.timeout</c> is 60s in the shipped config, and until this handler existed
        /// the screen read "VERIFYING n/total" for that whole minute - naming the wrong phase, and
        /// unchanging, which is what a hang looks like too.
        /// <para>
        /// <c>MainController</c> also subscribes this event, but to feed <c>Core.MarkBootStage</c> - the
        /// boot watchdog's deadline reset. That is the log and the stall timer, not the screen.
        /// </para>
        /// <para>
        /// <b>The two events deliberately carry different counts, and that divergence is load-bearing -
        /// do not unify the payloads.</b> Verification passes
        /// <c>++validatedAssets - totalRetryCount</c>; this passes <c>validatedAssets</c> raw. The boot
        /// watchdog resets its deadline only when the <c>_bootStage</c> <i>string</i> changes, and that
        /// string is <c>assets {done}/{total}</c> - so during a retry the verification number is
        /// unchanged and only this raw one moves. Make them agree and a 4-attempt asset holds one stage
        /// string for 4 x 60s against a 125s budget, tripping a false BOOT STALLED, which latches
        /// <c>_bootFailed</c> and makes <c>LoadNextScene</c> refuse permanently. So the label is
        /// corrected <i>here</i>, in the handler, and the payload is left alone.
        /// </para>
        /// </remarks>
        private void AssetManager_onAssetDownloadEvent(int arg1, string arg2, int arg3)
        {
            if (assetManagerOutputText == null)
                return;

            // A retry re-enters the same loop iteration (AssetManager does i--), so this fires again
            // for the same file with a HIGHER number. Unlabelled, three 60-second attempts read as
            // "5/225 -> 6/225 -> 7/225" against one unchanging url - progress, when nothing is
            // progressing - and a single asset failing three times could render "4/1 FILES".
            downloadAttempts.TryGetValue(arg2, out int attempt);
            attempt++;
            downloadAttempts[arg2] = attempt;

            if (attempt > 1)
                downloadRetries++;

            // File name, not the url: arg2 is absolute, so the full client hostname would otherwise be
            // on a boot screen at a public site. Query string dropped as well - a signed CDN url puts
            // its whole token after the '?', which is both unreadable and not for showing. The fallback
            // does print the raw arg2, hostname and all - but only when the url ends in a separator and
            // so has no file name, which an asset url never does; a blank second line would read as a
            // failure, and that is the worse trade on a boot screen.
            // Clamp, not Min: the payload counts retries and this subtracts them, so the result can
            // legitimately be lower than it was a moment ago - but never negative, and never above
            // the total.
            int shown = Mathf.Clamp(arg1 - downloadRetries, 0, arg3);
            string file = StringUtil.GetFileNameFromUrl(arg2.Split('?')[0]);
            if (string.IsNullOrEmpty(file))
                file = arg2;

            assetManagerOutputText.text = attempt > 1
                ? string.Format("RETRYING ({0}) {1}/{2} FILES\n{3}", attempt, shown, arg3, file)
                : string.Format("DOWNLOADING {0}/{1} FILES\n{2}", shown, arg3, file);

            assetManagerOutputText.gameObject.SetActive(true);
        }

        private void LoginPanel_onValidLoginEvent()
        {
            loginPanel.Hide();
            _appInfo.gameObject.SetActive(true);
            _appInfo.Show(true);
        }

        void OnAppinfoHidden()
        {
            onHiddenEvent.Invoke();
        }

        private void OnLongTouchClicked()
        {
            //_appInfo.gameObject.SetActive(true);
            //_appInfo.Show(true);
            loginPanel.Show();
        }

        public void Show()
        {
            if (_appInfo)
                _appInfo.Show(false);
        }

        public void Hide(bool force = false)
        {
            // Clear the asset readout here, whichever way the splash goes away. Nothing else ever
            // deactivated it: the M73 message would otherwise stay painted over the client's content
            // for the whole session - and stale from the first frame, since it describes one boot-time
            // run and nothing updates it. `Background` is a sibling of the label, so fading that does
            // not touch this. The LogError is the durable record; the screen is for the operator who is
            // standing there while it boots.
            if (assetManagerOutputText != null)
                assetManagerOutputText.gameObject.SetActive(false);

            if (force)
            {
                _appInfo.gameObject.SetActive(false);
                OnAppinfoHidden();
                LeanTween.alpha(Background, 0f, .001f);
            }
            else
            {
                if (_appInfo)
                    _appInfo.Hide(hideTime, hideDelay);
                else
                    this.CallLater(hideTime + hideDelay, OnAppinfoHidden, ref _pendingHidden);

                LeanTween.alpha(Background, 0f, hideTime).setDelay(hideDelay).setOnComplete(() => { Destroy(Background.gameObject); });
            }
        }
    }
}
