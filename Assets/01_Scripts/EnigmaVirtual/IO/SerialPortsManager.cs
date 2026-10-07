/*******************************************************
 * Copyright (C) 2025 Gema Digital Agency
 * 
 * SerialPortsManager can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EnigmaVirtual
{
    public class SerialPortDataEvent : UnityEvent<string, ComMessage> { }
    public class SerialPortRawDataEvent : UnityEvent<string, string> { }

    public class SerialPortsManager : MonoBehaviour
    {
        [Header("Events")]
        /// <summary>
        /// (int) serial, (int) sensor, (object) value
        /// </summary>
        private SerialPortDataEvent _onSerialData = new SerialPortDataEvent();
        public SerialPortDataEvent onSerialDataEvent => _onSerialData;
        private SerialPortRawDataEvent _onSerialRawData = new SerialPortRawDataEvent();
        public SerialPortRawDataEvent onSerialRawDataEvent => _onSerialRawData;

        // Everything below this point is serial-port machinery. The two events above stay ungated
        // on purpose: App code subscribes to them, and that has to keep compiling on every target.
        //
        // The gate text is identical in all six places that carry it, so one grep finds the set: this
        // file, MainController's SetupSerialPorts call site, SerialPortSettings' two computed
        // properties, and External/Unity_SerialPort's UnitySerialPort.cs and GUIManager.cs, plus
        // ArduinoController.cs. A literal rather than a shared symbol on purpose: #if cannot consult a
        // runtime bool, and a per-target scripting define lives in binary ProjectSettings.asset and
        // gets forgotten on one platform. When Unity_SerialPort gets its own
        // .asmdef, platform Include/Exclude replaces these gates outright, and identical text makes
        // that swap mechanical.
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
        private List<UnitySerialPort> controllers;

        void Start()
        {
            if (!Core.IsSet)
            {
                // This class needs *both* halves. enabled = false stops any future
                // per-frame message, but a disabled MonoBehaviour still has callable public methods
                // - and SendSerialData/SendSerialDataAsLine below read `controllers`, which is
                // assigned after this point. So they are guarded individually too.
                //
                // As with ActivityMonitor: unreachable in this project, since MainController
                // creates this from Init() after Core.IsSet is true and nothing scene-places it.
                // Live for a derived project that does.
                Development.LogError("SerialPortsManager disabled: Core is not set, so no serial "
                    + "ports were opened. Nothing will be sent to or received from serial hardware "
                    + "this session.");
                enabled = false;
                return;
            }

            controllers = new List<UnitySerialPort>();

            StartCoroutine(InitSerialPorts());
        }

        private void Update()
        {
            for (int i = 0; i <= 9; i++)
            {
                KeyCode key = KeyCode.Alpha0 + i;

                if (Input.GetKeyDown(key))
                    SerialPort_SerialDataParseEvent(new string[] { }, $"{i}#i#1", "FAKEPORT");

                if (Input.GetKeyUp(key))
                    SerialPort_SerialDataParseEvent(new string[] { }, $"{i}#i#0", "FAKEPORT");
            }
        }

        private IEnumerator InitSerialPorts()
        {
            if (!Core.settings.SerialPorts.isActive)
                yield break;

            Development.LogIf(Core.settings.SerialPorts.doLogs, string.Concat("[", Core.settings.SerialPorts.serialPorts.Length, "] SerialPorts available"));

            UnitySerialPort serialPort = null;
            GameObject go;

            for (int i = 0; i < Core.settings.SerialPorts.serialPorts.Length; i++)
            {
                if (!Core.settings.SerialPorts.serialPorts[i].isActive)
                    continue;

                Development.LogIf(Core.settings.SerialPorts.doLogs, ("Adding device [" + Core.settings.SerialPorts.serialPorts[i].name + "] at COM Port [" + Core.settings.SerialPorts.serialPorts[i].comPort + "]"));

                go = new GameObject(Core.settings.SerialPorts.serialPorts[i].name.IsNullOrEmpty() ? Core.settings.SerialPorts.serialPorts[i].comPort : Core.settings.SerialPorts.serialPorts[i].name);
                go.transform.SetParent(this.transform);

                serialPort = go.AddComponent<UnitySerialPort>();
                serialPort.SerialPortName = Core.settings.SerialPorts.serialPorts[i].name.IsNullOrEmpty() ? Core.settings.SerialPorts.serialPorts[i].comPort : Core.settings.SerialPorts.serialPorts[i].name;
                serialPort.ComPort = Core.settings.SerialPorts.serialPorts[i].comPort;
                serialPort.BaudRate = Core.settings.SerialPorts.serialPorts[i].baudRate;
                serialPort.Parity = Core.settings.SerialPorts.serialPorts[i].parity;
                serialPort.StopBits = Core.settings.SerialPorts.serialPorts[i].stopBits;
                serialPort.DataBits = Core.settings.SerialPorts.serialPorts[i].dataBits;
                serialPort.DtrEnable = Core.settings.SerialPorts.serialPorts[i].dtrEnable;
                serialPort.RtsEnable = Core.settings.SerialPorts.serialPorts[i].rtsEnable;
                serialPort.ReadTimeout = Core.settings.SerialPorts.serialPorts[i].ReadTimeout;
                serialPort.WriteTimeout = Core.settings.SerialPorts.serialPorts[i].WriteTimeout;
                serialPort.KeepAliveInterval = Core.settings.SerialPorts.serialPorts[i].keepAliveInterval;
                serialPort.KeepAliveCommand = Core.settings.SerialPorts.serialPorts[i].keepAliveCommand;
                serialPort.doLogs = Core.settings.SerialPorts.serialPorts[i].doLogs;
                serialPort.showNotifications = Core.settings.SerialPorts.serialPorts[i].showNotifications;
                serialPort.SerialPortOpenEvent += SerialPort_SerialPortOpenEvent;
                serialPort.SerialDataParseEvent += SerialPort_SerialDataParseEvent;
                serialPort.SerialPortCloseEvent += SerialPort_SerialPortCloseEvent;
                serialPort.OpenSerialPort();

                // Still added when it failed to open, and that is deliberate. Dropping it would
                // make SendSerialData report "no serial port named [x]", sending someone to check
                // settings.json for a port that is configured perfectly well and simply is not
                // open. Keeping it means the send path reports "the port is closed" instead, which
                // is the actual problem. OpenSerialPort is void and swallows its own exception, so
                // IsRunning is the only signal available here.
                if (!serialPort.IsRunning)
                {
                    Development.LogError($"Serial port [{serialPort.SerialPortName}] on "
                        + $"{serialPort.ComPort} did not open. Commands addressed to it will be "
                        + "reported as not sent.");
                }

                controllers.Add(serialPort);
            }
        }

        /// <summary>Returns whether the data actually reached the hardware. Callers that care can
        /// now act on a dropped command instead of only finding it in the log.</summary>
        public bool SendSerialData(string name, string data)
        {
            if (!HasControllers(nameof(SendSerialData), name))
                return false;

            bool found = false;
            bool sent = false;

            for (int i = 0; i < controllers.Count; i++)
                if (controllers[i].SerialPortName == name)
                {
                    found = true;
                    sent |= controllers[i].SendSerialData(data);
                }

            // "no serial port named", not "no *open* one": ports that failed to open are kept in
            // this list deliberately, so reaching here means the name is not configured at all -
            // a different problem, pointing at settings.json rather than at the cable. A port that
            // exists but is shut reports through UnitySerialPort.CanWrite instead.
            if (!found)
                Development.LogError($"SendSerialData: no serial port named [{name}] is configured. "
                    + "The command was NOT sent. Check SerialPorts.serialPorts in settings.json.");

            return sent;
        }

        /// <summary>Returns whether the data actually reached the hardware. See
        /// <see cref="SendSerialData"/>.</summary>
        public bool SendSerialDataAsLine(string name, string data)
        {
            if (!HasControllers(nameof(SendSerialDataAsLine), name))
                return false;

            bool found = false;
            bool sent = false;

            for (int i = 0; i < controllers.Count; i++)
                if (controllers[i].SerialPortName == name)
                {
                    found = true;
                    sent |= controllers[i].SendSerialDataAsLine(data);
                }

            if (!found)
                Development.LogError($"SendSerialDataAsLine: no serial port named [{name}] is "
                    + "configured. The command was NOT sent. Check SerialPorts.serialPorts in "
                    + "settings.json.");

            return sent;
        }

        /// <summary>
        /// Whether this manager ever initialised. False means <see cref="Start"/> bailed because
        /// Core was not set, which leaves <c>controllers</c> null.
        ///
        /// Reports at Error rather than returning quietly: a command that never reaches the
        /// hardware is a projector that did not switch on or a relay that did not fire, and that
        /// has to be distinguishable from one that was sent.
        /// </summary>
        private bool HasControllers(string caller, string portName)
        {
            if (controllers != null)
                return true;

            Development.LogError($"{caller}([{portName}]) ignored: SerialPortsManager never "
                + "initialised, because Core was not set when it started. The command was NOT sent "
                + "to the hardware.");
            return false;
        }

        private void SerialPort_SerialPortOpenEvent(string comPort)
        {
            Development.LogIf(Core.settings.SerialPorts.doLogs, "SerialPort [" + comPort + "] Open!");
        }

        private void SerialPort_SerialPortCloseEvent(string comPort)
        {
            Development.LogIf(Core.settings.SerialPorts.doLogs, "SerialPort [" + comPort + "] Closed!");
        }

        private void SerialPort_SerialDataParseEvent(string[] data, string rawData, string comPort)
        {
            Development.LogIf(Core.settings.SerialPorts.doLogs, "SerialData [" + comPort + "] :" + rawData);

            _onSerialRawData?.Invoke(comPort, rawData);

            if (ComMessage.TryParse(rawData, out ComMessage msg))
            {
                Development.LogIf(Core.settings.SerialPorts.doLogs, $"Parsed ComMessage: Id={msg.Id}, Type={msg.Type}, Value={msg.Value}");
                _onSerialData?.Invoke(comPort, msg);
            }
            else
            {
                // Was double-gated (Assert is conditional on DO_LOGS *and* took doLogs), so a
                // device whose message format no longer matches ComMessage failed every single
                // frame in complete silence. Left at Warning rather than raised to Error: a
                // single unparseable frame is ordinary line noise, and serial ports only run on
                // Standalone/Android, where DO_LOGS is defined. Raw data still reaches
                // _onSerialRawData subscribers regardless.
                Development.LogWarning($"Failed to parse ComMessage from data: {rawData}");
                return;
            }
        }

#else
        private void Update()
        {
            for (int i = 0; i <= 9; i++)
            {
                KeyCode key = KeyCode.Alpha0 + i;

                if (Input.GetKeyDown(key))
                    SerialPort_SerialDataParseEvent(new string[] { }, $"{i}#i#1", "FAKEPORT");

                if (Input.GetKeyUp(key))
                    SerialPort_SerialDataParseEvent(new string[] { }, $"{i}#i#0", "FAKEPORT");
            }
        }

        /// <summary>
        /// Reports, rather than doing nothing. Serial ports need <c>System.IO.Ports</c>, which this target
        /// does not have - so the public surface is kept and every send says the command was not
        /// delivered. A silent no-op here would be the codebase's dominant defect class: the caller
        /// believes a projector switched on or a relay fired, and nothing says otherwise.
        /// </summary>
        public bool SendSerialData(string name, string data)
        {
            return ReportUnsupported(nameof(SendSerialData), name);
        }

        /// <summary>See <see cref="SendSerialData"/>.</summary>
        public bool SendSerialDataAsLine(string name, string data)
        {
            return ReportUnsupported(nameof(SendSerialDataAsLine), name);
        }

        private static readonly HashSet<string> _reportedPorts = new HashSet<string>();

        private static bool ReportUnsupported(string caller, string portName)
        {
            // Once per port name. LogError is always compiled, and a keep-alive or per-frame send
            // to a stubbed target would otherwise fill the log at frame rate - same reasoning as
            // UnitySerialPort's write-failure suppression. Unlike the open check there is no recovery to
            // reset on: the platform will not acquire serial support at runtime.
            if (_reportedPorts.Add(portName))
            {
                Development.LogError($"{caller}([{portName}]) ignored: serial ports are not supported "
                    + "on this platform. The command was NOT sent to the hardware, and further sends to "
                    + "this port stay silent. (M21)");
            }

            return false;
        }
#endif

        // Handled on each connection
        //void OnApplicationQuit()
        //{
        //    for (int i = 0; i < controllers.Count; i++)
        //        if (controllers[i].IsRunning)
        //            controllers[i].CloseSerialPort();
        //}
    }
}