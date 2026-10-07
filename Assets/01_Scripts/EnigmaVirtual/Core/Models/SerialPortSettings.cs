/*******************************************************
 * Copyright (C) 2025 Gema Digital Agency
 * 
 * Can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/

using System;
// Only the two computed properties below need this namespace - every *serialized* field on
// this class is a string, which is why the settings schema is identical on every platform whether
// the gate is open or not, which is what makes the platform gate tractable.
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.IO.Ports;
#endif

namespace EnigmaVirtual
{
    [System.Serializable]
    public class SerialPortSettings
    {
        public bool isActive = false;
        public bool doLogs;
        public string name = "";
        public bool showNotifications = true;
        public string comPort;
        public int baudRate;
        public string rawParity = "None";
        public string rawStopBits = "One";
        public int dataBits = 8;
        // The state of the Data Terminal Ready(DTR) signal during serial communication.
        public bool dtrEnable = false;
        // Whether or not the Request to Send(RTS) signal is enabled during serial communication.
        public bool rtsEnable = false;
        // Read and write timeouts
        public int ReadTimeout = 10;
        public int WriteTimeout = 1000;
        // Keepalive: send a command periodically to prevent device sleep
        // Set to 0 to disable. Value is in seconds.
        public float keepAliveInterval = 30f;
        public string keepAliveCommand = "STATUS";

#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
        internal Parity parity
        {
            get
            {
                if (Enum.TryParse(rawParity, true, out Parity parsedValue))
                    return parsedValue;

                Development.LogWarning("Parity [" + rawParity + "] not found!");

                return default;
            }
        }

        internal StopBits stopBits
        {
            get
            {
                if (Enum.TryParse(rawStopBits, true, out StopBits parsedValue))
                    return parsedValue;

                Development.LogWarning("StopBits [" + rawStopBits + "] not found!");

                return default;
            }
        }
#endif
    }

}