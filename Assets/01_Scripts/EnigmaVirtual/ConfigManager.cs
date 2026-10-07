using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace EnigmaVirtual
{
    /// <summary>
    /// Seeds the writable config folder from the shipped one at startup, and brings an already-deployed
    /// config up to date with any keys the shipped file has gained.
    /// </summary>
    /// <remarks>
    /// <para><b>Runs in every player except WebGL as of v0.16.0.</b> It used to be
    /// <c>#if (UNITY_STANDALONE_WIN &amp;&amp; !UNITY_EDITOR) || DEBUG_MODE</c>, so there was no config
    /// seeding anywhere but a Windows player - the one place it matters least, since a Windows operator
    /// can drop files into the folder by hand. On Android the shipped config is inside the APK and cannot
    /// be opened with <c>File.*</c> at all, so seeding is the only way a writable copy ever exists.</para>
    ///
    /// <para><b>How the shipped side is read is decided by the path, not the platform</b> - the same rule
    /// <see cref="FileManager"/> now follows. A real file is read inline with <c>File.ReadAllText</c>, so
    /// the seed or patch is complete before <c>Core.Awake</c> reads the config in the same startup. Only a
    /// path inside the package (<c>jar:file://</c>) goes through <see cref="UnityWebRequest"/> and a
    /// coroutine, because nothing else can read it - and that path is unavoidably one launch behind.</para>
    ///
    /// <para>Reading it uniformly through <c>UnityWebRequest</c> would be tidier and was the first attempt,
    /// but it moved Windows off the synchronous path and made every config patch take effect only after a
    /// restart. The three near-identical copy/patch blocks still collapsed into
    /// one <see cref="Apply"/>; only the fetch differs.</para>
    /// </remarks>
    public class ConfigManager : MonoBehaviour
    {
        private const string settingsFilename = "settings.json";
        private const string appSettingsFilename = "appSettings.json";
        private const string appDataFilename = "appData.json";

        /// <summary>
        /// Suffix of the stamp written beside a freshly seeded <c>appData.json</c>, recording its size and
        /// write time so <c>AppDataController</c> can tell the seed from a cache the CMS wrote.
        /// </summary>
        /// <remarks>
        /// The alternative - comparing the deployed file with the shipped one at read time - is what this
        /// started as, and it fails silently on exactly the population that needs it: every branched project
        /// edits its shipped <c>appData.json</c>, and <c>appData.json</c> is seeded once and never patched
        /// (see <see cref="Apply"/>), so after any content release the deployed seed no longer matches the
        /// shipped file it came from and the comparison says "real cache". The stamp is written at seed time
        /// and so does not care what later builds ship. It self-invalidates rather than needing deleting: a
        /// cache write changes both the length and the write time, and the reader requires both to match.
        /// </remarks>
        internal const string seedStampSuffix = ".seeded";

        /// <summary>
        /// The files kept in step with the shipped defaults. <c>appData.json</c> is seeded but never
        /// patched - it is Newtonsoft-managed content rather than a settings DTO, and the merge below
        /// reasons about settings keys.
        /// </summary>
        private static readonly string[] SeededFiles = { settingsFilename, appSettingsFilename, appDataFilename };

        /// <summary>Cap on the package read. Only the Android path fetches, and it reads a local file.</summary>
        private const int PackageReadTimeoutSeconds = 10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Preload()
        {
            // AppPaths.UsesDeployedConfig, not a #if of this class's own. The body used to be wrapped in
            // `(UNITY_STANDALONE_WIN && !UNITY_EDITOR) || DEBUG_MODE` while the reader in
            // FileManager.CoreSettings() used `(!UNITY_EDITOR && !UNITY_WEBGL) || DEBUG_MODE`, and that gap
            // was the defect - every non-Windows player looked for a deployed config nothing had written. Keeping
            // two spellings in step by comment is how that comes back, so there is now one definition.
            //
            // A runtime check rather than a #if also means this whole class still compiles on every target
            // instead of vanishing, which is the bug class this belongs to: code that cannot be wrong
            // because it does not exist.
            if (!AppPaths.UsesDeployedConfig)
                return;

            try
            {
                AppPaths.EnsurePersistentConfig();
            }
            catch (Exception e)
            {
                // Nothing below can work without the folder, and this runs before the boot chain, so an
                // escaping exception aborts startup with an empty screen. Report and leave the app to run
                // off the shipped config.
                Development.LogError($"could not create the config folder [{AppPaths.PersistentConfig}]: "
                    + $"{e.Message}. No deployed config will be seeded or patched this launch; the app "
                    + "will read the shipped files instead.");
                return;
            }

            for (int i = 0; i < SeededFiles.Length; i++)
            {
                string filename = SeededFiles[i];
                string shipped = AppPaths.StreamingConfigFile(filename);

                if (shipped.Contains("://"))
                {
                    // Inside the package (Android's jar:file://), where UnityWebRequest is the only reader
                    // that works - so this file is necessarily a launch behind, see the note on SeedAsync.
                    ConfigManagerHelper.StartCoroutineHelper.Instance.StartCoroutine(SeedAsync(filename, shipped));
                    continue;
                }

                // Synchronous wherever the shipped file is a real file, and that is load-bearing rather
                // than an optimisation: Core.Awake reads the deployed config during this same startup
                // (it consumes FileManager.CoreSettings() inline), so anything that lands after
                // the first `yield` arrives a launch too late. The first version of this fix routed
                // everything through the coroutine and silently made every Windows config patch take
                // effect only after a restart - "the setting only works the second time you launch it",
                // which is a support call, not a bug report. Same path-vs-platform routing as
                // FileManager.LoadStreamingFile.
                //
                // The try covers the *read* only. It used to wrap Apply too, so any fault further in - a
                // bad deployed path, a merge that threw - was reported as "could not read the shipped
                // file", which is the wrong cause and sends whoever reads the log to the wrong file.
                string shippedText;

                try
                {
                    shippedText = File.ReadAllText(shipped);
                }
                catch (Exception e)
                {
                    Development.LogError($"could not read the shipped [{filename}] from [{shipped}]: "
                        + $"{e.Message}. The deployed copy was neither created nor patched.");
                    continue;
                }

                Apply(filename, shippedText);
            }
        }

        /// <summary>
        /// Fetches one shipped config file and applies it, for targets where the shipped file lives inside
        /// the package and cannot be opened with <c>File.*</c>.
        /// </summary>
        /// <remarks>
        /// <b>This path is inherently one launch behind</b> and cannot be otherwise: the earliest hook
        /// available is <c>BeforeSceneLoad</c>, a <c>UnityWebRequest</c> cannot complete without the update
        /// loop, and <c>Core.Awake</c> runs before the loop gets a frame. So on Android the first launch
        /// after an update reads the unpatched config and the patch applies from the second. Acceptable
        /// because <c>AppPaths.Config</c> falls back to the shipped file, so nothing is missing - only
        /// stale. Making startup wait on this is a larger change and is logged separately.
        /// </remarks>
        private static IEnumerator SeedAsync(string filename, string shipped)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(shipped))
            {
                // Generous for a read out of the local package, and load-bearing anyway: this runs before
                // the boot chain, which has no timeout of its own, so an unbounded wait here is a startup
                // that never finishes.
                www.timeout = PackageReadTimeoutSeconds;

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    // The old CopyFile had no else at all: a failed fetch left no file and said nothing,
                    // and the app then ran off whatever the resolution order found next. On a target where
                    // the shipped file is the only source, that is a silent boot failure.
                    Development.LogError($"could not read the shipped [{filename}] from [{shipped}]: "
                        + $"{www.error}. The deployed copy was neither created nor patched.");
                    yield break;
                }

                Apply(filename, www.downloadHandler.text);
            }
        }

        /// <summary>
        /// Writes the shipped text out if there is no deployed copy yet, or merges its keys into the
        /// deployed one if there is.
        /// </summary>
        private static void Apply(string filename, string shippedText)
        {
            string deployed = AppPaths.PersistentConfigFile(filename);

            if (!File.Exists(deployed))
            {
                // Gated on the write, not announced before it. WriteAtomic returns bool precisely so the
                // caller does not claim something it did not do, and this call site was discarding it.
                if (WriteAtomic(deployed, shippedText, "seeding the deployed config from the shipped one"))
                {
                    Development.Log($"Seeded {filename} into the local config folder.");

                    if (filename == appDataFilename)
                        StampSeededAppData(deployed);
                }

                return;
            }

            if (filename == appDataFilename)
            {
                Development.Log($"Local {filename} already exists; content files are not patched.");
                return;
            }

            PatchDeployed(deployed, shippedText);
        }

        /// <summary>
        /// Records the size and write time of a just-seeded <c>appData.json</c>, so a later boot can tell it
        /// apart from a cache.
        /// </summary>
        /// <remarks>
        /// Best effort by design, and deliberately not through <see cref="WriteAtomic"/>: a torn or missing
        /// stamp costs a diagnosis, while a failed seed costs the boot, and the two should not share a
        /// failure path. Everything downstream treats an absent or unreadable stamp as "cannot tell from the
        /// stamp" and falls back to comparing content, so this may fail as long as it says so.
        /// </remarks>
        private static void StampSeededAppData(string deployed)
        {
            try
            {
                FileInfo info = new FileInfo(deployed);

                File.WriteAllText(deployed + seedStampSuffix,
                    $"{info.Length}|{info.LastWriteTimeUtc.Ticks}");
            }
            catch (Exception e)
            {
                Development.LogError($"seeded {appDataFilename} but could not write its stamp "
                    + $"[{deployed + seedStampSuffix}]: {e.Message}. The app will fall back to comparing "
                    + "the file with the shipped copy, which stops working once a release edits it - so a "
                    + "placeholder may later be reported as a real cache.");
            }
        }

        /// <summary>
        /// Writes <paramref name="text"/> to <paramref name="path"/> without ever leaving a partial file
        /// in place.
        /// </summary>
        /// <remarks>
        /// The old copy did <c>File.WriteAllText</c> straight over the target, which truncates first - so
        /// an interruption left a zero-length or half-written settings.json that <c>File.Exists</c> still
        /// reports as present, and the resolution order then prefers it over the shipped file. That is the
        /// boot hang with no error attached. <c>File.Replace</c> needs an existing destination, so the
        /// first write is a <c>Move</c>; the same shape as the <c>.part</c> download swap.
        /// <para><c>internal</c> rather than <c>private</c> so <c>FileManager.SaveCoreSettings</c> can reuse it
        /// instead of carrying a second copy of the same seven lines. <c>ConfigManager</c> owns writing
        /// config files, so this is its home; the two other write-then-replace implementations in the
        /// framework are tracked rather than left as an aspiration in a comment.</para>
        /// </remarks>
        /// <returns>
        /// <c>false</c> if nothing was written. Returned rather than void because the caller logs success:
        /// a swallowed failure here produced a <c>LogError</c> and "Patched … with new keys" in the same
        /// boot, and the merged content survived only in the <c>.tmp</c>, which the next launch overwrites.
        /// </returns>
        /// <param name="context">
        /// What the caller was doing, in words an operator can act on, and <b>required for a reason</b>.
        /// <c>Development.LogError</c> attributes a message to the file the call sits in, so every failure
        /// through this shared method reads as a <c>ConfigManager</c> problem no matter who asked for the
        /// write - and the six callers are not equivalent. A discardable API response cache and a failed
        /// <c>appSettings.json</c> write produced identically shaped errors, with the caller's own framing
        /// ("best effort, it will be re-fetched") lost. That was the price of consolidating three
        /// write-then-replace copies into one, and this is the refund. Say what was being written and what
        /// it costs to lose - not the path, which is added here.
        /// </param>
        internal static bool WriteAtomic(string path, string text, string context)
        {
            string tempPath = path + ".tmp";

            try
            {
                File.WriteAllText(tempPath, text);

                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);

                return true;
            }
            catch (Exception e)
            {
                Development.LogError($"{context}: failed while writing [{path}]: {e.Message}. The "
                    + "existing file was not replaced.");

                // Best effort, and separate from the message above. A blocked File.Replace (AV, a backup
                // agent) otherwise left the whole merged config sitting in a .tmp that nothing ever reads
                // or cleans - an orphan copy of settings.json, next to settings.json, indefinitely.
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception cleanup)
                {
                    Development.LogError($"{context}: could not remove the temporary file "
                        + $"[{tempPath}]: {cleanup.Message}. Delete it by hand - it holds a full copy of "
                        + "the file that was being written.");
                }

                return false;
            }
        }

        /// <summary>
        /// Brings an operator's deployed config up to date with the shipped one: any key the shipped
        /// file has and theirs does not is added, and every value they did set is preserved.
        ///
        /// Merged as JSON rather than as deserialized objects, which is what the old
        /// <c>MergeWithDefaults</c> got wrong twice. It walked <c>GetProperties()</c>, and every
        /// settings DTO is public *fields* by convention, so the loop body never executed once and the
        /// whole feature was a silent no-op. Walking fields instead would not have fixed it:
        /// reflection over a deserialized object cannot tell "this key was absent from the operator's
        /// file" from "this key was present and happens to equal the C# field initializer", and telling
        /// those apart is the entire point - the shipped settings.json value has to win for an absent
        /// key, not the initializer. It was also non-recursive, so nested blocks were never patched.
        ///
        /// Dropping the type parameter also removed <c>LoadAndPatchJSON&lt;AppSettings&gt;</c>, one of
        /// the framework-to-App references blocking the .asmdef split.
        /// </summary>
        /// <param name="shippedText">
        /// The shipped file's contents, already fetched. Takes text rather than a path because on Android
        /// the shipped file is inside the APK and there is no path <c>File.ReadAllText</c> can open.
        /// </param>
        private static void PatchDeployed(string deployedPath, string shippedText)
        {
            Development.Log("Patching " + Path.GetFileName(deployedPath) + "!");

            JObject merged;

            try
            {
                // Start from the shipped file so its keys are the baseline, then overlay the
                // operator's on top so anything they set wins.
                merged = ParseJson(shippedText);
                JObject deployed = ParseJson(File.ReadAllText(deployedPath));

                merged.Merge(deployed, new JsonMergeSettings
                {
                    // Replace, not Concat: an operator who deleted an entry from an array meant to
                    // delete it. Concat would silently put it back on every launch. The cost is that a
                    // key newly added *inside* an array element never reaches a deployed config -
                    // Phidgets.phidgets[], SerialPorts.serialPorts[] and Api.ConnectCommands[] are all
                    // arrays of objects, so those get new keys only on a fresh install.
                    MergeArrayHandling = MergeArrayHandling.Replace,

                    // Merge, not the default Ignore: an explicit null in the operator's file is a
                    // decision they made, and Ignore would silently overwrite it with the shipped value
                    // and then erase the null when the file is rewritten. The code this replaced set
                    // NullValueHandling.Include with the comment "critical for seeing explicit nulls",
                    // so that intent was dropped rather than decided.
                    MergeNullValueHandling = MergeNullValueHandling.Merge
                });

                if (JToken.DeepEquals(merged, deployed))
                {
                    // Nothing to add. Returning here is not an optimisation: the write below replaces
                    // the site's only settings.json, so doing it on every boot when nothing changed is
                    // a data-loss window (power cut, AV lock, backup agent) opened for no reason.
                    Development.Log(Path.GetFileName(deployedPath) + " is already up to date.");
                    return;
                }
            }
            catch (Exception e)
            {
                // This runs from a coroutine started before the boot chain, so an escaping exception
                // would be swallowed by Unity with only a stack trace. A malformed deployed config is
                // exactly the case this method exists for - leave it untouched and let the normal
                // settings parse report it. Nothing has been written at this point, so the file really
                // is unchanged.
                Development.LogError($"Could not patch [{deployedPath}] against the shipped "
                    + $"defaults: {e.Message}. The deployed file was left unchanged.");
                return;
            }

            // Separate call, and its own message: a failure in the write may have left something behind.
            // Claiming "left unchanged" from a catch that wraps the write is how a truncated config gets
            // blamed on the operator at the next boot. And the success line is gated on the write actually
            // happening - it used to be unconditional, so a blocked File.Replace logged the failure and the
            // success together.
            if (WriteAtomic(deployedPath, merged.ToString(Formatting.Indented),
            "adding newly shipped keys to the operator's config"))
                Development.Log("Patched " + Path.GetFileName(deployedPath) + " with new keys from the shipped defaults.");
        }

        /// <summary>
        /// Reads JSON as text without letting Json.NET reinterpret any of it. <c>JObject.Parse</c>
        /// defaults to <c>DateParseHandling.DateTime</c>, so a string value that merely looks like a
        /// date - "2026-08-10" - is parsed into a <c>DateTime</c> and written back in a different
        /// format. Nothing in the shipped template trips it, but this is framework code every branched
        /// project inherits, and a silently rewritten setting is the worst kind of config bug.
        /// </summary>
        internal static JObject ParseJson(string json)
        {
            using (JsonTextReader reader = new JsonTextReader(new StringReader(json)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                return JObject.Load(reader);
            }
        }
    }

    public static class ConfigManagerHelper
    {
        // The helper MonoBehaviour that will be used to start coroutines
        public class StartCoroutineHelper : MonoBehaviour
        {
            private static StartCoroutineHelper _instance;

            public static StartCoroutineHelper Instance
            {
                get
                {
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("StartCoroutineHelper");
                        _instance = go.AddComponent<StartCoroutineHelper>();
                        DontDestroyOnLoad(go);
                    }
                    return _instance;
                }
            }
        }
    }
}
