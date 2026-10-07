#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnigmaVirtual;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Refuses builds that would ship a known-bad state.
///
/// Every rule here exists because the thing it checks actually happened, not because it seemed
/// prudent. Most failure messages name their issue id, so the register explains the history.
///
/// All rules run and are reported together rather than throwing on the first. Failing one at a
/// time means an operator fixes a problem, waits for a build, and discovers the next one - and a
/// build is not a fast feedback loop.
/// </summary>
public class BuildValidator : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    /// <summary>Config that ships inside the player. Anything here reaches the client site.</summary>
    private const string ConfigFolder = "Assets/StreamingAssets/config";
    private const string SettingsFile = ConfigFolder + "/settings.json";

    /// <summary>
    /// The password the template ships with. A build still carrying it means the settings panel
    /// on that site opens for anyone who has ever seen this repo.
    /// </summary>
    private const string DefaultPassword = "gema";

    /// <summary>
    /// Name fragments that mean "somebody's working copy". These are the real risk in the config
    /// folder: a backup of settings.json carries the same password, key and client hostname as the
    /// original, and StreamingAssets is copied verbatim into the player - so it travels to the site.
    /// Matching on backup-shaped names rather than an allowlist, so a derived project can add its
    /// own legitimate config file without editing this list.
    /// </summary>
    private static readonly string[] BackupNameFragments = { "_bck", "_bak", ".bak", ".orig", "copy", "backup", "~" };

    public void OnPreprocessBuild(BuildReport report)
    {
        // A development build is for testing, and blocking one costs more than it protects: the
        // template ships the default password, so a hard gate on it would stop this base project
        // building at all. Rules that describe a *deployment* risk are therefore release-only;
        // rules where a dev build is just as dangerous are not.
        bool isRelease = !report.summary.options.HasFlag(BuildOptions.Development);

        List<string> failures = new List<string>();

        // Separate channel, and deliberately not merged into `failures`. A schema mismatch is a real
        // defect but it cannot be a gate: legitimately model-invisible keys exist today, so a
        // hard failure would ship with a suppression list on day one, and that list is the
        // maintenance tail that eventually gets the whole rule deleted.
        List<string> warnings = new List<string>();

        // Always: DEBUG_MODE changes behaviour, and a backup file leaks credentials from any build.
        CheckDebugMode(failures);
        CheckForStrayConfigFiles(failures);

        // Always: a Dynamic font asset with an unreachable source renders text as spaces, in any build.
        CheckDynamicFontSources(failures);

        // Always, and warn-only. Not release-only: a key the model cannot receive is just as dead in
        // a development build, and this is the one check that catches a key no field can receive.
        CheckSettingsSchema(warnings);
        CheckPanelPassword(warnings);

        if (isRelease)
        {
            CheckVersionConsistency(failures);
            CheckShippedSettings(failures, warnings);
        }
        else
        {
            // Say so. A gate that silently does not run is worth very little the day it matters,
            // and this codebase has enough of those already.
            Debug.LogWarning("[BuildValidator] Development build: skipped the release-only checks "
                + "(version consistency, plaintext and default-hashed panel passwords, and the cleanUnlistedAssets warning). Do a non-development "
                + "build before shipping to a site.");
        }

        // Warnings first, so they survive a failing build. Reported after the throw they would be
        // invisible in exactly the case where someone is already looking at this output.
        foreach (string warning in warnings)
            Debug.LogWarning("[BuildValidator] " + warning);

        if (failures.Count == 0)
            return;

        string message = $"BUILD FAILED: {failures.Count} problem(s) that would ship to a site.\n\n"
            + string.Join("\n\n", failures.Select((f, i) => $"  {i + 1}. {f}"));

        throw new BuildFailedException(message);
    }

    /// <summary>
    /// <c>DEBUG_MODE</c> changes behaviour, not just logging - it is one of the two terms in
    /// <c>AppPaths.UsesDeployedConfig</c>, so it switches the entire config-seeding path on - and a
    /// build carrying it is therefore not the build that was tested. It is a per-developer
    /// working-tree toggle and must never reach a player. (It used to be named here as
    /// <c>ConfigManager</c> and <c>FileManager</c> branching on it directly; both went through the
    /// shared predicate in v0.16.0.)
    /// </summary>
    private static void CheckDebugMode(List<string> failures)
    {
        NamedBuildTarget namedTarget = NamedBuildTarget.FromBuildTargetGroup(
            EditorUserBuildSettings.selectedBuildTargetGroup);

        PlayerSettings.GetScriptingDefineSymbols(namedTarget, out string[] defines);

        if (System.Array.Exists(defines, d => d == "DEBUG_MODE"))
        {
            failures.Add("DEBUG_MODE is defined for " + namedTarget.TargetName + ". It changes behaviour, "
                + "not just logging, so this would not be the build you tested. Remove it from Player "
                + "Settings > Scripting Define Symbols. (M18)");
        }
    }

    /// <summary>
    /// VERSION.txt is the single source of truth, but <c>AppVersionSync</c> only propagates it on a
    /// domain reload - which happens *after* a bump is made and committed. Measured across the
    /// version commits of 2026-08-10, <c>bundleVersion</c> and <c>Descriptor.Version</c> trailed
    /// VERSION.txt in every single one and matched in none. A build from a fresh checkout, made
    /// before the Editor has reloaded, therefore stamps the wrong version on the executable name,
    /// the splash screen and the analytics payload.
    /// </summary>
    private static void CheckVersionConsistency(List<string> failures)
    {
        string version = AppVersionSync.Read();

        if (string.IsNullOrEmpty(version))
        {
            failures.Add("VERSION.txt is missing or unreadable at the repo root, so there is no version "
                + "to stamp this build with. (M33)");
            return;
        }

        List<string> drifted = new List<string>();

        if (PlayerSettings.bundleVersion != version)
            drifted.Add($"PlayerSettings.bundleVersion is [{PlayerSettings.bundleVersion}]");

        ApplicationDescription descriptor =
            AssetDatabase.LoadAssetAtPath<ApplicationDescription>(AppVersionSync.DescriptorPath);

        // A missing descriptor is not a failure here - AppVersionSync treats it as optional, and
        // Gema > App Descriptor creates one. Only a descriptor that exists and disagrees is a problem.
        if (descriptor != null && descriptor.Version != version)
            drifted.Add($"Descriptor.Version is [{descriptor.Version}]");

        if (drifted.Count > 0)
        {
            failures.Add($"VERSION.txt says [{version}] but {string.Join(" and ", drifted)}. The build "
                + "would be stamped with the wrong version - it names the output folder and executable, "
                + "and it is what the splash screen and analytics report. Run Gema > Sync App Version, "
                + "then build again. (M33)");
        }
    }

    /// <summary>
    /// Checks the settings.json that will be copied into the player. Both rules here are about a
    /// value being *shipped*, not about the value being wrong to use - an operator may absolutely
    /// enable either one at a site, deliberately, on the deployed config.
    /// </summary>
    private static void CheckShippedSettings(List<string> failures, List<string> warnings)
    {
        if (!File.Exists(SettingsFile))
        {
            failures.Add($"{SettingsFile} is missing, so this build would ship with no framework "
                + "config at all and would not get past the settings parse.");
            return;
        }

        JObject settings;

        try
        {
            settings = JObject.Parse(File.ReadAllText(SettingsFile));
        }
        catch (System.Exception e)
        {
            // Worth failing on: a settings.json that cannot be parsed here cannot be parsed at
            // runtime either: the app sits on the preloader forever.
            failures.Add($"{SettingsFile} is not valid JSON ({e.Message}), so the build would ship a "
                + "config the app cannot read. (C3)");
            return;
        }

        string password = (string)settings.SelectToken("EditorSettings.password");
        string passwordHash = (string)settings.SelectToken("EditorSettings.passwordHash");

        if (password == DefaultPassword)
        {
            failures.Add($"{SettingsFile} still has the default settings-panel password. Change it "
                + "before shipping to a client - it is in this repo and in every project branched "
                + "from it. (H14)");
        }
        else if (!string.IsNullOrEmpty(password))
        {
            // Any plaintext, not just the default one: this file ships inside the player, so the value
            // is readable by anyone who can unzip a build. Does not newly break this template, which
            // already fails the check above.
            failures.Add($"{SettingsFile} ships a plaintext settings-panel password in "
                + "EditorSettings.password. Move it to EditorSettings.passwordHash with "
                + "Gema > Set Panel Password and set this field to \"\". (H14)");
        }

        // The migration instructions were a way to turn the check above OFF: blank `password`, hash the
        // same "gema", and every gate passed while the panel still accepted it - and 10k PBKDF2 over a
        // four-character dictionary word is recovered offline in seconds. It was a regression
        // introduced by that migration rather than a pre-existing hole.
        if (!string.IsNullOrEmpty(passwordHash) && PanelPassword.Verify(DefaultPassword, passwordHash))
        {
            failures.Add($"{SettingsFile} has EditorSettings.passwordHash set to a hash OF THE DEFAULT "
                + "PASSWORD. Hashing it does not make it secret - it is published in this repo, so the "
                + "hash is trivially confirmed offline. Choose a different password with "
                + "Gema > Set Panel Password. (H14)");
        }

        // Warns rather than fails, and the distinction is the point. A release build is often
        // pre-configured for one site, so a baked-in cleanUnlistedAssets can be exactly what was
        // intended - and unlike the password rules above there is no alternative spelling that
        // expresses "yes, on purpose", so a hard gate leaves such a build no way through at all.
        // The same reasoning as the settings-schema check: legitimate exceptions exist, so it
        // reports instead of refusing.
        //
        // Note the deliberate null-safety: a derived project may not have the block at all, and a
        // missing block means the feature is off, which is the safe state.
        if ((bool?)settings.SelectToken("AppData.Cache.AssetDownload.cleanUnlistedAssets") == true)
        {
            warnings.Add($"{SettingsFile} ships with cleanUnlistedAssets ENABLED. On first run this "
                + "app will RECURSIVELY DELETE every file under the asset root that the current "
                + "appData does not reference, on a machine nobody is watching - and if "
                + "ProjectAppDataController is not authored under Core the keep-list is empty and it "
                + "clears the whole media cache. Intended for a pre-configured site build; a mistake "
                + "in the template, where it must be false. Verify the delete set against a COPY of a "
                + "media folder before shipping. (L12, C2)");
        }
    }

    /// <summary>
    /// Warns on the settings-panel credential states a development build can still be in.
    /// </summary>
    /// <remarks>
    /// Always runs, unlike <see cref="CheckShippedSettings"/>: a committed plaintext password is just as
    /// leaked in a development build, and that is the build where somebody still has time to migrate.
    /// Warn-only for the same reason as the schema check - this template ships a plaintext password by
    /// definition, so a gate would stop the base project building at all. The release gates in
    /// <see cref="CheckShippedSettings"/> are what actually stop it reaching a site.
    /// </remarks>
    private static void CheckPanelPassword(List<string> warnings)
    {
        JObject settings;

        try
        {
            settings = JObject.Parse(File.ReadAllText(SettingsFile));
        }
        catch (System.Exception)
        {
            // Silent on purpose: a missing or unparseable settings.json is already reported by
            // CheckForStrayConfigFiles and CheckShippedSettings, and a second copy of the same news
            // trains people to skim this output.
            return;
        }

        string password = (string)settings.SelectToken("EditorSettings.password");
        string passwordHash = (string)settings.SelectToken("EditorSettings.passwordHash");

        if (!string.IsNullOrEmpty(password))
        {
            warnings.Add($"{SettingsFile} carries a PLAINTEXT settings-panel password in "
                + "EditorSettings.password. It is committed here and in every project branched from "
                + "this one, so a reused password leaks to anyone who can read a repo. Run "
                + "Gema > Set Panel Password, put the result in EditorSettings.passwordHash, and set "
                + "EditorSettings.password to \"\" BY HAND - the tool does not edit settings.json. The "
                + "panel keeps accepting the plaintext value until you do. A release build fails on "
                + "this. (H14)");
        }

        // A hash that is present but not the right shape is worse than an absent one: the no-credential
        // warning below stays quiet because the field is non-empty, PanelPassword refuses every entry at
        // runtime, and the build looks clean. Pasting out of a JSON file and keeping the quotes or losing
        // the tail is the likely cause, since the tool hands the value over via the clipboard.
        if (!string.IsNullOrEmpty(passwordHash) && !PanelPassword.LooksLikeHash(passwordHash))
        {
            warnings.Add($"{SettingsFile} has an EditorSettings.passwordHash that is not in the expected "
                + "format, so the panel will refuse every password. Check for stray quotes or a "
                + "truncated paste, and regenerate it with Gema > Set Panel Password - its Verify box "
                + "confirms a value before you deploy it. (H14)");
        }

        // A panel that is switched on with nothing to check against can never be opened. Fail-closed is
        // the right behaviour, but it is silent until someone is standing at the kiosk needing it.
        if ((bool?)settings.SelectToken("EditorSettings.isActive") == true
            && string.IsNullOrEmpty(password) && string.IsNullOrEmpty(passwordHash))
        {
            warnings.Add($"{SettingsFile} has EditorSettings.isActive true but neither password nor "
                + "passwordHash, so the settings panel can never be opened - the long-press will show a "
                + "panel that refuses every entry. Set one with Gema > Set Panel Password, or set "
                + "isActive to false. (H14)");
        }
    }

    /// <summary>
    /// Cross-checks the shipped settings.json against <see cref="CoreSettings"/>: every key is walked
    /// alongside the C# type that would receive it, and any key no field can receive is reported.
    /// </summary>
    /// <remarks>
    /// <para><b>This class of bug is invisible by construction.</b> <c>JsonUtility</c> matches by field
    /// name and says nothing about a key it cannot place, so a mismatch produces a block that is simply
    /// never read. That has happened - <c>SystemSettings.Android</c> against an <c>AndroidSettings</c>
    /// key - and it went unnoticed for the life of the project, found only by accident while fixing
    /// something else. Nothing at runtime could have surfaced it.</para>
    ///
    /// <para><b>Warn, never fail.</b> Legitimately unreachable keys exist: <c>OneCoreCommand</c>'s
    /// <c>dynamic</c> fields are <c>object</c> and invisible to Unity's serializer. A gate would
    /// need a suppression list immediately, and that list is what turns a useful rule into one somebody
    /// deletes.</para>
    ///
    /// <para><b>Worth more in a branched project than here.</b> Operators hand-edit settings.json at
    /// sites, and a typo there produces exactly that silence. This runs in every project branched from
    /// this repo.</para>
    ///
    /// <para><b>Known limits, stated rather than hidden</b> - the same choice
    /// <c>ConfigManager.PatchDeployed</c> makes about arrays. Array elements are not descended into, so a
    /// mismatch inside <c>serialPorts[]</c> or <c>ConnectCommands[]</c> is not caught; and only
    /// settings.json against <c>CoreSettings</c> is checked, not <c>appData.json</c>, which is Newtonsoft
    /// with different matching rules. Both are named in the message so nobody reads a clean result as
    /// broader than it is.</para>
    /// </remarks>
    private static void CheckSettingsSchema(List<string> warnings)
    {
        // A missing or unparseable file is CheckShippedSettings' business, and it fails the build for
        // it on release. Nothing useful to add from here.
        if (!File.Exists(SettingsFile))
            return;

        JObject settings;

        try
        {
            settings = JObject.Parse(File.ReadAllText(SettingsFile));
        }
        catch (System.Exception)
        {
            return;
        }

        List<string> orphans = new List<string>();
        List<string> unsetKnobs = new List<string>();

        CompareToModel(settings, typeof(CoreSettings), string.Empty, orphans, unsetKnobs);

        if (orphans.Count > 0)
        {
            warnings.Add($"{SettingsFile} has {orphans.Count} key(s) that no field on CoreSettings can "
                + $"receive, so they are read by nothing:\n      {string.Join("\n      ", orphans)}\n"
                + "    Either the key is misspelled or the C# field is - and JsonUtility reports neither. "
                + "This is M54's failure mode. Note the check does not descend into array elements, so a "
                + "clean result here does not cover serialPorts[] or ConnectCommands[]. (E2)");
        }

        // Informational, and scalars only. A missing *block* is normal - an absent key keeps the C# field
        // initializer, which is documented behaviour, not a defect. A missing scalar is different in kind:
        // it is a knob an operator cannot turn because it is not in the file they were given, which is
        // what the skip-validation key ran into. Not a warning, because nothing is broken.
        if (unsetKnobs.Count > 0)
        {
            Debug.Log($"[BuildValidator] {unsetKnobs.Count} setting(s) exist in CoreSettings but not in "
                + $"{SettingsFile}, so an operator cannot see or change them (they keep their C# "
                + $"defaults):\n      {string.Join("\n      ", unsetKnobs)}");
        }
    }

    /// <summary>
    /// Walks a JSON object beside the type that would deserialize it, collecting keys with no matching
    /// field and scalar fields with no matching key.
    /// </summary>
    private static void CompareToModel(JObject node, System.Type type, string path,
        List<string> orphans, List<string> unsetKnobs)
    {
        // Fields only, public, instance - which is exactly what JsonUtility looks at. Properties are
        // invisible to it, and so is anything [NonSerialized].
        System.Reflection.FieldInfo[] fields = type.GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (JProperty property in node.Properties())
        {
            string here = path.Length == 0 ? property.Name : path + "." + property.Name;

            // Case-sensitive, because JsonUtility is.
            System.Reflection.FieldInfo field = System.Array.Find(fields,
                f => f.Name == property.Name && !f.IsNotSerialized);

            if (field == null)
            {
                orphans.Add(here);
                continue;
            }

            if (property.Value is JObject child && CanDescendInto(field.FieldType))
                CompareToModel(child, field.FieldType, here, orphans, unsetKnobs);
        }

        foreach (System.Reflection.FieldInfo field in fields)
        {
            if (field.IsNotSerialized || CanDescendInto(field.FieldType) || field.FieldType.IsArray)
                continue;

            if (node.Property(field.Name) == null)
                unsetKnobs.Add(path.Length == 0 ? field.Name : path + "." + field.Name);
        }
    }

    /// <summary>
    /// Whether a nested object under this field can be compared against it.
    /// </summary>
    /// <remarks>
    /// <c>object</c> is the case that matters: <c>OneCoreCommand</c>'s <c>object</c> fields land here,
    /// and reflecting over <c>object</c> finds no fields - so descending would report every child key as
    /// an orphan. Refusing to descend reports nothing instead, which is the honest answer for a field
    /// whose contents this check genuinely cannot reason about.
    /// </remarks>
    private static bool CanDescendInto(System.Type type)
    {
        return type.IsClass
            && type != typeof(string)
            && type != typeof(object)
            && !type.IsArray
            && type.GetFields(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance).Length > 0;
    }

    /// <summary>
    /// StreamingAssets is copied verbatim into the player, so a backup left in the config folder
    /// travels to the client site carrying the same password, API key and hostname as the original.
    /// This happened on 2026-08-10 with a settings_bck.json; it was caught by eye, which is not a
    /// control.
    /// </summary>
    private static void CheckForStrayConfigFiles(List<string> failures)
    {
        if (!Directory.Exists(ConfigFolder))
            return;

        List<string> stray = Directory.GetFiles(ConfigFolder)
            .Select(Path.GetFileName)
            .Where(name => !name.EndsWith(".meta"))
            .Where(name => BackupNameFragments.Any(
                fragment => name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();

        if (stray.Count > 0)
        {
            failures.Add($"{ConfigFolder} contains what look like backup files: {string.Join(", ", stray)}. "
                + "StreamingAssets is copied into the player as-is, so these would ship to the site with "
                + "whatever credentials and hostnames they contain. Move them outside Assets/.");
        }
    }

    /// <summary>
    /// A <c>Dynamic</c> TMP font asset rasterises glyphs at runtime from its source font, so a source
    /// that cannot be reached means every character outside the baked atlas fails to draw. The failure is
    /// invisible: TMP substitutes the configured missing-glyph character, which is <c>0</c> here, so the
    /// text renders as spaces rather than boxes, and TMP's own "unable to load font face" warning is
    /// gated the wrong way round upstream and never fires.
    ///
    /// Not release-only. A development build with blank text is exactly as broken, and this is the kind of
    /// thing found late precisely because it looks like a layout problem rather than a missing asset.
    /// </summary>
    private static void CheckDynamicFontSources(List<string> failures)
    {
        // Scoped to Assets/ deliberately. An unscoped FindAssets also returns Packages/ paths, and a
        // package-owned font asset with an unreachable source would block every build with nothing an
        // operator could fix - PackageCache is regenerated on resolve. A gate nobody can satisfy is a
        // gate that gets deleted.
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TMPro.TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
            if (font == null)
                continue;

            string problem = DescribeDynamicFontProblem(font, path);
            if (problem != null)
                failures.Add(problem);
        }
    }

    /// <summary>
    /// How many glyphs this asset would actually ship with. <c>m_ClearDynamicDataOnBuild</c> empties the
    /// tables at build time, so an asset carrying that flag ships **none** however many it shows in the
    /// editor - which is exactly the shape L32 was, and quoting the editor-time count there would
    /// understate the damage.
    ///
    /// Read by reflection because the <c>clearDynamicDataOnBuild</c> property is <c>internal</c> to
    /// <c>Unity.TextMeshPro</c>, whose <c>InternalsVisibleTo</c> list does not include
    /// <c>Assembly-CSharp-Editor</c>. Falls back to the editor-time count if the field ever disappears,
    /// which only understates the number in a message - it cannot turn a pass into a failure.
    /// </summary>
    private static int ShippedGlyphCount(TMPro.TMP_FontAsset font)
    {
        int authored = font.characterTable?.Count ?? 0;

        System.Reflection.FieldInfo field = typeof(TMPro.TMP_FontAsset).GetField(
            "m_ClearDynamicDataOnBuild",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (field == null)
            return authored;

        return field.GetValue(font) is bool cleared && cleared ? 0 : authored;
    }

    /// <summary>
    /// The judgement for one font asset, split out so it can be exercised directly against a constructed
    /// asset. Returns null when there is nothing wrong.
    ///
    /// Two shapes are legitimate and must not be reported. A <c>Static</c> asset is self-contained and
    /// deliberately nulls its runtime source so the font is not dragged into the build, so it is not
    /// checked at all. And a <c>Dynamic</c> asset may be backed by an OS font through
    /// <c>m_SourceFontFilePath</c> rather than by a project asset.
    /// </summary>
    /// <remarks>
    /// Reads <c>sourceFontFile</c> rather than the editor-only reference on purpose: TMP repairs the
    /// runtime field from that reference inside <c>#if UNITY_EDITOR</c>, so an asset can render correctly
    /// in the editor and still ship broken. This checks the field the player uses.
    ///
    /// Known ceiling: it judges every font asset under <c>Assets/</c>, not the build's dependency closure,
    /// so it will also report an unreferenced leftover that would never have shipped. That is why the
    /// message offers deleting the asset as one of the fixes.
    /// </remarks>
    private static string DescribeDynamicFontProblem(TMPro.TMP_FontAsset font, string path)
    {
        if (font == null || font.atlasPopulationMode != TMPro.AtlasPopulationMode.Dynamic)
            return null;

        // An OS font is a valid source and has no project asset to check. Reached by reflection because
        // TMP exposes no public accessor for it - `m_SourceFontFilePath` is a private serialized field, and
        // a guessed `sourceFontFilePath` property does not exist in this version.
        if (!string.IsNullOrEmpty(ReadOsFontPath(font)))
            return null;

        if (font.sourceFontFile == null)
        {
            return $"'{path}' is a Dynamic TMP font asset with no source font. Every character outside "
                + $"the {ShippedGlyphCount(font)} glyph(s) it ships with will render as a space at runtime, "
                + "with no error. Restore the source font; or set the asset to Static if its baked atlas is "
                + "meant to be the whole coverage; or delete the asset if nothing uses it. (M120, L32)";
        }

        string sourcePath = AssetDatabase.GetAssetPath(font.sourceFontFile);
        TrueTypeFontImporter importer = AssetImporter.GetAtPath(sourcePath) as TrueTypeFontImporter;

        if (importer == null)
        {
            // Not a failure: a built-in font source has no TrueTypeFontImporter, and failing closed here
            // would block builds on a state nobody owns. Said out loud, because this file's own rule is
            // that a check which silently does not run is worth very little the day it matters.
            Debug.Log($"[BuildValidator] Could not inspect the font importer for '{sourcePath}' "
                + $"(source of '{path}'), so Include Font Data was not checked for it.");
            return null;
        }

        // Without the embedded bytes FontEngine has nothing to rasterise from, so the asset is as broken
        // as if the font were absent - and this one is easy to flip by accident in the importer.
        if (!importer.includeFontData)
        {
            return $"'{path}' is a Dynamic TMP font asset whose source '{sourcePath}' has "
                + "Include Font Data disabled. FontEngine reads the embedded bytes, so glyphs outside the "
                + $"{ShippedGlyphCount(font)} it ships with will not render and nothing will say so. "
                + "Re-enable Include Font Data on the font importer. (M120)";
        }

        return null;
    }

    /// <summary>
    /// A Dynamic font asset can be backed by a font installed on the machine instead of a project asset.
    /// TMP keeps that as the private serialized field <c>m_SourceFontFilePath</c> with no public accessor,
    /// so reflection is the only route.
    ///
    /// Returns empty when the field is absent, and that is NOT fail-safe in both directions: it means a
    /// future TMP that renames the field would make this rule report a path-backed Dynamic asset as
    /// sourceless - a false positive that blocks a build. No such asset exists here, so the risk is
    /// theoretical, but the direction is worth stating rather than claiming a safety this does not have.
    /// </summary>
    private static string ReadOsFontPath(TMPro.TMP_FontAsset font)
    {
        System.Reflection.FieldInfo field = typeof(TMPro.TMP_FontAsset).GetField(
            "m_SourceFontFilePath",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (field == null)
        {
            Debug.Log("[BuildValidator] TMP_FontAsset.m_SourceFontFilePath is absent in this TextMeshPro "
                + "version, so a Dynamic font asset backed by an OS font cannot be recognised as such and "
                + "may be reported as sourceless. Update ReadOsFontPath if that happens.");
            return string.Empty;
        }

        return field.GetValue(font) as string ?? string.Empty;
    }
}
#endif
