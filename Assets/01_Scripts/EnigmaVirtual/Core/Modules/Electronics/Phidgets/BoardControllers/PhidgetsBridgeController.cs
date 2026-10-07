/*******************************************************
 * Copyright (C) 2023 Gema Digital Agency
 * 
 * PhidgetsBridgeController can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
// This file uses the Phidget22 SDK, whose managed assembly is now excluded from every
// non-Windows player - so the file has to carry the same gate or those builds stop compiling.
// Gate spelling matches MainController's existing Phidgets call site, so one grep
// finds every place that carries it.
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
using Phidget22;
using Phidget22.Events;
using System.Collections.Generic;
using UnityEngine;

namespace EnigmaVirtual
{
    public class PhidgetsBridgeController : MonoBehaviour, IPhidgetsController
    {
        /// <summary>
        /// Returns Phidget serial number
        /// </summary>
        public int serialNumber { get { return settings.serialNumber; } }

        private PhidgetSettings settings;
        private PhidgetsManager manager;
        private List<VoltageRatioInput> voltageRatioInputs;

        /// <summary>
        /// 
        /// </summary>
        public void Init(PhidgetSettings settings, PhidgetsManager manager)
        {
            this.settings = settings;
            this.manager = manager;

            Development.Log("Initializing Phidget Bridge " + settings.serialNumber);

            voltageRatioInputs = new List<VoltageRatioInput>();
            if (!settings.digitalInputs.IsNullOrEmpty())
            {
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
                                //Open your Phidgets and wait for attachment
                                vri.Open(5000);
                                //if (i < settings.voltageRatioSensorType.Length)
                                //    vri.SensorType = GetVoltageRatioSensorType(settings.voltageRatioSensorType[i]);

                                voltageRatioInputs.Add(vri);
                            }
                            catch (PhidgetException ex)
                            {
                                //Catch Phidget Exceptions
                                Development.Log(ex.ToString());
                                Development.LogError("PhidgetException [" + serialNumber + "] [" + i + "] " + ex.ErrorCode + " (" + ex.Description + "): " + ex.Detail);
                            }
                        }
                    }
                }
            }
        }

        //private VoltageRatioSensorType GetVoltageRatioSensorType(string value)
        //{
        //    if (string.IsNullOrEmpty(value))
        //        return VoltageRatioSensorType.VoltageRatio;

        //    if (Enum.TryParse(value, true, out VoltageRatioSensorType parsed_value))
        //        return parsed_value;
        //    return VoltageRatioSensorType.VoltageRatio;
        //}

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

        private void VoltageRatioInput_VoltageRatioChange(object sender, VoltageRatioInputVoltageRatioChangeEventArgs e)
        {
            VoltageRatioInput evChannel = (VoltageRatioInput)sender;
            manager.SensorVoltageRatioDispatch(serialNumber, evChannel.Channel, e.VoltageRatio);

            if (settings.doLogs)
                Development.Log("SensorVoltageRatioDispatch [" + serialNumber + "] channel [" + evChannel.Channel + "] value [" + e.VoltageRatio + "]");
        }

        private void VoltageRatioInput_SensorChange(object sender, VoltageRatioInputSensorChangeEventArgs e)
        {
            VoltageRatioInput evChannel = (VoltageRatioInput)sender;
            manager.SensorDataDispatch(serialNumber, evChannel.Channel, e.SensorValue);

            if (settings.doLogs)
                Development.Log("SensorDataDispatch [" + serialNumber + "] channel [" + evChannel.Channel + "] value [" + e.SensorValue + "]");
        }

        /// <summary>
        /// Closes all Phidget channels communications
        /// </summary>
        public void CloseAll()
        {
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
        /// Close selected Phidget channel
        /// </summary>
        /// <param name="channel">Phidget channel to close</param>
        public void Close(int channel)
        {
            for (int i = 0; i < voltageRatioInputs.Count; i++)
            {
                if (voltageRatioInputs[i].Channel == channel)
                {
                    voltageRatioInputs[i].VoltageRatioChange -= VoltageRatioInput_VoltageRatioChange;
                    voltageRatioInputs[i].SensorChange -= VoltageRatioInput_SensorChange;
                    voltageRatioInputs[i].Attach -= VoltageRatioInput_Attach;
                    voltageRatioInputs[i].Error -= VoltageRatioInput_Error;

                    if (voltageRatioInputs[i].IsOpen)
                        voltageRatioInputs[i].Close();

                    voltageRatioInputs[i].Detach -= VoltageRatioInput_Detach;
                    return;
                }
            }
        }
    }
}
#endif
