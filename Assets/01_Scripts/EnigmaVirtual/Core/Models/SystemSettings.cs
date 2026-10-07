/*******************************************************
 * Copyright (C) 2024 Gema Digital Agency
 * 
 * Can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/

namespace EnigmaVirtual
{
    [System.Serializable]
    public class SystemSettings
    {
        public bool doLogs;
        public int frameRate = 30;
        public bool mouseVisibility = true;
        public bool showImageDownloadError = true;
        /// <summary>
        /// Named to match the <c>AndroidSettings</c> key in settings.json, which it did not.
        /// </summary>
        /// <remarks>
        /// <para>This was <c>Android</c>. <c>JsonUtility</c> matches by field name, so <b>the entire
        /// Android settings block had never been read</b> - anyone who debugged Android behaviour against
        /// it was looking at the C# initializer. It survived because the shipped
        /// <c>sleepTimeout</c> is <c>-1</c>, identical to that initializer, so the defect had no
        /// observable effect until an operator changed the value.</para>
        ///
        /// <para><b>Whether the old field was null or default-constructed is unresolved, and the
        /// initializer below is why that no longer matters.</b> I argued it must have been
        /// default-constructed, on the grounds that the sibling nested fields have no initializers either
        /// and <c>MainController.Init</c> dereferences them unguarded on the Windows path that runs daily.
        /// That evidence is invalid: every one of those siblings <i>is</i> present
        /// in the shipped settings.json, so they only demonstrate the present-key case, and <c>Android</c>'s
        /// key was absent - the case actually in question. <c>JsonUtility</c>'s behaviour for an absent
        /// nested-class key is undocumented and there is no exercised in-repo example to settle it. So the
        /// severity of a name/key mismatch - a silently ignored setting, or an NRE inside <c>Init</c> on
        /// Android - is genuinely unknown, and is recorded that way rather than guessed.</para>
        ///
        /// <para>The <b>field</b> was renamed rather than the JSON key. Every deployed config already says
        /// <c>AndroidSettings</c>, so this makes those start working - with the caveat below that a device
        /// holding a <c>PlayerPrefs</c> snapshot never reads the file at all. But the argument that actually
        /// settles it is this: <c>FileManager.SaveCoreSettings</c> merges
        /// <c>JsonUtility.ToJson(Core.settings)</c> into the operator's file, so <b>the field name is the
        /// write key as well as the read key</b>. Supporting both names would make the next panel save emit
        /// both and let them diverge forever; a read-only compatibility shim would have the first save
        /// write the old name and turn the operator's block into dead weight that still parses. One name is
        /// the only stable state. (The config patcher does not inject a stale block here, which was
        /// wrong about the mechanism: <c>PatchDeployed</c> merges shipped JSON into deployed JSON, not the
        /// C# model, so it only matters if the shipped key is renamed too.)</para>
        ///
        /// <para><b>A device that used the panel's PlayerPrefs save keeps the old name.</b>
        /// <c>PlayerPrefs["Settings"]</c> holds a <c>ToJson</c> snapshot of this model, and
        /// <c>Core.Awake</c> prefers it over the file - on Android it reads the file not at all - so the
        /// rename silently falls back to the initializer there until the prefs entry is cleared. Harmless
        /// today, because the old snapshot's value equals the default; the general rule is what matters,
        /// and it is in the breaking-changes table in overview.md.</para>
        ///
        /// <para><b>Breaking change for branched projects</b> - a compile error on
        /// <c>Core.settings.System.Android</c>, fixed by one identifier.</para>
        /// </remarks>
        /// <para>The initializer is not decoration. It removes the null question entirely, matches
        /// <c>AppDataSettings.Cache</c> and <c>localSettings</c>, and satisfies CLAUDE.md's own rule about
        /// settings fields. It also covers the one case where an absent key is <b>guaranteed</b>: a stale
        /// <c>PlayerPrefs["Settings"]</c> blob was serialized under the old name, so reading it back finds
        /// no <c>AndroidSettings</c> node at all - on Android, which is the only platform this field is
        /// read on and the only one where the panel writes prefs.</para>
        public AndroidSettings AndroidSettings = new AndroidSettings();
        // Same reason as the others: absent 'System' block leaves this null.
        public WindowsSettings Windows = new WindowsSettings();
    }

    [System.Serializable]
    public class ProcessesSettings
    {
        public string explorerProcess = "explorer";
        public bool killExplorer = false;
        public bool restoreExplorerOnQuit = true;
        public string[] killProcesses;
    }

    [System.Serializable]
    public class PresentationSettings
    {
        public bool minimizeWindow = false;
        public bool bringToFront = false;
        public float bringToFrontTimeInterval = 0;
    }

    [System.Serializable]
    public class KeyLoggerSettings
    {
        public bool isActive = false;
        public bool workInBackround = true;
    }

    [System.Serializable]
    public class AndroidSettings
    {
        // A power saving setting, allowing the screen to dim some time after the last active user interaction. -1 = NeverSleep
        public int sleepTimeout = -1;
    }

    [System.Serializable]
    public class WindowsSettings
    {
        /// <summary>
        /// KeyCode for the operator shortcut that skips asset validation at boot, stored as an
        /// int the way DebugSettings.Console.toggleKeyCode already is. 0 means "not configured" and
        /// falls back to the hardcoded default - see MainController.SkipAssetValidationKey.
        /// </summary>
        public int skipAssetValidationKeyCode = (int)UnityEngine.KeyCode.K;

        public ProcessesSettings Processes;
        public PresentationSettings Presentation;
        public KeyLoggerSettings KeyLogger;
        public WindowsSettingsScreen Screen;
    }

    [System.Serializable]
    public class WindowsSettingsScreen
    {
        public bool forceResolution = false;
        public bool fullScreen;
        public int width;
        public int height;
    }
}