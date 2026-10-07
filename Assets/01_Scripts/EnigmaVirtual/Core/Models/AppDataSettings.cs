using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EnigmaVirtual
{
    [System.Serializable]
    public sealed class AppDataOneCoreSettingsVO
    {
        public bool isActive = false;
        public int idContent = 0;
    }

    [System.Serializable]
    public sealed class AppDataSettings
    {
        /// <summary>
        /// Collapses a doubled slash without touching a URL scheme. Hoisted out of the
        /// <c>assetsPath</c> getter, which built the pattern through the static
        /// <c>Regex.Replace</c> on every access.
        /// </summary>
        /// <remarks>
        /// <para><b>Do not cache this value - it would be a correctness bug.</b> Every input to
        /// <c>assetsPath</c> -
        /// <c>Cache.AssetDownload.isActive</c> and both settings blocks -
        /// is rewritten at runtime when the settings panel saves, so a cached string would go stale
        /// with nothing to invalidate it. The cost it would buy back: 4.9us per call, so about
        /// <b>2.2ms once</b> across a 500-asset walk. Not a trade worth making.</para>
        ///
        /// <para>Hoisting the instance is the whole fix - 4.9us to 3.3us, a third off, and it works
        /// everywhere. <c>RegexOptions.Compiled</c> would reach 2.7us but needs Reflection.Emit,
        /// which IL2CPP does not have; Android ships IL2CPP, so that 0.7us exists only under Mono
        /// and is not worth a platform-dependent gain.</para>
        ///
        /// <para><b>The lookbehind is load-bearing.</b> A plain
        /// <c>path.Replace("//", "/")</c> turns <c>https://cms.example.com//assets</c> into
        /// <c>https:/cms.example.com/assets</c> - measured, and the reason this is a regex at
        /// all.</para>
        /// </remarks>
        private static readonly Regex DoubleSlash = new Regex(@"(?<!(http:|https:))//");

        public bool isActive = false;
        public bool useLocalAppData;
        public bool doLogs;
        /// <summary>
        /// Seconds allowed for the appData fetch, under the API policy: 0 means <b>unset</b> and
        /// resolves to <see cref="WebRequestExtensions.ApiUnconfiguredSeconds"/>, and the ceiling is
        /// <see cref="WebRequestExtensions.ApiCeilingSeconds"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>A key of its own rather than reusing <c>Api.timeout</c>.</b> The two blocks are
        /// independently switchable and ship opposite: <c>Api.isActive</c> is <b>false</b> while
        /// <c>AppData.isActive</c> is <b>true</b>, so an operator raising <c>Api.timeout</c> for a slow
        /// endpoint would not expect it to change the appData fetch - and would have no way to tune
        /// appData on a deployment where the Api module is off entirely.</para>
        ///
        /// <para><b>The API policy, not the file-transfer one</b>, because this is a small JSON
        /// document and not a resumable download: a value of 0 here means "I did not set this" and
        /// becomes 30s, where 0 on a download block means "no limit" and becomes 3600.</para>
        ///
        /// <para>Widening it widens the boot stall budget with it - <c>Core.StallBudgetSeconds</c>
        /// reads this - because the fetch happens inside a single boot stage and a budget shorter than
        /// the request it is timing would latch <c>BOOT STALLED</c> on a request that was still
        /// legitimately in flight.</para>
        ///
        /// <para><b>New in v0.49.20, so a machine with a <c>PlayerPrefs["Settings"]</c> snapshot will
        /// not see it</b> - the snapshot has no key for it and <c>Core.Awake</c> prefers the snapshot
        /// over the file, so the initializer below applies there until Player Prefs are cleared. That
        /// is the normal state on Android and iOS, where the snapshot is the only save target.</para>
        /// </remarks>
        public int timeout = WebRequestExtensions.ApiUnconfiguredSeconds;
        /// <summary>
        /// Characters of raw appData echoed at each end of the debug dump.
        /// </summary>
        /// <remarks>
        /// Spelled <c>appDataMaxLenght</c> until v0.49.2. Renaming a settings field is
        /// normally a config-format change - the field name IS the JSON key, and a rename also
        /// invalidates any PlayerPrefs snapshot - but this key was never present in
        /// the shipped <c>settings.json</c>, so no deployed machine carries it and the C#
        /// initializer below has always been what applied. Verified by grepping the shipped
        /// file, not assumed.
        /// </remarks>
        public int appDataMaxLength = 50;
        public CacheSettings Cache = new CacheSettings();
        // Initialised, and that is load-bearing rather than tidy: JsonUtility builds the
        // object graph exactly ONE level deeper than the JSON goes (measured 2026-08-14).
        // With this block absent from settings.json the parent is still built, but this
        // field would come back null - and AppDataController.Start dereferences
        // oneCoreDataModel.isActive on the boot path. Measured: JsonUtility builds one level below
        // whatever the JSON mentions, so depth 1 is safe and depth 2 is not.
        public AppDataOneCoreSettingsVO oneCoreDataModel = new AppDataOneCoreSettingsVO();
        // Initialized, like localSettings below. Without it, an operator deleting the remoteSettings
        // block from settings.json made the panel's "remoteHostUrl" field NRE out of the save loop before
        // any notification - so the operator got no toast at all, in either direction.
        public AppSettingsSettingsVO remoteSettings = new AppSettingsSettingsVO();
        public AppSettingsSettingsVO localSettings = new AppSettingsSettingsVO();

        /// <summary>
        /// Applies a single operator-edited setting from the on-device settings panel
        /// (<see cref="SettingInput"/>). The panel saves every field on each save-click and,
        /// when its "overwrite json" toggle is on, then writes the whole settings object back
        /// to settings.json — so a bad value here can reach disk. Unparseable input keeps the
        /// previous value and reports it.
        ///
        /// The keys below must match the Key fields authored on the SettingInput components in
        /// CoreSetup.unity (Group 6 = AppData). A key present in the scene but missing here
        /// silently fails to save, which is how localAssetsPath, localDataJsonPath and
        /// remoteAssetsPath were dead until this was fixed.
        /// </summary>
        internal void SetData(string key, string value)
        {
            switch (key)
            {
                case "isActive":
                    isActive = SettingsParse.Bool(key, value, isActive);
                    break;
                case "useLocalAppData":
                    useLocalAppData = SettingsParse.Bool(key, value, useLocalAppData);
                    break;
                case "downloadableContent":
                    Cache.AssetDownload.isActive = SettingsParse.Bool(key, value, Cache.AssetDownload.isActive);
                    break;
                case "downloadThreads":
                    // 0 would make the chunking in AssetManager/TextureCacheManager produce zero
                    // chunks, so nothing downloads and the wait completes as if it had.
                    Cache.AssetDownload.downloadThreads = SettingsParse.Int(key, value, Cache.AssetDownload.downloadThreads, 1);
                    break;
                case "remoteHostUrl":
                    remoteSettings.hostUrl = value;
                    break;
                case "remoteDataJsonPath":
                    remoteSettings.dataJsonPath = value;
                    break;
                case "remoteAssetsPath":
                    remoteSettings.assetsPath = value;
                    break;
                case "localDataJsonPath":
                    localSettings.dataJsonPath = value;
                    break;
                case "localAssetsPath":
                    localSettings.assetsPath = value;
                    break;
                default:
                    // Silently ignoring an unknown key makes a mistyped Key on a prefab, or a
                    // renamed field, look like a successful save.
                    SettingsParse.Error($"SetData: unknown key [{key}] - nothing was saved for it.");
                    break;
            }
        }

        internal string dataJsonCompletePath
        {
            get
            {
                // Unreachable. TryLoadData calls LoadLocalData() directly when useLocalAppData is set
                // and only reaches LoadRemoteData - this property's one consumer - in the else, so
                // nothing ever reads this branch. Left in place rather than deleted: removing it makes
                // the property remote-only, which wants a rename, and that is a breaking change for
                // every branched project. Do not trust it as evidence of what local mode reads -
                // downloadedDataJsonPath below is the real answer.
                if (useLocalAppData)
                    return AppPaths.StreamingConfigFile(localSettings.dataJsonPath);

                return string.Concat(remoteSettings.hostUrl, remoteSettings.dataJsonPath);
            }
        }

        /// <summary>
        /// The appData at the config <b>root</b> - the one place every writer puts one.
        /// </summary>
        /// <remarks>
        /// <para><c>ConfigManager</c> seeds <c>PersistentConfigFile("appData.json")</c>, the generated
        /// deployment <c>.bat</c> copies into <c>config\</c>, an operator drops one there by hand, and
        /// the remote pipeline caches there. <c>ConfigManager.Preload</c> is a
        /// <c>RuntimeInitializeOnLoadMethod(BeforeSceneLoad)</c> - it runs before settings are parsed,
        /// because it is what makes settings readable at all - so a writer that needed a settings value
        /// to find this path could not work. The root is a necessity, not a choice.</para>
        /// <para><b>There used to be a second resolver</b> that composed an <c>AppData.downloadFolder</c>
        /// segment for the cache, so a project could in principle keep its runtime cache apart from an
        /// operator's override. It never did: the field shipped empty, no branch ever set it, and
        /// <c>Path.Combine(root, "", x)</c> is <c>Path.Combine(root, x)</c> - so the two resolvers
        /// returned the same string and the separation was notional. The field is gone as of v0.51.0.
        /// One file, said out loud, rather than two names for one file.</para>
        /// <para>A method rather than a property, and it returns <c>false</c> instead of throwing,
        /// because <b>its segment comes from settings.json</b>. <c>Path.Combine</c> throws
        /// <c>ArgumentException</c> on invalid path characters, so an operator pasting a remote-style
        /// <c>"api/entrance?pc=2"</c> into <c>localSettings.dataJsonPath</c> would otherwise throw out of
        /// whichever coroutine touched it first - and on the write side that happens *after*
        /// <c>OnGetAppData</c> has run, so the data is live and the boot chain stops anyway. An early
        /// version wrapped only the write in a try and left the path composition outside it, so the
        /// comment claimed a guarantee the code did not give.</para>
        /// <para>The containment check comes free with the same <c>GetFullPath</c> call. It is
        /// consistency with <c>Api.TryResolveCachePath</c> and <c>AssetManager</c>'s cleanup guard rather
        /// than a security boundary - an operator who can edit <c>settings.json</c> can already write
        /// anything the app can. What it stops is a typo silently redirecting a write:
        /// <c>Path.Combine</c> discards everything before an absolute segment, so an absolute
        /// <c>dataJsonPath</c> drops <c>PersistentConfig</c> entirely and writes wherever it names.</para>
        /// </remarks>
        internal bool TryGetDeployedDataJsonPath(out string fullPath)
        {
            return TryResolveConfigPath("the appData path under config",
                $"Check localSettings.dataJsonPath [{localSettings.dataJsonPath}] for an absolute path "
                + "or a stray '..'.",
                out fullPath, AppPaths.PersistentConfig, localSettings.dataJsonPath);
        }

        /// <summary>
        /// Composes a config path and refuses it if it escapes <c>persistentDataPath</c>.
        /// </summary>
        /// <remarks>
        /// <para><b>One implementation, two callers</b>, because two spellings of a containment check is
        /// what H22 and H6 were both about. Every segment below the root is operator-editable, so both
        /// the compose and the bounds test have to happen here rather than at the call sites.</para>
        ///
        /// <para>A <c>bool</c>-returning method rather than a property so a caller cannot be made to
        /// throw: <c>Path.Combine</c> throws on invalid path characters, and these segments come
        /// straight out of <c>settings.json</c>.</para>
        /// </remarks>
        private bool TryResolveConfigPath(string what, string diagnostic, out string fullPath,
            params string[] segments)
        {
            fullPath = null;

            try
            {
                string resolved = Path.GetFullPath(Path.Combine(segments));
                string root = Path.GetFullPath(Application.persistentDataPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    Development.LogError($"REFUSED {what} [{resolved}]: it resolves outside "
                        + $"[{root}]. {diagnostic}");
                    return false;
                }

                fullPath = resolved;
                return true;
            }
            catch (Exception e)
            {
                Development.LogError($"{what} could not be composed: {e.Message}. {diagnostic} "
                    + "That path is unavailable this run.");
                return false;
            }
        }

        /// <summary>
        /// Where the locally cached appData lives - the copy <c>AppDataController</c> writes after a
        /// successful fetch, and the one the offline fallback reads.
        /// </summary>
        /// <remarks>
        /// <b>Deliberately not routed through <see cref="AppPaths.Config"/>.</b> That method gained the
        /// <c>UsesDeployedConfig</c> gate, which is right for an operator override but wrong
        /// here: this is a runtime cache, so it has to be readable on every target including the editor.
        /// Gating it made the editor's offline fallback resolve to the shipped stub instead - and that
        /// stub carries <c>"code": 0</c>, which is <c>APIResponseCode.Success</c>, so a failed fetch
        /// would have loaded "Ipsum Lorem" placeholder content and reported success.
        /// <para><b>Two candidates</b>: the appData at the config root - written by the remote
        /// pipeline, the seeder, the deployment .bat or an operator, see
        /// <see cref="TryGetDeployedDataJsonPath"/> - and then the file that shipped in the build.</para>
        ///
        /// <para><b>There were three until v0.51.0</b>, the first being a <c>downloadFolder</c> cache.
        /// It always resolved to the same string as the second, because the field shipped empty and no
        /// branch set it. Removing it does not separate the cache from an operator override - it accepts
        /// that they are <i>one file</i> and stops offering a knob that implied otherwise. The
        /// alternative was giving <c>downloadFolder</c> a real default, which would have separated them
        /// and relocated every deployed machine's media, since the same value composed the asset folder.
        /// Note gating this read is separately known-wrong: the shipped stub carries
        /// <c>"code": 0</c>, so a gated read would report placeholder content as a success.</para>
        /// </remarks>
        internal string downloadedDataJsonPath
        {
            get
            {
                // No `useLocalAppData && AssetDownload.isActive` special case any more. That branch
                // returned that path with **no fallback**, and in local mode TryLoadData calls
                // LoadLocalData directly - so the primary appData source was a path only the *remote*
                // pipeline ever writes. It landed on the file ConfigManager seeds and worked by
                // accident; with any real folder name the read failed, OnDataLoadFailedEvent fired into
                // no subscriber and boot never advanced.
                //
                // The file at the config ROOT wins over the shipped one, on every target - see the
                // remarks. This is where ConfigManager's seed lands, where an operator or the generated
                // .bat puts a file, and where the remote pipeline caches. A false from the resolver
                // (unusable or out-of-bounds path) is treated exactly like a missing file: it has
                // already logged, and falling through is what keeps this from throwing into
                // LoadLocalData, which has no try of its own.
                if (TryGetDeployedDataJsonPath(out string deployed) && File.Exists(deployed))
                    return deployed;

                // The shipped copy, and the one candidate that is NOT containment-checked. Two of the
                // three above run localSettings.dataJsonPath through TryResolveConfigPath and refuse
                // anything escaping persistentDataPath; this composes it verbatim, so a dataJsonPath of
                // "../../x.json" resolves outside StreamingAssets. Both guarded candidates decline
                // together in that case - they share the segment - so the chain always lands here.
                //
                // Reported rather than refused, deliberately. This is a READ, settings.json is
                // operator-editable, and H22's reasoning applies: it is a consistency defect, not a
                // boundary breach. Refusing would mean returning null or a substituted filename into a
                // caller that has no fallback left, which trades a visible odd path for a silent wrong
                // one. What was actually wrong is that two of three reported and the third said nothing,
                // so a reader of TryResolveConfigPath concluded the resolver was contained.
                string shipped = AppPaths.StreamingConfigFile(localSettings.dataJsonPath);

                if (!IsUnderStreamingConfig(shipped))
                {
                    Development.LogError($"AppData.localSettings.dataJsonPath [{localSettings.dataJsonPath}] "
                        + $"resolves OUTSIDE the shipped config folder, to [{shipped}]. It is being used "
                        + "anyway - this is the last fallback and there is nothing else to return - but a "
                        + "relative path with '..' here is almost certainly a typo, and the two deployed "
                        + "candidates above have already refused it for the same reason.");
                }

                return shipped;
            }
        }

        /// <summary>Whether a composed shipped-config path stays inside StreamingAssets/config.</summary>
        /// <remarks>Separate from <c>TryResolveConfigPath</c>, which resolves against
        /// <c>persistentDataPath</c>; this one has a different root and no writer, so it reports rather
        /// than refuses.</remarks>
        private static bool IsUnderStreamingConfig(string fullPath)
        {
            try
            {
                string root = Path.GetFullPath(AppPaths.StreamingConfigFile(string.Empty));
                return Path.GetFullPath(fullPath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // An unusable path cannot be shown to escape, and this method exists to report, not to
                // gate - so a throw here must not become a second failure on the last fallback.
                return true;
            }
        }

        /// <summary>
        /// Returns asset path accordingly to settings
        /// </summary>
        /// <remarks>
        /// <c>public</c> rather than <c>internal</c> because App code calls it and the two now live
        /// in different assemblies. `internal` never meant anything here - everything shared
        /// one assembly - and the .asmdef split turns exactly this into CS0122.
        /// </remarks>
        public string GetAssetPath(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.IsValidUrl())
            {
                Development.LogWarning("External URL [" + value + "]");
                return value;
            }

            // Already an absolute path — return as-is (covers Windows, Android, iOS)
            if (Path.IsPathRooted(value))
                return value;

            return string.Concat(assetsPath, value);
        }

        /// <summary>
        /// Returns assets folder path as set in settings.json
        /// </summary>
        internal string assetsPath
        {
            get
            {
                string assetsFolder;
                string path;
                if (useLocalAppData)
                {
                    assetsFolder = string.IsNullOrEmpty(localSettings.assetsPath) ? "" : localSettings.assetsPath;

                    if (localSettings.usePersistentDataPath)
                        // Under StreamingAssets, mirroring the shipped layout - see the download
                        // branch below for why.
                        path = string.Concat(Application.persistentDataPath, "/", AppPaths.PersistentStreamingFolder, "/", assetsFolder);
                    else
                        path = string.Concat(Application.streamingAssetsPath, "/", assetsFolder);
                }
                else
                {
                    assetsFolder = string.IsNullOrEmpty(remoteSettings.assetsPath) ? localSettings.assetsPath : remoteSettings.assetsPath;

                    if (Cache.AssetDownload.isActive)
                        // The download cache lives under persistentDataPath/StreamingAssets, the same
                        // root as the config override - so the persistent folder mirrors the shipped
                        // StreamingAssets layout instead of scattering siblings beside it. It used to
                        // compose persistentDataPath + downloadFolder + assetsFolder directly, and
                        // that folder was "" in the shipped config, so the cache landed *next to*
                        // StreamingAssets rather than inside it.
                        //
                        // The downloadFolder segment is gone with the field, as of v0.51.0. It
                        // was always empty, and on Android it was not merely redundant: the
                        // `#if UNITY_ANDROID` return below happens BEFORE the DoubleSlash collapse, so
                        // the empty segment left a literal `StreamingAssets//assets` on the one
                        // platform that never gets it cleaned up.
                        path = string.Concat(Application.persistentDataPath, "/", AppPaths.PersistentStreamingFolder, "/",
                            assetsFolder);
                    else
                        path = string.Concat(remoteSettings.hostUrl, remoteSettings.assetsPath);
                }
#if UNITY_ANDROID
                return path;
#else
                return DoubleSlash.Replace(path, "/");
#endif
            }
        }

        /// <summary>
        /// Returns local assets folder path
        /// </summary>
        internal string assetsLocalCompletePath
        {
            get
            {
                return string.Concat(Application.streamingAssetsPath, "/", localSettings.assetsPath);
            }
        }

        /// <summary>
        /// Returns remote assets folder path
        /// </summary>
        internal string assetsRemotePath
        {
            get
            {
                return string.Concat(remoteSettings.hostUrl, remoteSettings.assetsPath);
            }
        }
    }

    [System.Serializable]
    public sealed class AppSettingsSettingsVO
    {
        public string hostUrl = "";
        public string dataJsonPath = "appData.json";
        public string assetsPath = "assets/";
        public bool usePersistentDataPath = false;

        internal string dataJsonCompletePath
        {
            get
            {
                return string.Concat(hostUrl, "/", dataJsonPath);
            }
        }

        internal string assetsCompletePath
        {
            get
            {
                return string.Concat(hostUrl, "/", assetsPath);
            }
        }
    }

    [System.Serializable]
    public sealed class CacheSettings
    {
        public AssetDownloadSettings AssetDownload = new AssetDownloadSettings();
        public TextureCacheSettings TextureCache = new TextureCacheSettings();
    }

    [System.Serializable]
    public sealed class TextureCacheSettings
    {
        public bool isActive = false;
        public bool doLogs = false;
        public bool showErrorImage = false;
        public int cacheThreads = 5;

        /// <summary>
        /// Seconds before one texture request is abandoned. <b>0 does not mean "wait forever" any
        /// more</b> - it resolves to the 3600s ceiling, which is the nearest bounded reading of the
        /// unlimited wait it used to buy. Note this differs from <c>Api.timeout</c>, where 0 means
        /// "unset" and resolves to 30; the field name is shared, the policy is not.
        /// </summary>
        public uint timeout = 30;
    }

    [System.Serializable]
    public sealed class AssetDownloadSettings
    {
        public bool isActive = false;
        public bool doLogs = false;
        public int downloadThreads = 1;

        /// <summary>
        /// Seconds before one asset request is abandoned. <b>0 does not mean "wait forever" any
        /// more</b> - it resolves to the 3600s ceiling. Set a real number rather than a generous
        /// one: a request cut short deletes its <c>.part</c> file and the retry restarts at byte
        /// zero, so a value below the honest transfer time makes the asset permanently
        /// undownloadable. Note <c>Api.timeout</c> resolves 0 to 30 instead - shared name, different
        /// policy.
        /// </summary>
        public uint timeout = 30;

        /// <summary>
        /// Recursively DELETES every file under <c>AppData.assetsPath</c> that is not referenced by
        /// the current appData. Off by default and deliberately separate from
        /// <see cref="isActive"/>: enabling downloads and enabling a recursive delete are very
        /// different risk profiles, and they used to share one switch.
        ///
        /// Do not enable it until the reflection walk covers fields as well as properties -
        /// an asset referenced only by a field never reaches the keep-list, so it would be deleted.
        /// </summary>
        public bool cleanUnlistedAssets = false;
    }
}
