//#define DO_LOGS // use this on classes to print logs.>
using EnigmaVirtual.OneCoreClient;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using UnityEngine;
using UnityEngine.Android;
using UnityRawInput;

namespace EnigmaVirtual
{
    public class MainController : MonoBehaviour
    {
        /// <summary>
        /// The live instance. Must stay <c>private static</c>: as an instance field the duplicate
        /// guard in <see cref="Awake"/> reads a fresh component's own field and can never fire, and
        /// as a public field Unity serializes it and offers it in the inspector, where a dragged-in
        /// reference makes this component destroy itself on the next launch.
        /// </summary>
        private static MainController Instance;
        public AppDataController AppDataController { get; private set; }
        public OneCore OneCore { get; private set; }
        public PhidgetsManager Phidgets { get; private set; }
        public ActivityMonitor ActivityMonitor { get; private set; }
        public SocketManager SocketManager { get; private set; }
        public ApiController Api { get; private set; }
        public OSCController OscController { get; private set; }
        public ForegroundEnforcer ForegroundEnforcer { get; private set; }
        public TextureCacheManager TextureCacheManager { get; private set; }
        public SerialPortsManager SerialPorts { get; private set; }

        // Declared as the framework base types, not the App-layer subclasses: nothing in the
        // framework needs a derived member, and naming them here would make the framework assembly
        // depend on App. App code that needs its own type casts once, e.g.
        //     public static AppData Data => (AppData)Core.main.AppData;
        public AppDataBase AppData
        {
            get
            {
                if (AppDataController != null)
                    return AppDataController.AppData;
                return null;
            }
        }

        public AppSettingsBase AppSettings
        {
            get
            {
                if (AppDataController != null)
                    return AppDataController.AppSettings;
                return null;
            }
        }

        [SerializeField]
        private AssetManager assetManager;

        /// <summary>
        /// The asset manager, or <c>null</c> until one exists.
        /// </summary>
        /// <remarks>
        /// Read-only, and a property over the field rather than an auto-property like
        /// <see cref="TextureCacheManager"/>, because the field is <c>[SerializeField]</c> and Unity
        /// does not serialize auto-properties.
        /// <para>
        /// <b>Null for most of the boot.</b> Unlike <see cref="TextureCacheManager"/>, which
        /// <see cref="Init"/> creates, this one is created by <c>SetupAssetManager</c> from the
        /// appData-loaded handler - so anything wanting it has to wait for
        /// <c>AppDataController.OnDataLoadedEvent</c> rather than reading it at <c>Init</c> time.
        /// </para>
        /// </remarks>
        public AssetManager AssetManager => assetManager;

        private Console console;

        private ImageLoaderThread imageLoadThreadPool;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroy, not DestroyImmediate: DestroyImmediate is not for runtime. The message
                // names what to look for, because "there can be only one" tells nobody where the
                // second one came from.
                Development.LogError("A second MainController was found and destroyed. Exactly one "
                    + "belongs on Core - check for a duplicated prefab or a stray copy in the scene.");

                // enabled = false before Destroy, because Destroy is deferred to end of frame while
                // Core resolves `main` with GetComponentInChildren<MainController>() and Awake order
                // between two of them is undefined - so Core could otherwise bind to the copy that
                // is about to die, and call Init() on it.
                enabled = false;
                Destroy(this);
                return;
            }

            Instance = this;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
            {
                // Plain Log, never LogIf: Awake ordering between components is undefined, so
                // Core.settings is very likely still null here, and DO_LOGS is defined for Android
                // — a LogIf argument would be evaluated and would throw during Awake.
                Development.Log("Requesting storage permission...");
                Permission.RequestUserPermission(Permission.ExternalStorageRead);
            }
#endif
        }

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
            {
                Development.LogError("No storage permission granted!");
                return;
            }
#endif
        }

#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>
        /// True while either Ctrl and either Alt are held. Shared by every operator shortcut so the
        /// modifier check exists once — retyped per shortcut, the <c>&amp;&amp;</c>/<c>||</c>
        /// precedence is easy to get wrong in a way that fires on a single modifier.
        /// </summary>
        private static bool CtrlAlt =>
            (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
            (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));

#if !UNITY_EDITOR
        private bool _restarting;
#endif

        /// <summary>
        /// <c>killExplorer</c>, and <c>false</c> whenever settings are unavailable.
        ///
        /// The operator shortcuts below run before — or instead of — a successful settings parse,
        /// because <see cref="Update"/> is a Unity message and does not wait for the boot chain.
        /// So this cannot assume <c>Core.settings</c> exists. The whole chain is
        /// null-conditional, not just the root: a partially parsed settings.json can leave
        /// <c>System</c> or <c>Windows</c> null while <c>Core.settings</c> itself is fine.
        ///
        /// <c>false</c> is the safe default in both directions — not killing the shell is the
        /// non-destructive choice, and <see cref="OnDestroy"/> only restores explorer if it
        /// believes it killed it.
        /// </summary>
        /// <summary>
        /// How long to wait for a killed process to actually exit before giving up on it.
        /// </summary>
        /// <remarks>
        /// A bound rather than <c>WaitForExit()</c>, which is what this was. <c>Kill</c> is
        /// asynchronous and a process sitting in an uninterruptible kernel wait never dies, so the
        /// unbounded form blocks <c>Init()</c> permanently - and because the boot watchdog is a
        /// coroutine, a blocked main thread means it cannot fire either. The failure was a frozen boot
        /// with nothing in the log.
        /// </remarks>
        private const int KillWaitMilliseconds = 5000;

        private static bool KillExplorerRequested =>
            Core.settings?.System?.Windows?.Processes?.killExplorer ?? false;

        /// <summary>Used when the setting is absent or explicitly None. See
        /// <see cref="SkipAssetValidationKey"/> for why that fallback is load-bearing.</summary>
        private const KeyCode DefaultSkipAssetValidationKey = KeyCode.K;

        /// <summary>
        /// The key that skips asset validation, from settings, falling back to
        /// <see cref="DefaultSkipAssetValidationKey"/>.
        ///
        /// The fallback is not decoration. A settings.json deployed before this feature existed has
        /// no such key, so it deserializes to 0 - <c>KeyCode.None</c> - and
        /// <c>Input.GetKeyDown(None)</c> never fires. Without the fallback the escape hatch would be
        /// silently missing on precisely the older installations most likely to need it.
        ///
        /// Config patching does not cover this: the patcher copies keys the *shipped*
        /// StreamingAssets file has, and until that file carries the key there is nothing to copy.
        /// The fallback is the mechanism, not a safety net for it.
        /// </summary>
        private static KeyCode SkipAssetValidationKey
        {
            get
            {
                int configured = Core.settings?.System?.Windows?.skipAssetValidationKeyCode ?? 0;

                return configured == 0 ? DefaultSkipAssetValidationKey : (KeyCode)configured;
            }
        }

        private void Update()
        {
            if (!CtrlAlt)
                return;

            // These three logs are deliberately NOT LogIf(Core.settings.System.doLogs, …).
            // That form dereferences the settings object whose absence is the very thing these
            // shortcuts exist to recover from, and DO_LOGS is defined for Standalone — the only
            // platform this block compiles for — so the argument is evaluated and throws, killing
            // the shortcut before it runs.
            //
            // Ungated on purpose: an operator reaching for a recovery hotkey is exactly the
            // breadcrumb you want in the log of a machine where startup already failed, and doLogs
            // is unreachable in that state. Development.Log is itself [Conditional("DO_LOGS")], so
            // it still compiles only where this block does.
            if (Input.GetKeyDown(KeyCode.S))
            {
                Development.Log("OpenConfigFolder shortcut (Ctrl + Alt + S) Pressed!");

                OpenConfigFolder();
            }

            if (Input.GetKeyDown(KeyCode.I))
            {
                Development.Log("OpenInstallFolder shortcut (Ctrl + Alt + I) Pressed!");

                OpenInstallFolder();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                Development.Log("FullRestart shortcut (Ctrl + Alt + R) Pressed!");

                FullRestart();
            }

            // Behind the same Ctrl+Alt gate as the others, deliberately: a bare keypress that
            // skipped asset validation would be far easier to hit by accident than to diagnose
            // afterwards.
            if (Input.GetKeyDown(SkipAssetValidationKey))
            {
                Development.Log($"Skip asset validation shortcut (Ctrl + Alt + {SkipAssetValidationKey}) Pressed!");

                Core.RequestSkipAssetValidation();
            }
        }

        private void FullRestart()
        {
#if UNITY_EDITOR
            // In the editor MainModule is Unity.exe and Application.Quit() is a no-op, so a
            // "restart" would launch a second editor and never close this one.
            UnityEngine.Debug.LogWarning("FullRestart ignored in the editor.");
#else
            // Application.Quit() only takes effect at end of frame, so without this guard a
            // repeated trigger spawns one process per frame.
            if (_restarting)
                return;

            _restarting = true;

            try
            {
                string exePath;
                using (Process current = Process.GetCurrentProcess())
                    exePath = current.MainModule.FileName;

                Process.Start(exePath)?.Dispose();
                Application.Quit();
            }
            catch (Exception e)
            {
                // UnityEngine.Debug deliberately, not Development.LogError: it keeps this catch
                // provably independent of the logging wrapper. Full exception, not e.Message: "Access is
                // denied" alone hides which of the two calls failed.
                UnityEngine.Debug.LogError("FullRestart failed: " + e);
                _restarting = false;
            }
#endif
        }
#endif

#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
        private void OpenConfigFolder()
        {
            // KillExplorerRequested, not a direct deref: reachable with Core.settings null.
            if (KillExplorerRequested)
            {
                try
                {
                    Process.Start(string.Concat(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"\explorer.exe"));
                }
                catch (Exception e) when (e is Win32Exception || e is FileNotFoundException
                    || e is InvalidOperationException || e is NotSupportedException)
                {
                    Development.LogError(string.Concat("The following exception was raised: ", e.Message));
                }
            }

            // AppPaths, not a hand-built LocalLow path: opens the folder the app actually reads,
            // and survives a company/product rename or a change in Unity's layout.
            //
            // ForShell is required, not cosmetic: AppPaths values are mixed-separator on Windows
            // (persistentDataPath gives '/', Path.Combine gives '\'), and explorer.exe silently
            // does nothing when handed a path containing a forward slash.
            // Ensure the folder too - explorer also opens nothing for a path that doesn't exist,
            // which is the normal state on a machine that has never written a config override.
            AppPaths.EnsurePersistentConfig();
            string folderPath = AppPaths.ForShell(AppPaths.PersistentConfig);

            try
            {
                Process.Start(string.Concat(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"\explorer.exe"), folderPath);
            }
            catch (Exception e) when (e is Win32Exception || e is FileNotFoundException
                    || e is InvalidOperationException || e is NotSupportedException)
            {
                Development.LogError(string.Concat("The following exception was raised: ", e.Message));
            }
        }

        private void OpenInstallFolder()
        {
            // KillExplorerRequested, not a direct deref: reachable with Core.settings null.
            if (KillExplorerRequested)
            {
                try
                {
                    Process.Start(string.Concat(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"\explorer.exe"));
                }
                catch (Exception e) when (e is Win32Exception || e is FileNotFoundException
                    || e is InvalidOperationException || e is NotSupportedException)
                {
                    Development.LogError(string.Concat("The following exception was raised: ", e.Message));
                }
            }

            string folderPath = Directory.GetParent(Application.dataPath).FullName;

            try
            {
                Process.Start(string.Concat(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"\explorer.exe"), folderPath);
            }
            catch (Exception e) when (e is Win32Exception || e is FileNotFoundException
                    || e is InvalidOperationException || e is NotSupportedException)
            {
                Development.LogError(string.Concat("The following exception was raised: ", e.Message));
            }
        }
#endif

        public void Init()
        {
            if (Core.settings.DebugSettings.Console.isActive)
                SetupDebugConsole();
            if (Core.settings.OSC.isActive)
                SetupOscController();
            if (Core.settings.Api.isActive)
                SetupApiController();
            if (Core.settings.Sockets.isActive)
                SetupSocketManager();
            if (Core.settings.Activity.isActive)
                SetupActivityMonitor();
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (Core.settings.Phidgets.isActive)
                SetupPhidgets();
#endif
            if (Core.settings.OneCore.isActive)
                SetupOneCore();
            if (Core.settings.AppData.isActive)
                SetupAppData();
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (Core.settings.SerialPorts.isActive)
                SetupSerialPorts();
#endif

            SetupTextureCacheManager();

            imageLoadThreadPool = GetComponentInChildren<ImageLoaderThread>();

#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN

            // Set keylogger. The bool is checked rather than discarded: Start returns false both
            // when the hook could not be installed and when one is already running, and RawKeyInput
            // now logs the Win32 error for the first case - so an unreported false here would leave
            // "the key logger does nothing" with no evidence either way.
            if (Core.settings.System.Windows.KeyLogger.isActive
                && !RawKeyInput.Start(Core.settings.System.Windows.KeyLogger.workInBackround))
            {
                Development.LogError("Key logger is enabled in settings but the keyboard hook did "
                    + "not start, so no key input will be reported. If a Win32 error is logged above, "
                    + "that is the reason; otherwise a hook was already running.");
            }

            // Cached now, while Core.settings is certainly alive, because OnDestroy cannot read it.
            // Init runs only from CoreSetupComplete(), after a successful parse.
            restoreExplorerOnQuit = Core.settings.System.Windows.Processes.killExplorer
                && Core.settings.System.Windows.Processes.restoreExplorerOnQuit;

            if (!Core.settings.System.Windows.Processes.killProcesses.IsNullOrEmpty())
            {
                for (int i = 0; i < Core.settings.System.Windows.Processes.killProcesses.Length; i++)
                {
                    if (Core.settings.System.Windows.Processes.killProcesses[i].Equals("explorer", StringComparison.InvariantCulture) ||
                        Core.settings.System.Windows.Processes.killProcesses[i].IsNullOrEmpty())
                        continue;

                    string targetName = Core.settings.System.Windows.Processes.killProcesses[i];

                    // Two levels of try, both scoped deliberately, because the whole defect here was a
                    // catch placed too far out. This one covers the enumeration for ONE name: it used to
                    // sit outside this loop, so a GetProcessesByName that threw on the first name
                    // silently abandoned every remaining name the operator had listed.
                    try
                    {
                        Process[] processes = Process.GetProcessesByName(targetName);
                        if (processes.IsNullOrEmpty())
                            continue;

                        Development.LogIf(Core.settings.System.doLogs, "Trying to close process [" + targetName + "]!");

                        foreach (Process process in processes)
                        {
                            // ...and this one covers ONE process, for the same reason one level down:
                            // the first process that refused to die used to abandon the rest of the name
                            // list too.
                            try
                            {
                                if (process != null && !process.HasExited)
                                {
                                    process.Kill();

                                    // Bounded, and this is the important argument in the call. Kill() is
                                    // asynchronous; a process in an uninterruptible kernel wait never
                                    // dies, and an unbounded WaitForExit then blocks Init() forever.
                                    // BootWatchdog_IE is a coroutine, so a blocked main thread means no
                                    // BOOT STALLED either - a frozen boot with an empty log, which is
                                    // the worst failure shape this framework has.
                                    if (!process.WaitForExit(KillWaitMilliseconds))
                                    {
                                        Development.LogError($"[{targetName}] did not exit within "
                                            + $"{KillWaitMilliseconds}ms of being killed. Continuing "
                                            + "rather than waiting - it may still hold a port or a file "
                                            + "this app needs.");
                                    }
                                }
                            }
                            catch (Exception e) when (e is Win32Exception
                                || e is InvalidOperationException
                                || e is NotSupportedException)
                            {
                                // InvalidOperationException is the HasExited-then-Kill race: the process
                                // may exit between the two, and Kill on an exited process throws.
                                // NotSupportedException is a remote process. Neither was in the old
                                // filter, so either one escaped this method and took the caller's boot
                                // step with it.
                                Development.LogError($"Could not close [{targetName}]: "
                                    + $"{e.GetType().Name}: {e.Message}. Continuing with the rest of "
                                    + "killProcesses.");
                            }
                            finally
                            {
                                // finally, not inside the if: a process that had already exited was
                                // never disposed, leaking a handle per pass.
                                process?.Dispose();
                            }
                        }
                    }
                    catch (Exception e) when (e is Win32Exception
                        || e is InvalidOperationException
                        || e is NotSupportedException)
                    {
                        Development.LogError($"Could not enumerate processes named [{targetName}]: "
                            + $"{e.GetType().Name}: {e.Message}. Continuing with the rest of "
                            + "killProcesses.");
                    }
                }
            }

            if (Core.settings.System.Windows.Processes.killExplorer)
            {
                // Logged unconditionally, at Error, before it happens. This is the most destructive
                // thing the framework does to a machine it does not own and it used to leave no record
                // at all - so "the desktop is gone" arrived at whoever reads the log with nothing to
                // connect it to. Not LogIf: doLogs ships false, which is precisely when this is needed.
                // restoreExplorerOnQuit is read into the sentence rather than assumed: it is a real
                // operator setting, and promising a restore that will not happen makes this line worse
                // than no line - it is the one message somebody relies on when the desktop disappears.
                Development.LogError("Killing explorer.exe because "
                    + "System.Windows.Processes.killExplorer is true. "
                    + (restoreExplorerOnQuit
                        ? "OnDestroy restarts it, so a clean exit restores the shell - but if this "
                            + "process is killed instead, the desktop stays down until explorer.exe is "
                            + "started by hand or the machine is rebooted."
                        : "restoreExplorerOnQuit is FALSE, so nothing will restart it: the desktop "
                            + "stays down after this app exits, however it exits, until explorer.exe "
                            + "is started by hand or the machine is rebooted."));

                try
                {
                    // ?.Dispose(): the returned Process was never disposed, leaking a handle on every
                    // boot that kills the shell.
                    Process.Start(string.Concat(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"\System32\taskkill.exe"), @"/F /IM explorer.exe")?.Dispose();
                }
                catch (Exception e) when (e is Win32Exception || e is FileNotFoundException
                    || e is InvalidOperationException || e is NotSupportedException)
                {
                    // Unguarded until now, so a missing or blocked taskkill.exe threw straight out of
                    // the boot sequence. Reported rather than fatal: the shell surviving is the
                    // non-destructive outcome, and the app can run over it.
                    Development.LogError($"Could not kill explorer.exe: {e.GetType().Name}: "
                        + $"{e.Message}. The shell is still running and the app will start over it.");
                }
            }

            if (Core.settings.System.Windows.Presentation.minimizeWindow)
                WindowUtils.MinimizeWindow();

            if (Core.settings.System.Windows.Presentation.bringToFront)
                SetupForegroundEnforcer();

            if (Core.settings.System.Windows.Screen.forceResolution)
                Screen.SetResolution(Core.settings.System.Windows.Screen.width, Core.settings.System.Windows.Screen.height, Core.settings.System.Windows.Screen.fullScreen);
#endif

#if UNITY_ANDROID
            // .AndroidSettings, not .Android: the field name has to match the settings.json key or
            // JsonUtility never fills it - the value applied would silently be the C# initializer,
            // whatever the operator's file said.
            Screen.sleepTimeout = Core.settings.System.AndroidSettings.sleepTimeout;

            // Not gated on doLogs - which is what "ungated" means here, since Development.Log is itself
            // [Conditional("DO_LOGS")] and DO_LOGS is defined for Android. This is the only consumer of that
            // block, Android was first exercised on 2026-08-14; see the platform matrix, and the value silently disagreeing with
            // settings.json is precisely the defect that took this long to notice.
            //
            // Log rather than LogIf: LogIf logs when its condition is *true*, so it reads backwards
            // here. Core.settings is non-null at this point - Init() runs only from
            // CoreSetupComplete(), after a successful parse.
            Development.Log($"Screen.sleepTimeout set to {Screen.sleepTimeout} from "
                + "System.AndroidSettings.sleepTimeout.");
#endif

            if (!Core.settings.AppData.isActive)
                Core.Instance.LoadNextScene();
        }

        /// <summary>
        /// Whether to relaunch explorer.exe on quit, decided at <c>Init</c> rather than at teardown.
        /// </summary>
        /// <remarks>
        /// <c>OnDestroy</c> must not read <c>Core.settings</c> for this: <c>Core.OnDestroy</c> sets
        /// <c>_isSet = false</c> and the teardown order between the two objects is undefined, so the
        /// restore would sit below a guard that could skip it entirely. Of everything in
        /// <c>OnDestroy</c> this is the one with the worst failure: a stranded keyboard hook
        /// costs a domain reload, a kiosk left with no Windows shell costs a site visit - and the
        /// whole branch being bypassed meant there was nothing in the log to say why.
        /// </remarks>
        private bool restoreExplorerOnQuit;

        private void OnDestroy()
        {
            // Everything that outlives this process goes ABOVE the Core.IsSet guard.
            // Core.OnDestroy sets _isSet = false and the teardown order between the two objects is
            // undefined, so anything below the guard may simply not run - and it fails silently,
            // because the whole branch is bypassed rather than reporting anything.
            //
            // Neither of these can read Core.settings here, for the same reason. RawKeyInput.Stop()
            // needs no flag - it is idempotent, RemoveHook tests the pointer - and the explorer
            // decision was cached at Init.
            RawKeyInput.Stop();

#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (restoreExplorerOnQuit)
            {
                try
                {
                    Process.Start(string.Concat(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"\explorer.exe"));
                }
                catch (Exception e) when (e is Win32Exception || e is FileNotFoundException
                    || e is InvalidOperationException || e is NotSupportedException)
                {
                    Development.LogError(string.Concat("explorer.exe could not be restarted, so this "
                        + "machine is left with no Windows shell until it is rebooted: ", e.Message));
                }
            }
#endif
        }

        // The `if (!Core.IsSet) return;` that used to open OnDestroy is gone rather than
        // moved to the bottom, where it would have been dead code that still looked
        // load-bearing. Nothing in OnDestroy may depend on Core being alive: add anything
        // here that reads Core.settings and it will work in the editor and skip itself on a
        // real quit, silently. Cache what you need at Init instead.

        private void SetupSerialPorts()
        {
            if (SerialPorts == null)
                SerialPorts = GetComponentInChildren<SerialPortsManager>();

            if (SerialPorts == null)
            {
                GameObject go = new GameObject("SerialPorts");
                go.transform.SetParent(gameObject.transform);
                SerialPorts = go.AddComponent<SerialPortsManager>();
            }
        }

        private void SetupForegroundEnforcer()
        {
            if (ForegroundEnforcer == null)
                ForegroundEnforcer = GetComponentInChildren<ForegroundEnforcer>();

            if (ForegroundEnforcer == null)
            {
                GameObject go = new GameObject("ForegroundEnforcer");
                go.transform.SetParent(gameObject.transform);
                ForegroundEnforcer = go.AddComponent<ForegroundEnforcer>();
            }
        }

        private void SetupDebugConsole()
        {
            if (console == null)
                console = GetComponentInChildren<Console>();

            if (console == null)
            {
                GameObject go = new GameObject("Console");
                go.transform.SetParent(gameObject.transform);
                console = go.AddComponent<Console>();
            }

            if (console == null)
            {
                Development.LogError("Console could not be adopted or created - the in-app debug console is unavailable, so its settings were not applied.");
                return;
            }
            console.toggleKey = (KeyCode)Core.settings.DebugSettings.Console.toggleKeyCode;
            console.restrictLogCount = Core.settings.DebugSettings.Console.restrictLogCount;
            console.maxLogCount = Core.settings.DebugSettings.Console.maxLogCount;
            //console.logFontSize = 14;
            //console.scaleFactor = 1;            
#if UNITY_ANDROID
            console.shakeToOpen = Core.settings.DebugSettings.Console.shakeToOpen;
            console.shakeRequiresTouch = true;
#endif
        }

        private void SetupAppData()
        {
            if (AppDataController == null)
                AppDataController = GetComponentInChildren<AppDataController>(true);

            // Then the whole scene. GetComponentInChildren searches only MainController's OWN subtree,
            // and the project's controller is conventionally authored as a SIBLING - the shipped scene has
            // Core > AppData beside Core > MainController - so it was never found and the fallback below
            // quietly created a base-typed one instead. That cost nothing while the fallback deserialized
            // into the project's model directly; it is a silent content wipe now that the fallback is
            // base-typed. Include inactive: a controller parked inactive is still the one the
            // project meant to use, and finding nothing here is worse than finding it disabled.
            if (AppDataController == null)
                AppDataController = FindAnyObjectByType<AppDataController>(FindObjectsInactive.Include);

            // A scene-authored controller must fall through to the subscribe, the boot stage and Init
            // below. Returning early here discovers it and never starts it: no data load, no
            // OnDataLoadedEvent, and the boot chain stops on the preloader with nothing surfaced.
            // Subclassing this controller is how a project gets its own model deserialized, and
            // discovery is the only way to reach it.
            if (AppDataController == null)
            {
                GameObject go = new GameObject("AppData");
                go.transform.SetParent(gameObject.transform);
                go.AddComponent<AppDataController>();

                AppDataController = go.GetComponent<AppDataController>();
            }

            AppDataController.OnDataLoadedEvent += AppDataController_OnDataLoadedEvent;
            Core.MarkBootStage("loading appData");
            AppDataController.Init();
        }

        private void AppDataController_OnDataLoadedEvent()
        {
            //if (!assetManager)
            //    TextureCacheManager = GetComponentInChildren<TextureCacheManager>();

            AppDataController.OnDataLoadedEvent -= AppDataController_OnDataLoadedEvent;
            Core.MarkBootStage("appData loaded");

            // Defensive, and honestly labelled as such: a parse that produces no model does not raise
            // this event at all - AppDataController recovers that payload itself. This branch exists
            // so that a null model is not fatal if some future path announces one.
            //
            // Continue rather than abort. The rest of this method still runs, so with
            // AssetDownload.isActive true and no skip key the boot lands in AssetManager's own
            // null-AppData branch, which reports and releases the chain. With downloads off it
            // goes straight to LoadNextScene and the App layer sees a null model - which is a worse
            // outcome, but a louder one than a silent hang.
            if (AppData == null)
            {
                Development.LogError("appData was announced as loaded but produced no model, so there "
                    + "is nothing to configure languages or content from. Continuing the boot; expect "
                    + "empty content. Look above for the parse error that produced this.");
            }
            else if (!AppData.languages.IsNullOrEmpty())
                Core.localizationSettings.SetLanguages(AppData.languages);
            else
                Development.LogWarning("No available languages defined!");

            // Core.SkipAssetValidation is checked here as well as inside the download loop, so a
            // key pressed while appData was still loading is honoured - the operator cannot tell
            // which phase is slow, only that boot is.
            if (!Core.settings.AppData.Cache.AssetDownload.isActive || Core.SkipAssetValidation)
                Core.Instance.LoadNextScene();
            else
                SetupAssetManager();
        }
        private void SetupAssetManager()
        {
            if (assetManager == null)
                assetManager = GetComponentInChildren<AssetManager>();

            // Then anywhere in the scene, before creating one - the rung SetupTextureCacheManager
            // already has, where it adopts TextureCacheManager.Instance "wherever it lives". Without
            // it, an AssetManager authored OUTSIDE this transform makes this create a second one and
            // drive it, while CoreSystemUI stays bound to the authored one: every preloader label
            // stops updating, nothing is logged, and the boot still completes.
            //
            // Active only, deliberately, and for the reason SetupTextureCacheManager states:
            // StartCoroutine on an inactive GameObject does nothing but log, so adopting an inactive
            // instance would stall the asset phase instead of merely splitting the readout.
            if (assetManager == null)
                assetManager = FindAnyObjectByType<AssetManager>();

            if (assetManager == null)
            {
                GameObject go = new GameObject("AssetManager");
                go.transform.SetParent(gameObject.transform);
                go.AddComponent<AssetManager>();

                assetManager = go.GetComponent<AssetManager>();
            }

            assetManager.onAssetCleaningCompleteEvent += AssetManager_onAssetCleaningCompleteEvent;

            // Per-asset progress feeds the boot watchdog. Without this the entire download phase is
            // one stage, so the 90s stall budget has to cover a legitimate first-run download of
            // hundreds of files - two assets hitting the 60s request timeout back to back would be
            // enough to put "STARTUP FAILED" on a client's screen mid-boot. The events already
            // existed for the preloader readout; they just were not wired to anything here.
            assetManager.onAssetVerificationEvent += AssetManager_onAssetProgress;
            assetManager.onAssetDownloadEvent += AssetManager_onAssetProgress;

            Core.MarkBootStage("validating assets");
            StartCoroutine(SetupAssetManager_IE());
        }

        private System.Collections.IEnumerator SetupAssetManager_IE()
        {
            yield return new WaitForSeconds(2.5f);
            assetManager.ValidateAssets();
        }

        private void SetupTextureCacheManager()
        {
            // Adopt an existing instance first, wherever it lives. Creating a second one would
            // hit TextureCacheManager.Awake's duplicate branch, which calls Destroy(this) - and
            // Destroy is deferred, so AddComponent would still hand back a non-null component
            // that dies at end of frame, taking its coroutines and the completion event with it.
            if (TextureCacheManager == null)
                TextureCacheManager = TextureCacheManager.Instance;

            // Active only: StartCoroutine on an inactive GameObject does nothing but log, which
            // would stall the boot chain at the texture-cache hop.
            if (TextureCacheManager == null)
                TextureCacheManager = GetComponentInChildren<TextureCacheManager>();

            if (TextureCacheManager == null)
            {
                GameObject go = new GameObject("TextureCacheManager");
                go.transform.SetParent(gameObject.transform, false);

                // Use AddComponent's return value: reading the component back with GetComponent<>()
                // on `this` rather than on `go` returns null.
                // CoreSetup.unity has carried a TextureCacheManager under MainController since the
                // initial import, so GetComponentInChildren above adopts it and this branch does not
                // run in the shipped scene. It runs for a project that removed the scene component.
                TextureCacheManager = go.AddComponent<TextureCacheManager>();
            }

            if (TextureCacheManager == null)
                Development.LogError("TextureCacheManager could not be created - texture caching is unavailable.");
        }

        private void AssetManager_onAssetProgress(int done, string url, int total)
        {
            // The stage mark is unconditional; only its log line follows doLogs. Gating the whole call
            // suppressed the spam as intended, but MarkBootStage also resets the watchdog deadline -
            // and this is the longest phase of startup, so with doLogs false (the shipped default) a
            // healthy 200-asset download would trip the stall timeout on its own.
            Core.MarkBootStage($"assets {done}/{total}",
                Core.settings?.AppData?.Cache?.AssetDownload?.doLogs ?? false);
        }

        private void AssetManager_onAssetCleaningCompleteEvent()
        {
            assetManager.onAssetCleaningCompleteEvent -= AssetManager_onAssetCleaningCompleteEvent;
            assetManager.onAssetVerificationEvent -= AssetManager_onAssetProgress;
            assetManager.onAssetDownloadEvent -= AssetManager_onAssetProgress;
            Core.MarkBootStage("assets validated");

            if (!Core.settings.AppData.Cache.TextureCache.isActive)
                Core.Instance.LoadNextScene();
            else
            {
                TextureCacheManager.onAssetVerificationCompleteEvent += TextureCacheManager_onAssetVerificationCompleteEvent;
                TextureCacheManager.onTextureCachedEvent += TextureCacheManager_onTextureCached;

                // A starting mark, immediately superseded per image. Without the per-image marks this
                // string stood for the ENTIRE pass, so the stall watchdog measured a 300-image
                // pre-cache against a budget derived from one request timeout - and it latches, so a
                // false fire makes LoadNextScene refuse for good. A healthy boot failing closed.
                Core.MarkBootStage("caching textures");
                TextureCacheManager.CacheAssets(AppData, true);
            }
        }

        private void TextureCacheManager_onTextureCached(int done, string url, int total)
        {
            // Same shape as AssetManager_onAssetProgress, and for the same reason: the mark is
            // unconditional and only its log line follows doLogs. MarkBootStage resets the watchdog
            // deadline, so gating the whole call would put the shipped default (doLogs false) back
            // where it started.
            //
            // Every processed item marks - success, failure and all three skips - because the manager
            // raises from MarkProcessed rather than from the success branch. That matters more than it
            // sounds: failure is the SLOW outcome, so a version that marked only successes would report
            // progress on exactly the path that never stalls. An earlier comment here named duplicates
            // as the risk instead; duplicates are a dictionary hit and cannot be slow, and mediaAssets is
            // de-duplicated upstream anyway. Documenting away the wrong risk is worse than saying
            // nothing.
            Core.MarkBootStage($"textures {done}/{total}",
                Core.settings?.AppData?.Cache?.TextureCache?.doLogs ?? false);
        }

        private void TextureCacheManager_onAssetVerificationCompleteEvent()
        {
            TextureCacheManager.onAssetVerificationCompleteEvent -= TextureCacheManager_onAssetVerificationCompleteEvent;
            TextureCacheManager.onTextureCachedEvent -= TextureCacheManager_onTextureCached;
            Core.MarkBootStage("textures cached");
            Core.Instance.LoadNextScene();
        }

        private void SetupOscController()
        {
            if (OscController == null)
                OscController = GetComponentInChildren<OSCController>();

            if (OscController == null)
            {
                GameObject go = new GameObject("OSCController");
                go.transform.SetParent(gameObject.transform);
                OscController = go.AddComponent<OSCController>();
            }

            if (OscController == null)
            {
                Development.LogError("OSCController could not be adopted or created - OSC is unavailable, so no client was created and no start messages were sent.");
                return;
            }

            if (Core.settings.OSC.useBurst && !Core.settings.OSC.startMessages.IsNullOrEmpty())
                OscController.SendOsc(Core.settings.OSC.startMessages);

            if (!Core.settings.OSC.useBurst)
                OscController.CreateClient(IPAddress.Parse(Core.settings.OSC.serverIp), Core.settings.OSC.serverPort, Core.settings.OSC.clientName, Core.settings.OSC.startMessages, Core.settings.OSC.closeMessages);
        }

        private void SetupApiController()
        {
            if (Api == null)
                Api = GetComponentInChildren<ApiController>();

            if (Api == null)
            {
                GameObject go = new GameObject("ApiController");
                go.transform.SetParent(gameObject.transform);
                Api = go.AddComponent<ApiController>();
            }

            if (Api == null)
            {
                Development.LogError("ApiController could not be adopted or created - the API is unavailable and Init() was not called.");
                return;
            }
            Api.Init();
        }

        private void SetupSocketManager()
        {
            if (SocketManager == null)
                SocketManager = GetComponentInChildren<SocketManager>();

            if (SocketManager == null)
            {
                GameObject go = new GameObject("SocketManager");
                go.transform.SetParent(gameObject.transform);
                SocketManager = go.AddComponent<SocketManager>();
            }
        }

        private void SetupActivityMonitor()
        {
            if (ActivityMonitor == null)
                ActivityMonitor = GetComponentInChildren<ActivityMonitor>();

            if (ActivityMonitor == null)
            {
                GameObject go = new GameObject("ActivityMonitor");
                go.transform.SetParent(gameObject.transform);
                ActivityMonitor = go.AddComponent<ActivityMonitor>();
            }
        }

        private void SetupPhidgets()
        {
            if (Phidgets == null)
                Phidgets = GetComponentInChildren<PhidgetsManager>();

            if (Phidgets == null)
            {
                GameObject go = new GameObject("PhidgetsManager");
                go.transform.SetParent(gameObject.transform);
                Phidgets = go.AddComponent<PhidgetsManager>();
            }
        }

        private void SetupOneCore()
        {
            if (OneCore == null)
                OneCore = GetComponentInChildren<OneCore>();

            if (OneCore == null)
            {
                GameObject go = new GameObject("OneCore");
                go.transform.SetParent(gameObject.transform);
                OneCore = go.AddComponent<OneCore>();
            }
        }

        /// <summary>
        /// Queues a new item in ImageLoadThreadPool.
        /// </summary>
        /// <param name="queueItem"></param>
        public void EnqueueImageLoadThreadPool(ImageQueueItem queueItem)
        {
            if (imageLoadThreadPool == null)
            {
                // GetComponentInChildren returns null silently, so a Core without the component used
                // to surface as an NRE here rather than as the authoring mistake it is. The callback
                // still fires - with null - so the caller reports "sprite is null" instead of waiting
                // forever on a load that will never complete.
                Development.LogError("No ImageLoaderThread under Core, so no sprite can be created. "
                    + "Add the component to Core in CoreSetup.unity.");
                queueItem.outMethod?.Invoke(null);
                return;
            }

            imageLoadThreadPool.Enqueue(queueItem);
        }

        /// <summary>
        /// Returns requested translation for "key" in "language" if available.
        /// If parameter "language" not used, will return translation in current set language.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="language"></param>
        /// <returns></returns>
        public string GetTranslation(string key, string language = null)
        {
            return AppDataController.GetTranslation(key, language);
        }
    }
}

