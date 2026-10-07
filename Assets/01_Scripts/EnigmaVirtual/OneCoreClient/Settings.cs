using EnigmaVirtual.OneCoreClient.Model;

namespace EnigmaVirtual.OneCoreClient
{
    [System.Serializable]
    public class Settings : SocketsSettings
    {
        public int apiPort;
        // Initialised, not left null: settings.json ships an Authentication block but an
        // operator can remove it, and JsonUtility leaves the field null rather than defaulting
        // it - which NRE'd both the settings panel's load path and its save path.
        public SignInAuthentication Authentication = new SignInAuthentication();
        public OneCoreCommand[] ConnectCommands;
        public OneCoreCommand[] DisconnectCommands;

        /// <summary>
        /// Handles this subclass's own keys, then delegates the rest to
        /// <see cref="SocketsSettings.SetData"/>. The order matters: calling base first would
        /// trip its unknown-key report on every key handled here.
        /// </summary>
        public override void SetData(string key, string value)
        {
            switch (key)
            {
                case "apiPort":
                    apiPort = SettingsParse.Int(key, value, apiPort, 1, SettingsParse.MaxPort);
                    return;
                case "authenticationTags":
                    Authentication.tags = SettingsParse.CsvList(value);
                    return;
                case "authenticationKey":
                    Authentication.key = value;
                    return;
            }

            base.SetData(key, value);
        }
    }

    [System.Serializable]
    public class SignInAuthentication
    {
        public string[] tags;
        public string key;
    }
}
