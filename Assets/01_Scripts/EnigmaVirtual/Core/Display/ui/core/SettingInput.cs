/*******************************************************
 * Copyright (C) 2016 Gema Digital
 * 
 * SettingInput can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmaVirtual
{
    public class SettingInput : MonoBehaviour
    {
        public SettingsGroup Group;
        public string Key;
        private InputField _input;

        private IEnumerator Start()
        {
            yield return new WaitUntil(() => Core.IsSet);
            _input = GetComponent<InputField>();
            GetData();
        }

        /// <summary>
        /// Re-reads this field's value from the live settings object into the input box.
        /// </summary>
        /// <remarks>
        /// <c>internal</c> so <see cref="ConfigsPanel"/> can call it after a save. A rejected value is
        /// kept out of the model by <c>SettingsParse</c>, but the text the operator typed stayed in the
        /// box - so the screen showed the value they wanted rather than the one in force. Calling
        /// this after a save makes a rejection visible where they are looking.
        /// </remarks>
        internal void GetData()
        {
            if (_input == null)
                return;
            switch (Group)
            {
                case SettingsGroup.None:
                    break;
                case SettingsGroup.Activity:
                    break;
                case SettingsGroup.Sockets:
                    switch (Key)
                    {
                        case "isActive":
                            _input.text = Core.settings.Sockets.isActive.ToString();
                            break;
                        case "socketServerIp":
                            _input.text = Core.settings.Sockets.socketServerIp;
                            break;
                        case "socketServerPort":
                            _input.text = Core.settings.Sockets.socketServerPort.ToString();
                            break;
                        case "keepAliveTime":
                            _input.text = Core.settings.Sockets.keepAliveTime.ToString();
                            break;
                        case "reconnectTime":
                            _input.text = Core.settings.Sockets.reconnectTime.ToString();
                            break;
                        default:
                            break;
                    }
                    break;
                case SettingsGroup.OneCore:
                    switch (Key)
                    {
                        case "isActive":
                            _input.text = Core.settings.OneCore.isActive.ToString();
                            break;
                        case "socketServerIp":
                            _input.text = Core.settings.OneCore.socketServerIp;
                            break;
                        case "socketServerPort":
                            _input.text = Core.settings.OneCore.socketServerPort.ToString();
                            break;
                        case "keepAliveTime":
                            _input.text = Core.settings.OneCore.keepAliveTime.ToString();
                            break;
                        case "reconnectTime":
                            _input.text = Core.settings.OneCore.reconnectTime.ToString();
                            break;
                        case "apiPort":
                            _input.text = Core.settings.OneCore.apiPort.ToString();
                            break;
                        case "authenticationTags":
                            _input.text = Core.settings.OneCore.Authentication.tags.IsNullOrEmpty() ? "" : 
                                string.Join(",", Core.settings.OneCore.Authentication.tags);
                            break;
                        case "authenticationKey":
                            _input.text = Core.settings.OneCore.Authentication.key;
                            break;
                        default:
                            break;
                    }
                    break;
                case SettingsGroup.AppData:
                    switch (Key)
                    {
                        case "isActive":
                            _input.text = Core.settings.AppData.isActive.ToString();
                            break;
                        case "useLocalAppData":
                            _input.text = Core.settings.AppData.useLocalAppData.ToString();
                            break;
                        case "downloadableContent":
                            _input.text = Core.settings.AppData.Cache.AssetDownload.isActive.ToString();
                            break;
                        case "downloadThreads":
                            _input.text = Core.settings.AppData.Cache.AssetDownload.downloadThreads.ToString();
                            break;
                        case "remoteHostUrl":
                            _input.text = Core.settings.AppData.remoteSettings.hostUrl.ToString();
                            break;
                        case "remoteDataJsonPath":
                            _input.text = Core.settings.AppData.remoteSettings.dataJsonPath.ToString();
                            break;
                        case "remoteAssetsPath":
                            _input.text = Core.settings.AppData.remoteSettings.assetsPath.ToString();
                            break;
                        case "localDataJsonPath":
                            _input.text = Core.settings.AppData.localSettings.dataJsonPath.ToString();
                            break;
                        case "localAssetsPath":
                            _input.text = Core.settings.AppData.localSettings.assetsPath.ToString();
                            break;
                        default:
                            break;
                    }
                    break;
                case SettingsGroup.OSC:
                    switch (Key)
                    {
                        case "isActive":
                            _input.text = Core.settings.OSC.isActive.ToString();
                            break;
                        case "serverIp":
                            _input.text = Core.settings.OSC.serverIp;
                            break;
                        case "serverPort":
                            _input.text = Core.settings.OSC.serverPort.ToString();
                            break;
                        case "startMessages":
                            _input.text = Core.settings.OSC.startMessages.IsNullOrEmpty() ? "" :
                                string.Join(",", Core.settings.OSC.startMessages);
                            break;
                        default:
                            break;
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Applies this field's value to the live settings object. <b>Does not write anything to disk.</b>
        /// </summary>
        /// <remarks>
        /// This took a <c>bool overwriteJson</c> and, when true, wrote the whole of
        /// <see cref="Core.settings"/> to disk - so <see cref="ConfigsPanel"/> calling it per field
        /// rewrote the entire file once for every input on the panel. The file write is
        /// <c>FileManager.SaveCoreSettings</c> now, called once after the loop.
        /// <para><b>Breaking change for branched projects:</b> the parameter is gone. A project with its
        /// own settings panel calling <c>field.Save(true)</c> gets a compile error, which is the intent -
        /// it needs to call <c>Core.file.SaveCoreSettings()</c> once itself rather than per field.</para>
        /// <para>The old write also produced a stray <c>Assets/StreamingAssets/settings.json</c> in the
        /// editor. <c>BuildValidator</c> would <b>not</b> have caught it: the backup-filename check scans
        /// <c>Assets/StreamingAssets/config</c> only, so that file would have reached a client site
        /// unflagged. Attributing the safeguard to that check was
        /// wrong.</para>
        /// </remarks>
        public void Save()
        {
            if (_input == null)
            {
                // Through SettingsParse.Error, so the panel's count sees it. A bare
                // Development.LogError left the log disagreeing with a clean success toast, which is the
                // state this exists to end.
                //
                // The panel skips fields that are inactive in the hierarchy before calling this, so
                // reaching here means an *active* field with no InputField - a real misconfiguration.
                // (This used to except the four dead Phidgets rows; they were deleted from the scene in
                // deleted, along with the commented-out branches that would have served them.)
                SettingsParse.Error($"SetData [{Group}/{Key}]: no InputField on this active field, so "
                    + "nothing was saved for it.");
                return;
            }
            switch (Group)
            {
                case SettingsGroup.None:
                    break;
                case SettingsGroup.Activity:
                    break;
                case SettingsGroup.Sockets:
                    SocketsSettings sockets = Core.settings.Sockets;
                    sockets.SetData(Key, _input.text);
                    break;
                case SettingsGroup.OneCore:
                    OneCoreClient.Settings ocSettings = Core.settings.OneCore;
                    ocSettings.SetData(Key, _input.text);
                    break;
                case SettingsGroup.OSC:
                    OSCSettings oSCSettings = Core.settings.OSC;
                    oSCSettings.SetData(Key, _input.text);
                    break;
                case SettingsGroup.AppData:
                    AppDataSettings appDataSettings = Core.settings.AppData;
                    appDataSettings.SetData(Key, _input.text);
                    break;
                default:
                    // Reached by None and Activity - and now by Phidgets, which no longer has a branch at
                    // all. Nothing is applied anywhere, and this used to fall through silently, so
                    // the panel reported a clean success for a field that was never saved. Routed through
                    // SettingsParse.Error so it lands in the same count the panel reads. That is
                    // also the safety net if a project re-authors a Phidgets row: it reports rather than
                    // silently doing nothing.
                    SettingsParse.Error($"SetData [{Group}/{Key}]: this settings group has no save "
                        + "handler, so the value was NOT applied anywhere.");
                    break;
            }

            Development.CheckPoint();
        }
    }
}
