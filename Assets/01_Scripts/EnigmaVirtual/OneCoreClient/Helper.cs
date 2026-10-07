using Newtonsoft.Json;
using System.Collections.Generic;

namespace EnigmaVirtual.OneCoreClient
{
    public static class Helper 
    {
        /// <summary>
        ///  Converts raw translation data (in the form of a JSON object) into a nested dictionary structure suitable for managing translations in a multi-language application
        /// </summary>
        /// <param name="translationsObj"></param>
        /// <returns></returns>
        public static LocalizedField<LocalizedField<string>> ParseTranslations(object translationsObj)
        {
            LocalizedField<LocalizedField<string>> tf = new LocalizedField<LocalizedField<string>>();
            RawTranslation[] rts = JsonConvert.DeserializeObject<RawTranslation[]>(JsonConvert.SerializeObject(translationsObj));

            foreach (RawTranslation rt in rts)
            {
                LocalizedField<string> values = new LocalizedField<string>();

                // Indexer, not Add. LocalizedField is case-insensitive, so a server sending
                // both "EN" and "en" now collides - and Add THROWS on the second, where the old
                // case-sensitive dictionary stored both. AppDataController catches that exception and
                // leaves _translations null, so one duplicated key would have cost the app every string
                // it has. Last-wins also matches what the Newtonsoft path at AppDataController.cs:411
                // already does, so the two parsers no longer disagree on malformed data.
                foreach (KeyValuePair<string, string> kp in rt.Value)
                    values[kp.Key] = kp.Value;

                tf[rt.Key] = values;
            }
            return tf;
        }
    }
}