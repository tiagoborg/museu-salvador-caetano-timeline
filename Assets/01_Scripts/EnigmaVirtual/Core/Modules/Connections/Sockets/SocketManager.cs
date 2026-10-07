using UnityEngine;
using UnityEngine.Events;

namespace EnigmaVirtual
{
    [System.Serializable]
    public class SocketDataReceived : UnityEvent<string> { }

    [RequireComponent(typeof(SocketConnection))]
    public class SocketManager : MonoBehaviour
    {
        [Header("Events")]
        public SocketDataReceived onSocketDataReceivedEvent = new SocketDataReceived();
        public UnityEvent onSocketConnectedEvent = new UnityEvent();
        public UnityEvent onSocketDisconnectedEvent = new UnityEvent();

        private SocketConnection socket;

        /// <summary>
        /// 
        /// </summary>
        void Awake()
        {
            socket = this.GetComponent<SocketConnection>();
            socket.onConnectionChangeEvent.AddListener(OnConnectionChanged);
        }

        /// <summary>
        /// 
        /// </summary>
        void Start()
        {
            socket.settings = Core.settings.Sockets;
            socket.connectionName = "Socket Server";
            if (Core.settings.Sockets.autoConnect)
                StartSocket();
        }

        /// <summary>
        /// Checks for messages on Update
        /// </summary>
        void Update()
        {
            SocketResponse();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        void OnConnectionChanged(bool state)
        {
            if (state)
                onSocketConnectedEvent.Invoke();
            else
                onSocketDisconnectedEvent.Invoke();
        }

        /// <summary>
        /// Raises <see cref="onSocketDataReceivedEvent"/> for every message that arrived since the last
        /// frame.
        /// </summary>
        /// <remarks>
        /// A loop, not a single read: several messages can arrive between frames, and the old code took
        /// one 8 KB buffer per frame, split it on '|' and iterated <c>Length - 1</c>. That last-element
        /// skip was load-bearing by accident - it discarded the buffer's unwritten tail - and it also
        /// discarded any message split across two packets rather than reassembling it. Framing now lives
        /// in <see cref="SocketConnection"/>.
        /// </remarks>
        void SocketResponse()
        {
            string message;

            while (socket.TryGetMessage(out message))
            {
                onSocketDataReceivedEvent.Invoke(message);

                Development.LogIf(Core.settings.Sockets.doLogs, "Received: " + message);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void StartSocket()
        {
            if (!socket.ServerReady)
            {
                socket.SetupSocket();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void StopSocket()
        {
            socket.CloseSocket();
        }

        public void Send(string message)
        {
            socket.WriteSocket(message);
        }

        /// <summary>
        /// 
        /// </summary>
        void OnApplicationQuit()
        {
            StopSocket();
        }
    }
}
