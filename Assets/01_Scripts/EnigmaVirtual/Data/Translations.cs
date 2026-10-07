/*******************************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * Cannot be copied and/or distributed without the express
 * permission of Gema Digital Agency. All rights reserved.
 *******************************************************************/

namespace EnigmaVirtual
{
    [System.Serializable]
    public class Translations
    {
        [UnityEngine.HideInInspector]
        public LocalizedField<LocalizedField<string>> translations => _translations;

        /// <summary>
        /// True when there are no translations loaded.
        /// </summary>
        /// <remarks>
        /// Deliberately not <c>_translations.IsNullOrEmpty()</c>. A <c>Dictionary</c> is neither
        /// <c>IList&lt;T&gt;</c> nor an array, so that extension resolves to the
        /// <c>IEnumerable&lt;T&gt;</c> overload, whose <c>Any()</c> takes the enumerator through the
        /// interface and <b>boxes the struct enumerator</b> - one heap allocation on every
        /// <see cref="GetTranslation"/> call, since that is the first thing it evaluates. <c>Count</c>
        /// answers the same question with none.
        /// </remarks>
        public bool IsNullOrEmpty => _translations == null || _translations.Count == 0;

        private LocalizedField<LocalizedField<string>> _translations;

        /// <summary>
        /// Set translations data.
        /// </summary>
        /// <param name="translations"></param>
        public void SetTranslations(LocalizedField<LocalizedField<string>> translations)
        {
            _translations = translations;
        }

        /// <summary>
        /// Retrieves a translation for a given key in a specified language with optional fallback behavior.
        /// </summary>
        /// <param name="key">The identifier for the translation</param>
        /// <param name="language">The desired language for the translation. If not provided, it defaults to the current language.</param>
        /// <param name="fallbackToDefault">A boolean flag indicating whether to fallback to the default language if the translation is not found.</param>
        /// <returns>If a valid translation is found, it returns the translated string. 
        /// If no translation is found or if there are errors in the input parameters, it returns the original key</returns>
        public string GetTranslation(string key, string language = null, bool fallbackToDefault = false)
        {
            if (IsNullOrEmpty)
            {
                Development.LogError("Translations not available!");
                return key;
            }

            if (string.IsNullOrEmpty(key))
            {
                Development.LogWarning($"Key empty or null [{key}]");
                return key;
            }

            string lang = language ?? Core.localizationSettings.currentLanguage.ToString();

            if (_translations.TryGet(out LocalizedField<string> trans, key))
            {
                string localized = trans.GetLocalized(lang, fallbackToDefault);

                // The summary above promises the key back when nothing is found, and until v0.29.0 this
                // returned GetLocalized's result unconditionally - which is null for a language the
                // entry does not carry. MultiLangText seeds langText with "[key]" as a visible
                // diagnostic, overwrote it with this null, then skipped the whole assignment on
                // `!string.IsNullOrEmpty(langText)`: the placeholder was discarded and *whatever was on
                // screen before* stayed there. It did log a warning at that point - the failure was
                // visible in the console and invisible on screen, which is the worse half.
                //
                // != null, not !IsNullOrEmpty. An empty string is a value the content author wrote - a
                // label deliberately blanked for one language - and turning that into the key would put
                // "menu_title_home" on screen where someone asked for nothing. Only a genuinely absent
                // value falls through to the key. LocalizedField draws the line in the same place, so
                // the two agree: non-null is present, empty is a value.
                if (localized != null)
                    return localized;

                Development.LogWarning($"Key [{key}] has no value for language [{lang}]. "
                    + "Returning the key.");
            }

            return key;
        }
    }
}
