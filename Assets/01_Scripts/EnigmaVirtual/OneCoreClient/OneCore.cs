using EnigmaVirtual.Notifications;
using EnigmaVirtual.OneCoreClient.Model;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Events;

namespace EnigmaVirtual.OneCoreClient
{
    [System.Serializable]
    public class OnDataReceived : UnityEvent<string> { }
    [System.Serializable]
    public class OnSignInAuthenticationError : UnityEvent<string[]> { }
    [System.Serializable]
    public class OnLoggedIn : UnityEvent<SignInResponse.SignInResponseData> { }

    internal static class Command
    {
        public const string EVENT = "EVENT",
            SIGN_IN = "SIGN_IN",
            BROADCAST = "BROADCAST",
            PUSH_MESSAGE = "PUSH_MESSAGE",
            PUSH_COMMAND = "PUSH_COMMAND",
            PING = "PING",
            PUSH_EVENT = "PUSH_EVENT";
    }

    internal static class Response
    {
        public const string SIGN_IN_RESPONSE = "SIGN_IN_RESPONSE";
        public const string PUSH_EVENT_RESPONSE = "PUSH_EVENT_RESPONSE";
    }

    [RequireComponent(typeof(SocketConnection))]
    public class OneCore : MonoBehaviour
    {
        public enum OneCoreMessageType { NONE = -1, RAW = 0, JSON };

        public const string OK = "OK",
            ERROR = "ERROR",
            TIMEOUT = "timeout";

        private Regex commandsPat = new Regex("EVENT|SIGN_IN|BROADCAST|PUSH_MESSAGE|PUSH_COMMAND|PING");
        private Regex processPat = new Regex("^%(.+)%(.+)?%([0-1])?%(.+)$");

        [SerializeField]
        private bool autoConnect = false;
        [SerializeField]
        private bool autoSignIn = false;

        private Dictionary<int, Action<object[]>> _reqCallback = new Dictionary<int, Action<object[]>>();

        public bool isConnected { get; private set; } = false;
        private bool initialized;

        /// <summary>Keeps <see cref="HandleTimeout"/> to one report per connection.</summary>
        private bool _timeoutReported;

        // SocketConnection, not the old OneCoreSocketConnection: that class was a copy of this
        // one's socket layer and had drifted from it, keeping four of its five defects while
        // losing the fifth. It is gone - OneCoreClient.Settings already derives from
        // SocketsSettings, so a single connection class always served both and the duplicate
        // bought nothing but a second place for the same bugs to live.
        [SerializeField]
        private SocketConnection socket;
        private Settings settings;

        [Header("Events")]
        public OnDataReceived onDataReceivedEvent = new OnDataReceived();
        public UnityEvent onConnectedEvent = new UnityEvent();
        public UnityEvent onDisconnectedEvent = new UnityEvent();
        public OnLoggedIn onLoggedInEvent = new OnLoggedIn();
        public UnityEvent onLoggedOutEvent = new UnityEvent();
        public OnSignInAuthenticationError onLoginAuthError = new OnSignInAuthenticationError();

        private Stack<OneCoreCommand> commandStack = new Stack<OneCoreCommand>();
        private float stackPopTimer = 0f;
        private float popInterval = 0.125f;
        private int stackSize = 100;

        void Awake()
        {
            if (socket == null)
                socket = this.GetComponent<SocketConnection>();
        }

        private void Reset()
        {
            socket = this.GetComponent<SocketConnection>();
        }

        private void Start()
        {
            if (initialized && Core.settings.OneCore.doLogs)
                Development.LogWarning("Already initialized!");

            settings = Core.settings.OneCore;
            socket.settings = settings;
            socket.connectionName = "OneCore Server";
            autoConnect = settings.autoConnect;
            autoSignIn = settings.autoSignIn;

            socket.onConnectionChangeEvent.AddListener(OnConnectionChanged);

            if (autoConnect)
                StartOneCore();

            initialized = true;
        }

        void Update()
        {
            SocketResponse();
            StackPopper();
        }

        private void OnDestroy()
        {
            if (socket != null)
            {
                socket.CloseSocket();
                socket.onConnectionChangeEvent.RemoveAllListeners();
            }
        }

        private void StackPopper()
        {
            stackPopTimer += Time.deltaTime;

            if (stackPopTimer >= popInterval)
            {
                stackPopTimer = 0f;
                if (commandStack.Count > 0)
                {
                    SendOneCoreCommand(commandStack.Pop());
                }
            }
        }

        internal void StackOneCoreCommand(OneCoreCommand value)
        {
            if (commandStack.Count < stackSize)
            {
                commandStack.Push(value);
            }
            else
            {
                Development.LogWarning("Command stack is full!");
            }
        }

        internal void StackOneCoreCommands(IEnumerable<OneCoreCommand> collection)
        {
            if (collection.IsNullOrEmpty())
                return;

            foreach (var item in collection)
            {
                if (commandStack.Count < stackSize)
                {
                    commandStack.Push(item);
                }
                else
                {
                    Development.LogWarning("Command stack is full!");
                }
            }
        }

        private bool IsSocketValid()
        {
            return socket != null && isConnected;
        }

        /// <summary>
        /// Reports a <c>timeout</c> message once per connection. <b>Does not act on it</b> - see remarks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The obvious change here would be to drop the connection and let the reconnect loop run, and
        /// that is the obvious proposal. The evidence argues against inventing any behaviour at all:
        /// <c>TIMEOUT</c>'s sibling constants <c>OK</c> and <c>ERROR</c> survive only inside commented-out
        /// code referring to <c>MossCommand</c> and <c>_webSocket.Close()</c> - a WebSocket client this one
        /// replaced - nothing in this project sends or produces the token, and a bare <c>timeout</c> does
        /// not match the protocol's own <c>%</c>-delimited message shape. Giving an unexplained server
        /// token the power to kill a connection, on a guess, is worse than not acting on it.
        /// </para>
        /// <para>
        /// What <i>was</i> a defect is that the branch swallowed the message: it reported and
        /// <c>continue</c>d, so <see cref="onDataReceivedEvent"/> never fired and a project that does know
        /// the protocol could not act on it either. It now falls through to <see cref="OnMessage"/> like
        /// any other unrecognised message. Until someone documents the Gema Core side, that is the whole
        /// of what can be said honestly.
        /// </para>
        /// <para>
        /// Once per connection, because this is a per-message path and a server repeating the token would
        /// otherwise fill the log at the rate it arrives.
        /// </para>
        /// </remarks>
        private void HandleTimeout()
        {
            if (_timeoutReported)
                return;

            _timeoutReported = true;

            Development.LogError("OneCore sent \"timeout\". This client does not act on it - nothing has "
                + "ever defined what it should mean, and it is not in the protocol's %-delimited form. It "
                + "is passed to onDataReceivedEvent like any other unrecognised message, so a project that "
                + "knows the protocol can handle it. Further occurrences on this connection are not "
                + "logged. (M61)");
        }

        /// <summary>
        /// Handles every message that arrived since the last frame.
        /// </summary>
        /// <remarks>
        /// Two silent defects died with the rewrite:
        /// <list type="number">
        /// <item><b><c>HandleTimeout</c> had never run.</b> The test was <c>response == TIMEOUT</c>
        /// against <c>"timeout"</c>, but the old <c>ReadSocket</c> returned a whole 8 KB buffer with an
        /// unwritten tail, so that comparison could not be true for any input the server sent. Framing
        /// makes it a real test - and made this visible, since the branch it reached then swallowed the
        /// message.</item>
        /// <item><b>Every message after the first, per frame, was dropped.</b> <c>ProcessResponse</c> cut
        /// the response at the first newline and discarded the remainder - and the newline it looked for
        /// was an artefact of the old writer emitting both a newline and a delimiter, so the two callers
        /// of one method framed it two different ways.</item>
        /// </list>
        /// </remarks>
        void SocketResponse()
        {
            if (socket == null)
                return;

            // Drains regardless of isConnected. Gating on it stranded messages that had already arrived:
            // the queue is drained by nobody while disconnected, and SocketConnection now discards them on
            // teardown - so a response that did arrive was thrown away unread.
            string message;

            while (socket.TryGetMessage(out message))
            {
                // Case-insensitive: OK and ERROR are upper case and TIMEOUT's value is lower, so the
                // constant's casing is not something to rely on.
                if (string.Equals(message, TIMEOUT, StringComparison.OrdinalIgnoreCase))
                    HandleTimeout();

                Development.LogIf(Core.settings.OneCore.doLogs, $"Received: {message}");

                // No `continue` above: the timeout message is reported AND delivered. Swallowing it was
                // the actual defect - see HandleTimeout.
                OnMessage(message);
            }
        }

        private void OnConnectionChanged(bool state)
        {
            isConnected = state;

            if (state)
                _timeoutReported = false;

            if (state)
            {
                // No notification here. SocketConnection announces the connection under
                // connectionName, so this was the second toast saying the same thing - and after the two
                // socket classes were merged it was the third. The event is the API; the toast was not.
                onConnectedEvent.Invoke();
                if (autoSignIn)
                    SendOneCoreMessage(Command.SIGN_IN, OneCoreMessageType.JSON, Core.settings.OneCore.Authentication);
            }
            else
            {
                // Likewise. This path double-notified even before the merge, and SocketConnection's
                // version carries the reason, which this one never did.
                onDisconnectedEvent.Invoke();
            }
        }

        protected void OnSocketConnectionFailed() { }

        public void StartOneCore()
        {
            if (!socket.ServerReady)
                socket.SetupSocket();
        }

        public void StopOneCore()
        {
            if (isConnected)
                StartCoroutine(StopOneCoreIE());
        }

        private IEnumerator StopOneCoreIE()
        {
            Development.LogIf(Core.settings.OneCore.doLogs, "StopOneCoreIE");
            if (isConnected)
            {
                if (!Core.settings.OneCore.DisconnectCommands.IsNullOrEmpty())
                    foreach (OneCoreCommand occ in Core.settings.OneCore.DisconnectCommands)
                        SendOneCoreCommand(occ);

                socket.CloseSocket();

                yield return null;
            }
        }

        private void SendOneCoreRawMessage(string message)
        {
            Development.LogIf(Core.settings.OneCore.doLogs, message);
            if (isConnected)
                socket.WriteSocket(message);
        }

        internal void SendOneCoreMessage(string command, OneCoreMessageType type, object data, string destiny = "")
        {
            Development.CheckPoint(Core.settings.OneCore.doLogs);
            SendOneCoreRawMessage(string.Concat("%", command, "%", destiny, "%", ((int)type).ToString(), "%", type == OneCoreMessageType.JSON ? JsonConvert.SerializeObject(data) : data.ToString()));
        }

        /// <summary>
        /// Send analytics object to OneCore
        /// </summary>
        /// <param name="analytics"></param>
        internal void SendAnalytics(AnalyticsPayload analytics)
        {
            Development.CheckPoint(Core.settings.OneCore.doLogs);
            //SendOneCoreRawMessage(string.Concat("%", command, "%", destiny, "%", ((int)type).ToString(), "%", JsonConvert.SerializeObject(analytics.data)));
        }

        internal void SendOneCoreCommand(OneCoreCommand command)
        {
            Development.CheckPoint(Core.settings.OneCore.doLogs);
            if (!isConnected)
                return;

            switch (command.type)
            {
                case "broadcast":
                    Development.LogIf(Core.settings.OneCore.doLogs, string.Concat("Send BroadCast: ", "%", Command.BROADCAST, "%", command.tag, "%", (command.format).ToString(), "%", (OneCoreMessageType)command.format == OneCoreMessageType.JSON ? JsonConvert.SerializeObject(command.message) : command.message.ToString()));
                    SendOneCoreRawMessage(string.Concat("%" + Command.BROADCAST + "%", command.tag, "%", (command.format).ToString(), "%", (OneCoreMessageType)command.format == OneCoreMessageType.JSON ? JsonConvert.SerializeObject(command.message) : command.message.ToString()));
                    break;

                case "command":
                    if (command.command_data == null)
                    {
                        Development.LogError("OneCore message ignored! REASON: command_data null");
                        break;
                    }
                    Development.LogIf(Core.settings.OneCore.doLogs, string.Concat("%", Command.PUSH_COMMAND, "%%1%", JsonConvert.SerializeObject(command.command_data)));
                    SendOneCoreRawMessage(string.Concat("%", Command.PUSH_COMMAND, "%%1%", JsonConvert.SerializeObject(command.command_data)));
                    break;
                case "event":
                    if (command.event_data == null)
                    {
                        Development.LogError("OneCore event ignored! REASON: event_data null");
                        break;
                    }
                    // event_data, not command_data. This logged the wrong field while the line below
                    // sent the right one, so an operator debugging a failed event read a usually-null
                    // value and never saw what actually went out.
                    Development.LogIf(Core.settings.OneCore.doLogs, string.Concat("%", Command.EVENT, "%%1%", JsonConvert.SerializeObject(command.event_data)));
                    SendOneCoreRawMessage(string.Concat("%", Command.EVENT, "%%1%", JsonConvert.SerializeObject(command.event_data)));
                    break;
                case "push_event":
                    // Example: event_data = JsonConvert.DeserializeObject<ExpandoObject>("{\"data\":{\"message\": \"Erro na maquina de rolhas " + args[2] + "\"}, \"event\": 3}");
                    if (command.event_data == null)
                    {
                        Development.LogError("OneCore push event ignored! REASON: event_data null");
                        break;
                    }
                    // event_data, not command_data - same mislabel as the EVENT case above.
                    Development.LogIf(Core.settings.OneCore.doLogs, string.Concat("%", Command.PUSH_EVENT, "%%1%", JsonConvert.SerializeObject(command.event_data)));
                    SendOneCoreRawMessage(string.Concat("%", Command.PUSH_EVENT, "%%1%", JsonConvert.SerializeObject(command.event_data)));
                    break;
                default:
                    break;
            }
        }

        private void OnMessage(string msg)
        {
            Development.CheckPoint(Core.settings.OneCore.doLogs);

            var match = processPat.Match(msg);

            if (match.Success)
            {
                string[] matches = processPat.Split(msg);
                if (matches.Length < 3)
                    return;

                string command = matches[1];
                OneCoreMessageType type = !string.IsNullOrEmpty(matches[2]) ? (OneCoreMessageType)int.Parse(matches[2]) : OneCoreMessageType.NONE;
                string message = matches[3];
                string destiny = matches[4];

                Development.LogIf(Core.settings.OneCore.doLogs, string.Concat("Process message action: " + command + ", destiny: ", destiny, "type : ", type.ToString(), " message:" + message));

                switch (command)
                {
                    case Response.SIGN_IN_RESPONSE:
                        //% SIGN_IN_RESPONSE %% 1 %{ "errors":[],"data":{ "permissions":[]} }

                        SignInResponse ocsr = JsonConvert.DeserializeObject<SignInResponse>(message);
                        if (ocsr.success)
                        {
                            Development.LogIf(Core.settings.OneCore.doLogs, "Login success!");
                            onLoggedInEvent?.Invoke(ocsr.data);

                            StartCoroutine(SendStartCommands());
                        }
                        else
                        {
                            if (Core.settings.OneCore.doLogs)
                                Development.LogWarning("Login failed!");

                            onLoginAuthError?.Invoke(ocsr.errors);
                        }
                        break;
                    // KEPT: a record of the OneCore protocol surface this client does not implement.
                // The live switch handles a subset; this is the only note of what else exists.
                    //case Response.PUSH_EVENT_RESPONSE:
                    //    break;
                    //case MossCommand.VALIDATE_VERSION:
                    //    switch (message)
                    //    {
                    //        case OK:
                    //            break;
                    //        case ERROR:
                    //            _validVersion = false;
                    //            _webSocket.Close();
                    //            versionValidationError.SafeInvoke();
                    //            break;
                    //    }
                    //    break;

                    default:
                        if (Core.settings.OneCore.doLogs)
                            Development.LogWarning(command + " not implemented, please update OneCore client!");

                        ResponseBase dData = JsonConvert.DeserializeObject<SignInResponse>(message);


                        if (!dData.success)
                            Development.LogError(string.Concat("Errors: ", String.Join("; ", dData.errors)));

                        break;
                }
            }
            else
            {
                if (Core.settings.OneCore.doLogs)
                    Development.LogWarning("Message doesn't match regular expression!");

                onDataReceivedEvent?.Invoke(msg);
            }
        }

        private void OnApplicationQuit()
        {
            StopOneCore();
        }

        private IEnumerator SendStartCommands()
        {
            Development.LogIf(Core.settings.OneCore.doLogs, "SendStartCommands");

            if (!Core.settings.OneCore.ConnectCommands.IsNullOrEmpty())
            {
                foreach (OneCoreCommand occ in Core.settings.OneCore.ConnectCommands)
                {
                    SendOneCoreCommand(occ);
                    yield return new WaitForEndOfFrame();
                }
            }
        }
    }
}