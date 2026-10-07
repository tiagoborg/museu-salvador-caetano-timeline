using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UnityEngine;

namespace EnigmaVirtual
{
    [RequireComponent(typeof(OSCHandler))]
    public class OSCController : MonoBehaviour
    {
        [SerializeField]
        private string clientName = "RESOLUME";

        private int historyCapacity = 12;
        private Queue<string> _clientMessageHistory;

        private string[] closeMessages;
        private int oscClientCount = 0;

        private void Start()
        {
            if (_clientMessageHistory == null)
                _clientMessageHistory = new Queue<string>(historyCapacity);
            //CreateClient();
        }

        void OnApplicationQuit()
        {
            if (!closeMessages.IsNullOrEmpty())
            {
                Development.LogIf(Core.settings.OSC.doLogs, "Processing OSC closeMessages");
                StartCoroutine(ProcessMessages_IE(Core.settings.OSC.closeMessages, Core.settings.OSC.useBurst));
            }

            OSCHandler.Instance.CloseClient(false);
        }

        public void CreateClient(IPAddress destination, int port, string clientId = null, string[] startMessages = null, string[] closeMessages = null)
        {
            if (_clientMessageHistory == null)
                _clientMessageHistory = new Queue<string>(historyCapacity);

            clientName = string.IsNullOrEmpty(clientId) ? clientName : clientId;
            this.closeMessages = closeMessages;

            if (OSCHandler.Instance.Clients.ContainsKey(clientName))
            {
                Development.LogWarning("OSC Client [" + clientName + "] already exists!");
            }
            else
            {
                OSCHandler.Instance.CreateClient(clientName, destination, port);
            }

            Development.LogIf(Core.settings.OSC.doLogs, $"CreateClient {clientName}, {destination}, {port}");

            if (!startMessages.IsNullOrEmpty())
            {
                Development.LogIf(Core.settings.OSC.doLogs, "Processing OSC Messages");
                StartCoroutine(ProcessMessages_IE(startMessages, Core.settings.OSC.useBurst));
            }
        }

        public void SendOsc(params string[] osc)
        {
            StartCoroutine(SendOsc_IE(osc));
        }

        private IEnumerator SendOsc_IE(params string[] oscs)
        {
            if (Core.settings.OSC.useBurst)
                CreateClient(IPAddress.Parse(Core.settings.OSC.serverIp), Core.settings.OSC.serverPort, Core.settings.OSC.clientName + (++oscClientCount).ToString(), oscs, null);
            else
                StartCoroutine(ProcessMessages_IE(oscs, Core.settings.OSC.useBurst));

            yield return null;
        }

        private void CloseClient()
        {
            OSCHandler.Instance.CloseClient(true);
        }

        private void ProcessMessages(string[] messages)
        {
            for (int i = 0; i < messages.Length; i++)
                SendOscMessage(messages[i]);
        }

        private IEnumerator ProcessMessages_IE(string[] messages, bool closeClient)
        {
            for (int i = 0; i < messages.Length; i++)
            {
                SendOscMessage(messages[i]);
                yield return new WaitForEndOfFrame();
            }

            if (closeClient)
                CloseClient();
        }

        public void SendOscCommand(params OscCommand[] oscCommand)
        {
            string[] oscStrings = oscCommand.Select(c => c.ToString()).ToArray();
            SendOsc(oscStrings);
        }

        private void SendOscMessage(string message)
        {
            string[] data = message.Split('|');
            SendOscMessage(data[0], data.Length > 1 ? int.Parse(data[1]) : 1);
        }

        private void SendOscMessage(string message, int carrier)
        {
            Development.LogIf(Core.settings.OSC.doLogs, $"SendOscMessage [{clientName}] : [{message}], [{carrier}]");

            try
            {
                OSCHandler.Instance.SendMessageToClient(clientName, message, carrier);
                AddClientHistory(clientName, message, carrier);
            }
            catch (Exception e)
            {
                Development.LogError(e.Message);
            }
        }

        private void AddClientHistory(string clientName, string msg, int carrier)
        {
            Development.LogIf(Core.settings.OSC.doLogs, $"AddClientHistory [{clientName}] : [{msg}], [{carrier}]");

            _clientMessageHistory.Enqueue($"[{DateTime.Now.ToString("HH:mm:ss:ms")}] {clientName}:: {msg}, {carrier}");

            if (_clientMessageHistory.Count > historyCapacity)
                _clientMessageHistory.Dequeue();
        }

        public Queue<string> clientHistory
        {
            get { return _clientMessageHistory; }
        }
    }

    // OSCCommandType and OscCommand sat OUTSIDE the namespace block above, in the global namespace,
    // while the rest of this file was already in EnigmaVirtual. They were the last two types L17 had
    // left to move, and they needed moving INTO an existing block rather than the file wrapping -
    // which is why the slice that wrapped nine other files skipped this one rather than forcing it.

    public static class OSCCommandType
    {
        /// <summary>
        /// Connect to composion/layer.
        /// Carrier 0|1
        /// </summary>
        public const string Connect = "/connect";
        /// <summary>
        /// Move video timeline to position
        /// Carrier 0|NumberOfFrames
        /// </summary>
        public const string VideoPosition = "/transport/position";
        /// <summary>
        /// Sets video at timeline position 0
        /// </summary>
        public const string VideoRestart = "/transport/position|0";
        /// <summary>
        /// Pauses video on layer.
        /// </summary>
        public const string VideoPause = "/transport/position/behaviour/playdirection|1";
        /// <summary>
        /// Unpauses video on layer.
        /// </summary>
        public const string VideoUnPause = "/transport/position/behaviour/playdirection|2";
        /// <summary>
        /// Reverses video, on layer, play direction.
        /// </summary>
        public const string VideoReverse = "/transport/position/behaviour/playdirection|0";
    }

    [Serializable]
    public class OscCommand
    {
        public int column { get; }
        public int layer { get; }
        public string commandType { get; }
        public int carrier { get; }

        /// <summary>
        /// If layer is 0 or less, it will be ignored for connect command type, and the OSC path will be /composition/columns/{column}/connect
        public OscCommand(int column, int layer, string commandType, int carrier = 1)
        {
            this.column = column;
            this.layer = layer;
            this.commandType = commandType;
            this.carrier = carrier;

            if (commandType == OSCCommandType.VideoRestart || commandType == OSCCommandType.VideoReverse)
                this.carrier = 0;
            else if (commandType == OSCCommandType.VideoPause)
                this.carrier = 1;
            else if (commandType == OSCCommandType.VideoUnPause)
                this.carrier = 2;
        }

        /// <summary>
        /// Resolume-style OSC path
        /// /composition/columns/{column}/layers/{layer}{command}|{carrier}
        /// </summary>
        public string Build()
        {
            if (commandType == OSCCommandType.Connect && layer <= 0)
                return $"/composition/columns/{column}{commandType}";

            if (commandType == OSCCommandType.VideoRestart)
                return $"/composition/layers/{layer}/clips/{column}{commandType}";

            if (commandType == OSCCommandType.VideoPause || commandType == OSCCommandType.VideoUnPause || commandType == OSCCommandType.VideoReverse)
                return $"/composition/layers/{layer}/clips/{column}{commandType}";

            return $"/composition/layers/{layer}/clips/{column}{commandType}|{carrier}";
        }

        public override string ToString()
        {
            return Build();
        }
    }
}
