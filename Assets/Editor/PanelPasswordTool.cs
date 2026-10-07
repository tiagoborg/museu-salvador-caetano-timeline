/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * PanelPasswordTool can not be copied and/or distributed without
 * the express permission of Gema Digital
 *******************************************************/
using UnityEditor;
using UnityEngine;

namespace EnigmaVirtual
{
    /// <summary>
    /// Generates a <see cref="PanelPassword"/> hash for <c>EditorSettings.passwordHash</c>, and verifies
    /// an existing one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It deliberately does not write settings.json.</b> That file is hand-maintained, 4-space indented
    /// with braces on their own lines - a shape <c>JObject.ToString(Formatting.Indented)</c> does not
    /// reproduce - so rewriting it through Newtonsoft would reformat every line and turn a two-key change
    /// into an unreviewable diff. It also would not help where it is most needed: the config that matters
    /// on a site is the deployed one under <c>persistentDataPath</c>, which this editor cannot reach.
    /// Copy-to-clipboard works for both.
    /// </para>
    /// <para>
    /// The verify box is the part worth keeping: it confirms a hash accepts the intended password
    /// <i>before</i> anyone deploys it, using the same code path the panel uses at runtime.
    /// </para>
    /// </remarks>
    public class PanelPasswordTool : EditorWindow
    {
        private const int ShortPasswordLength = 6;

        private string _password = string.Empty;
        private string _confirm = string.Empty;
        private string _generated = string.Empty;

        private string _verifyHash = string.Empty;
        private string _verifyPassword = string.Empty;
        private string _verifyResult = string.Empty;

        [MenuItem("Gema/Set Panel Password", false, 3)]
        private static void Init()
        {
            PanelPasswordTool window = GetWindow<PanelPasswordTool>(true, "Set Panel Password");
            window.minSize = new Vector2(520f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Generates the value for EditorSettings.passwordHash in "
                + "settings.json. The panel prefers passwordHash over the legacy plaintext password "
                + "field, so set one and blank the other.", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("New password", EditorStyles.boldLabel);

            _password = EditorGUILayout.PasswordField("Password", _password);
            _confirm = EditorGUILayout.PasswordField("Confirm", _confirm);

            bool empty = string.IsNullOrEmpty(_password);
            bool mismatch = !empty && _password != _confirm;

            if (mismatch)
                EditorGUILayout.HelpBox("The two entries do not match.", MessageType.Error);
            else if (!empty && _password.Length < ShortPasswordLength)
                EditorGUILayout.HelpBox($"Shorter than {ShortPasswordLength} characters. Allowed, but a "
                    + "short password is guessable at the rate the panel's lockout permits.",
                    MessageType.Warning);

            using (new EditorGUI.DisabledScope(empty || mismatch))
            {
                if (GUILayout.Button("Generate hash and copy to clipboard"))
                {
                    _generated = PanelPassword.Hash(_password);
                    EditorGUIUtility.systemCopyBuffer = _generated;

                    // Not the password, and not into the log: Editor.log is a file that gets attached to
                    // support mails. The hash is safe to name; the password never appears anywhere.
                    Debug.Log("[PanelPasswordTool] Hash generated and copied to the clipboard. Paste it "
                        + "into EditorSettings.passwordHash and set EditorSettings.password to \"\".");
                }
            }

            if (!string.IsNullOrEmpty(_generated))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("passwordHash", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(_generated, EditorStyles.textArea,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f));

                EditorGUILayout.HelpBox("In settings.json, set:\n"
                    + "    \"passwordHash\": \"<the value above>\",\n"
                    + "    \"password\": \"\"\n\n"
                    + "By hand, in both places: the shipped template "
                    + "(Assets/StreamingAssets/config/settings.json) and any already-deployed config "
                    + "under persistentDataPath, because the deployed copy wins at runtime. "
                    + "BuildValidator warns on every build while password is still set, and fails a "
                    + "release build.\n\n"
                    + "THEN CLEAR PLAYER PREFS - Gema > Clear PlayerPrefs in the editor, or the "
                    + "Reset button in the app's own settings panel on a deployed machine, where the "
                    + "menu item does not exist. Core prefers a "
                    + "PlayerPrefs[\"Settings\"] snapshot over settings.json when one exists, and that "
                    + "snapshot has no passwordHash key - so on any machine that ever used the settings "
                    + "panel to save, the old plaintext password keeps working and none of this takes "
                    + "effect. On Android and iOS that snapshot is the only save target.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Verify an existing hash", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Same code path the panel uses, so a pass here means the panel "
                + "will accept it.", EditorStyles.wordWrappedMiniLabel);

            _verifyHash = EditorGUILayout.TextField("passwordHash", _verifyHash);
            _verifyPassword = EditorGUILayout.PasswordField("Password to test", _verifyPassword);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_verifyHash)))
            {
                if (GUILayout.Button("Verify"))
                {
                    // Trimmed: pasting out of a JSON file picks up quotes and whitespace far more often
                    // than not, and "it did not work" would be blamed on the hash rather than the paste.
                    string hash = _verifyHash.Trim().Trim('"', ',');

                    _verifyResult = PanelPassword.Verify(_verifyPassword, hash)
                        ? "MATCH - the panel will accept this password."
                        : "NO MATCH - the panel will reject this password. If the Console shows a "
                          + "malformed-hash error, the pasted value is the problem, not the password.";
                }
            }

            if (!string.IsNullOrEmpty(_verifyResult))
            {
                EditorGUILayout.HelpBox(_verifyResult,
                    _verifyResult.StartsWith("MATCH") ? MessageType.Info : MessageType.Warning);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Run self-test"))
                RunSelfTest();
        }

        /// <summary>
        /// Exercises <see cref="PanelPassword"/> against the cases that matter, and reports a count.
        /// </summary>
        /// <remarks>
        /// This project has no test framework, and adding one for a single class is not the trade worth
        /// making - but a hash that silently accepts everything, or throws inside a click handler, is the
        /// exact defect shape this register is full of, so the check has to live somewhere runnable rather
        /// than in whoever-wrote-it's scratch folder. A button is the cheapest place that survives.
        ///
        /// The malformed cases are not decoration: a 3-byte salt makes <c>Rfc2898DeriveBytes</c> throw, and
        /// that one reached a passing state only because it is guarded explicitly.
        /// </remarks>
        private static void RunSelfTest()
        {
            int passed = 0, failed = 0;

            System.Action<string, bool> check = (name, condition) =>
            {
                if (condition)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    Debug.LogError("[PanelPassword self-test] FAILED: " + name);
                }
            };

            string hash = PanelPassword.Hash("gema");

            // Timed FIRST, before the assertions below warm the JIT. The number is meant to describe the
            // frame a kiosk actually freezes for, and a warm measurement flatters it.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            PanelPassword.Verify("gema", hash);
            stopwatch.Stop();

            check("a correct password verifies", PanelPassword.Verify("gema", hash));
            check("a wrong password is rejected", !PanelPassword.Verify("gemaa", hash));
            check("an empty attempt is rejected", !PanelPassword.Verify(string.Empty, hash));
            check("comparison is case-sensitive", !PanelPassword.Verify("Gema", hash));
            check("a null attempt is rejected", !PanelPassword.Verify(null, hash));
            check("an accented password round-trips", PanelPassword.Verify("pás$wörd", PanelPassword.Hash("pás$wörd")));
            check("hashing is salted (two hashes differ)", PanelPassword.Hash("gema") != PanelPassword.Hash("gema"));
            check("a second salted hash still verifies", PanelPassword.Verify("gema", PanelPassword.Hash("gema")));
            check("an empty password yields no hash", PanelPassword.Hash(string.Empty) == string.Empty);
            check("an empty stored hash accepts nothing", !PanelPassword.Verify("gema", string.Empty));
            check("a generated hash is recognised by shape", PanelPassword.LooksLikeHash(hash));
            check("a plaintext password is not mistaken for a hash", !PanelPassword.LooksLikeHash("gema"));

            // Every one of these must return false without throwing. Expect Console errors from
            // PanelPassword itself while this runs - those are the report, not a failure.
            string[] malformed =
            {
                "garbage",
                "pbkdf2-sha1$10000$$",
                "pbkdf2-sha1$abc$AAAAAAAAAAAAAAAAAAAAAA==$AAAA",
                "pbkdf2-sha1$0$AAAAAAAAAAAAAAAAAAAAAA==$AAAA",
                "pbkdf2-sha1$999999999$AAAAAAAAAAAAAAAAAAAAAA==$AAAA",
                "pbkdf2-sha1$10000$!!notbase64!!$AAAA",
                "pbkdf2-sha1$10000$AAAA$AAAA",
                "pbkdf2-sha256$10000$AAAAAAAAAAAAAAAAAAAAAA==$AAAA",
                "a$b$c$d$e",

                // Just outside each bound rather than far outside it, so an off-by-one in a guard fails
                // here instead of passing. 200001 iterations, a 65-byte salt and a 65-byte hash.
                "pbkdf2-sha1$200001$AAAAAAAAAAAAAAAAAAAAAA==$AAAA",
                "pbkdf2-sha1$10000$" + new string('A', 88) + "$AAAA",
                "pbkdf2-sha1$10000$AAAAAAAAAAAAAAAAAAAAAA==$" + new string('A', 88)
            };

            foreach (string bad in malformed)
            {
                bool rejected;

                try
                {
                    rejected = !PanelPassword.Verify("gema", bad);
                }
                catch (System.Exception e)
                {
                    rejected = false;
                    Debug.LogError($"[PanelPassword self-test] threw on '{bad}': {e.GetType().Name}");
                }

                check($"malformed stored hash rejected without throwing: '{bad}'", rejected);
            }

            string summary = $"[PanelPassword self-test] {passed} passed, {failed} failed. "
                + $"The first verification took {stopwatch.ElapsedMilliseconds} ms - that is a frozen "
                + "frame on every login attempt, so treat anything above ~100 ms as a UX regression.";

            if (failed == 0)
                Debug.Log(summary);
            else
                Debug.LogError(summary);
        }
    }
}
