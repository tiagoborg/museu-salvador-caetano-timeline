/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * PhidgetSettings can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/

namespace EnigmaVirtual
{
    [System.Serializable]
    public class PhidgetSettings
    {
        public string name;
        public bool isActive = false;
        public int serialNumber;
        public string boardType = "InterfaceKit";
        // Read by NOTHING in the framework - verified by grep across Assets/01_Scripts, where the only
        // other hits are SocketsSettings/SocketConnection's unrelated reconnectTime. Boards are addressed
        // by serialNumber over USB, which is what the shipped config uses, and these three keys are not
        // even present in it. Kept rather than deleted because network-attached Phidgets are a real
        // product feature these would serve if anyone implements it, and removing settings fields is a
        // config-format change; but do not read them expecting them to have been honoured.
        public string boardIp = "127.0.0.1";
        public int boardPort = 1300;
        public float reconnectTime = 5;
        public int[] minValues;
        public int[] maxValues;
        public bool doLogs = true;
        public bool enableDigitalOutputsOnStart = true;
        public int timeout = 1000;

        public bool[] digitalInputs = new bool[] { false, false, false, false, false, false, false, false };
        public bool[] digitalOutputs = new bool[] { false, false, false, false, false, false, false, false };
        public bool[] voltageInputs = new bool[] { false, false, false, false, false, false, false, false };
        public string[] voltageSensorType = new string[] { "", "", "", "", "", "", "", "" };
        public bool[] voltageRatioInputs = new bool[] { false, false, false, false, false, false, false, false };
        public string[] voltageRatioSensorType = new string[] { "", "", "", "", "", "", "", "" };

        public PhidgetsBoard PhidgetsBoardType => (PhidgetsBoard)System.Enum.Parse(typeof(PhidgetsBoard), boardType, true);

        /// <summary>
        /// Applies one operator-edited setting. Unparseable input keeps the previous value and
        /// reports it - see <see cref="SettingsParse"/>.
        /// </summary>
        /// <remarks>
        /// <b>No caller in the framework.</b> It was written for a settings-panel group whose four rows
        /// were deleted: the panel exposed one board's fields while the model holds an array
        /// (<c>PhidgetsSettings.phidgets</c>), and <c>SettingInput</c> keys a field by group and name with
        /// no concept of an index, so there was nothing to bind them to. The rows had also been inert
        /// since before this method existed - both the load and save branches were commented out, and
        /// both addressed <c>PhidgetsSettings</c> rather than this class, so neither would have compiled.
        /// <para>Kept because it is correct and is the natural extension point if per-board editing is
        /// ever wanted: it needs a board selector feeding an index, not a rewrite. Deleting it would be a
        /// breaking change for any branched project that drove it from a panel of its own.</para>
        /// </remarks>
        public void SetData(string key, string value)
        {
            switch (key)
            {
                case "isActive":
                    isActive = SettingsParse.Bool(key, value, isActive);
                    break;
                case "serialNumber":
                    serialNumber = SettingsParse.Int(key, value, serialNumber);
                    break;
                case "boardType":
                    boardType = value;
                    break;
                case "boardIp":
                    boardIp = value;
                    break;
                case "boardPort":
                    boardPort = SettingsParse.Int(key, value, boardPort, 1, SettingsParse.MaxPort);
                    break;
                case "reconnectTime":
                    // float, not int - the field is a float and int.Parse silently rejected
                    // any decimal an operator entered.
                    reconnectTime = SettingsParse.Float(key, value, reconnectTime, 0.1f);
                    break;
                default:
                    SettingsParse.Error($"SetData: unknown key [{key}] - nothing was saved for it.");
                    break;
            }
        }
    }
}
