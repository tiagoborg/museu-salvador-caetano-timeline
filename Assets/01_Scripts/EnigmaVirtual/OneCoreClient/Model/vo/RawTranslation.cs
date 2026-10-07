using System.Collections.Generic;

namespace EnigmaVirtual.OneCoreClient
{
    [System.Serializable]
    public class RawTranslation
    {
        public string translation_key;
        public Dictionary<string, string> translation_text;

        public string Key => translation_key; 
        public Dictionary<string, string> Value  => translation_text; 
    }
}