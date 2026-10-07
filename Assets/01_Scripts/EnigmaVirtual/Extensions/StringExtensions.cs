using EnigmaVirtual;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using UnityEngine;

public static class StringExtensions
{
    private static readonly Regex rxNonDigits = new Regex(@"[^\d]+");

    /// <summary>
    /// Converts a HTML color string into UnityEngine.Color. 
    /// See UnityEngine.ColorUtility.TryParseHtmlString for conversion conditions.
    /// </summary>
    public static Color ToUnityColor(this string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            Development.LogError("Source color null or empty!");
            return Color.white;
        }

        if (!source.StartsWith("#"))
            source = "#" + source;

        if (source.Length == 7)
            source += "FF";

        ColorUtility.TryParseHtmlString(source, out Color result);
        return result;
    }

    /// <summary>
    /// Determines whether the end of this string instance matches the specified strings.
    /// </summary>
    /// <param name="value">The string to compare to the substring at the end of this instance.</param>
    /// <param name="suffixes">suffixes to test with</param>
    /// <returns>true if any suffix matches the end of this instance; otherwise, false.</returns>
    public static bool EndsWithAny(this string value, params string[] suffixes)
    {
        return suffixes.Any(value.EndsWith);
    }

    /// <summary>
    /// Determines whether two specified System.String objects have the same value.A parameter specifies the culture, case, and sort rules used in the comparison
    /// </summary>
    /// <param name="a"></param>
    /// <returns>true if the value of the a parameter is equal to the value of the b parameter; otherwise, false.</returns>
    public static bool EqualsIgnoreCase(this string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Turns first letter to uppercase
    /// </summary>
    /// <param name="input"></param>
    /// <returns>Returns input with first letter in upper case</returns>
    public static string FirstCharToUpper(this string input)
    {
        switch (input)
        {
            case null: throw new ArgumentNullException(nameof(input));
            case "": throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input));
            default: return input.First().ToString().ToUpper() + input.Substring(1);
        }
    }

    /// <summary>
    /// Validates if it's in valid e-mail format.
    /// </summary>
    /// <param name="input"></param>
    /// <returns>Return true if input string it's in valid e-mail format.</returns>
    public static bool IsValidEmail(this string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            // Normalize the domain
            email = Regex.Replace(email, @"(@)(.+)$", DomainMapper, RegexOptions.None, TimeSpan.FromMilliseconds(200));

            // Examines the domain part of the email and normalizes it.
            string DomainMapper(Match match)
            {
                // Use IdnMapping class to convert Unicode domain names.
                IdnMapping idn = new IdnMapping();

                // Pull out and process domain name (throws ArgumentException on invalid)
                string domainName = idn.GetAscii(match.Groups[2].Value);

                return match.Groups[1].Value + domainName;
            }
        }
        catch (RegexMatchTimeoutException e)
        {
            // Kept. A timeout is not "this address is invalid" - it is 200ms of CPU burnt on a
            // pathological input, and a repeat of it is a ReDoS signal rather than a user typo. Silencing
            // this one hid the only case here worth hearing about.
            Development.LogException(e);
            return false;
        }
        catch (ArgumentException)
        {
            // Dropped, which is what the commented-out log was reaching for. IdnMapping.GetAscii throws
            // for any malformed domain, so every mistyped address logged an exception - and an invalid
            // address is precisely what this method exists to detect. The `false` return IS the report;
            // logging it as an error made a validation result look like a fault.
            //
            // Not binding `e` also removes the CS0168 the commented-out version left behind.
            return false;
        }

        try
        {
            const string emailPattern =
            @"^(?("")("".+?(?<!\\)""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)" +
            @"(?<=[0-9a-z])@))(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-0-9a-z]*[0-9a-z]*\.)+" +
            @"[a-z0-9][\-a-z0-9]{0,22}[a-z0-9]))$";

            return Regex.IsMatch(email, emailPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException e)
        {
            // Same reasoning as the timeout above: kept, because a 250ms regex timeout is a property of
            // the pattern and the input, not a verdict on the address.
            Development.LogException(e);
            return false;
        }
    }

    /// <summary>
    /// Validates if it's in valid url format.
    /// </summary>
    /// <param name="input"></param>
    /// <returns>Return true if input string it's in valid url format.</returns>
    public static bool IsValidUrl(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        return Uri.TryCreate(input, UriKind.Absolute, out var uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Removes non digit characters.
    /// </summary>
    /// <param name="input"></param>
    /// <returns>Returns input string without characters</returns>
    public static string CleanOfNonDigits(this string input)
    {
        return string.IsNullOrEmpty(input) ? input : Regex.Replace(input, @"[^\d]+", "");
    }

    /// <summary>
    /// Converts Html code to plain text
    /// </summary>
    /// <param name="html"></param>
    /// <returns></returns>
    public static string HtmlToPlainText(this string html)
    {
        const string tagWhiteSpace = @"(>|$)(\W|\n|\r)+<";//matches one or more (white space or line breaks) between '>' and '<'
        const string stripFormatting = @"<[^>]*(>|$)";//match any character between '<' and '>', even when end tag is missing
        const string lineBreak = @"<(br|BR)\s{0,1}\/{0,1}>";//matches: <br>,<br/>,<br />,<BR>,<BR/>,<BR />

        var lineBreakRegex = new Regex(lineBreak, RegexOptions.Multiline);
        var stripFormattingRegex = new Regex(stripFormatting, RegexOptions.Multiline);
        var tagWhiteSpaceRegex = new Regex(tagWhiteSpace, RegexOptions.Multiline);

        var text = html;

        //Decode html specific characters
        text = System.Net.WebUtility.HtmlDecode(text);

        //Remove tag whitespace/line breaks
        text = tagWhiteSpaceRegex.Replace(text, "><");

        //Replace <br /> with line breaks
        text = lineBreakRegex.Replace(text, Environment.NewLine);

        //Strip formatting
        text = stripFormattingRegex.Replace(text, string.Empty);

        return text;
    }

    /// <summary>
    /// Remove all text tags.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static string RemoveAllTextTags(this string input)
    {
        // Same guard as ConvertTextTagsForUnity, so the pair agrees about null. Guarding only one of
        // two sibling extensions that do the same job is its own trap.
        if (input == null)
        {
            Development.LogError("RemoveAllTextTags received null. Returning null; the caller is "
                + "handing it an unresolved value.");
            return null;
        }

        string[] tagsToRemove = {"<br />", "<b/>", "</p><p>", "<em>", "</em>", "<p>", "</p>",
            "<strong>", "</strong>", "&nbsp;", "&#8211;", "&#8212;", "&#8216;", "&#8217;", "&#8218;",
            "&#8220;", "&#8221;", "&#8222;", "&#8224;", "&#8225;", "&#8226;", "&#8230;", "&#8240;",
            "&#8364;", "&#8482;", "\n", "\n\n", "<i>", "</i>", "<b>", "</b>"};

        foreach (var tag in tagsToRemove)
            input = input.Replace(tag, string.Empty);

        return input;
    }

    /// <summary>
    /// Converts Html text tags to Unity, only processes text that's surrounded by double quotes ("")
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static string ConvertTextTagsForUnity(this string input)
    {
        // Null in, null out. This is a text-munging extension on a string that comes from a CMS, and it
        // reached Regex.Replace(input, ...) unguarded - which throws ArgumentNullException rather than
        // returning anything. A LocalizedField entry holding "en": null reaches here; the root
        // fix is upstream, but a public extension that throws on the one input it is most likely to be
        // handed is worth closing at the source too.
        if (input == null)
        {
            // Logged, because the guard's whole effect is to replace an ArgumentNullException with a
            // quiet null - and an unlogged null is worse than a stack trace. LogError is always
            // compiled, so this reaches the log on every target.
            Development.LogError("ConvertTextTagsForUnity received null. Returning null; the caller is "
                + "handing it an unresolved value.");
            return null;
        }

        // Define tag replacements dictionary
        Dictionary<string, string> tagReplacements = new Dictionary<string, string>
        {
            { "<br />", "\n" },
            { "<b/>", "\n" },
            { "</p><p>", "\n\n" },
            { "<\\/p><p>", "\n\n" },
            { "<em>", "<i>" },
            { "</em>", "</i>" },
            { "<\\/em>", "</i>" },
            { "<p>", string.Empty },
            { "</p>", string.Empty },
            { "<//p>", string.Empty },
            { "<\\/p>", string.Empty },
            { "<strong>", "<b>" },
            { "</strong>", "</b>" },
            { "&nbsp;", " " },
            { "&#8211;", "–" },
            { "&#8212;", "—" },
            { "&#8216;", "‘" },
            { "&#8217;", "’" },
            { "&#8218;", "‚" },
            { "&#8220;", "“" },
            { "&#8221;", "”" },
            { "&#8222;", "„" },
            { "&#8224;", "†" },
            { "&#8225;", "‡" },
            { "&#8226;", "•" },
            { "&#8230;", "…" },
            { "&#8240;", "‰" },
            { "&#8364;", "€" },
            { "&#8482;", "™" },
            { "<ul>", "" },
            { "</ul>", "\n" },
            { "<\\/ul>", "\n" },
            { "<li>", "\n•" },
            { "<\\/li>", "" },
            { "</li>", "" }
        };

        // Regular expression to find text between double quotes
        Regex quoteRegex = new Regex("\"([^\"]*)\"", RegexOptions.Compiled);

        // Process only the text inside quotes
        input = quoteRegex.Replace(input, match =>
        {
            string quotedText = match.Groups[1].Value;

            // Apply the tag replacements
            foreach (var replacement in tagReplacements)
            {
                quotedText = quotedText.Replace(replacement.Key, replacement.Value);
            }

            // Remove image tags
            Regex imgReg = new Regex(@"</?img((\s+\w+(\s*=\s*(?:"".*?""|\'.*?\'|[^\'"">\s]+))?)+\s*|\s*)/?>");
            quotedText = imgReg.Replace(quotedText, string.Empty);

            // Remove heading tags (e.g., <h1>...</h1>)
            Regex rxHeadings = new Regex(@"<[hH][1-9][^>]*>[^<]*</[hH][1-9]\s*>", RegexOptions.Compiled | RegexOptions.Multiline);
            quotedText = rxHeadings.Replace(quotedText, string.Empty);

            // Call the markup bullet handler
            quotedText = TPM_MarkupBullets(quotedText);

            // Decode HTML entities
            quotedText = WebUtility.HtmlDecode(quotedText);

            // Return the processed text wrapped back in quotes
            return "\"" + quotedText + "\"";
        });

        return input;
    }

    public static string TPM_MarkupBullets(this string rawText, char findBullet = '•', float indent = 1f, float leftMargin = 1f)
    {
        bool isInBullet = false;
        bool isMargin = false;
        string markedup = String.Format("<margin-left={0}em>", leftMargin);
        string indentLeftTag = String.Format("<indent={0}em>", indent);
        string indentRightTag = "</indent>";

        foreach (char c in rawText)
        {
            if (c == findBullet)
            {
                markedup += findBullet + indentLeftTag;
                isInBullet = true;
                isMargin = true;
            }
            else if (isInBullet && c == (char)10)
            {
                markedup += indentRightTag + c;
                isInBullet = false;
            }
            else
            {
                markedup += c;
            }
        }

        if (isInBullet)
            markedup += indentRightTag;

        if (isMargin)
            markedup += "</margin>";

        return (isInBullet || isMargin) ? markedup : rawText;
    }

    public static string FixDoubleBackSlash(this string input)
    {
        Dictionary<string, string> escapeReplacements = new Dictionary<string, string>
        {
            { "\\r", "\r" },
            { "\\n", "\n" }
        };

        foreach (var replacement in escapeReplacements)
            input = input.Replace(replacement.Key, replacement.Value);

        return input;
    }

    public static string FixDoubleFowardSlash(this string input)
    {
        return Regex.Replace(input, @"(?<!:)/{2}", "/");
    }

    /// <summary>
    ///  Gets everything before the stop substring occurs within a given string.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="stopAt"></param>
    /// <returns></returns>
    public static string GetUntilOccurrence(this string text, string stopAt)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            int charLocation = text.IndexOf(stopAt, StringComparison.Ordinal);

            if (charLocation > 0)
                return text.Substring(0, charLocation);
        }

        return text;
    }

    /// <summary>
    ///  Gets everything after the stop substring occurs within a given string.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="stopAt"></param>
    /// <returns></returns>
    public static string GetAfterOccurrence(this string text, string stopAt)
    {
        if (!string.IsNullOrEmpty(text))
        {
            int charLocation = text.IndexOf(stopAt, StringComparison.Ordinal);

            if (charLocation > 0)
                return text.Substring(charLocation);
        }

        return text;
    }

    //public enum FileTypes { AUDIO, VIDEO}

    public static string GetFileType(this string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        return Path.GetExtension(filePath);

        //if (!string.IsNullOrEmpty(text))
        //{
        //    int charLocation = text.LastIndexOf(".", StringComparison.Ordinal);

        //    if (charLocation >= 0 && charLocation < text.Length - 1)
        //        return text.Substring(charLocation);
        //}

        //return text;
    }

    /// <summary>
    /// Gets the file name from a full file path.
    /// </summary>
    /// <param name="fullPath">The complete path to the file.</param>
    /// <param name="withExtension">Whether to include the file extension.</param>
    /// <returns>The file name, with or without extension.</returns>
    public static string GetFileName(this string fullPath, bool withExtension = true)
    {
        if (string.IsNullOrEmpty(fullPath))
            return string.Empty;

        return withExtension ? Path.GetFileName(fullPath) : Path.GetFileNameWithoutExtension(fullPath);
    }

    /// <summary>
    /// "Camel case string" => "CamelCaseString" 
    /// </summary>
    public static string ToCamelCase(this string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        // Replace hyphens and underscores with spaces
        message = message.Replace("-", " ").Replace("_", " ");

        // Split the message into words
        string[] words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Convert the first word to lowercase and capitalize the remaining words
        for (int i = 1; i < words.Length; i++)
            words[i] = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words[i]);

        // Concatenate the words without spaces
        message = string.Concat(words);

        return message;
    }

    /// <summary>
    /// "CamelCaseString" => "Camel Case String"
    /// </summary>
    public static string SplitCamelCase(this string camelCaseString)
    {
        if (string.IsNullOrEmpty(camelCaseString))
            return camelCaseString;

        string spacedString = Regex.Replace(Regex.Replace(camelCaseString, @"(\P{Ll})(\P{Ll}\p{Ll})", "$1 $2"), @"(\p{Ll})(\P{Ll})", "$1 $2");

        // Capitalize the first letter and concatenate with the rest
        string result = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spacedString);

        return result;
    }

    /// <summary>
    /// Convert a string value to an Enum value.
    /// </summary>
    public static T AsEnum<T>(this string source, bool ignoreCase = true) where T : Enum => (T)Enum.Parse(typeof(T), source, ignoreCase);

    /// <summary>
    /// Get the "message" string with the "surround" string at the both sides 
    /// </summary>
    public static string SurroundedWith(this string message, string surround) => surround + message + surround;

    /// <summary>
    /// Get the "message" string with the "start" at the beginning and "end" at the end of the string
    /// </summary>
    public static string SurroundedWith(this string message, string start, string end) => start + message + end;

    /// <summary>
    /// Surround string with "color" tag
    /// </summary>
    public static string Colored(this string message, Colors color) => $"<color={color}>{message}</color>";

    /// <summary>
    /// Surround string with "color" tag
    /// </summary>
    public static string Colored(this string message, Color color) => $"<color={color.ToHex()}>{message}</color>";

    /// <summary>
    /// Surround string with "color" tag
    /// </summary>
    public static string Colored(this string message, string colorCode) => $"<color={colorCode}>{message}</color>";

    /// <summary>
    /// Surround string with "size" tag
    /// </summary>
    public static string Sized(this string message, int size) => $"<size={size}>{message}</size>";

    /// <summary>
    /// Surround string with "u" tag
    /// </summary>
    public static string Underlined(this string message) => $"<u>{message}</u>";

    /// <summary>
    /// Surround string with "s" tag
    /// </summary>
    public static string Strikethrough(this string message) => $"<s>{message}</s>";

    /// <summary>
    /// Surround string with "b" tag
    /// </summary>
    public static string Bold(this string message) => $"<b>{message}</b>";

    /// <summary>
    /// Surround string with "i" tag
    /// </summary>
    public static string Italics(this string message) => $"<i>{message}</i>";

    // GetTranslation(this string) => message was removed in v0.24.1. It was an identity
    // function whose own doc said "Returns same value..". Both it and the plain-string MediaVO.url
    // are there in the initial import, so git cannot say which came first - but every one of its 14
    // call sites was on a media URL, so it read as a translation that had been flattened away and
    // was never restored. Do not reintroduce it: it made `media.url.GetTranslation()` read as "the URL for the
    // current language" while doing nothing of the kind. If media needs to be per-language, make
    // the field translatable - the remarks on the project's MediaVO say what that costs.

    private static Dictionary<string, string> diacriticsChars = new Dictionary<string, string>
    {
        { "", "" },
        { "!", "%21" },
        { "\"", "%22" },
        { "#", "%23" },
        { "$", "%24" },
        { "%", "%25" },
        { "&", "%26" },
        { "'", "%27" },
        { "(", "%28" },
        { ")", "%29" },
        { "*", "%2A" },
        { "+", "%2B" },
        { ",", "%2C" },
        { ":", "%3A" },
        { ";", "%3B" },
        { "<", "%3C" },
        { "=", "%3D" },
        { ">", "%3E" },
        { "?", "%3F" },
        { "@", "%40" },
        { "[", "%5B" },
        { "]", "%5D" },
        { "^", "%5E" },
        { "`", "%60" },
        { "{", "%7B" },
        { "|", "%7C" },
        { "}", "%7D" },
        { "~", "%7E" },
        { "€", "%80" },
        { "‚", "%82" },
        { "ƒ", "%83" },
        { "„", "%84" },
        { "ˆ", "%88" },
        { "‰", "%89" },
        { "Š", "%8A" },
        { "‹", "%8B" },
        { "Œ", "%8C" },
        { "Ž", "%8E" },
        { "‘", "%91" },
        { "’", "%92" },
        { "“", "%93" },
        { "”", "%94" },
        { "•", "%95" },
        { "˜", "%98" },
        { "™", "%99" },
        { "›", "%9B" },
        { "œ", "%9C" },
        { "ž", "%9E" },
        { "Ÿ", "%9F" },
        { "¢", "%A2" },
        { "£", "%A3" },
        { "¦", "%A6" },
        { "«", "%AB" },
        { "¯", "%AF" },
        { "°", "%B0" },
        { "´", "%B4" },
        { "·", "%B7" },
        { "¸", "%B8" },
        { "¹", "%B9" },
        { "º", "%BA" },
        { "»", "%BB" },
        { "¿", "%BF" },
        { "À", "%C0" },
        { "Á", "%C1" },
        { "Â", "%C2" },
        { "Ã", "%C3" },
        { "Ä", "%C4" },
        { "Å", "%C5" },
        { "Æ", "%C6" },
        { "Ç", "%C7" },
        { "È", "%C8" },
        { "É", "%C9" },
        { "Ê", "%CA" },
        { "Ë", "%CB" },
        { "Ì", "%CC" },
        { "Í", "%CD" },
        { "Î", "%CE" },
        { "Ï", "%CF" },
        { "Ð", "%D0" },
        { "Ñ", "%D1" },
        { "Ò", "%D2" },
        { "Ó", "%D3" },
        { "Ô", "%D4" },
        { "Õ", "%D5" },
        { "Ö", "%D6" },
        { "Ø", "%D8" },
        { "Ù", "%D9" },
        { "Ú", "%DA" },
        { "Û", "%DB" },
        { "Ü", "%DC" },
        { "Ý", "%DD" },
        { "Þ", "%DE" },
        { "ß", "%DF" },
        { "à", "%E0" },
        { "á", "%E1" },
        { "â", "%E2" },
        { "ã", "%E3" },
        { "ä", "%E4" },
        { "å", "%E5" },
        { "æ", "%E6" },
        { "ç", "%E7" },
        { "è", "%E8" },
        { "é", "%E9" },
        { "ê", "%EA" },
        { "ë", "%EB" },
        { "ì", "%EC" },
        { "í", "%ED" },
        { "î", "%EE" },
        { "ï", "%EF" },
        { "ð", "%F0" },
        { "ñ", "%F1" },
        { "ò", "%F2" },
        { "ó", "%F3" },
        { "ô", "%F4" },
        { "õ", "%F5" },
        { "ö", "%F6" },
        { "÷", "%F7" },
        { "ø", "%F8" },
        { "ù", "%F9" },
        { "ú", "%FA" },
        { "û", "%FB" },
        { "ü", "%FC" },
        { "ý", "%FD" },
        { "þ", "%FE" },
        { "ÿ", "%FF" }
    };

    /// <summary>
    /// Cannot work, and never could: every value in the table is a three-character percent-encoding, so
    /// there is no single <see cref="char"/> that represents one. It used to return
    /// <c>entry.Value[0]</c>, which is <c>'%'</c> for every mapped character - the two hex digits were
    /// discarded, so a caller got the same meaningless answer for every accent.
    ///
    /// It now returns the character unchanged, which is at least honest about having no answer. Use the
    /// <see cref="ReplaceDiacritics(string)"/> overload, which is correct.
    /// </summary>
    [System.Obsolete("A char cannot hold a percent-encoding; this always returned '%'. " +
        "Use ReplaceDiacritics(string).")]
    public static char ReplaceDiacritics(this char c) => c;

    public static string ReplaceDiacritics(this string s)
    {
        string text = "";

        foreach (char c in s)
        {
            // Same shape as RemoveDiacritics: track whether a mapping was found, not whether the output
            // grew. No entry in this table maps to an empty string today, so a length test would still be
            // correct - but it is correct only by accident, and adding one empty mapping would silently
            // pass that character through instead of removing it.
            bool mapped = false;

            foreach (KeyValuePair<string, string> entry in diacriticsChars)
            {
                if (entry.Key.IndexOf(c) != -1)
                {
                    text += entry.Value;
                    mapped = true;
                    break;
                }
            }

            if (!mapped)
                text += c;
        }
        return text;
    }

    static Dictionary<string, string> foreign_characters = new Dictionary<string, string>
    {
        { "äæǽ", "ae" },
        { "öœ", "oe" },
        { "ü", "ue" },
        { "Ä", "Ae" },
        { "Ü", "Ue" },
        { "Ö", "Oe" },
        { "ÀÁÂÃÄÅǺĀĂĄǍΑΆẢẠẦẪẨẬẰẮẴẲẶА", "A" },
        { "àáâãåǻāăąǎªαάảạầấẫẩậằắẵẳặа", "a" },
        { "Б", "B" },
        { "б", "b" },
        { "ÇĆĈĊČ", "C" },
        { "çćĉċč", "c" },
        { "Д", "D" },
        { "д", "d" },
        { "ÐĎĐΔ", "Dj" },
        { "ðďđδ", "dj" },
        { "ÈÉÊËĒĔĖĘĚΕΈẼẺẸỀẾỄỂỆЕЭ", "E" },
        { "èéêëēĕėęěέεẽẻẹềếễểệеэ", "e" },
        { "Ф", "F" },
        { "ф", "f" },
        { "ĜĞĠĢΓГҐ", "G" },
        { "ĝğġģγгґ", "g" },
        { "ĤĦ", "H" },
        { "ĥħ", "h" },
        { "ÌÍÎÏĨĪĬǏĮİΗΉΊΙΪỈỊИЫ", "I" },
        { "ìíîïĩīĭǐįıηήίιϊỉịиыї", "i" },
        { "Ĵ", "J" },
        { "ĵ", "j" },
        { "ĶΚК", "K" },
        { "ķκк", "k" },
        { "ĹĻĽĿŁΛЛ", "L" },
        { "ĺļľŀłλл", "l" },
        { "М", "M" },
        { "м", "m" },
        { "ÑŃŅŇΝН", "N" },
        { "ñńņňŉνн", "n" },
        { "ÒÓÔÕŌŎǑŐƠØǾΟΌΩΏỎỌỒỐỖỔỘỜỚỠỞỢО", "O" },
        { "òóôõōŏǒőơøǿºοόωώỏọồốỗổộờớỡởợо", "o" },
        { "П", "P" },
        { "п", "p" },
        { "ŔŖŘΡР", "R" },
        { "ŕŗřρр", "r" },
        { "ŚŜŞȘŠΣС", "S" },
        { "śŝşșšſσςс", "s" },
        { "ȚŢŤŦτТ", "T" },
        { "țţťŧт", "t" },
        { "ÙÚÛŨŪŬŮŰŲƯǓǕǗǙǛŨỦỤỪỨỮỬỰУ", "U" },
        { "ùúûũūŭůűųưǔǖǘǚǜυύϋủụừứữửựу", "u" },
        { "ÝŸŶΥΎΫỲỸỶỴЙ", "Y" },
        { "ýÿŷỳỹỷỵй", "y" },
        { "В", "V" },
        { "в", "v" },
        { "Ŵ", "W" },
        { "ŵ", "w" },
        { "ŹŻŽΖЗ", "Z" },
        { "źżžζз", "z" },
        { "ÆǼ", "AE" },
        { "ß", "ss" },
        { "Ĳ", "IJ" },
        { "ĳ", "ij" },
        { "Œ", "OE" },
        { "ƒ", "f" },
        { "ξ", "ks" },
        { "π", "p" },
        { "β", "v" },
        { "μ", "m" },
        { "ψ", "ps" },
        { "Ё", "Yo" },
        { "ё", "yo" },
        { "Є", "Ye" },
        { "є", "ye" },
        { "Ї", "Yi" },
        { "Ж", "Zh" },
        { "ж", "zh" },
        { "Х", "Kh" },
        { "х", "kh" },
        { "Ц", "Ts" },
        { "ц", "ts" },
        { "Ч", "Ch" },
        { "ч", "ch" },
        { "Ш", "Sh" },
        { "ш", "sh" },
        { "Щ", "Shch" },
        { "щ", "shch" },
        { "ЪъЬь", "" },
        { "Ю", "Yu" },
        { "ю", "yu" },
        { "Я", "Ya" },
        { "я", "ya" },
    };

    /// <summary>
    /// Best-effort only, and wrong for 37 of the 89 table entries - which is why it is deprecated rather
    /// than repaired. 36 values are multi-character transliterations (<c>ß</c> to <c>ss</c>, <c>Æ</c> to
    /// <c>AE</c>, <c>Ð</c> to <c>Dj</c>), and a <see cref="char"/> return can only carry the first letter,
    /// so those are silently truncated. One value is empty (the Russian hard and soft signs, which
    /// transliterate to nothing) and used to throw <see cref="System.IndexOutOfRangeException"/> here.
    ///
    /// The empty case is now returned as the character unchanged instead of throwing, because a crash is
    /// the worse of the two failures. Use the <see cref="RemoveDiacritics(string)"/> overload, which
    /// handles every entry correctly.
    /// </summary>
    [System.Obsolete("Truncates multi-character transliterations and cannot express an empty one. " +
        "Use RemoveDiacritics(string).")]
    public static char RemoveDiacritics(this char c)
    {
        foreach (KeyValuePair<string, string> entry in foreign_characters)
            if (entry.Key.IndexOf(c) != -1)
                return entry.Value.Length > 0 ? entry.Value[0] : c;

        return c;
    }

    public static string RemoveDiacritics(this string s)
    {
        string text = "";

        foreach (char c in s)
        {
            // Track whether a mapping was found rather than whether the output grew. Those are not the
            // same question: the Russian hard and soft signs transliterate to nothing, so appending their
            // empty value leaves the length unchanged and a length test reads it as "no mapping" - which
            // appended the original character and passed the sign straight through.
            bool mapped = false;

            foreach (KeyValuePair<string, string> entry in foreign_characters)
            {
                if (entry.Key.IndexOf(c) != -1)
                {
                    text += entry.Value;
                    mapped = true;
                    break;
                }
            }

            if (!mapped)
                text += c;
        }
        return text;
    }
}
