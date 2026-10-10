#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using EnigmaVirtual;
using UnityEngine;

/// <summary>Runs the existing downloader against a loopback fixture without saving deployment settings.</summary>
public class TimelineMediaDownloadCheck : MonoBehaviour
{
    public static string Result { get; private set; } = "Not run";
    public static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "TimelineMediaDownloads"));
    private static TimelineMediaDownloadCheck _running;
    private CoreSettings _previous;
    private MethodInfo _setter;
    private AssetManager _manager;
    private bool _complete;
    private bool _restored;

    [UnityEditor.MenuItem("Gema/Timeline Scaling/Run Local Download Check")]
    private static void RunMenu() { UnityEngine.Debug.Log(Begin()); }

    public static string Begin(string host = "http://127.0.0.1:8765/")
    {
        if (!Application.isPlaying || Core.main == null || AppData.Current == null)
            throw new InvalidOperationException("Enter Play and wait for content first.");
        if (_running != null)
            throw new InvalidOperationException("A download check is already running.");
        Uri uri = new Uri(host);
        if (!uri.IsLoopback || uri.Scheme != "http" || uri.AbsolutePath != "/")
            throw new ArgumentException("This check accepts only a loopback HTTP root.");
        _running = new GameObject("Timeline media download check").AddComponent<TimelineMediaDownloadCheck>();
        _running.StartCoroutine(_running.Run(uri.AbsoluteUri));
        return Result;
    }

    private IEnumerator Run(string host)
    {
        Result = "RUNNING";
        _previous = Core.settings;
        _setter = typeof(Core).GetProperty("settings").GetSetMethod(true);
        CoreSettings test = JsonUtility.FromJson<CoreSettings>(JsonUtility.ToJson(_previous));
        test.AppData.useLocalAppData = true;
        test.AppData.localSettings.usePersistentDataPath = false;
        // Keep test downloads outside Assets so the Editor does not import each arriving file.
        test.AppData.localSettings.assetsPath = "../../Library/TimelineMediaDownloads/";
        test.AppData.remoteSettings.hostUrl = host;
        test.AppData.remoteSettings.assetsPath = "assets/";
        test.AppData.Cache.AssetDownload.isActive = true;
        test.AppData.Cache.AssetDownload.cleanUnlistedAssets = false;
        test.AppData.Cache.AssetDownload.downloadThreads = 4;
        test.AppData.Cache.AssetDownload.timeout = 3;
        test.AppData.Cache.TextureCache.isActive = false;
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            _setter.Invoke(null, new object[] { test });
            _manager = gameObject.AddComponent<AssetManager>();
            _manager.onAssetCleaningCompleteEvent += Completed;
            _manager.ValidateAssets();
            while (!_complete && watch.Elapsed.TotalSeconds < 180)
                yield return null;
            if (!_complete)
                throw new TimeoutException("Media download check exceeded 180 seconds.");

            int found = 0, matching = 0, total = 0;
            long bytes = 0;
            HashSet<string> urls = new HashSet<string>(StringComparer.Ordinal);
            using (SHA256 sha = SHA256.Create())
            {
                foreach (TimelineItemVO item in AppData.Current.timeline)
                {
                    if (item?.images == null) continue;
                    foreach (MediaVO image in item.images)
                    {
                        if (image == null || !urls.Add(image.url)) continue;
                        total++;
                        string target = Contained(Root, image.url);
                        string source = Contained(Path.Combine(Application.streamingAssetsPath, "assets"), image.url);
                        if (!File.Exists(target)) continue;
                        found++;
                        bytes += new FileInfo(target).Length;
                        if (File.Exists(source)
                            && Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(target)))
                            == Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(source)))) matching++;
                    }
                }
            }
            Result = $"COMPLETE seconds={watch.Elapsed.TotalSeconds:F2} requested={total} files={found} "
                + $"sourceHashMatches={matching} bytes={bytes} missing={_manager.MissingAssetCount} "
                + $"refused={_manager.RefusedAssetCount}";
        }
        finally
        {
            Restore();
            if (Result == "RUNNING") Result = "FAIL verification did not produce a report";
            Result += "; settingsRestored=" + ReferenceEquals(Core.settings, _previous);
            if (!_complete) Result = "FAIL " + Result;
            UnityEngine.Debug.Log("Timeline media download check: " + Result);
            _running = null;
            Destroy(gameObject);
        }
    }

    private static string Contained(string root, string relative)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Media test path escapes its root.");
        return full;
    }

    private void Completed() { _complete = true; }
    private void Restore()
    {
        if (_restored) return;
        _restored = true;
        if (_setter != null && _previous != null) _setter.Invoke(null, new object[] { _previous });
        if (_manager != null) _manager.onAssetCleaningCompleteEvent -= Completed;
    }
    private void OnDisable() { Restore(); }
}
#endif
