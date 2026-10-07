/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * SocketsSettings can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/

namespace EnigmaVirtual
{
    [System.Serializable]
    public class OSCSettings
    {
        public string clientName = "RESOLUME";
        public bool isActive = false;
        public bool useBurst = true;
        public string serverIp;
        public int serverPort;
        public string[] startMessages;
        public string[] closeMessages;
        public bool doLogs;

        /// <summary>
        /// Applies one operator-edited setting from the on-device settings panel.
        /// Unparseable input keeps the previous value and reports it - see
        /// <see cref="SettingsParse"/> for why throwing here is not an option.
        /// </summary>
        public void SetData(string key, string value)
        {
            switch (key)
            {
                case "isActive":
                    isActive = SettingsParse.Bool(key, value, isActive);
                    break;
                case "serverIp":
                    serverIp = value;
                    break;
                case "serverPort":
                    serverPort = SettingsParse.Int(key, value, serverPort, 1, SettingsParse.MaxPort);
                    break;
                case "startMessages":
                    // CsvList, not Split: "".Split(',') yields [""], which passes IsNullOrEmpty()
                    // in MainController.SetupOscController and sends one blank OSC message.
                    startMessages = SettingsParse.CsvList(value);
                    break;
                default:
                    SettingsParse.Error($"SetData: unknown key [{key}] - nothing was saved for it.");
                    break;
            }
        }
    }
}