using System;
using System.IO;
using System.Text.RegularExpressions;
using EnigmaVirtual;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Propagates the semver in the repo-root VERSION.txt to everything that shows or stamps a
/// version, on every domain reload. VERSION.txt is the single source of truth:
///
///   VERSION.txt  ->  PlayerSettings.bundleVersion   (binary ProjectSettings.asset, undiffable)
///                ->  Descriptor.asset .Version      (shown by AppInfo, sent to analytics,
///                                                    stamps the build folder and exe name)
///
/// Before this, the Descriptor kept its own version with its own increment buttons, so there were
/// two competing truths - and they had drifted: VERSION.txt said 0.5.1 while the descriptor (the
/// one an operator actually sees on the splash screen) still said 1.0.0.
/// </summary>
[InitializeOnLoad]
public static class AppVersionSync
{
    private const string VersionFileName = "VERSION.txt";

    /// <summary>
    /// The descriptor's AssetDatabase path. Project-relative and forward-slash — the backslash form
    /// this codebase used in four places is undocumented and not portable off Windows. Shared so
    /// there is one spelling rather than five.
    /// </summary>
    internal const string DescriptorPath = "Assets/Resources/Descriptor.asset";

    private static readonly Regex SemVer = new Regex(@"^\d+\.\d+\.\d+$");

    // delayCall, not a direct call: the AssetDatabase is not necessarily ready during the
    // static constructor, and Sync() saves through it.
    static AppVersionSync() => EditorApplication.delayCall += Sync;

    /// <summary>Absolute path to VERSION.txt. dataPath is &lt;repo&gt;/Assets, so it sits one level up.</summary>
    private static string VersionFilePath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, VersionFileName);

    /// <summary>The version in VERSION.txt, or null if it is missing, unreadable or malformed.</summary>
    internal static string Read()
    {
        string path = VersionFilePath;

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[AppVersionSync] {VersionFileName} not found at {path}.");
            return null;
        }

        string version;
        try
        {
            version = File.ReadAllText(path).Trim();
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // UnauthorizedAccessException is not an IOException: a read-only VERSION.txt or a
            // version-control lock would otherwise escape out of EditorApplication.delayCall.
            Debug.LogWarning($"[AppVersionSync] Could not read {VersionFileName}: {e.Message}");
            return null;
        }

        if (!SemVer.IsMatch(version))
        {
            Debug.LogError($"[AppVersionSync] '{version}' is not Major.Minor.Patch.");
            return null;
        }

        return version;
    }

    /// <summary>
    /// Writes a new version to VERSION.txt and propagates it. This is the only correct way to
    /// change the app version - editing the descriptor or Player Settings directly re-creates the
    /// drift this class exists to prevent.
    /// </summary>
    internal static bool Write(string version)
    {
        if (version == null || !SemVer.IsMatch(version))
        {
            Debug.LogError($"[AppVersionSync] Refusing to write '{version}' - not Major.Minor.Patch.");
            return false;
        }

        try
        {
            File.WriteAllText(VersionFilePath, version + "\n");
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Debug.LogError($"[AppVersionSync] Could not write {VersionFileName}: {e.Message}");
            return false;
        }

        Sync();
        return true;
    }

    [MenuItem("Gema/Sync App Version")]
    internal static void Sync()
    {
        string version = Read();
        if (version == null)
            return;

        bool changed = false;

        if (PlayerSettings.bundleVersion != version)
        {
            Debug.Log($"[AppVersionSync] bundleVersion {PlayerSettings.bundleVersion} -> {version}");
            PlayerSettings.bundleVersion = version;
            changed = true;
        }

        ApplicationDescription descriptor =
            AssetDatabase.LoadAssetAtPath<ApplicationDescription>(DescriptorPath);

        if (descriptor == null)
        {
            // Not an error: a fresh project may not have one yet. Gema > App Descriptor creates it.
            Debug.LogWarning($"[AppVersionSync] No descriptor at {DescriptorPath} - version not applied to it.");
        }
        else if (descriptor.Version != version)
        {
            Debug.Log($"[AppVersionSync] Descriptor.Version {descriptor.Version} -> {version}");
            descriptor.Version = version;
            EditorUtility.SetDirty(descriptor);
            changed = true;
        }

        // Setting PlayerSettings or a ScriptableObject field only changes it in memory - Unity
        // flushes to disk on an explicit save or on quit. Without this, VERSION.txt and the
        // committed bundleVersion drift apart silently: it sat at 0.2.0 through three bumps to 0.5.0.
        if (changed)
            AssetDatabase.SaveAssets();
    }
}
