using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace EnigmaVirtual
{
    public class LoadJsonEvent : UnityEvent<string, string>
    {
        private int _listenerCount;

        public int ListenerCount { get { return _listenerCount; } }

        public LoadJsonEvent()
        {
            _listenerCount = 0;
        }

        public new void AddListener(UnityAction<string, string> call)
        {
            base.AddListener(call);
            _listenerCount++;
        }

        public new void RemoveListener(UnityAction<string, string> call)
        {
            base.RemoveListener(call);
            _listenerCount--;
        }

    }

    public class FileManager
    {
        public LoadJsonEvent OnLoadJson = new LoadJsonEvent();

        // Held on every platform now, not just Android/WebGL. Whether a coroutine is needed depends on
        // the path being fetched, not on the target - see LoadStreamingFile.
        private MonoBehaviour _behaviour; // used to start coroutines

        public FileManager(MonoBehaviour behaviour)
        {
            _behaviour = behaviour;
        }

        public string CoreSettings()
        {
            // One shared predicate instead of this method's own #if spelling. It used to differ from
            // ConfigManager's by more than formatting, and the difference mattered: this looked for a
            // deployed config on every non-WebGL player while only a Windows player ever wrote one.
            if (AppPaths.UsesDeployedConfig && File.Exists(AppPaths.PersistentConfigFile(SettingsFileName)))
                return LoadStreamingFile(AppPaths.PersistentConfig, SettingsFileName);

            return LoadStreamingFile(AppPaths.StreamingConfig, SettingsFileName);
        }

        /// <summary>
        /// Writes the live <see cref="Core.settings"/> to the operator's config folder - the write half of
        /// <see cref="CoreSettings"/>.
        /// </summary>
        /// <returns><c>false</c> if nothing was written; the reason is already logged.</returns>
        /// <remarks>
        /// <para>Lives here, beside the reader of the same file and sharing its
        /// <see cref="SettingsFileName"/>, rather than on the settings panel where the code used to be.
        /// It was a per-field <c>SettingInput</c> MonoBehaviour writing the whole file, and
        /// A UI component owning whole-file IO is the wrong home, and moving it was only free while
        /// the method was new.</para>
        ///
        /// <para><b>The persistent folder, not StreamingAssets.</b> The panel wrote
        /// <c>Application.streamingAssetsPath + "/settings.json"</c> - the install directory, and without
        /// the <c>config/</c> segment every reader uses, so it produced a file nothing ever opened. That
        /// root is wrong on every target: read-only under Program Files on a normal Windows deployment,
        /// and even where writable <see cref="CoreSettings"/> prefers the persistent copy, so the edit was
        /// shadowed by the seeded file and appeared to do nothing. In the editor it wrote into
        /// <c>Assets/StreamingAssets/</c> - an untracked settings.json beside the config folder, carrying
        /// the panel password, which is copied verbatim into the next build.</para>
        ///
        /// <para><b>In the editor this will not take effect</b>, because
        /// <see cref="AppPaths.UsesDeployedConfig"/> is false there and <see cref="CoreSettings"/> reads
        /// the repo's copy. Same asymmetry <c>AppDataController.SaveAppSettings</c> has. Writing to the
        /// repo instead is not the alternative.</para>
        ///
        /// <para><b>Merged into what is on disk, not written over it</b>, and that is not caution for its
        /// own sake. <c>JsonUtility.ToJson</c> emits only what the C# model declares, and the model was
        /// demonstrably incomplete when this was written: <c>SystemSettings.Android</c> did not match the
        /// <c>AndroidSettings</c> key so that block was never read and would have been replaced by
        /// defaults, and <c>OneCoreCommand.command_data</c>/<c>event_data</c> are
        /// <c>object</c>, which Unity's serializer skips entirely. Overwriting would
        /// delete such keys from an operator's config, which did not matter while this wrote to a path
        /// nothing read and matters a great deal now that it writes the file the app boots from.
        /// Merging means the next such gap does not have to be found
        /// first, which is the point.</para>
        ///
        /// <para><b>Arrays are still replaced wholesale</b>, the same limit
        /// <c>ConfigManager.PatchDeployed</c> documents, so a key the model cannot see <i>inside</i> an
        /// array element would be lost - and <c>command_data</c> was exactly that case. It is handled
        /// rather than tolerated now: <see cref="SettingsAsJObject"/> re-emits the OneCore block with
        /// Newtonsoft so the replacing array carries the payloads. Any <b>other</b> model-invisible key
        /// inside an array element is still lost, and would need the same treatment. Element-wise
        /// merging remains the wrong general answer: it would resurrect entries the panel had shortened
        /// out of a list.</para>
        /// </remarks>
        /// <summary>
        /// The live <see cref="Core.settings"/> as JSON, with the <c>OneCore</c> block emitted by
        /// Newtonsoft so its command payloads survive. <b>Every writer of the settings must use this,
        /// not <c>JsonUtility.ToJson</c> directly.</b>
        /// </summary>
        /// <remarks>
        /// <para><b>Why a shared emitter and not two call sites.</b> There are exactly two writers -
        /// this file's <see cref="SaveCoreSettings"/> and the settings panel's
        /// <c>PlayerPrefs["Settings"]</c> snapshot - and they must agree, because <c>Core.Awake</c>
        /// <b>prefers the snapshot over the file</b>. Fixing only one leaves the other silently
        /// authoritative: on Android and iOS the prefs toggle is the panel's only writer, so a single
        /// Save would have written a snapshot with no <c>command_data</c> in it and permanently shadowed
        /// the file that had one.</para>
        ///
        /// <para><b>What it protects.</b> <c>OneCoreCommand.command_data</c>/<c>event_data</c> are
        /// <c>object</c>, which <c>JsonUtility</c> cannot emit - and they live <i>inside an array
        /// element</i>, so the <c>MergeArrayHandling.Replace</c> below cannot preserve them either: the
        /// whole <c>ConnectCommands</c> array is swapped for the one without them. Measured: without
        /// this, an operator's payload is deleted from their own file by the next Save.</para>
        ///
        /// <para>Falls back to the plain <c>JsonUtility</c> output if the OneCore re-emit fails, because
        /// a settings save that writes slightly less is better than one that writes nothing.</para>
        /// </remarks>
        internal static JObject SettingsAsJObject()
        {
            JObject root = ConfigManager.ParseJson(JsonUtility.ToJson(Core.settings));

            if (Core.settings?.OneCore == null)
                return root;

            try
            {
                root["OneCore"] = ConfigManager.ParseJson(
                    JsonConvert.SerializeObject(Core.settings.OneCore));
            }
            catch (Exception e)
            {
                Development.LogError("the OneCore block could not be re-emitted for saving: "
                    + $"{e.Message}. Everything else is written normally, but any command_data or "
                    + "event_data payload will be missing from what is saved - check it against the "
                    + "file before relying on it.");
            }

            return root;
        }

        /// <summary><see cref="SettingsAsJObject"/> as a compact string, for the PlayerPrefs snapshot.</summary>
        internal static string SettingsAsJson()
        {
            return SettingsAsJObject().ToString(Formatting.None);
        }

        public bool SaveCoreSettings()
        {
            if (Core.settings == null)
            {
                Development.LogError("settings were not saved: Core.settings is null, so there is nothing "
                    + "to write.");
                return false;
            }

            try
            {
                AppPaths.EnsurePersistentConfig();
            }
            catch (Exception e)
            {
                Development.LogError("settings were not saved: could not create "
                    + $"[{AppPaths.PersistentConfig}]: {e.Message}");
                return false;
            }

            string path = AppPaths.PersistentConfigFile(SettingsFileName);
            string merged;

            try
            {
                // On-disk as the base, the live settings overlaid: the panel's values win, and any key the
                // C# model cannot see survives. ConfigManager.ParseJson rather than JObject.Parse, because
                // the latter defaults to DateParseHandling.DateTime and would silently reformat any
                // date-shaped string value.
                JObject onDisk = File.Exists(path)
                    ? ConfigManager.ParseJson(File.ReadAllText(path))
                    : new JObject();

                onDisk.Merge(SettingsAsJObject(), new JsonMergeSettings
                {
                    MergeArrayHandling = MergeArrayHandling.Replace,
                    MergeNullValueHandling = MergeNullValueHandling.Merge
                });

                merged = onDisk.ToString(Formatting.Indented);
            }
            catch (Exception e)
            {
                // Nothing is written at this point, so the operator's file really is untouched.
                Development.LogError($"settings were not saved: could not merge into [{path}]: "
                    + $"{e.Message}. The existing file was left unchanged - if it is malformed, fix it "
                    + "there first.");
                return false;
            }

            // ConfigManager.WriteAtomic, not a bare WriteAllText. This replaces the file the app cannot
            // boot without, so a truncating write interrupted halfway leaves a settings.json that parses
            // as nothing and a machine that sits on the preloader. Indented because operators edit
            // this file by hand - the old call put the whole thing on one line.
            if (!ConfigManager.WriteAtomic(path, merged,
                "saving settings.json (SaveCoreSettings)"))
                return false;

            // Written, but say what it will and will not do. In the editor and on WebGL
            // UsesDeployedConfig is false, so CoreSettings() above reads the shipped copy and this save
            // has no effect on the next play - and unlike AppPaths.Config, which logs when it ignores a
            // deployed copy, nothing here would have said so. The second sentence is the one that
            // matters: the editor and a local player share persistentDataPath, so this file silently
            // becomes the config any locally built player prefers.
            if (!AppPaths.UsesDeployedConfig)
            {
                Development.LogError($"settings.json was written to [{path}], but this build reads the "
                    + "SHIPPED config, so the change will not take effect here. It WILL be preferred by "
                    + "any player built on this machine, which shares the same persistentDataPath - "
                    + "delete that file if you did not mean that. Define DEBUG_MODE to read the deployed "
                    + "copy in-editor.");
            }

            return true;
        }

        private const string SettingsFileName = "settings.json";

        /// <summary>Cap on a fetched config read - see the note at the request itself.</summary>
        private const int ReadTimeoutSeconds = 10;

        public IEnumerator LoadJsonWeb(string fileFolder, string fileName)
        {
            string fileText = string.Empty;

            // '/' between the two, which was missing: AppPaths.StreamingConfig has no trailing separator,
            // so `fileFolder + fileName` requested ".../configsettings.json" and every read on this
            // branch failed. That is the *first* launch on Android - before any seeded copy exists -
            // so seeding would work from launch two onwards and still hang on launch one, with
            // only a raw www.error in the log to explain it. Joined with '/' rather than Path.Combine
            // because this is a URL: Path.Combine would insert a backslash on Windows.
            string url = fileFolder.TrimEnd('/', '\\') + "/" + fileName;

            if (fileFolder.Contains("://"))
            {
                //if we're on the web, we have to wait until the WWW returns the real file path
                using (UnityWebRequest www = UnityWebRequest.Get(url))
                {
                    // This feeds the settings parse and the boot chain has no timeout of its own, so an
                    // unbounded wait here is a startup that never finishes. Generous for a read out of
                    // the local package - and this method is public, so a derived project pointing it at
                    // a remote folder is the case that really needs the cap.
                    www.timeout = ReadTimeoutSeconds;

                    yield return www.SendWebRequest();

                    if (www.result != UnityWebRequest.Result.Success)
                    {
                        // Named, because a bare www.error ("Cannot connect to destination host") in a
                        // log with no URL next to it is unactionable - and this failure means an empty
                        // settings.json, which means Core.settings null and the preloader hang.
                        Development.LogError($"could not fetch [{url}]: {www.error}. The file will be "
                            + "reported as empty, which will stall the boot chain.");
                    }
                    else
                        fileText = www.downloadHandler.text;
                }
            }
            else
            {
                Development.LogError($"[{url}] is not a URL, so it cannot be fetched. This method is "
                    + "only for package-internal paths; a real file should have been read directly.");
            }

            if (OnLoadJson.ListenerCount == 0)
                Development.Log("Warning!You need to addlistener to OnLoadJson to get the file string!");

            OnLoadJson?.Invoke(fileName, fileText);
        }

        /// <summary>
        /// Reads a config file, fetching it if the folder is a URL and reading it directly if not.
        /// </summary>
        /// <returns>
        /// <c>null</c> when a fetch was started, in which case the text arrives through
        /// <see cref="OnLoadJson"/> once the coroutine finishes. Otherwise whatever was read, which is
        /// <b>empty</b> when no text was obtained: the file was missing or unreadable - both logged
        /// here - or it exists and is genuinely empty, which is <b>not</b> logged here, because
        /// nothing failed. A caller distinguishes the three by the return value, never by the platform:
        /// <c>null</c> means wait, empty means there are no contents and no event is coming, non-empty
        /// means go.
        /// </returns>
        /// <remarks>
        /// <para><b>Routed on the path, not on the platform.</b> This used to be
        /// <c>#if !UNITY_EDITOR &amp;&amp; (UNITY_ANDROID || UNITY_WEBGL)</c>, which reads as
        /// "StreamingAssets lives inside the package on those targets" - true, but it applied to
        /// <i>every</i> folder handed in, including <c>persistentDataPath</c>, which is an ordinary
        /// readable file on Android.</para>
        ///
        /// <para>That only stayed hidden because <c>ConfigManager</c> was itself <c>#if</c>'d out off
        /// Windows, so Android never had a deployed config to read and always took the
        /// StreamingAssets branch. Config seeding without this would send a freshly seeded
        /// <c>settings.json</c> to <see cref="LoadJsonWeb"/>, which rejects anything without
        /// <c>"://"</c> and returns an empty string - and an empty settings.json means
        /// <c>Core.settings</c> is null, <c>CoreSetupComplete()</c> never runs, and the app sits on
        /// the preloader with nothing surfaced.</para>
        ///
        /// <para><b>The delivery is routed on the path too, and that is what makes the boot order the
        /// same everywhere.</b> A second <c>#if</c> used to sit at the end of this method: on Android
        /// and WebGL players it raised <see cref="OnLoadJson"/> with the text it had just read and
        /// returned an empty string, because <c>Core</c>'s branch for those targets subscribed to the
        /// event and discarded the return value. The consequence was two boot orders on one platform,
        /// picked by whether a deployed config happened to exist yet: a first Android launch read
        /// StreamingAssets (a <c>jar://</c> URL, so genuinely asynchronous) while a second launch read
        /// the seeded file and delivered it synchronously *through the event*, from inside
        /// <c>Core.Awake</c>. Returning the text unconditionally leaves one rule - a non-empty return
        /// means the settings are already in hand - and the asynchronous path is the one case that
        /// needs the event.</para>
        /// </remarks>
        string LoadStreamingFile(string fileFolder, string filename)
        {
            if (fileFolder.Contains("://"))
            {
                // null, not "": the caller has to tell "the text is coming through the event" apart from
                // "a read was attempted and produced nothing". Both of the failure exits below leave
                // `text` empty, and treating that as "wait for the event" would wait forever.
                _behaviour.StartCoroutine(LoadJsonWeb(fileFolder, filename));
                return null;
            }

            // File.ReadAllText replaces three defects at once: the StreamReader from
            // OpenText() was never disposed, so the handle stayed open and a later write to
            // settings.json failed with a sharing violation; the `fileText +=` loop was O(n^2); and
            // ReadLine silently dropped every newline, so the returned text was not the file.
            string path = Path.Combine(fileFolder, filename);
            string text = "";

            if (!File.Exists(path))
            {
                Development.LogError(path + " :: not found or unreadable");
            }
            else
            {
                try
                {
                    text = File.ReadAllText(path);
                }
                catch (Exception e)
                {
                    // This feeds the settings parse, which runs before anything else. An exception
                    // escaping here aborts Core.Awake, Core.IsSet never becomes true, and the app sits
                    // on the preloader with nothing surfaced.
                    Development.LogError($"Could not read [{path}]: {e.Message}");
                }
            }

            return text;
        }

    }
}

