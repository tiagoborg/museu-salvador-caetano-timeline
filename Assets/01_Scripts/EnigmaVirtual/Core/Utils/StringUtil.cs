using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EnigmaVirtual
{
    public static class StringUtil
    {
        private static readonly Regex rxNonDigits = new Regex(@"[^\d]+");

        /// <summary>
        /// Converts a time value in seconds to a clock format (MM:SS).
        /// </summary>
        /// <remarks>
        /// <b>Produced "01:60" for 119.6 seconds.</b> The minutes were floored while the seconds were
        /// handed to <c>ToString("00")</c>, which <i>rounds</i> - so 59.6 became "60" beside a minute
        /// count that had not carried. Every value in the last 0.5s of a minute displayed a clock that
        /// does not exist.
        /// <para>
        /// Delegates to <see cref="TimeFormat"/>, which truncates both halves and was always correct.
        /// The two were duplicates and only one of them worked; deprecated rather than deleted because
        /// this is framework API a branched project may call, and it does real work - as does
        /// <c>Timer.SetDuration</c>.
        /// </para>
        /// </remarks>
        [Obsolete("Use TimeFormat instead - ToClock was the same method with a rounding bug.")]
        public static string ToClock(float time) => TimeFormat(time);

        /// <summary>
        /// Formats an elapsed duration the way an operator reads it: "3 day(s)", "45 minute(s)".
        /// </summary>
        /// <remarks>
        /// For cache ages and other "how old is this?" reporting, where the alternatives read badly in
        /// a log: <c>TimeSpan.ToString()</c> gives <c>3.00:00:00</c> and <c>TotalSeconds</c> gives
        /// <c>259200</c>, and both get skipped. Distinct from <see cref="TimeFormat"/>, which is a
        /// clock (<c>MM:SS</c>) for durations a user is watching, and from
        /// <c>IntExtensions.FormatTimeFull</c> (<c>HH:MM:SS</c>) - neither produces "2 day(s)".
        /// <para>
        /// <b>A negative span is not clamped to zero.</b> It means the file is dated in the future -
        /// a kiosk with a dead CMOS battery, or a copy from a machine whose clock ran ahead - and
        /// flooring it would make the caller assert that arbitrarily old content is fresh, which is
        /// worse than saying nothing - a trap this codebase hit twice, in two files.
        /// </para>
        /// <para>
        /// <c>internal</c>, deliberately. It has two callers and both are inside this assembly, so
        /// making it <c>public</c> would add framework API every branched project inherits forever to
        /// remove a duplicate that no branch can see. Widen it when a project needs it - that is the
        /// one-member-at-a-time rule the asmdef split established.
        /// </para>
        /// </remarks>
        internal static string FormatElapsed(TimeSpan age)
        {
            if (age < TimeSpan.Zero)
                return "an unreadable amount of time (the timestamp is in the future)";

            if (age.TotalDays >= 1)
                return $"{(int)age.TotalDays} day(s)";

            if (age.TotalHours >= 1)
                return $"{(int)age.TotalHours} hour(s)";

            if (age.TotalMinutes >= 1)
                return $"{(int)age.TotalMinutes} minute(s)";

            return $"{(int)age.TotalSeconds} second(s)";
        }

        /// <summary>
        /// Formats a time value in seconds to a string in the format "MM:SS".
        /// </summary>
        /// <param name="time">The time in seconds.</param>
        /// <returns>The time in "MM:SS" format.</returns>
        public static string TimeFormat(float time)
        {
            // Negative input would render "0-1:-5" - the D2 format pads the digits, not the sign, and
            // each half carries its own. Clamped rather than special-cased: a clock has no negative
            // reading, and the one caller (TimeControllerDisplay) counts down toward zero.
            if (time < 0f)
                time = 0f;

            int seconds = (int)(time % 60);
            int minutes = (int)(time / 60);
            return string.Format("{0:D2}:{1:D2}", minutes, seconds);
        }

        /// <summary>
        /// Calculates the MD5 hash of the input string and returns it as a hexadecimal string.
        /// </summary>
        /// <param name="input">The input string.</param>
        /// <returns>The MD5 hash as a hexadecimal string.</returns>
        /// <remarks>
        /// <b>Not for passwords or anything secret</b> - MD5 is broken for that, and
        /// <see cref="PanelPassword"/> says so at its own hash. This is a content fingerprint: cache
        /// keys, change detection, filenames.
        /// <para>
        /// <b>The <c>[Obsolete]</c> carries that warning to the call site; it is not retiring the
        /// method.</b> This prose does not reach a caller - quick-info shows the summary, and someone
        /// reaching for a method with <c>Hash</c> in its name has usually stopped reading by then. A
        /// compiler warning is the only channel that fires at the moment of misuse, and in a base
        /// project that moment happens in someone else's repo. Same reasoning that put the default
        /// password behind a build gate rather than a comment.
        /// </para>
        /// <para>
        /// <b>The encoding changed in v0.33.0 and so did the hash for non-ASCII input.</b> It was
        /// <c>Encoding.ASCII</c>, which maps every byte above 0x7F to <c>?</c> - so any two strings
        /// differing <i>only</i> in characters above 0x7F hashed identically. Note what that does and
        /// does not mean: two differently-accented spellings of a word collided with each other, but
        /// not with the unaccented spelling, whose letters are real ASCII and survive. An earlier
        /// draft of this comment said the opposite. UTF8 now.
        /// A branched project comparing against stored hashes of text above 0x7F will see them all
        /// miss once; they were wrong before, not now.
        /// </para>
        /// </remarks>
        [Obsolete("Content fingerprints only - cache keys, change detection, filenames. MD5 is not " +
                  "safe for credentials or anything secret; use PanelPassword for those. NOTE: v0.33.0 " +
                  "switched ASCII to UTF8, so the hash CHANGED for any input above 0x7F.", error: false)]
        public static string CalculateMD5Hash(string input)
        {
            if (input == null)
            {
                Development.LogError("CalculateMD5Hash called with null.");
                return string.Empty;
            }

            // using: MD5 holds unmanaged state and is IDisposable. One per call was leaked before.
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sb = new StringBuilder(hash.Length * 2);

                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("X2"));

                return sb.ToString();
            }
        }

        /// <summary>
        /// Prints the contents of an array to the log, with an optional name for identification.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="array"></param>
        /// <param name="name"></param>
        public static void PrintArray<T>(T[] array, string name = "")
        {
            if (array == null)
            {
                Development.Log(" " + name + " :{null}");
                return;
            }

            // string.Join, not += in a loop: the old form allocated a new string per element, so
            // printing a 550-entry media list built 550 strings to throw 549 of them away.
            // A null element used to throw inside ToString(); it prints as empty now.
            Development.Log(" " + name + " :{" + string.Join(",", array) + "}");
        }

        /// <summary>
        /// Checks if the provided string is a valid date in the formats "dd.MM.yyyy", "dd-MM-yyyy", or "dd/MM/yyyy".
        /// </summary>
        /// <param name="dateTime"></param>
        /// <returns></returns>
        public static bool IsValidDateTime(string dateTime)
        {
            string[] formats = new[] { "dd.MM.yyyy", "dd-MM-yyyy", "dd/MM/yyyy" };
            DateTime parsedDateTime;
            return DateTime.TryParseExact(
                dateTime,
                formats,
                DateTimeFormatInfo.InvariantInfo,
                DateTimeStyles.None,
                out parsedDateTime);
        }

        /// <summary>
        /// Removes the file name from a given URL or path, returning the directory path.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        /// <remarks>
        /// <b>The one method in this file on a live path</b> - <c>AssetManager</c> uses the result as the
        /// folder to create before downloading an asset - and it was wrong in three ways.
        /// <list type="number">
        /// <item><b>It split on <c>'/'</c> only</b>, and returned <b>empty</b> for a path whose final
        /// separator was a backslash - so the download folder was never created. Note where that path
        /// comes from, because it is not what it looks like: the framework's own composition is
        /// forward-slash throughout (<c>AppDataSettings</c> concatenates a literal <c>/</c> and collapses
        /// doubles by regex), so a backslash arrives only through a **rooted value authored in the CMS**,
        /// which <c>GetAssetPath</c> returns verbatim. Mixed separators are still the general rule on
        /// Windows; they just are not the mechanism here.</item>
        /// <item><b><c>Replace</c> removes every occurrence, not the last one.</b> A path whose directory
        /// repeats the file's own name - <c>.../logo.png/logo.png</c> - came back with both gone.</item>
        /// <item><b>A trailing separator returned empty</b>, discarding the whole path, because the last
        /// element was the empty string and the guard rejected it.</item>
        /// </list>
        /// One <c>LastIndexOfAny</c> answers all three. The trailing separator is kept, which is what the
        /// old <c>Replace</c> produced on the path that worked, so <c>AssetManager</c>'s normalisation
        /// downstream is unaffected.
        /// </remarks>
        public static string RemoveFileNameFromPath(string url)
        {
            if (string.IsNullOrEmpty(url))
                return string.Empty;

            int cut = url.LastIndexOfAny(PathSeparators);

            // No separator at all means a bare filename, which has no directory part. Empty is the
            // honest answer and is what the old code returned for this case too.
            return cut < 0 ? string.Empty : url.Substring(0, cut + 1);
        }

        /// <summary>Both separators, because a composed path on Windows carries both.</summary>
        private static readonly char[] PathSeparators = { '/', '\\' };

        /// <summary>
        /// Extracts the file name from a given URL or path.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        /// <remarks>
        /// Same mixed-separator problem as <see cref="RemoveFileNameFromPath"/>, and the two now share
        /// <c>PathSeparators</c> so they cannot drift apart. <c>Api.cs</c> already carried a comment
        /// noting this one split on <c>'/'</c> only.
        /// </remarks>
        public static string GetFileNameFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return string.Empty;

            int cut = url.LastIndexOfAny(PathSeparators);

            return cut < 0 ? url : url.Substring(cut + 1);
        }

        /// <remarks>
        /// <c>RandomNumberGenerator.Create()</c> in a <c>using</c>, matching <see cref="PanelPassword"/>,
        /// which already did it this way. It was <c>new RNGCryptoServiceProvider()</c> - undisposed, and
        /// the type is obsolete from .NET 6 on, so it would fail to compile the day this project moves
        /// off the .NET Framework profile.
        /// <para>
        /// Note the return is Base64, so the string is about 4/3 the length of
        /// <paramref name="keyLength"/> - the parameter counts <b>bytes of entropy</b>, not characters.
        /// </para>
        /// </remarks>
        public static string GenerateRandomCryptographicKey(int keyLength)
        {
            if (keyLength <= 0)
            {
                Development.LogError($"GenerateRandomCryptographicKey asked for {keyLength} bytes.");
                return string.Empty;
            }

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                byte[] randomBytes = new byte[keyLength];
                rng.GetBytes(randomBytes);
                return Convert.ToBase64String(randomBytes);
            }
        }

        /// <summary>
        /// Thousands-separated, in <c>pt-PT</c> where that culture is available and invariant otherwise.
        /// </summary>
        /// <remarks>
        /// <b>This threw outright where culture data is stripped.</b> <c>CultureInfo.GetCultureInfo</c>
        /// raises <c>CultureNotFoundException</c> under invariant globalization - which is what a
        /// culture-stripped IL2CPP or WebGL build runs in - so formatting a number threw. The fallback to
        /// invariant is the fix; the worst case is now differently-grouped digits rather than an
        /// exception. The static is tidiness, not speed - <c>GetCultureInfo</c> is itself a cached
        /// lookup, which is its whole difference from <c>new CultureInfo</c>.
        /// <para>
        /// The hardcoded <c>pt-PT</c> is a client-specific default in a base project, the same shape as
        /// the API's <c>sessionName</c>. Left in place because changing it is a visible behaviour change for
        /// every branch; the fallback is the part that makes it safe.
        /// </para>
        /// </remarks>
        public static string FormatNumbers(int value) => value.ToString("N0", NumberCulture);

        /// <inheritdoc cref="FormatNumbers(int)"/>
        public static string FormatNumbers(float value) => value.ToString("N0", NumberCulture);

        private static CultureInfo numberCulture;

        private static CultureInfo NumberCulture
        {
            get
            {
                if (numberCulture != null)
                    return numberCulture;

                try
                {
                    numberCulture = CultureInfo.GetCultureInfo("pt-PT");
                }
                catch (CultureNotFoundException)
                {
                    // Not an error: a culture-stripped build is a deployment choice, not a fault. The
                    // number still formats, with invariant grouping.
                    // LogError, not LogWarning: LogWarning is [Conditional("DO_LOGS")], defined for
                    // Android and Standalone only - so on WebGL, the target this message is about, the
                    // only signal that formatting silently changed would have been compiled out.
                    Development.LogError("Culture [pt-PT] is unavailable in this build, so numbers are "
                        + "formatted with the invariant culture. Expected on a culture-stripped IL2CPP "
                        + "or WebGL player.");
                    numberCulture = CultureInfo.InvariantCulture;
                }

                return numberCulture;
            }
        }

        //public static string FormatPrice(string value) => value + " " + Core.locale["coin"];

        //public static string FormatCredits(string value) => value + " " + Core.locale["cr"];

        //public static string FormatCredits(float value) => value + " " + Core.locale["cr"];

        //public static string FormatCredits(double value) => value + " " + Core.locale["cr"];        

        //public static string RichTextColor(Color32 color, string text) => "<color=#" + ColorUtil.ColorToHex(color) + ">" + text + "</color>";

        //public static string RichTextSize(int size, string text) => "<size=" + size + ">" + text + "</size>";
    }
}