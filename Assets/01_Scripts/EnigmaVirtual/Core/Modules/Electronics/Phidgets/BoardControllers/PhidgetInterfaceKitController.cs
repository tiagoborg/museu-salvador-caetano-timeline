/*******************************************************
 * Copyright (C) 2023 Gema Digital
 * 
 * PhidgetInterfaceKitController can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
// This file uses the Phidget22 SDK, whose managed assembly is now excluded from every
// non-Windows player - so the file has to carry the same gate or those builds stop compiling.
// Gate spelling matches MainController's existing Phidgets call site, so one grep
// finds every place that carries it.
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
using Phidget22;
using Phidget22.Events;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnigmaVirtual
{
    public class PhidgetInterfaceKitController : MonoBehaviour, IPhidgetsController
    {
        /// <summary>
        /// Returns Phidget serial number
        /// </summary>
        public int serialNumber { get { return settings.serialNumber; } }

        private PhidgetSettings settings;
        private PhidgetsManager manager;
        private List<DigitalInput> digitalInputs;
        private List<DigitalOutput> digitalOutputs;
        private List<VoltageInput> voltageInputs;
        private List<VoltageRatioInput> voltageRatioInputs;

        /// <summary>
        /// Initializes Phidget
        /// </summary>,
        /// <param name="settings">Phidget settings</param>
        /// <param name="manager">Phidgets Manager</param>
        public void Init(PhidgetSettings settings, PhidgetsManager manager)
        {
            Development.Log("Initializing Phidget " + settings.serialNumber);

            this.settings = settings;
            this.manager = manager;

            digitalInputs = new List<DigitalInput>();
            digitalOutputs = new List<DigitalOutput>();
            voltageInputs = new List<VoltageInput>();
            voltageRatioInputs = new List<VoltageRatioInput>();

            // Initializes digital inputs request in core settings
            if (!settings.digitalInputs.IsNullOrEmpty())
            {
                for (int i = 0; i < settings.digitalInputs.Length; i++)
                {
                    if (settings.digitalInputs[i])
                    {
                        //Create Phidget channel
                        DigitalInput di = new DigitalInput();
                        di.DeviceSerialNumber = settings.serialNumber;
                        di.Channel = i;

                        //Assign any event handlers
                        di.StateChange += DigitalInput_StateChange;
                        di.Attach += DigitalInput_Attach;
                        di.Detach += DigitalInput_Detach;
                        di.Error += DigitalInput_Error;

                        try
                        {
                            //Try open Phidget channel
                            di.Open(settings.timeout);
                            digitalInputs.Add(di);
                        }
                        catch (PhidgetException ex)
                        {
                            //Catch Phidget Exceptions
                            Development.LogError("PhidgetException " + ex.ErrorCode + " (" + ex.Description + "): " + ex.Detail);
                            Core.notificationManager.ShowNotification(Notifications.NotificationManager.NotificationType.Error, "Phidget " + ex.Description, ex.Detail);

                            if (ex.ErrorCode.ToString().Contains("Timeout"))
                            {
                                Development.LogError("Phidget board [" + settings.serialNumber + "] not attached!");
                                return;
                            }
                        }
                    }
                }

                // Initializes digital outputs request in core settings
                if (!settings.digitalOutputs.IsNullOrEmpty())
                {
                    for (int i = 0; i < settings.digitalOutputs.Length; i++)
                    {
                        if (settings.digitalOutputs[i])
                        {
                            //Create Phidget channel
                            DigitalOutput dout = new DigitalOutput();
                            dout.DeviceSerialNumber = settings.serialNumber;
                            dout.Channel = i;

                            //Assign any event handlers
                            dout.Attach += DigitalOutput_Attach;
                            dout.Detach += DigitalOutput_Detach;
                            dout.Error += DigitalOutput_Error;

                            try
                            {
                                //Try open Phidget channel
                                dout.Open(settings.timeout);
                                dout.DutyCycle = 1;
                                digitalOutputs.Add(dout);
                                // set digital output state
                                SetState(i, settings.enableDigitalOutputsOnStart);
                            }
                            catch (PhidgetException ex)
                            {
                                //Catch Phidget Exceptions
                                Development.LogError("PhidgetException " + ex.ErrorCode + " (" + ex.Description + "): " + ex.Detail);
                                Core.notificationManager.ShowNotification(Notifications.NotificationManager.NotificationType.Error, "Phidget " + ex.Description, ex.Detail);

                                if (ex.ErrorCode.ToString().Contains("Timeout"))
                                {
                                    Development.LogError("Phidget board [" + settings.serialNumber + "] not attached!");
                                    return;
                                }
                            }
                        }
                    }
                }

                // Initializes voltage inputs request in core settings
                if (!settings.voltageInputs.IsNullOrEmpty())
                {
                    for (int i = 0; i < settings.voltageInputs.Length; i++)
                    {
                        if (settings.voltageInputs[i])
                        {
                            //Create Phidget channel
                            VoltageInput vi = new VoltageInput();
                            vi.DeviceSerialNumber = settings.serialNumber;
                            vi.Channel = i;

                            //Assign any event handlers

                            vi.VoltageChange += VoltageInput_VoltageChange;
                            vi.SensorChange += VoltageInput_SensorChange;
                            vi.Attach += VoltageInput_Attach;
                            vi.Detach += VoltageInput_Detach;
                            vi.Error += VoltageInput_Error;

                            try
                            {
                                //Try open Phidget channel
                                vi.Open(settings.timeout);
                                if (i < settings.voltageSensorType.Length)
                                    vi.SensorType = GetVoltageSensorType(settings.voltageSensorType[i]);
                                voltageInputs.Add(vi);
                            }
                            catch (PhidgetException ex)
                            {
                                //Catch Phidget Exceptions
                                Development.LogError("PhidgetException " + ex.ErrorCode + " (" + ex.Description + "): " + ex.Detail);
                                Core.notificationManager.ShowNotification(Notifications.NotificationManager.NotificationType.Error, "Phidget " + ex.Description, ex.Detail);

                                if (ex.ErrorCode.ToString().Contains("Timeout"))
                                {
                                    Development.LogError("Phidget board [" + settings.serialNumber + "] not attached!");
                                    return;
                                }
                            }
                        }
                    }
                }

                // Initializes voltage ratio inputs request in core settings
                if (!settings.voltageRatioInputs.IsNullOrEmpty())
                {
                    for (int i = 0; i < settings.voltageRatioInputs.Length; i++)
                    {
                        if (settings.voltageRatioInputs[i])
                        {
                            //Create Phidget channel
                            VoltageRatioInput vri = new VoltageRatioInput();
                            vri.DeviceSerialNumber = settings.serialNumber;
                            vri.Channel = i;

                            //Assign any event handlers
                            vri.VoltageRatioChange += VoltageRatioInput_VoltageRatioChange;
                            vri.SensorChange += VoltageRatioInput_SensorChange;
                            vri.Attach += VoltageRatioInput_Attach;
                            vri.Detach += VoltageRatioInput_Detach;
                            vri.Error += VoltageRatioInput_Error;

                            try
                            {
                                //Try open Phidget channel
                                vri.Open(settings.timeout);
                                if (i < settings.voltageRatioSensorType.Length)
                                    vri.SensorType = GetVoltageRatioSensorType(settings.voltageRatioSensorType[i]);

                                voltageRatioInputs.Add(vri);
                            }
                            catch (PhidgetException ex)
                            {
                                //Catch Phidget Exceptions
                                Development.LogError("PhidgetException " + ex.ErrorCode + " (" + ex.Description + "): " + ex.Detail);
                                Core.notificationManager.ShowNotification(Notifications.NotificationManager.NotificationType.Error, "Phidget " + ex.Description, ex.Detail);

                                if (ex.ErrorCode.ToString().Contains("Timeout"))
                                {
                                    Development.LogError("Phidget board [" + settings.serialNumber + "] not attached!");
                                    return;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void DigitalInput_Attach(object sender, AttachEventArgs e)
        {
            DigitalInput evChannel = (DigitalInput)sender;
            if (settings.doLogs)
                Development.Log("DigitalInput_Attach [" + serialNumber + "][" + evChannel.Channel + "]");
        }

        private void DigitalInput_Detach(object sender, DetachEventArgs e)
        {
            DigitalInput evChannel = (DigitalInput)sender;
            if (settings.doLogs)
                Development.Log("DigitalInput_Detach [" + serialNumber + "][" + evChannel.Channel + "]");
        }

        private void DigitalInput_Error(object sender, ErrorEventArgs e)
        {
            DigitalInput evChannel = (DigitalInput)sender;
            if (settings.doLogs)
                Development.LogError("DigitalInput_Error [" + serialNumber + "][" + evChannel.Channel + "] code [" + e.Code + "] description [" + e.Description + "]");
        }

        private void DigitalOutput_Attach(object sender, AttachEventArgs e)
        {
            DigitalOutput evChannel = (DigitalOutput)sender;
            if (settings.doLogs)
                Development.Log("DigitalOutput_Attach [" + serialNumber + "][" + evChannel.Channel + "]");
        }

        private void DigitalOutput_Detach(object sender, DetachEventArgs e)
        {
            DigitalOutput evChannel = (DigitalOutput)sender;
            if (settings.doLogs)
                Development.Log("DigitalOutput_Detach [" + serialNumber + "][" + evChannel.Channel + "]");
        }

        private void DigitalOutput_Error(object sender, ErrorEventArgs e)
        {
            DigitalOutput evChannel = (DigitalOutput)sender;
            if (settings.doLogs)
                Development.LogError("DigitalOutput_Error [" + serialNumber + "][" + evChannel.Channel + "] code [" + e.Code + "] description [" + e.Description + "]");
        }

        private void VoltageRatioInput_Attach(object sender, AttachEventArgs e)
        {
            VoltageRatioInput evChannel = (VoltageRatioInput)sender;
            if (settings.doLogs)
                Development.Log("VoltageRatioInput_Attach [" + serialNumber + "][" + evChannel.Channel + "]");
        }

        private void VoltageRatioInput_Detach(object sender, DetachEventArgs e)
        {
            VoltageRatioInput evChannel = (VoltageRatioInput)sender;
            if (settings.doLogs)
                Development.Log("VoltageRatioInput_Detach [" + serialNumber + "][" + evChannel.Channel + "]");
        }

        private void VoltageRatioInput_Error(object sender, ErrorEventArgs e)
        {
            //VoltageRatioInput evChannel = (VoltageRatioInput)sender;
            //Development.LogError("VoltageRatioInput_Error [" + serialNumber + "][" + evChannel.Channel + "] code [" + e.Code + "] description [" + e.Description + "]");
        }

        private void VoltageInput_Attach(object sender, AttachEventArgs e)
        {
            VoltageInput evChannel = (VoltageInput)sender;
            if (settings.doLogs)
                Development.Log("VoltageInput_Attach [" + serialNumber + "][" + evChannel.Channel + "]");
        }

        private void VoltageInput_Detach(object sender, DetachEventArgs e)
        {
            VoltageInput evChannel = (VoltageInput)sender;
            if (settings.doLogs)
                Development.Log("VoltageInput_Detach [" + serialNumber + "][" + evChannel.Channel + "]");
        }

        private void VoltageInput_Error(object sender, ErrorEventArgs e)
        {
            //VoltageInput evChannel = (VoltageInput)sender;
            //Development.LogError("VoltageInput_Error [" + serialNumber + "][" + evChannel.Channel + "] code [" + e.Code + "] description [" + e.Description + "]");
        }

        private VoltageSensorType GetVoltageSensorType(string value)
        {
            if (Enum.TryParse(value, true, out VoltageSensorType parsed_value))
                return parsed_value;
            return VoltageSensorType.Voltage;
        }

        private VoltageRatioSensorType GetVoltageRatioSensorType(string value)
        {
            if (Enum.TryParse(value, true, out VoltageRatioSensorType parsed_value))
                return parsed_value;
            return VoltageRatioSensorType.VoltageRatio;
        }

        private void VoltageInput_SensorChange(object sender, VoltageInputSensorChangeEventArgs e)
        {
            VoltageRatioInput evChannel = (VoltageRatioInput)sender;
            manager.SensorDataDispatch(serialNumber, evChannel.Channel, e.SensorValue);

            //Development.LogError("SensorValue: " + e.SensorValue);
            //Development.LogError("SensorUnit: " + e.SensorUnit.Symbol);

            if (settings.doLogs)
                Development.Log("SensorDataDispatch [" + serialNumber + "] [" + evChannel.Channel + "] value [" + e.SensorValue + "]");
        }

        private void VoltageRatioInput_SensorChange(object sender, VoltageRatioInputSensorChangeEventArgs e)
        {
            VoltageRatioInput evChannel = (VoltageRatioInput)sender;
            manager.SensorDataDispatch(serialNumber, evChannel.Channel, e.SensorValue);

            if (settings.doLogs)
                Development.Log("SensorDataDispatch [" + serialNumber + "] [" + evChannel.Channel + "] value [" + e.SensorValue + "]");
        }

        private void VoltageRatioInput_VoltageRatioChange(object sender, VoltageRatioInputVoltageRatioChangeEventArgs e)
        {
            VoltageRatioInput evChannel = (VoltageRatioInput)sender;
            manager.SensorVoltageRatioDispatch(serialNumber, evChannel.Channel, e.VoltageRatio);

            if (settings.doLogs)
                Development.Log("SensorVoltageRatioDispatch [" + serialNumber + "] [" + evChannel.Channel + "] value [" + e.VoltageRatio + "]");
        }

        private void VoltageInput_VoltageChange(object sender, VoltageInputVoltageChangeEventArgs e)
        {
            VoltageInput evChannel = (VoltageInput)sender;
            manager.SensorVoltageDispatch(serialNumber, evChannel.Channel, e.Voltage);

            if (settings.doLogs)
                Development.Log("SensorVoltageDispatch [" + serialNumber + "] [" + evChannel.Channel + "] value [" + e.Voltage + "]");
        }

        private void DigitalInput_StateChange(object sender, DigitalInputStateChangeEventArgs e)
        {
            DigitalInput evChannel = (DigitalInput)sender;
            manager.SensorDataDispatch(serialNumber, evChannel.Channel, e.State);

            if (settings.doLogs)
                Development.Log("SensorDataDispatch [" + serialNumber + "] [" + evChannel.Channel + "] value [" + e.State + "]");
        }

        /// <summary>
        /// Set channel state
        /// </summary>
        /// <param name="channel">Phidget channel</param>
        /// <param name="value">New value</param>
        internal void SetState(int channel, bool value)
        {
            Development.Log("SetState [" + serialNumber + "] [" + channel + "] : " + value);

            if (digitalOutputs.IsNullOrEmpty())
                Development.LogError("No digital outputs initialized!");

            if (TryGetDigitalOutput(channel, out DigitalOutput digitalOutput))
                digitalOutput.State = value;
            else
                Development.LogError("Selected digital output unavailable!");
        }

        /// <summary>
        /// Tries to return the requested digital output.
        /// </summary>
        /// <param name="channel">Phidget channel</param>
        /// <param name="digitalOutput">DigitalOutput</param>
        /// <returns></returns>
        private bool TryGetDigitalOutput(int channel, out DigitalOutput digitalOutput)
        {
            digitalOutput = null;

            for (int i = 0; i < digitalOutputs.Count; i++)
            {
                if (digitalOutputs[i].Channel == channel && digitalOutputs[i].IsOpen)
                {
                    digitalOutput = digitalOutputs[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Closes all Phidget channels communications
        /// </summary>
        public void CloseAll()
        {
            Development.Log("Close Phidget InterfaceKit [" + serialNumber + "]");

            for (int i = 0; i < digitalInputs.Count; i++)
            {
                digitalInputs[i].StateChange -= DigitalInput_StateChange;
                digitalInputs[i].Attach -= DigitalInput_Attach;
                digitalInputs[i].Error -= DigitalInput_Error;

                if (digitalInputs[i].IsOpen)
                    digitalInputs[i].Close();

                digitalInputs[i].Detach -= DigitalInput_Detach;
            }

            for (int i = 0; i < digitalOutputs.Count; i++)
            {
                digitalOutputs[i].Attach -= DigitalOutput_Attach;
                digitalOutputs[i].Error -= DigitalOutput_Error;

                if (digitalOutputs[i].IsOpen)
                    digitalOutputs[i].Close();

                digitalOutputs[i].Detach -= DigitalOutput_Detach;
            }

            for (int i = 0; i < voltageInputs.Count; i++)
            {
                voltageInputs[i].VoltageChange -= VoltageInput_VoltageChange;
                voltageInputs[i].SensorChange -= VoltageInput_SensorChange;
                voltageInputs[i].Attach -= VoltageInput_Attach;
                voltageInputs[i].Error -= VoltageInput_Error;

                if (voltageInputs[i].IsOpen)
                    voltageInputs[i].Close();

                voltageInputs[i].Detach -= VoltageInput_Detach;
            }

            for (int i = 0; i < voltageRatioInputs.Count; i++)
            {
                voltageRatioInputs[i].VoltageRatioChange -= VoltageRatioInput_VoltageRatioChange;
                voltageRatioInputs[i].SensorChange -= VoltageRatioInput_SensorChange;
                voltageRatioInputs[i].Attach -= VoltageRatioInput_Attach;
                voltageRatioInputs[i].Error -= VoltageRatioInput_Error;

                if (voltageRatioInputs[i].IsOpen)
                    voltageRatioInputs[i].Close();

                voltageRatioInputs[i].Detach -= VoltageRatioInput_Detach;
            }
        }

        /// <summary>
        /// Filters received data only dispatching data inside settings min and max values 
        /// </summary>
        /// <param name="sender">Port that dispatched the data</param>
        /// <param name="data">Dispatched data</param>
        private void DataFilter(int sender, object data)
        {
            if (!AsMinValue(sender)) // doesnt have min value
            {
                if (!AsMaxValue(sender)) // doesnt have min or max value
                {
                    manager.SensorDataDispatch(settings.serialNumber, sender, data);
                    return;
                }

                if ((int)data <= settings.maxValues[sender])  // only as max value
                    manager.SensorDataDispatch(settings.serialNumber, sender, data);

                return;
            }

            if (AsMaxValue(sender)) // as min and max value
            {
                if ((int)data >= settings.minValues[sender] && (int)data <= settings.maxValues[sender])
                    manager.SensorDataDispatch(settings.serialNumber, sender, data);
            }
            else // only as min value
            {
                if ((int)data >= settings.minValues[sender])
                    manager.SensorDataDispatch(settings.serialNumber, sender, data);
            }

            //Development.Log(String.Concat("Phidget [", serial, "] [", sender, "] ignored! ", "Data [", data, "] lower than setting min value [", _settings.minValues[sender], "]."));
        }

        /// <summary>
        /// Returns if Phidget settings has a min value for parameter port
        /// </summary>
        /// <param name="sender">Port that dispatched the data</param>
        /// <returns></returns>
        private bool AsMinValue(int sender)
        {
            return !(settings.minValues.IsNullOrEmpty() || (!settings.minValues.IsNullOrEmpty() && sender >= settings.minValues.Length));
        }

        /// <summary>
        /// Returns if Phidget settings has a max value for parameter port
        /// </summary>
        /// <param name="sender">Port that dispatched the data</param>
        /// <returns></returns>
        private bool AsMaxValue(int sender)
        {
            return !(settings.maxValues.IsNullOrEmpty() || (!settings.maxValues.IsNullOrEmpty() && sender >= settings.maxValues.Length));
        }
    }
}
#endif
