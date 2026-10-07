using System;

namespace EnigmaVirtual
{
    public class AppSettingsBase
    {
        public string[] mediaPaths = new string[] { "url", "thumb" };

        internal bool isMediaType(string value)
        {
            for (int i = 0; i < mediaPaths.Length; i++)
                if (string.Equals(value, mediaPaths[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
