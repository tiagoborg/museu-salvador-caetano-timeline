using System.IO;
using UnityEngine;

namespace EnigmaVirtual
{
    /// <summary>
    /// Single source of truth for the framework's config and cache locations.
    ///
    /// Every path here is composed with <see cref="Path.Combine"/>. The previous approach kept a
    /// separator inside the folder value (<c>configDirectory = "config\\"</c>) and concatenated
    /// it, which produced a directory literally named <c>config\</c> on any platform where
    /// backslash is a filename character rather than a separator - i.e. everything except
    /// Windows. Never reintroduce a separator into one of these constants.
    ///
    /// Windows-only code that builds shell or batch strings is a separate case: those need
    /// literal backslashes and should append them explicitly, not rely on these values.
    /// </summary>
    public static class AppPaths
    {
        /// <summary>Config folder name. A single path segment - no separators.</summary>
        public const string ConfigFolder = "config";

        /// <summary>
        /// Folder under persistentDataPath that mirrors the shipped StreamingAssets layout.
        /// Case matters on Android and Linux: this exact spelling is what every reader uses.
        /// </summary>
        public const string PersistentStreamingFolder = "StreamingAssets";

        /// <summary>Read-only config shipped inside the build.</summary>
        public static string StreamingConfig => Path.Combine(Application.streamingAssetsPath, ConfigFolder);

        /// <summary>
        /// Writable config. An operator can drop files here to override a deployed build, which
        /// is why this is checked first - see <see cref="Config"/>.
        /// </summary>
        public static string PersistentConfig =>
            Path.Combine(Application.persistentDataPath, PersistentStreamingFolder, ConfigFolder);

        /// <summary>Path to <paramref name="fileName"/> inside the shipped config folder.</summary>
        public static string StreamingConfigFile(string fileName) => Path.Combine(StreamingConfig, fileName);

        /// <summary>Path to <paramref name="fileName"/> inside the writable config folder.</summary>
        public static string PersistentConfigFile(string fileName) => Path.Combine(PersistentConfig, fileName);

        /// <summary>
        /// Gives <paramref name="path"/> a <c>file://</c> scheme unless it already has one, so a local
        /// file can be handed to <c>UnityWebRequest</c>.
        /// </summary>
        /// <remarks>
        /// <para><b>Route on the path, not the platform.</b> Whether a value needs a scheme depends on
        /// whether it already is a URL, not on which target is compiling - and every site that decided
        /// this for itself got it wrong in a different way. Two tested
        /// <c>StartsWith("/storage/")</c> or <c>"/Users/"</c>, neither of which matches a real
        /// <c>persistentDataPath</c> here; two tested nothing at all; and one prepended
        /// unconditionally, which on Android turns <c>jar:file://…</c> into
        /// <c>file://jar:file://…</c>. Five sites, four spellings, one question.</para>
        ///
        /// <para>Returns <paramref name="path"/> unchanged when it is null or empty rather than
        /// throwing: every caller is on a load path that already guards, and a path helper that can
        /// throw just moves the failure somewhere less obvious.</para>
        ///
        /// <para><b>UriBuilder, not string concatenation</b>, and that distinction was measured after
        /// a plain <c>"file://" + path</c> shipped in the first version of this method.
        /// A filename containing <c>#</c> works as a bare path
        /// and <b>404s</b> once the scheme is prefixed, because <c>#</c> opens the URI fragment - so the
        /// fix would have broken, on Windows, files that used to load. An operator would have gone
        /// hunting for a missing CMS asset that was sitting on the kiosk's own disk.</para>
        ///
        /// <para>Four constructions were compared against a Windows drive path and an Android-style
        /// rooted path, each with <c>#</c>, <c>%</c> and a space. Only this one is correct on all of
        /// them - and only with the <c>%</c> escape below: measured, without it a
        /// filename containing <c>%20</c> is read as pre-escaped and 404s. <c>Uri.TryCreate(path, UriKind.Absolute, …)</c> returns <b>false</b> for
        /// <c>/data/user/0/…</c> - it is not an absolute URI - which would have silently left Android
        /// exactly as broken as before, on the platform this whole method exists for.
        /// <c>new Uri("file://" + path)</c> and <c>new Uri(new Uri("file:///"), path)</c> both leave
        /// <c>#</c> unescaped. Escaping by hand leaves Windows backslashes and misses <c>%</c>.</para>
        /// </remarks>
        public static string ToFileUrl(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Contains("://"))
                return path;

            try
            {
                // Escape '%' first, or UriBuilder reads an existing "%20" in a *filename* as an
                // already-escaped space and hands UnityWebRequest a path to a file that does not
                // exist. Measured both ways, round-tripped through Uri.LocalPath: with the escape,
                // "a%20b.jpg", "100%.png", "tag#1.png" and "a b.png" all come back byte-identical;
                // without it, only the first is wrong - which is exactly the kind of narrow case that
                // reaches a site as "one image out of four hundred does not load". These are
                // filesystem paths, never pre-encoded URLs, so treating '%' as literal is right.
                return new System.UriBuilder("file", string.Empty, -1, path.Replace("%", "%25"))
                    .Uri.AbsoluteUri;
            }
            catch (System.Exception e)
            {
                // Falls back to the value that worked before this method existed rather than throwing
                // into a loader coroutine. A bare path is what every one of these call sites passed
                // until now, and it loads on Windows; it is the failure mode we already understand.
                Development.LogError($"could not build a file:// URL from [{path}]: {e.Message}. "
                    + "Using the raw path, which works on Windows and fails on Android.");

                return path;
            }
        }

        /// <summary>
        /// Whether this build keeps a deployed copy of the config at all - i.e. whether anything ever
        /// seeds, patches or prefers <see cref="PersistentConfig"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>One definition, because the seeder and the reader have to agree and did not.</b>
        /// <c>ConfigManager</c> was <c>#if (UNITY_STANDALONE_WIN &amp;&amp; !UNITY_EDITOR) ||
        /// DEBUG_MODE</c> while <c>FileManager.CoreSettings()</c> was
        /// <c>#if (!UNITY_EDITOR &amp;&amp; !UNITY_WEBGL) || DEBUG_MODE</c>, so every non-Windows player
        /// looked for a deployed config that nothing had ever written. That gap was the defect, and
        /// keeping two spellings in step by comment is how it comes back - there were four sites in
        /// three spellings before this existed.</para>
        ///
        /// <para><c>static readonly</c> rather than <c>const</c> on purpose: the guarded bodies then
        /// still compile on every target instead of vanishing behind a <c>#if</c>, which is the whole
        /// bug class it belonged to - code that cannot be wrong because it does not exist.</para>
        ///
        /// <para>The editor is excluded deliberately, and that is a separate concern:
        /// <see cref="FileManager"/> reads StreamingAssets directly in-editor so a developer sees the
        /// repo's file rather than a stale LocalLow copy, and seeding would write files nothing reads.
        /// <c>DEBUG_MODE</c> opts one developer into the player behaviour.</para>
        /// </remarks>
        public static readonly bool UsesDeployedConfig =
#if (!UNITY_EDITOR && !UNITY_WEBGL) || DEBUG_MODE
            true;
#else
            false;
#endif

        /// <summary>
        /// The persistent copy if this build keeps one and it exists, otherwise the shipped one. This
        /// is the resolution order the whole framework assumes.
        /// </summary>
        /// <remarks>
        /// <para><b><see cref="UsesDeployedConfig"/> is part of the test as of v0.16.1.</b> This was
        /// the only config resolver without it, which is how the editor came to read
        /// <c>settings.json</c> from the repo (<see cref="FileManager"/> gates) and
        /// <c>appData.json</c>/<c>appSettings.json</c> from LocalLow in the same session, silently -
        /// so an edit to the repo's copy appeared to do nothing. All three obey one rule now.</para>
        ///
        /// <para>Behaviour is unchanged in every <b>non-WebGL</b> player: <c>UsesDeployedConfig</c> is
        /// true there, so an operator's deployed override still wins and this gate cannot disable it.
        /// What changed is the editor, which now always reads the repo, and the WebGL player, which now
        /// matches <see cref="FileManager"/> - and strictly improves, since an idbfs copy previously
        /// made this return a page-relative path that <c>UnityWebRequest</c> answered with a 404.</para>
        ///
        /// <para>Only the <c>appSettings.json</c> read goes through here. The appData cache read is
        /// deliberately <i>not</i> routed through this method - see
        /// <c>AppDataSettings.downloadedDataJsonPath</c> - because a runtime cache has to be readable on
        /// every target, and gating it turned the editor's offline fallback into "load the shipped stub
        /// and report success".</para>
        ///
        /// <para>The editor still *writes* <c>appSettings.json</c> to the persistent folder
        /// (<c>SaveAppSettings</c>) and will no longer read it back. That asymmetry is deliberate: the
        /// write path stays exercised in-editor, where turning it into a no-op would be a silent
        /// nothing-happens. <c>DEBUG_MODE</c> is the opt-in for the full deployed path, and is already
        /// one of the two terms in <c>UsesDeployedConfig</c>. A branched project whose settings panel
        /// calls <c>SaveAppSettings</c> will see an in-editor save appear to work and revert next run;
        /// that is tracked separately.</para>
        ///
        /// <para>On Android and WebGL the streamingAssets result is inside the package and cannot be
        /// opened with <c>File.*</c> - callers on those platforms must fetch it with
        /// UnityWebRequest, which is what <see cref="FileManager"/> does.</para>
        /// </remarks>
        public static string Config(string fileName)
        {
            string persistent = PersistentConfigFile(fileName);

            if (UsesDeployedConfig)
                return File.Exists(persistent) ? persistent : StreamingConfigFile(fileName);

            // Said out loud rather than silently skipped. Dropping a file into LocalLow and seeing no
            // effect, with nothing in the console, is the confusion to avoid - and returning the
            // shipped copy without a word would have replaced one silence with another. Only reachable
            // from LoadSettings, once per load, so this cannot become log spam.
            if (File.Exists(persistent))
            {
                Development.Log($"Ignoring the deployed [{fileName}] at [{persistent}]: this build reads "
                    + "the shipped config. Define DEBUG_MODE to read the deployed one instead.");
            }

            return StreamingConfigFile(fileName);
        }

        /// <summary>Creates the writable config folder if it isn't there yet.</summary>
        public static void EnsurePersistentConfig()
        {
            if (!Directory.Exists(PersistentConfig))
                Directory.CreateDirectory(PersistentConfig);
        }

        /// <summary>
        /// Normalises a path for hand-off to a shell command (explorer.exe, cmd, a .bat).
        ///
        /// Every path here is safe for <c>File</c>/<c>Directory</c>, but not for the Windows
        /// shell: <see cref="Application.persistentDataPath"/> returns forward slashes on
        /// Windows while <see cref="Path.Combine"/> joins with backslashes, so the results are
        /// mixed-separator - and <c>explorer.exe</c> silently refuses to open a path containing
        /// a forward slash. It does not error; it just does nothing, which is how this went
        /// unnoticed through a compile check.
        ///
        /// Always wrap an AppPaths value in this before passing it to <c>Process.Start</c>.
        /// </summary>
        public static string ForShell(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
