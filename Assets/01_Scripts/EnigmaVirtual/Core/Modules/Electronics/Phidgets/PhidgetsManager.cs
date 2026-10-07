/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * PhidgetsManager can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EnigmaVirtual
{

    public class PhidgetsDataEvent : UnityEvent<int, int, object> { }
    public class PhidgetsErrorEvent : UnityEvent<string> { }

    public enum PhidgetsBoard
    {
        Bridge,
        InterfaceKit
    }

    public class PhidgetsManager : MonoBehaviour
    {
        [Header("Events")]
        /// <summary>
        /// (int) serial, (int) sensor, (object) value
        /// </summary>
        private PhidgetsDataEvent _onPhidgetData = new PhidgetsDataEvent();
        public PhidgetsDataEvent OnPhidgetData { get { return _onPhidgetData; } }
        // = null explicitly: with Start gated out, nothing assigns this on a non-Windows compile and
        // CS0649 fires in every one of those builds. OnApplicationQuit already null-checks it.
        private List<IPhidgetsController> _controllers = null;

        // InitPhidgets and SetState are the only members naming the two board controllers, which need
        // the Phidget22 SDK. Start is in here because it starts InitPhidgets, not because it names
        // either. Everything else stays on every
        // platform: the event types, OnPhidgetData, the three dispatch methods and OnApplicationQuit
        // touch only IPhidgetsController and the event, neither of which reaches the SDK - so App code
        // keeps compiling and MainController's PhidgetsManager-typed property still resolves.
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
        void Start()
        {
            if (!Core.IsSet)
            {
                // Same shape as SerialPortsManager, and it was wrongly left out of the first pass
                // here: `enabled = false` does not stop the public SetState below, and it does
                // not stop OnApplicationQuit either - that is a Unity message, so a disabled
                // component still receives it, and an NRE there aborts CloseAll and leaves the
                // hardware handles open at shutdown, when nobody is reading a log.
                Development.LogError("PhidgetsManager disabled: Core is not set, so no Phidget "
                    + "boards were opened. Nothing will be sent to or received from them this "
                    + "session.");
                enabled = false;
                return;
            }

            _controllers = new List<IPhidgetsController>();

            StartCoroutine(InitPhidgets());
        }
        private IEnumerator InitPhidgets()
        {
            if(!Core.settings.Phidgets.isActive)
                yield break;

            Development.Log(string.Concat("[", Core.settings.Phidgets.phidgets.Length, "] Phidgets available"));

            IPhidgetsController pc;
            GameObject go;

            for (int i = 0; i < Core.settings.Phidgets.phidgets.Length; i++)
            {
                if (!Core.settings.Phidgets.phidgets[i].isActive)
                    continue;

                Development.Log("Adding device [" + Core.settings.Phidgets.phidgets[i].PhidgetsBoardType.ToString() + "] with serial [" + Core.settings.Phidgets.phidgets[i].serialNumber + "]");

                pc = null;
                switch (Core.settings.Phidgets.phidgets[i].PhidgetsBoardType)
                {
                    case PhidgetsBoard.Bridge:
                        go = new GameObject("Phidget_Bridge_" + i);
                        go.transform.SetParent(this.transform);
                        pc = go.AddComponent<PhidgetsBridgeController>();
                        break;
                    case PhidgetsBoard.InterfaceKit:
                        go = new GameObject("Phidget_IC_" + i);
                        go.transform.SetParent(this.transform);
                        pc = go.AddComponent<PhidgetInterfaceKitController>();
                        break;
                    default:
                        break;
                }
                if (pc != null)
                {
                    _controllers.Add(pc);
                    pc.Init(Core.settings.Phidgets.phidgets[i], this);
                    yield return new WaitForSeconds(3.5f);
                }
            }
        }

        public void SetState(int serial, int portId, bool val)
        {
            Development.CheckPoint();

            if (_controllers == null)
            {
                Development.LogError($"SetState(serial {serial}, port {portId}) ignored: "
                    + "PhidgetsManager never initialised, because Core was not set when it started. "
                    + "The command was NOT sent to the board.");
                return;
            }

            for (int i = 0; i < _controllers.Count; i++)
            {
                if ((_controllers[i] as PhidgetInterfaceKitController) != null)
                {
                    if ((_controllers[i] as PhidgetInterfaceKitController).serialNumber == serial)
                    {
                        Development.Log($"Sending [{val}] to port [{portId}] on device [{serial}]");
                        (_controllers[i] as PhidgetInterfaceKitController).SetState(portId, val);
                        return;
                    }
                }
                else
                    Development.LogWarning("Phidget not PhidgetInterfaceKitController type!");
            }

            Development.LogWarning($"Phidget with serial [{serial}] not found!");
        }
#else
        /// <summary>
        /// Reports rather than doing nothing, for the one caller that can reach it.
        /// </summary>
        /// <remarks>
        /// Narrower than it looks: `MainController`
        /// gates `SetupPhidgets()` on the same symbol, so on a non-Windows target `Core.main.Phidgets` is
        /// null and an App caller going through it gets an NRE before arriving here. What this covers is a
        /// `PhidgetsManager` placed by hand in a scene - which is exactly how a branched project would hit
        /// it, and the case where silence would mean a relay that did not fire with nothing in the log.
        /// </remarks>
        public void SetState(int serial, int portId, bool val)
        {
            // Once per serial, for the same reason SerialPortsManager suppresses its own: LogError is
            // always compiled and a per-frame caller would fill the log at frame rate. There is no
            // recovery to reset on - the platform will not acquire the SDK at runtime.
            if (_reportedSerials.Add(serial))
            {
                Development.LogError($"SetState(serial {serial}, port {portId}) ignored: Phidget boards "
                    + "are not supported on this platform, because the Phidget22 SDK is Windows-only. The "
                    + "command was NOT sent to the board, and further commands to this serial stay silent. "
                    + "(M14)");
            }
        }

        // Instance, not static. A static set is shared by every manager and never resets, so an
        // operator re-testing after a scene reload gets silence. Same size, fewer surprises.
        private readonly HashSet<int> _reportedSerials = new HashSet<int>();
#endif

        void OnApplicationQuit()
        {
            // Null when Start bailed. Unity delivers this message to disabled components too, so
            // enabled = false does not cover it - and an NRE here would abort the loop and leave
            // every board's handles open on the way out.
            if (_controllers == null)
                return;

            for (int i = 0; i < _controllers.Count; i++)
                _controllers[i].CloseAll();
        }

        public void SensorDataDispatch(int serial, int channel, object value)
        {
            //Development.Log("SensorDataDispatch [" + serial + "] channel [" + channel + "] value [" + value + "]");
            _onPhidgetData?.Invoke(serial, channel, value);
        }

        public void SensorVoltageDispatch(int serial, int channel, object value)
        {
            //Development.Log("SensorVoltageDispatch [" + serial + "] channel [" + channel + "] value [" + value + "]");
            _onPhidgetData?.Invoke(serial, channel, value);
        }

        public void SensorVoltageRatioDispatch(int serial, int channel, object value)
        {
            //Development.Log("SensorVoltageRatioDispatch [" + serial + "] channel [" + channel + "] value [" + value + "]");
            _onPhidgetData?.Invoke(serial, channel, value);
        }
    }
}