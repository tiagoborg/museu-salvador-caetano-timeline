/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * LocalizationSettings can not be copied and/or distributed without
 * the express permission of Hey Digital
 *******************************************************/
using System.Collections.Generic;
using System;

namespace EnigmaVirtual
{
    [System.Serializable]
    public class LocalizationSettings
    {
        public Langs defaultLanguage { get; private set; } = Langs.PT;
        public Langs currentLanguage { get; private set; } = Langs.PT;
        public Langs[] languages { get; private set; }

        internal void SetCurrentLang(Langs language)
        {
            currentLanguage = language;
        }

        internal void SetLanguages(string[] langs)
        {
            languages = ParseLanguages(langs);

            if (!languages.IsNullOrEmpty())
            {
                defaultLanguage = languages[0];
                Core.ChangeCurrentLang(defaultLanguage);
            }
        }

        private Langs[] ParseLanguages(string[] langs)
        {
            List<Langs> _languages = new List<Langs>();

            if (!langs.IsNullOrEmpty())
            {
                foreach (string value in langs)
                {
                    if (Enum.TryParse(value, true, out Langs parsedValue))
                        _languages.Add(parsedValue);
                    else
                        Development.LogWarning($"Language [{value}] not found!");
                }
            }

            return _languages.ToArray();
        }
    }
}
