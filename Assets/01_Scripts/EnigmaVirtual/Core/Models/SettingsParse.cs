using System.Globalization;

namespace EnigmaVirtual
{
    /// <summary>
    /// Parsing helpers shared by the settings DTOs' <c>SetData</c> methods.
    ///
    /// Those are fed raw text an operator typed into the on-device settings panel, and
    /// <see cref="ConfigsPanel"/> saves every field in one loop with no try/catch — so a bare
    /// <c>bool.Parse</c>/<c>int.Parse</c> throwing on a typo aborts the whole loop, leaving the
    /// fields after it unsaved with no notification. These keep the previous value instead and
    /// report what happened.
    /// </summary>
    internal static class SettingsParse
    {
        /// <summary>Highest valid TCP/UDP port.</summary>
        internal const int MaxPort = 65535;

        internal static bool Bool(string key, string value, bool fallback)
        {
            if (bool.TryParse(value, out bool parsed))
                return parsed;

            Error($"SetData [{key}]: '{value}' is not true/false - keeping {fallback}.");
            return fallback;
        }

        internal static int Int(string key, string value, int fallback, int min = int.MinValue, int max = int.MaxValue)
        {
            // Integer + InvariantCulture: no thousands separators, no culture-dependent digits.
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                Error($"SetData [{key}]: '{value}' is not a whole number - keeping {fallback}.");
                return fallback;
            }

            return InRange(key, parsed, fallback, min, max);
        }

        internal static float Float(string key, string value, float fallback, float min = float.MinValue, float max = float.MaxValue)
        {
            // A bare float.TryParse uses CurrentCulture *and* allows thousands separators, so on
            // an en-US machine an operator typing "5,5" silently gets 55 - a 10x error where the
            // old int.Parse at least threw. Normalise the decimal mark and pin the culture:
            // these are seconds and intervals, so a comma is a decimal point, never a grouping.
            string normalised = value == null ? null : value.Trim().Replace(',', '.');

            if (!float.TryParse(normalised, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                Error($"SetData [{key}]: '{value}' is not a number - keeping {fallback}.");
                return fallback;
            }

            return InRange(key, parsed, fallback, min, max);
        }

        private static T InRange<T>(string key, T parsed, T fallback, T min, T max) where T : System.IComparable<T>
        {
            if (parsed.CompareTo(min) < 0)
            {
                Error($"SetData [{key}]: {parsed} is below the minimum of {min} - keeping {fallback}.");
                return fallback;
            }

            if (parsed.CompareTo(max) > 0)
            {
                Error($"SetData [{key}]: {parsed} is above the maximum of {max} - keeping {fallback}.");
                return fallback;
            }

            return parsed;
        }

        /// <summary>
        /// Splits a comma-separated operator entry. Returns an empty array rather than
        /// <c>[""]</c>, which is what <c>"".Split(',')</c> gives and which passes
        /// <c>IsNullOrEmpty()</c> checks while carrying one blank entry.
        /// </summary>
        internal static string[] CsvList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new string[0];

            return value.Replace(" ", string.Empty).Split(',');
        }

        /// <summary>
        /// How many values have been rejected since <see cref="ResetErrorCount"/>.
        /// </summary>
        /// <remarks>
        /// <para>Exists so <see cref="ConfigsPanel"/> can tell an operator that a field did not take.
        /// Every <c>SetData</c> keeps the previous value and logs, but <c>SettingInput.Save</c> returns
        /// <c>void</c>, so the panel had no way to know and reported the whole save as a success while
        /// the rejected text stayed in the box. Every visible signal said it worked and only
        /// <c>Player.log</c> disagreed.</para>
        ///
        /// <para>Process-wide mutable state, which is safe here for a specific reason rather than by
        /// luck: <c>SetData</c> is the only caller of anything in this class, and the panel is the only
        /// caller of <c>SetData</c>. Boot-time settings parsing goes through <c>JsonUtility</c> and never
        /// touches this. If that ever stops being true, this counter starts lying.</para>
        /// </remarks>
        internal static int ErrorCount { get; private set; }

        /// <summary>Call before a batch of <c>SetData</c> calls whose outcome you intend to report.</summary>
        internal static void ResetErrorCount() => ErrorCount = 0;

        /// <summary>
        /// Reports a value the operator typed that could not be used.
        /// </summary>
        /// <remarks>
        /// <c>UnityEngine.Debug.LogError</c> rather than <c>Development.LogError</c>, but <b>not</b> for
        /// the reason this comment used to give. It claimed <c>Development.LogError</c> is
        /// <c>[Conditional("DO_LOGS")]</c> and would vanish - that is no longer true. A later change
        /// made <c>LogError</c>/<c>LogException</c> unconditional, and the banner in
        /// <c>Development.cs</c> says so. The remaining reason is narrower and still worth it: this is the
        /// one message telling an operator their settings edit did not take, so it goes straight to Unity
        /// rather than through a wrapper any future change could gate.
        /// </remarks>
        internal static void Error(string message)
        {
            ErrorCount++;
            UnityEngine.Debug.LogError(message);
        }
    }
}
