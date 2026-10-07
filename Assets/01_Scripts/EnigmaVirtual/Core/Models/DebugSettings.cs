namespace EnigmaVirtual
{
    [System.Serializable]
    public sealed class DebugSettings
    {       
        // Initialised, and that is load-bearing rather than tidy: JsonUtility builds the
        // object graph exactly ONE level deeper than the JSON goes (measured 2026-08-14).
        // With this block absent from settings.json the parent is still built, but this
        // field would come back null - and MainController.Init dereferences Console.isActive on the boot
        // path. Measured: JsonUtility builds one level below whatever the JSON mentions, so depth 1 is
        // safe and depth 2 is not.
        public ConsoleSettings Console = new ConsoleSettings();
        // Same reason as Console above: NotificationPrefab.Awake reads this.
        public NotificationsSettings Notifications = new NotificationsSettings();
    }

    [System.Serializable]
    public sealed class ConsoleSettings
    {
        public bool isActive = false;
        public bool shakeToOpen = false;
        public int toggleKeyCode = 9;
        public bool restrictLogCount = true;
        public int maxLogCount = 1000;
    }

    [System.Serializable]
    /// <summary>
    /// Who may open the on-device settings panel, and for how long it stays open.
    /// </summary>
    /// <remarks>
    /// Called <c>EditorSettings</c> until v0.49.2, which is the simple name of
    /// <c>UnityEditor.EditorSettings</c> - a CS0104 waiting for the first editor script that has
    /// both <c>using UnityEditor</c> and <c>using EnigmaVirtual</c>. Four such files exist under
    /// <c>Assets/Editor/</c>, and none of them names the type: CS0104 fires on <i>use</i> of the
    /// ambiguous simple name, not on the imports, so this stayed latent. The rename removes the
    /// trap rather than a live error.
    ///
    /// <b>The FIELD on CoreSettings is still called EditorSettings</b>, because that name is the
    /// JSON key in settings.json and moving it would be a config-format change on every deployed
    /// machine. Only the type moved; verified against the shipped file.
    ///
    /// Named for what it holds - password, maxTries, lockoutSeconds, autoHideTime - rather than
    /// after the panel. An earlier attempt called it <c>ConfigPanelSettings</c>, one letter from
    /// the unrelated <c>ConfigsPanel</c>, which does not read it.
    /// </remarks>
    public sealed class PanelAccessSettings
    {
        public bool isActive = false;

        /// <summary>
        /// <b>Legacy plaintext password. Migrate to <see cref="passwordHash"/>.</b> Still honoured so
        /// that updating the framework does not lock an existing installation out of its own panel, but
        /// every comparison against it logs an error, and `BuildValidator` warns while it is set.
        /// </summary>
        public string password = "";

        /// <summary>
        /// PBKDF2 hash produced by <b>Gema > Set Panel Password</b>; see
        /// <see cref="PanelPassword"/> for the format. Takes precedence over
        /// <see cref="password"/> when both are set.
        /// </summary>
        public string passwordHash = "";

        public int touchTime = 5;
        public int autoHideTime = 30;

        /// <summary>Failed attempts allowed before <see cref="lockoutSeconds"/> starts.</summary>
        public int maxTries = 3;

        /// <summary>
        /// Seconds the panel refuses passwords after <see cref="maxTries"/> failures. Survives closing
        /// and reopening the panel, which is what makes it a lockout rather than a counter.
        /// </summary>
        public int lockoutSeconds = 60;
    }

    [System.Serializable]
    public sealed class NotificationsSettings
    {
        public bool isActive = false;
        public int successTimeout = 5;
        public int warningTimeout = 5;
        public int errorTimeout = 0;
        public int infoTimeout = 5;
    }
}
