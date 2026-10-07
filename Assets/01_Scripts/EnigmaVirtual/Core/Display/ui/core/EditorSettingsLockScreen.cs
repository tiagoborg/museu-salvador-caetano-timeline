using System;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    [RequireComponent(typeof(Timer))]
    public class EditorSettingsLockScreen : PanelBase
    {
        public event Action onValidLoginEvent;

        [SerializeField]
        private InputField inputField;
        [SerializeField]
        private Button loginButton;
        [SerializeField]
        private Button closeButton;
        [SerializeField]
        private Text errorText;
        [SerializeField]
        private Timer autoHideTimer;

        private int _failedTries = 0;

        /// <summary>
        /// <see cref="Time.realtimeSinceStartup"/> before which no password is evaluated. Instance
        /// state, and that is the whole fix: <c>PanelBase.Hide</c> only fades a CanvasGroup, so this
        /// component is never destroyed and the deadline outlives closing and reopening the panel.
        /// Not persisted across an app restart, and the honest limit of that: <c>MainController</c>
        /// restarts the app on Ctrl+Alt+R, so anyone with a keyboard clears the deadline. It is a real
        /// rate limit against the threat it is for - a visitor with a touchscreen - and persisting it
        /// would mean a lockout file that the same keyboard could delete.
        /// </summary>
        private float _lockedUntilRealtime = 0f;

        /// <summary>Authored error string, so the lockout message can borrow the label and put it back.</summary>
        private string _authoredErrorText = string.Empty;

        protected override void Awake()
        {
            if (autoHideTimer == null)
                autoHideTimer = gameObject.GetComponent<Timer>();

            if (errorText != null)
                _authoredErrorText = errorText.text;

            // The scene in this repo authors a message, but this is the template every project branches
            // from, and the previous code never wrote to `.text` - so an unauthored label used to fail
            // safe and would now show a visible blank instead of "wrong password".
            if (string.IsNullOrEmpty(_authoredErrorText))
                _authoredErrorText = "Incorrect password.";

            base.Awake();
        }

        public override void Show(bool animate = true, Action callback = null)
        {
            SetError(null);
            inputField.text = string.Empty;

            // _failedTries is deliberately NOT reset here. Resetting it on Show is what made the
            // lockout meaningless: three wrong guesses hid the panel, and the next long-press reopened
            // it with a fresh count, so the attempt limit cost an attacker one extra gesture.

            // Remove before add: Show is reachable while already visible - OnLongTouchClicked calls it
            // on every long press - and a doubled listener would count two attempts per click.
            loginButton.onClick.RemoveListener(OnLoginButtonClicked);
            closeButton.onClick.RemoveListener(OnCloseButtonClicked);
            loginButton.onClick.AddListener(OnLoginButtonClicked);
            closeButton.onClick.AddListener(OnCloseButtonClicked);
            base.Show(animate, callback);

            // Same reason as the buttons above, and this one was missed on the first pass: Show is
            // reachable while already visible, and UnityEvent does not deduplicate listeners.
            autoHideTimer.onTimerCompleteEvent.RemoveListener(OnTimerCompleteEvent);
            autoHideTimer.onTimerCompleteEvent.AddListener(OnTimerCompleteEvent);
            autoHideTimer.SetTimer(Core.settings.EditorSettings.autoHideTime);
            autoHideTimer.StartTimer();
        }

        public override void Hide(bool animate = true, Action callback = null)
        {
            autoHideTimer.StopTimer();
            autoHideTimer.onTimerCompleteEvent.RemoveListener(OnTimerCompleteEvent);
            loginButton.onClick.RemoveListener(OnLoginButtonClicked);
            closeButton.onClick.RemoveListener(OnCloseButtonClicked);
            base.Hide(animate, callback);
        }

        private void OnCloseButtonClicked()
        {
            Hide();
        }

        private void OnTimerCompleteEvent()
        {
            Hide();
        }

        private void OnLoginButtonClicked()
        {
            SetError(null);

            PanelAccessSettings settings = Core.settings.EditorSettings;
            int maxTries = Mathf.Max(1, settings.maxTries);
            int lockoutSeconds = settings.lockoutSeconds;

            float lockRemaining = _lockedUntilRealtime - Time.realtimeSinceStartup;
            if (lockRemaining > 0f)
            {
                SetError($"Locked. Try again in {Mathf.CeilToInt(lockRemaining)}s.");
                return;
            }

            if (string.IsNullOrEmpty(settings.passwordHash) && string.IsNullOrEmpty(settings.password))
            {
                // Fail closed, and say so. A panel that opens for anyone because a key is missing from
                // settings.json would be a far worse outcome than one nobody can open.
                Development.LogError("the settings panel cannot be unlocked: EditorSettings has neither "
                    + "passwordHash nor password, so there is nothing to check against. Set one with "
                    + "Gema > Set Panel Password. (H14)");
                SetError("No password configured.");
                return;
            }

            bool accepted = IsPasswordAccepted(inputField.text, settings);
            inputField.text = string.Empty;

            if (accepted)
            {
                _failedTries = 0;
                _lockedUntilRealtime = 0f;
                onValidLoginEvent?.Invoke();
                return;
            }

            _failedTries++;

            if (_failedTries < maxTries)
            {
                SetError(_authoredErrorText);
                return;
            }

            _failedTries = 0;

            // A 0 here is a value an operator typed, not an unset field (an absent JSON key keeps the
            // 60 initializer), so it is honoured as "no rate limiting" - but it is said out loud rather
            // than performed. The first version of this clamped with Mathf.Max(0, ...), which set the
            // deadline to *now*: the next click found no time remaining, _failedTries had just been
            // reset, and guessing continued unimpeded while the label read "Locked for 0s." and the log
            // claimed a lockout had been applied. A control that reports success and does nothing is
            // this codebase's most common defect shape.
            if (lockoutSeconds <= 0)
            {
                Development.LogError($"{maxTries} failed password attempts, but "
                    + $"EditorSettings.lockoutSeconds is {lockoutSeconds}, so the settings panel is NOT "
                    + "rate-limited - guessing can continue immediately. Set it above 0 to enable the "
                    + "lockout. (H14)");

                SetError(_authoredErrorText);
                Hide();
                return;
            }

            _lockedUntilRealtime = Time.realtimeSinceStartup + lockoutSeconds;

            // LogError rather than LogWarning: repeated lockouts on an unattended machine are the one
            // signal that someone is guessing at it, and LogWarning is [Conditional("DO_LOGS")], so it
            // would compile away on exactly the platforms with no console to watch.
            Development.LogError($"settings panel locked for {lockoutSeconds}s after {maxTries} failed "
                + "password attempts. Closing and reopening the panel does NOT clear this. (H14)");

            // No SetError here: Hide() fades over transitionTime, so the message would flash for an
            // eighth of a second. The useful telling is on the way back in, where the first click
            // reports the time still remaining.
            Hide();
        }

        /// <summary>
        /// Prefers <see cref="PanelAccessSettings.passwordHash"/>; falls back to the legacy plaintext field
        /// so that updating the framework cannot lock an installation out of its own panel.
        /// </summary>
        private static bool IsPasswordAccepted(string input, PanelAccessSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.passwordHash))
                return PanelPassword.Verify(input, settings.passwordHash);

            // Once per attempt, which is human-paced, and it is the only nudge an operator gets. It does
            // fire on this template, which still ships a plaintext password by definition - that is the
            // point of the message, not an oversight.
            Development.LogError("the settings-panel password is being compared in PLAINTEXT from "
                + "settings.json, which is committed in this repo and in every project branched from it. "
                + "Run Gema > Set Panel Password to replace it with a hash. (H14)");

            return string.Equals(input, settings.password);
        }

        /// <summary>
        /// Shows <paramref name="message"/> on the error label, or hides the label when it is null.
        /// </summary>
        private void SetError(string message)
        {
            if (errorText == null)
                return;

            if (message == null)
            {
                errorText.gameObject.SetActive(false);
                return;
            }

            errorText.text = message;
            errorText.gameObject.SetActive(true);
        }
    }
}
