/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * AppDescriptor can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EnigmaVirtual
{
    public class AppDescriptor : EditorWindow
    {
        private static ApplicationDescription _data;

        private int _version;
        private int _minor;
        private int _build;

        public BuildTarget platform = BuildTarget.StandaloneWindows64;
        public BuildTargetGroup group = BuildTargetGroup.Standalone;

        [MenuItem("Gema/App Descriptor", false, 2)]
        static void Init()
        {
            AppDescriptor window = (AppDescriptor)EditorWindow.GetWindow(typeof(AppDescriptor));
            window.Show();

            // Forward slashes via the shared const. AssetDatabase paths are project-relative,
            // not OS paths, and the backslash form is undocumented. It does happen to resolve on
            // Windows - the descriptor's non-default Slug/Client/Year prove it, since the
            // create-branch below would otherwise have blanked them long ago - but it is not
            // portable to a macOS or Linux editor.
            _data = AssetDatabase.LoadAssetAtPath<ApplicationDescription>(AppVersionSync.DescriptorPath);
            if (_data == null)
            {
                _data = CreateInstance<ApplicationDescription>();
                AssetDatabase.CreateAsset(_data, AppVersionSync.DescriptorPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // A new descriptor carries the class default "1.0.0". Pull the real version in
                // now, or the bump buttons would use 1.0.0 as their base.
                AppVersionSync.Sync();
            }

            window.titleContent = new GUIContent("App Descriptor");
            window.Show();
        }

        [MenuItem("Gema/Create Settings Batch File")]
        static void CreateBatchManually()
        {
            // Manually trigger batch file creation
            BatchFileCreator batchFileCreator = new BatchFileCreator();
            batchFileCreator.CreateBatchFile();
        }

        void OnGUI()
        {
            if (_data == null) return;

            CoreEditorHelper.DrawHeader();

            GUILayout.Space(10);
            GUILayout.Label("Application Description", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Product Name", GUILayout.Width(140));
            _data.FullName = EditorGUILayout.TextField(_data.FullName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Slug", GUILayout.Width(140));
            _data.Slug = EditorGUILayout.TextField(_data.Slug);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Client", GUILayout.Width(140));
            _data.Client = EditorGUILayout.TextField(_data.Client);
            EditorGUILayout.EndHorizontal();

            // Read-only: VERSION.txt is the source of truth and AppVersionSync propagates it
            // here. An editable field made this a second competing truth, and they drifted -
            // VERSION.txt reached 0.5.1 while the descriptor an operator sees still said 1.0.0.
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Version", GUILayout.Width(140));
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField(_data.Version);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("Version comes from VERSION.txt at the repo root. Use the "
                + "buttons below - they write that file, then re-sync bundleVersion and this "
                + "descriptor.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Year", GUILayout.Width(140));
            _data.Year = EditorGUILayout.IntField(_data.Year);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(20);
            GUILayout.Label("Design Size", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Width", GUILayout.Width(140));
            _data.DesignWidth = EditorGUILayout.FloatField(_data.DesignWidth);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Height", GUILayout.Width(140));
            _data.DesignHeight = EditorGUILayout.FloatField(_data.DesignHeight);
            EditorGUILayout.EndHorizontal();

            string copyright = "(c)" + _data.Year + " Gema Digital Agency";

            GUILayout.Space(20);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(copyright, GUILayout.ExpandWidth(true));
            _data.Copyright = copyright;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            if (GUILayout.Button("Save Data"))
            {
                Save();
            }

            GUILayout.Space(20);

            group = (BuildTargetGroup)EditorGUILayout.EnumPopup("Group:", group);

            GUILayout.Space(10);

            platform = (BuildTarget)EditorGUILayout.EnumPopup("Platform:", platform);

            // After the Group popup, because Group is now what decides which platform's application
            // identifier gets written. See ApplyToPlayerSettings.
            ApplyToPlayerSettings();

            GUILayout.Space(10);

            // Base the bump on VERSION.txt, never on the descriptor. If Sync() has not run or
            // bailed, the descriptor can hold a stale version, and bumping from that would push it
            // back into VERSION.txt - truth flowing backwards, which is how the two versions drift apart.
            string current = AppVersionSync.Read();
            if (current == null)
            {
                EditorGUILayout.HelpBox("VERSION.txt is missing or malformed - version controls "
                    + "hidden. Fix it at the repo root.", MessageType.Warning);
                return;
            }

            string[] versionPart = current.Split("."[0]);
            if (versionPart.Length == 3)
            {
                _version = versionPart[0].Length > 0 ? int.Parse(versionPart[0]) : 0;
                _minor = versionPart[1].Length > 0 ? int.Parse(versionPart[1]) : 0;
                _build = versionPart[2].Length > 0 ? int.Parse(versionPart[2]) : 0;

                // These write VERSION.txt; AppVersionSync then propagates to bundleVersion and
                // the descriptor. Writing _data.Version directly is what created the drift.
                if (GUILayout.Button("Major Update"))
                    Bump(_version + 1, 0, 0);

                if (GUILayout.Button("Minor Update"))
                    Bump(_version, _minor + 1, 0);

                if (GUILayout.Button("Patch Update"))
                    Bump(_version, _minor, _build + 1);

                EditorGUILayout.Space();
                EditorGUILayout.Space();

                if (GUILayout.Button("Build"))
                {
                    // Location first: bumping before the panel left a phantom version bump behind
                    // whenever the user cancelled.
                    string path = EditorUtility.SaveFolderPanel("Choose Location", GetProjectFolderPath() + "/Build/", "");

                    // Then bump, so VERSION.txt, bundleVersion, the descriptor and the output
                    // filename all agree on the version actually being built.
                    if (!string.IsNullOrEmpty(path) && Bump(_version, _minor, _build + 1))
                        MakeBuild(path);
                }
            }
        }

        /// <summary>
        /// Writes the new version to VERSION.txt and lets AppVersionSync propagate it. Returns
        /// false if the write failed, so callers do not proceed on a version that was not stored.
        /// </summary>
        private bool Bump(int major, int minor, int patch)
        {
            // No Save() here: AppVersionSync.Write -> Sync already dirtied the descriptor and
            // called SaveAssets. Calling it again cost a full AssetDatabase.Refresh per click.
            return AppVersionSync.Write($"{major}.{minor}.{patch}");
        }

        /// <summary>Persists descriptor edits. Version is not among them - see Bump.</summary>
        /// <summary>
        /// Pushes the descriptor's values into <c>PlayerSettings</c>, writing only what differs and
        /// saying so when it does.
        /// </summary>
        /// <remarks>
        /// <para><b>This used to be four unconditional assignments in the middle of <c>OnGUI</c></b>,
        /// so every one of them ran on every repaint - dozens of times a second while the window was
        /// open, with no user action involved. Harmless for three of them, because writing the same
        /// value back changes nothing. Not harmless for the fourth.</para>
        ///
        /// <para><b>The identifier now follows the window's own Group field, not
        /// <c>EditorUserBuildSettings.selectedBuildTargetGroup</c>.</b> Unity stores the application
        /// identifier per platform, so the old code wrote it to whichever target the editor happened
        /// to be set to. Switching this project to Android and opening this window silently changed
        /// the *Android* id from the placeholder slug it shipped with to
        /// <c>com.gemadigital.unityDefault.app</c> - a different id is a different app, so the test
        /// device ended up with two installs, no in-place upgrade and no shared
        /// <c>persistentDataPath</c>. On a site that is a second copy of the kiosk app sitting beside
        /// the real one. Group is right here because it is the field this window already uses to
        /// build, so what gets written is visible on screen rather than inherited from editor state.
        /// </para>
        ///
        /// <para>And it logs. A change to the bundle identifier is a deployment-visible act; it was
        /// previously invisible except as an unexplained hunk in <c>ProjectSettings.asset</c>, which
        /// is how this was found.</para>
        /// </remarks>
        private void ApplyToPlayerSettings()
        {
            if (_data == null)
                return;

            if (PlayerSettings.companyName != _data.Client)
                PlayerSettings.companyName = _data.Client;

            if (PlayerSettings.productName != _data.FullName)
                PlayerSettings.productName = _data.FullName;

            if (PlayerSettings.defaultScreenWidth != (int)_data.DesignWidth)
                PlayerSettings.defaultScreenWidth = (int)_data.DesignWidth;

            if (PlayerSettings.defaultScreenHeight != (int)_data.DesignHeight)
                PlayerSettings.defaultScreenHeight = (int)_data.DesignHeight;

            // NamedBuildTarget, not the BuildTargetGroup overload: that pair is [Obsolete] in Unity 6
            // and was already warning here before this change. Same target, documented API.
            NamedBuildTarget named = NamedBuildTarget.FromBuildTargetGroup(group);

            string wanted = "com.gemadigital." + _data.Slug;
            string current = PlayerSettings.GetApplicationIdentifier(named);

            if (current == wanted)
                return;

            PlayerSettings.SetApplicationIdentifier(named, wanted);

            Debug.LogWarning($"[AppDescriptor] Application identifier for {group} changed from "
                + $"[{current}] to [{wanted}], from the descriptor's Slug. This is a different app to "
                + "Android and to any store: an installed build under the old id will not upgrade in "
                + "place, and will not share its persistentDataPath. Change the Group field above to "
                + "target a different platform (M91).");
        }

        private void Save()
        {
            AssetDatabase.Refresh();
            EditorUtility.SetDirty(_data);
            AssetDatabase.SaveAssets();

            //CreateSettingsShortcut();
        }

        //private void CreateSettingsShortcut()
        //{
        //    string companyName = _data.Client; // Application.companyName;
        //    string productName = _data.FullName; //Application.productName;
        //    string streamingAssetsPath = Application.streamingAssetsPath;

        //    //// Define the target folder path inside AppData/LocalLow
        //    //string targetFolder = "%userprofile%\\AppData\\LocalLow\\" + _data.Client + "\\" + _data.FullName + "\\StreamingAssets";
        //    ////string targetFolder = "%userprofile%\\AppData\\LocalLow\\" + companyName + "\\" + productName + "\\StreamingAssets";

        //    //// Create batch file content
        //    //string batchContent = "@echo off\n" +
        //    //                      "explorer \"" + targetFolder + "\"";


        //    // Define the target folder path inside AppData/LocalLow with backslashes
        //    string targetFolder = "%userprofile%\\AppData\\LocalLow\\" + companyName + "\\" + productName + "\\StreamingAssets";
        //    string settingsFile = targetFolder + "\\settings.json";
        //    string sourceSettingsFile = "settings.json";

        //    // Create batch file content with conditional logic
        //    string batchContent = "@echo off\n" +
        //                          "if not exist \"" + targetFolder + "\" mkdir \"" + targetFolder + "\"\n" +
        //                          "if exist \"" + settingsFile + "\" (\n" +
        //                          "    explorer \"" + targetFolder + "\"\n" +
        //                          ") else (\n" +
        //                          "    copy \"%~dp0" + sourceSettingsFile + "\" \"" + targetFolder + "\" /Y\n" +
        //                          "    explorer \"" + targetFolder + "\"\n" +
        //                          ")";



        //    // Define the path for the .bat file
        //    string batFilePath = Path.Combine(streamingAssetsPath, "localSettings.bat");

        //    // Write the batch file
        //    try
        //    {
        //        File.WriteAllText(batFilePath, batchContent);
        //        Debug.Log("Batch file created successfully at: " + batFilePath);
        //    }
        //    catch (IOException e)
        //    {
        //        Debug.LogError("Error writing batch file: " + e.Message);
        //    }
        //}

        private string GetProjectFolderPath()
        {
            var s = Application.dataPath;
            s = s.Replace("Assets/", "");
            return s;
        }

        private void MakeBuild(string path)
        {
            string fileExtension = "";

            switch (platform)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    fileExtension = ".exe";
                    break;
                case BuildTarget.StandaloneOSX:
                    fileExtension = ".app";
                    break;
            }

            EditorUserBuildSettings.SwitchActiveBuildTarget(group, platform);

            string buildPath = path + System.IO.Path.DirectorySeparatorChar + _data.Version + System.IO.Path.DirectorySeparatorChar;
            string buildFilename = _data.Slug + "_" + _data.Version + fileExtension;
            BuildOptions options = platform == BuildTarget.StandaloneWindows64 || platform == BuildTarget.StandaloneWindows ? BuildOptions.ShowBuiltPlayer : BuildOptions.None;

            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            BuildPipeline.BuildPlayer(scenes, buildPath + buildFilename, platform, options);
        }
    }

}
