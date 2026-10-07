/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

using System;
using System.Collections.Generic;

namespace EnigmaVirtual
{
    /// <summary>
    /// One value per language, keyed by language code - the type behind every per-language string in
    /// the content model.
    /// </summary>
    /// <remarks>
    /// <b>Renamed from <c>TranslatableField&lt;T&gt;</c> in v0.28.0</b>, along with its members, so the
    /// whole type speaks one vocabulary: <c>GetLocalized</c> / <c>TryGetLocalized</c> /
    /// <c>HasLocalized</c>. It had accumulated three words for the same idea - <i>translatable</i> in
    /// the type name, <i>translation</i> in the methods, <i>localized</i> in the accessor that
    /// replaced them - and the odd one out was the only one anybody was supposed to call.
    /// <para>
    /// The rename is compile-visible everywhere it matters and cannot break silently: this type derives
    /// from <c>Dictionary</c>, so Unity never serializes it into a scene or prefab, and Newtonsoft
    /// matches <c>appData.json</c> by <i>field</i> name rather than type name. <c>appData.json</c> is
    /// therefore untouched - a branched project needs a find-and-replace on its own C#, nothing more.
    /// </para>
    /// </remarks>
    [System.Serializable]
    public class LocalizedField<T> : Dictionary<string, T>
    {
        /// <summary>
        /// Builds the dictionary with a case-insensitive comparer, which is what makes every lookup here
        /// O(1).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This type <i>was</i> a <c>Dictionary&lt;string, T&gt;</c> that never used its own hash table:
        /// every lookup ran a scan comparing keys with <c>EqualsIgnoreCase</c>. Since that extension is
        /// exactly <c>string.Equals(a, b, StringComparison.OrdinalIgnoreCase)</c>, moving the same rule
        /// into the comparer matches and misses on precisely the same keys - the behaviour is identical
        /// and only the cost changes.
        /// </para>
        /// <para>
        /// Both construction paths honour this: <c>Helper.ParseTranslations</c> calls it directly, and
        /// Newtonsoft builds <c>IDictionary</c> types through the parameterless constructor
        /// (<c>AppDataController</c> deserialises the <c>texts</c> block with <c>ToObject&lt;T&gt;()</c>).
        /// <see cref="TryGetByKey"/> does not assume it worked, because a lost comparer would turn
        /// lookups case-<i>sensitive</i> and translations would quietly degrade to raw keys.
        /// </para>
        /// </remarks>
        public LocalizedField() : base(StringComparer.OrdinalIgnoreCase) { }

        internal bool HasLocalized(string lang = null)
        {
            if (string.IsNullOrEmpty(lang))
                lang = Core.IsSet ? Core.localizationSettings.currentLanguage.ToString() : null;
            if (lang != null && TryGetByKey(lang, out T value) && value != null)
                return true;
            return false;
        }

        internal bool HasLocalized(Langs extLang)
        {
            return HasLocalized(extLang.ToString());
        }

        internal bool HasAnyLocalized()
        {
            return this.Count > 0;
        }

        /// <summary>
        /// The value for <paramref name="lang"/>, if there is one. <b>A key present with a
        /// <c>null</c> value counts as absent</b>, so <c>true</c> always means
        /// <paramref name="output"/> is usable - <b>non-null, not non-empty</b>. An empty string is a
        /// value the content author wrote and is returned as one; <c>Translations.GetTranslation</c>
        /// draws the line in the same place.
        /// </summary>
        /// <remarks>
        /// It used to report <c>true</c> for a present-but-<c>null</c> entry, on the reasoning that
        /// this is what "the dictionary has an entry" means - which is true of a dictionary and wrong
        /// for this method, whose whole purpose is to tell a caller whether it has something to show.
        /// <c>TextMeshProUGUIExtension.SetText</c> took that <c>true</c> and called
        /// <c>ConvertTextTagsForUnity()</c> on it, reaching <c>Regex.Replace(null, ...)</c> and
        /// throwing <c>ArgumentNullException</c>. A CMS emitting <c>"en": null</c> for an untranslated
        /// entry is all it took.
        /// <para>
        /// Fixed here rather than at that one call site: this is the shared function every caller
        /// routes through, and <see cref="HasLocalized"/> and <see cref="GetLocalized"/> already
        /// treated <c>null</c> as absent - so all three <c>*Localized</c> members now agree. If you
        /// genuinely need "is the key present, null or not", the inherited <c>Dictionary.TryGetValue</c>
        /// and <c>ContainsKey</c> answer it and are <c>public</c>. <see cref="TryGet"/> is <b>not</b>
        /// that escape hatch: it is <c>internal</c> - so <c>CS0122</c> from <c>App/</c> since v0.24.0 -
        /// it logs a warning on a miss, and it does no current-language resolution.
        /// </para>
        /// </remarks>
        internal bool TryGetLocalized(out T output, string lang = null)
        {
            if (string.IsNullOrEmpty(lang))
                lang = Core.IsSet ? Core.localizationSettings.currentLanguage.ToString() : null;

            if (lang != null && TryGetByKey(lang, out output) && output != null)
                return true;

            output = default;
            return false;
        }

        internal bool TryGetLocalized(out T output, Langs extLang)
        {
            return TryGetLocalized(out output, extLang.ToString());
        }

        /// <summary>
        /// Case-insensitive lookup, O(1) through the comparer set in the constructor.
        /// </summary>
        /// <remarks>
        /// An earlier version of this kept the old linear scan as a fallback, in case the comparer had not
        /// been applied. It cannot happen: **C# does not inherit constructors**, this type declares exactly
        /// one, and it always passes the comparer - so there is no reachable way to build a
        /// <c>LocalizedField&lt;T&gt;</c> with any other. The fallback was dead code that charged a
        /// <c>Comparer</c> read and a <c>ReferenceEquals</c> on every <i>miss</i>, and misses are ordinary
        /// here (<see cref="HasLocalized"/> asks about languages an entry may not carry). It also leaned
        /// on <c>Dictionary.Comparer</c> returning the same instance it was given, which is the
        /// <c>ConcurrentQueue&lt;T&gt;.Count</c> shape from CLAUDE.md - true as far as anyone traced it, and
        /// not worth depending on when the guarantee is already in the type system.
        /// </remarks>
        private bool TryGetByKey(string key, out T value)
        {
            // base.TryGetValue throws on a null key, where the old scan simply found nothing. GetValue and
            // TryGetValue take a caller-supplied key and do not guard it, so this stays.
            if (key == null)
            {
                value = default;
                return false;
            }

            return base.TryGetValue(key, out value);
        }

        /// <summary>
        /// Exact-case lookup, for callers that ask for <c>caseSensitive</c>.
        /// </summary>
        /// <remarks>
        /// It cannot use <c>ContainsKey</c>/<c>this[key]</c> any more: with the case-insensitive comparer
        /// those now match a differently-cased key, so the flag would have quietly meant its opposite.
        /// Nothing in this repo passes <c>true</c>, but a branched project's own code can reach the
        /// members that do - subject to the assembly boundary added in v0.24.0.
        /// </remarks>
        private bool TryGetExact(string key, out T value)
        {
            if (key != null)
            {
                foreach (KeyValuePair<string, T> kp in this)
                {
                    if (string.Equals(kp.Key, key, StringComparison.Ordinal))
                    {
                        value = kp.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        /// <summary>
        /// The value for <paramref name="lang"/>, or the default language's when
        /// <paramref name="fallToDefault"/> is set. Returns <c>default</c> - <c>null</c> for a string -
        /// when there is nothing to return.
        /// </summary>
        /// <remarks>
        /// <c>public</c> rather than <c>internal</c> because App code calls it and the two now live
        /// in different assemblies. `internal` never meant anything here - everything shared
        /// one assembly - and the .asmdef split turns exactly this into CS0122.
        /// </remarks>
        public T GetLocalized(Langs extLang, bool fallToDefault = false)
        {
            return GetLocalized(extLang.ToString(), fallToDefault);
        }

        public T GetLocalized(string lang = null, bool fallToDefault = false)
        {
            if (string.IsNullOrEmpty(lang))
                lang = Core.IsSet ? Core.localizationSettings.currentLanguage.ToString() : null;

            if (lang != null)
            {
                if (TryGetByKey(lang, out T value) && value != null)
                    return value;
            }

            // EqualsIgnoreCase, not != against a lower-cased string: lang comes from the enum's own
            // ToString ("EN"), so comparing it to "en" was true even when the current language WAS the
            // default, and the most common path in the app did its lookup twice.
            if (fallToDefault && !lang.EqualsIgnoreCase(Core.localizationSettings.defaultLanguage.ToString()))
                return GetLocalized(Core.localizationSettings.defaultLanguage, false);

            return default;
        }

        // GetTranslation(lang, fallToDefault) lived here, [Obsolete] since the GetLocalized migration
        // and deleted in v0.28.0 with the rename. It differed from GetLocalized in two ways, neither
        // of them reachable: string.Empty on a miss where GetLocalized returns default, and a return
        // type of string via value.ToString() where GetLocalized returns T - which diverges outright
        // for LocalizedField<MediaVO> and for the LocalizedField<LocalizedField<string>> that
        // Translations holds. The first was internal with zero callers,
        // and since the v0.24.0 assembly split internal also meant no branched project's App code
        // could reach it, so nothing could depend on either behaviour. One accessor, one contract.
        //
        // The remaining concern is still true and is not solved by deleting anything:
        // GetLocalized returns null for a missing string, and assigning null to TMP_Text.text risks an
        // NRE inside TMP's parser. Guard at the call site.

        /// <summary>
        /// Looks up an arbitrary key - a translation key, not a language - warning if it is absent.
        /// </summary>
        /// <remarks>
        /// <b>Named <c>TryGet</c>, not <c>TryGetValue</c>, so it cannot be confused with the one this
        /// type inherits.</b> The two took the same arguments in the opposite order, which never
        /// collided at compile time - an <c>out</c> argument cannot bind to the base's first parameter
        /// - but meant that typing <c>TryGetValue(key, out v)</c> from muscle memory silently took
        /// <c>Dictionary</c>'s: same result, but it throws on a null key and skips the warning below.
        /// <para>
        /// The rule for this type's members: <c>*Localized</c> resolves a <b>language</b>; a plain name
        /// looks up a <b>key</b>. Both exist because the same type is used both ways -
        /// <c>LocalizedField&lt;string&gt;</c> is keyed by language, while
        /// <c>LocalizedField&lt;LocalizedField&lt;string&gt;&gt;</c> in <c>Translations</c> is keyed by
        /// translation key and holds one of the former per entry. Do not "finish" the rename by
        /// pushing <c>Localized</c> onto these; it would be a lie about what they take.
        /// </para>
        /// </remarks>
        internal bool TryGet(out T output, string key, bool caseSensitive = false)
        {
            if (caseSensitive ? TryGetExact(key, out output) : TryGetByKey(key, out output))
                return true;

            Development.LogWarning($"Key not found [{key}]");

            output = default;
            return false;
        }

        internal T GetValue(string key, bool caseSensitive = false)
        {
            if (caseSensitive ? TryGetExact(key, out T value) : TryGetByKey(key, out value))
                return value;

            Development.LogWarning($"Key not found [{key}]");

            return default;
        }
    }
}
