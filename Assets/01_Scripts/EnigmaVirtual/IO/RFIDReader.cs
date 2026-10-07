using UnityEngine;
using UnityEngine.Events;

namespace EnigmaVirtual.IO
{
    public class CodeReceivedEvent : UnityEvent<string> { }

    /// <summary>
    /// Reads a keyboard-wedge RFID or barcode scanner: the device types the code and presses Enter.
    /// </summary>
    /// <remarks>
    /// <para><b>The character comes from the OS, not from a table here.</b> This used to
    /// translate <c>KeyCode</c> to <c>char</c> with a hand-written switch, and the shifted-digit arm
    /// was <c>specials[key - KeyCode.Alpha0]</c> over
    /// <c>{'!','@','#','$','%','^','&amp;','*','(',')'}</c>. That array is the US shift row for keys
    /// <b>1 to 0</b>, indexed from <b><c>Alpha0</c></b> - so every one of the ten is off by a
    /// position: <c>Alpha1 + shift</c> returned <c>@</c> where a US keyboard gives <c>!</c>, and
    /// <c>Alpha0 + shift</c> returned <c>!</c> where it gives <c>)</c>. All ten were checked against
    /// the deleted source and none of them match a US keyboard, so this was not merely "the US
    /// layout hardcoded" - it was a <i>misremembered</i> US layout, wrong on the machines it was
    /// written on, and wrong twice over on the pt-PT and de-DE keyboards this company deploys on.
    /// <c>Event.character</c> is what the OS produced under the <i>active</i> layout, so the whole
    /// table is gone.</para>
    ///
    /// <para>IMGUI may deliver a keypress as one event carrying both <c>keyCode</c> and
    /// <c>character</c>, or as a pair where one half carries only the <c>keyCode</c> and the other
    /// only the <c>character</c>. Reading <c>character</c> and ignoring <c>'\0'</c> is correct under
    /// either, and cannot double-count.</para>
    ///
    /// <para><b>A Tab suffix is not a terminator here</b>, and never was - <c>char.IsControl</c>
    /// drops it exactly as the old table's <c>default</c> case did. Tab is a common wedge-scanner
    /// suffix, so a device configured that way transcribes every character and never completes a
    /// code; what is new is that the timeout now says so instead of discarding in silence.</para>
    ///
    /// <para><c>Input.anyKeyDown</c> used to gate this. It was the only legacy Input Manager call
    /// in the file, and it could drop characters: it is true only during the frame a key went down,
    /// while the event queue is drained separately. Gone on both counts.</para>
    ///
    /// <para>That does <b>not</b> settle whether <c>OnGUI</c> blocks an Input System migration. IMGUI key
    /// events are <i>believed</i> to work under either input backend, which would make the clause
    /// wrong rather than outstanding - but nothing here has tested it, so the entry stays partly
    /// fixed and this remark does not claim it.</para>
    /// </remarks>
    public class RFIDReader : MonoBehaviour
    {
        public CodeReceivedEvent onCodeReceivedEvent = new CodeReceivedEvent();

        /// <summary>
        /// Seconds of silence after which a partially-typed code is discarded. Was a bare <c>1</c>
        /// in <c>Update</c>; a wedge scanner types a whole code in a few tens of milliseconds, so
        /// this only ever fires on a human at the keyboard or a half-read scan.
        /// </summary>
        [SerializeField]
        [Min(0.05f)]
        [Tooltip("Seconds of silence after which a partially-typed code is discarded. 0 does NOT "
            + "mean 'no timeout' here - it is clamped to 0.05s. (AssetDownload and TextureCache read "
            + "a 0 timeout as 'wait forever'; this block does not. See M43.)")]
        private float codeTimeoutSeconds = 1f;

        /// <summary>
        /// Longest code accepted before the input is treated as a fault. <c>code +=</c> was
        /// unbounded, so a stuck key on the scanner grew a string without limit for as long as the
        /// component stayed active.
        /// </summary>
        [SerializeField]
        [Min(1)]
        [Tooltip("Longest code accepted. Beyond this the whole read is treated as a stuck key and "
            + "discarded. This is a fault ceiling, not a content limit - raise it rather than let a "
            + "long symbology hit it. 0 does NOT mean 'unlimited'; it is clamped to 1.")]
        private int maxCodeLength = 256;

        public bool IsActive { get; private set; }

        private string code = string.Empty;
        private float internalTimeout;

        /// <summary>
        /// So an overrun reports once per read rather than once per surplus character - a stuck key
        /// would otherwise write an error every frame for as long as it is held. Note "per read",
        /// not "once": ResetRFID clears this, so a genuinely stuck key still reports roughly once
        /// every codeTimeoutSeconds. That is intended - it is a fault that should keep saying so.
        /// </summary>
        private bool overflowReported;

        /// <summary>
        /// Both serialized values are clamped at use rather than trusted. <c>maxCodeLength = 0</c> is
        /// the intuitive way to write "no limit" and would instead make <c>code.Length &gt;= max</c>
        /// true for the very first character - so nothing accumulates, the timeout below never sees a
        /// non-empty code, <c>overflowReported</c> never clears, and the component goes permanently,
        /// silently dead while <c>IsActive</c> stays true.
        /// </summary>
        private int MaxCodeLength => Mathf.Max(1, maxCodeLength);

        private float CodeTimeoutSeconds => Mathf.Max(0.05f, codeTimeoutSeconds);

        private void Update()
        {
            if (!IsActive)
                return;

            internalTimeout += Time.deltaTime;

            if (internalTimeout >= CodeTimeoutSeconds && !string.IsNullOrEmpty(code))
            {
                // This used to discard in silence, and that was the worst failure in the file: a
                // scanner configured with a Tab suffix rather than Enter transcribes every character
                // correctly and then produces nothing, forever, with an empty log - so the site
                // reports "the scanner does not work" and there is no evidence at all. Always
                // compiled, because the platforms without DO_LOGS need it most.
                //
                // Length, not content: an RFID code can be an access credential, and unlike the
                // success log below this one is compiled into every build.
                Development.LogError($"RFID discarded {code.Length} character(s) after "
                    + $"{CodeTimeoutSeconds}s with no terminator. The scanner is sending characters "
                    + "but never Enter - check its suffix setting; a Tab suffix does not terminate "
                    + "a code here.");

                ResetRFID();
            }
        }

        private void OnGUI()
        {
            // Split from the handler so the transcription can be exercised at all: Event.current is
            // bound to a live IMGUI pass and reads back null if you assign it from anywhere else
            // (measured), so nothing can drive OnGUI from outside. HandleKeyEvent takes an Event it
            // was handed, which a probe can build and invoke by reflection - that is how the
            // cases were checked, since this repo has no test assembly and no caller for this class.
            HandleKeyEvent(Event.current);
        }

        private void HandleKeyEvent(Event e)
        {
            if (!IsActive)
                return;

            if (e == null || e.type != EventType.KeyDown)
                return;

            // Enter terminates the code. Checked on both halves of the event pair, because the
            // keyCode half carries Return/KeypadEnter and the character half carries '\n' or '\r'.
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter
                || e.character == '\n' || e.character == '\r')
            {
                internalTimeout = 0f;
                ProcessCode();
                return;
            }

            // '\0' is the keyCode-only half of a pair, or a modifier being pressed on its own.
            // Control characters are not part of a code.
            char character = e.character;
            if (character == '\0' || char.IsControl(character))
                return;

            AppendToCode(character);
        }

        public void SetActive(bool value)
        {
            if (value)
                ResetRFID();

            IsActive = value;
        }

        private void AppendToCode(char character)
        {
            if (code.Length >= MaxCodeLength)
            {
                if (!overflowReported)
                {
                    overflowReported = true;

                    // LogError, not LogWarning: LogWarning is [Conditional("DO_LOGS")] and DO_LOGS is
                    // defined only for Standalone and Android, so a warning would vanish on every
                    // other target and a jammed scanner would look like a scanner that reads nothing.
                    Development.LogError($"RFID input passed {MaxCodeLength} characters with no "
                        + "Enter, which means a stuck key or a scanner sending the wrong terminator. "
                        + "Everything scanned in this read is being discarded - a truncation would "
                        + "be a plausible wrong code, which is worse than none.");
                }

                // Deliberately does NOT reset internalTimeout, so a stuck key cannot hold the state
                // open: Update's timeout runs out and clears it.
                return;
            }

            internalTimeout = 0f;
            code += character;
        }

        private void ProcessCode()
        {
            if (string.IsNullOrEmpty(code))
                return;

            // An overrun read must not be delivered. The surplus characters were dropped, so what
            // survives is a TRUNCATION of what was actually scanned - a code that looks entirely
            // ordinary to the listener and is wrong. The earlier version of this fix let the
            // truncation through and relied on the timeout to discard it, which never happens on the
            // common path: a scanner sends the whole code and its Enter within one frame, so Enter
            // arrives long before any timeout.
            if (overflowReported)
            {
                Development.LogError($"RFID read was discarded: it passed {MaxCodeLength} characters "
                    + $"before its terminator, so the {code.Length} kept are only the start of what "
                    + "was scanned. Raise maxCodeLength if the codes really are this long.");
                ResetRFID();
                return;
            }

            string received = code;

            // Reset BEFORE raising, so a listener that throws cannot leave the previous code in
            // place to be appended to by the next scan.
            ResetRFID();

            Development.Log($"RFID code received: {received}");

            // No per-listener try/catch, and that is not an oversight: a UnityEvent's runtime
            // listeners are private and cannot be enumerated, so they cannot be isolated the way a
            // raw Action list can. Invoke stops at the first listener that throws. A destroyed
            // subscriber is safe, though - UnityEvent null-checks a UnityEngine.Object target before
            // calling, so the destroyed-delegate hazard does not apply here.
            onCodeReceivedEvent?.Invoke(received);
        }

        private void ResetRFID()
        {
            code = string.Empty;
            internalTimeout = 0;
            overflowReported = false;
        }

        // ToChar(Event) lived here and is deleted, not fixed. It mapped KeyCode to char through a
        // hand-written US-layout table, and there is no layout-aware version of that worth writing:
        // Event.character already holds what the OS resolved under the active layout.
        //
        // Not claimed: IME composition. That arrives through Input.compositionString /
        // GUIUtility.compositionString, not through Event.character, so a CJK IME machine may read
        // nothing here. Untested - there is no IME on any machine this has run on. Every Latin
        // layout this company deploys on is covered.
    }
}
