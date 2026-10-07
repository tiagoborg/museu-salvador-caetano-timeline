/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 *
 * SocketConnection can not be copied and/or distributed without
 * the express permission of Gema Digital
 *******************************************************/
using EnigmaVirtual.Notifications;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

namespace EnigmaVirtual
{
    [System.Serializable]
    public class SocketConnectionChanged : UnityEvent<bool> { }

    /// <summary>
    /// TCP client for the Gema Core socket protocol: messages are delimited by
    /// <see cref="MessageDelimiter"/>. One worker thread owns the socket; the main thread reads
    /// completed messages with <see cref="TryGetMessage"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This replaced a design that could not be repaired in place.</b> The old version connected on a
    /// throwaway thread, invoked a <c>UnityEvent</c> from it, polled <c>NetworkStream.DataAvailable</c>
    /// every frame, and shared three unsynchronised <c>bool</c>s between the two. Each of those is a
    /// separate defect and they interlock, so they are fixed together:
    /// </para>
    /// <list type="bullet">
    /// <item><b>No Unity API off the main thread.</b> The worker only touches the socket, a
    /// <c>ConcurrentQueue</c> and <c>volatile</c> fields. Every event, log and notification is raised from
    /// <see cref="Update"/>. The old code got this right for one notification and wrong for the event
    /// immediately below it.</item>
    /// <item><b>One connection flag, <c>volatile</c>.</b> There were three (<c>serverReady</c>,
    /// <c>isConnected</c>, <c>threadFinished</c>), unsynchronised, so the main thread could see
    /// "thread done" before it could see "connected" and report a failure for a connection that had
    /// succeeded. The two serialized fields are now display mirrors written in <see cref="Update"/> and
    /// are read by nothing.</item>
    /// <item><b>A connect timeout.</b> <c>new TcpClient(host, port)</c> blocks with no timeout - about
    /// 21&#160;s to an unreachable host on Windows - while the old reconnect fired every
    /// <c>reconnectTime</c> (10&#160;s shipped). So attempts overlapped, each overwriting the socket field
    /// without closing it: two or three live threads and a leaked handle every 10&#160;s, indefinitely.</item>
    /// <item><b>Blocking reads instead of per-frame polling.</b> The old read allocated a
    /// <c>SendBufferSize</c>-sized array (8&#160;KB) <i>every frame</i> from <c>Update</c>, roughly
    /// 480&#160;KB/s of garbage while connected, and threw the return value of <c>Read</c> away so it
    /// decoded the whole buffer including the unwritten tail.</item>
    /// </list>
    /// <para>
    /// <b>Framing is the reason the callers had to change.</b> TCP is a byte stream, so a single
    /// <c>Read</c> is not a message: it can hold several or half of one. The old code returned the raw
    /// buffer and left the caller to guess - and the two callers guessed differently, one splitting on
    /// <c>'|'</c> and the other on <c>'\n'</c> while taking only the first result. Framing belongs here,
    /// once, which is why <c>ReadSocket</c> was replaced rather than repaired.
    /// </para>
    /// </remarks>
    public class SocketConnection : MonoBehaviour
    {
        /// <summary>
        /// Message delimiter of the Gema Core protocol, appended by <see cref="WriteSocket"/> and used to
        /// frame inbound bytes.
        /// </summary>
        public const char MessageDelimiter = '|';

        /// <summary>Milliseconds. Was <c>SendTimeout = 3</c>, i.e. 3&#160;ms, which fails any send under load.</summary>
        private const int SendTimeoutMs = 3000;

        /// <summary>
        /// Milliseconds to wait for the TCP handshake. Bounded so a dead host cannot hold a worker thread
        /// for the OS default while the reconnect loop starts another.
        /// </summary>
        private const int ConnectTimeoutMs = 5000;

        private const int ReceiveBufferBytes = 8192;

        /// <summary>
        /// Cap on undrained messages. A consumer that stops calling <see cref="TryGetMessage"/> must not
        /// turn a chatty server into unbounded memory growth on an unattended machine.
        /// </summary>
        private const int MaxQueuedMessages = 1000;

        public SocketConnectionChanged onConnectionChangeEvent = new SocketConnectionChanged();

        [HideInInspector]
        public SocketsSettings settings;

        /// <summary>
        /// What this connection is called in notifications and logs. Set by the owner before
        /// <see cref="SetupSocket"/>.
        /// </summary>
        /// <remarks>
        /// One class now serves two different servers, so it has to say which one it is talking about.
        /// Without this, unifying the two connection classes made the OneCore path announce "Trying to
        /// connect to Socket Server" - the deleted <c>OneCoreSocketConnection</c> had said "OneCore
        /// Server", and on a machine running both, two connections were reporting under one name.
        /// </remarks>
        [HideInInspector]
        public string connectionName = "Socket Server";

        [Header("DEBUG - display only, written from Update")]
        [SerializeField]
        private bool serverReady = false;
        [SerializeField]
        private bool isConnected = false;

        // volatile: written by the worker thread, read by the main thread. Not [SerializeField] - Unity
        // would show them, and a field that is both serialized and volatile invites someone to "fix" one
        // of the two attributes.
        private volatile bool _connected;
        private volatile bool _stopping;
        private volatile string _lastError;

        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _worker;
        private Coroutine _lifecycle;

        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();

        /// <summary>
        /// Length of <see cref="_inbox"/>, maintained with <see cref="Interlocked"/> because
        /// <b><c>ConcurrentQueue&lt;T&gt;.Count</c> does not exist in Unity's .NET profile</b> - it compiles
        /// against .NET Framework everywhere else and fails here with CS1061.
        /// </summary>
        /// <remarks>
        /// The alternatives were worse: <c>ToArray().Length</c> copies the whole queue, and LINQ's
        /// <c>Count()</c> enumerates it - both per received message, on the thread doing the reading.
        /// This may lag the true length by a few items while the two threads race, which is fine: it only
        /// feeds a soft cap, and being one or two out never decides anything.
        /// </remarks>
        private int _queued;

        /// <summary>Guards writes and teardown against each other; reads need no lock, they are the worker's.</summary>
        private readonly object _ioLock = new object();

        /// <summary>Set by the worker on the first dropped message of a connection; drained by <see cref="Update"/>.</summary>
        private volatile bool _queueOverflowed;

        // main thread only. _reportedState starts at 0, not -1: at -1 the very first Update saw a
        // transition that had not happened, reported "no error reported" as a kiosk-visible error before
        // any connect was attempted, raised onConnectionChangeEvent(false) to child-project code at boot,
        // and - because the slot was then spent - never reported any of the real connect failures that
        // followed. It was a regression against the old code, which reported
        // every attempt.
        private int _reportedState = 0;
        private bool _queueOverflowReported;

        /// <summary>
        /// Set by <see cref="OnApplicationQuit"/>, so teardown does not announce a disconnection nobody
        /// can act on.
        /// </summary>
        /// <remarks>
        /// Stopping play in the editor logged
        /// <c>Failed connecting to OneCore Server: 127.0.0.1:6501</c> as an <b>error</b>, every time:
        /// <c>OnApplicationQuit</c> closed the socket, <see cref="CloseSocket"/> raised its transition, and
        /// <c>OneCore</c>'s handler reports any disconnection as a failed connection. Nothing subscribing to
        /// that event can usefully run during quit, and a false error on every stop is how a log stops
        /// being read at all. This path is clear: the *second* CloseSocket
        /// fires nothing; the defect was that the first one fires anything.
        /// </remarks>
        private bool _quitting;

        /// <summary>
        /// True while a socket is connected. <see cref="IsConnected"/> is a synonym.
        /// </summary>
        /// <remarks>
        /// These used to differ - <c>serverReady</c> meant "streams constructed" and <c>isConnected</c>
        /// meant "the last read or write did not throw" - and keeping two racing views of one fact is what
        /// made the old state machine unanalysable. There is one connection now.
        /// </remarks>
        public bool ServerReady { get { return _connected; } }

        /// <inheritdoc cref="ServerReady"/>
        public bool IsConnected { get { return _connected; } }

        /// <summary>Starts connecting, and keeps reconnecting until <see cref="CloseSocket"/> is called.</summary>
        /// <remarks>
        /// Idempotent. The old entry point started a coroutine per call and its failure path called itself
        /// recursively, so a server that stayed down produced unbounded coroutines.
        /// </remarks>
        public void SetupSocket()
        {
            if (!Core.IsSet)
            {
                Development.LogError("SetupSocket ignored: Core is not set, so there are no settings to "
                    + "connect with. (C6)");
                return;
            }

            if (settings == null)
            {
                Development.LogError("SetupSocket ignored: settings were never assigned. The owning "
                    + "manager sets this in Start. (C6)");
                return;
            }

            _stopping = false;

            if (_lifecycle == null)
            {
                // Logged once, unconditionally: when a site cannot connect, the first question is which
                // host and which intervals were actually in force - and an operator's settings.json, or a
                // PlayerPrefs snapshot older than it, is not visible from a log that never says.
                Development.Log($"{connectionName}: connecting to {settings.socketServerIp}:"
                    + $"{settings.socketServerPort}, keep-alive {settings.keepAliveTime}s, reconnect "
                    + $"{settings.reconnectTime}s, connect timeout {ConnectTimeoutMs}ms.");

                _lifecycle = StartCoroutine(Lifecycle());
            }
        }

        /// <summary>
        /// Closes the socket and stops reconnecting. Safe to call repeatedly, and safe when nothing was
        /// ever connected.
        /// </summary>
        /// <remarks>
        /// The old version opened with <c>if (!serverReady) return;</c>, which made it a no-op on exactly
        /// the path that needed it - <c>CheckConnection</c> called it precisely when the connection had
        /// failed - so a failed attempt's socket was never closed. That guard existed because the body
        /// would NRE on a half-built connection; each resource is now closed on its own.
        /// </remarks>
        public void CloseSocket()
        {
            _stopping = true;

            bool wasReported = _reportedState != 0;

            if (_lifecycle != null)
            {
                StopCoroutine(_lifecycle);
                _lifecycle = null;
            }

            Teardown();

            _reportedState = 0;
            serverReady = false;
            isConnected = false;

            // Report synchronously, because a caller closing the socket in OnDestroy would otherwise never
            // see the transition - Update does not run again. But not while quitting: see _quitting.
            if (!_quitting && wasReported)
                onConnectionChangeEvent?.Invoke(false);
        }

        /// <summary>Sends a message, appending the protocol delimiter.</summary>
        public void WriteSocket(string message)
        {
            if (!_connected)
                return;

            lock (_ioLock)
            {
                if (_stream == null)
                    return;

                try
                {
                    // The pipe is the Gema Core delimiter, and the CRLF is kept because the old code sent
                    // one: it wrote through StreamWriter.WriteLine, whose NewLine defaults to
                    // Environment.NewLine, so every message that has ever worked against a Gema Core
                    // server arrived as "message|\r\n". Dropping the newline here would have been an
                    // untestable change to the wire format of a server this repo cannot see, so the bytes
                    // stay identical and only the framing of *inbound* data changed. Written literally
                    // rather than as Environment.NewLine so a non-Windows build sends the same thing.
                    byte[] payload = Encoding.UTF8.GetBytes(message + MessageDelimiter + "\r\n");
                    _stream.Write(payload, 0, payload.Length);
                    _stream.Flush();
                }
                catch (Exception e)
                {
                    // Losing the socket mid-write is a disconnect, not a send bug. Record it and let the
                    // lifecycle coroutine reconnect; logging happens on the main thread, which this is.
                    _lastError = e.Message;
                    _connected = false;
                }
            }
        }

        /// <summary>Sends a message that the Gema Core server will not broadcast.</summary>
        public void SendServerMessage(string message)
        {
            WriteSocket(settings.broadcastPrefix + message);
        }

        /// <summary>
        /// Takes the next complete message, or returns false when none is waiting. Call it in a loop -
        /// several messages can arrive between frames.
        /// </summary>
        /// <remarks>
        /// <b>Replaces <c>ReadSocket()</c>, deliberately as a compile error.</b> That method returned a
        /// raw 8&#160;KB buffer per frame and left framing to the caller, which is how one caller came to
        /// drop every message after the first and the other came to compare a padded buffer against
        /// <c>"timeout"</c> - a test that could never be true. A silent change of meaning would have left
        /// both bugs in place in every branched project; a missing method does not.
        /// </remarks>
        public bool TryGetMessage(out string message)
        {
            if (!_inbox.TryDequeue(out message))
                return false;

            Interlocked.Decrement(ref _queued);
            return true;
        }

        private void Update()
        {
            // Update runs from the moment the component exists, which is before the owning manager
            // assigns settings in its Start - and every reporting path below dereferences settings.
            if (settings == null)
                return;

            ReportQueueOverflow();

            // Display mirrors. Nothing reads these; they exist so the inspector still shows the state the
            // old serialized fields showed.
            serverReady = _connected;
            isConnected = _connected;

            int state = _connected ? 1 : 0;
            if (state == _reportedState)
                return;

            _reportedState = state;

            if (state == 1)
                OnConnectionSuccess();
            else
                OnConnectionError(_lastError);

            onConnectionChangeEvent?.Invoke(_connected);
        }

        /// <summary>
        /// Connect, then keep alive, then reconnect - one coroutine for the whole lifetime.
        /// </summary>
        /// <remarks>
        /// Replaces <c>InvokeRepeating("CheckConnection", ...)</c> plus a <c>Timer</c> component plus a
        /// self-calling coroutine. The string-based Invoke was refactor-hostile and strippable, and
        /// the three mechanisms between them decided when to reconnect by racing each other.
        /// </remarks>
        private IEnumerator Lifecycle()
        {
            while (!_stopping)
            {
                if (!_connected)
                {
                    // Close whatever the previous attempt or the previous live connection left behind
                    // BEFORE starting another worker. This is the fix for a compounding leak:
                    // WriteSocket can mark the connection dead without closing the
                    // stream - a half-open socket, which is what a pulled cable or a powered-off host
                    // produces, since no FIN or RST ever arrives - and a worker blocked in Read is only
                    // released by closing the stream under it. Without this, JoinWorker froze the main
                    // thread for its full 6 s timeout, gave up, and left a live thread holding a socket;
                    // when the OS eventually killed that read, the abandoned worker reported the by-then
                    // *healthy* connection as dead and the cycle repeated, one leaked thread per blip.
                    Teardown();

                    if (settings.showNotifications)
                        Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Info,
                            $"Trying to connect to {connectionName}",
                            settings.socketServerIp + ":" + settings.socketServerPort);

                    StartWorker();

                    while (_worker != null && _worker.IsAlive && !_connected && !_stopping)
                        yield return null;

                    if (!_connected && !_stopping)
                    {
                        // Every attempt, not just the first. Update only reports on a state *transition*,
                        // so a kiosk whose server has moved would otherwise log one line at boot and then
                        // stay mute for months - and two minutes of that looks exactly like six months
                        // of it. Mathf.Max because reconnectTime has no minimum in SettingsParse and 0
                        // would spin this loop at frame rate.
                        ReportAttemptFailed();
                        yield return new WaitForSeconds(Mathf.Max(1, settings.reconnectTime));
                    }

                    continue;
                }

                // keepAliveTime is clamped to >= 1 by SettingsParse; Max is belt and braces for a
                // PlayerPrefs snapshot written before that clamp existed.
                yield return new WaitForSeconds(Mathf.Max(1, settings.keepAliveTime));

                if (_connected && !_stopping)
                    SendServerMessage(settings.keepAliveMsg);
            }

            _lifecycle = null;
        }

        private void StartWorker()
        {
            JoinWorker();

            _lastError = null;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "SocketConnection" };
            _worker.Start();
        }

        /// <summary>
        /// Owns the socket for its lifetime: connect, then read until the connection ends. Touches no
        /// Unity API - see the class remarks.
        /// </summary>
        private void WorkerLoop()
        {
            TcpClient client = null;

            try
            {
                client = new TcpClient { SendTimeout = SendTimeoutMs };

                // BeginConnect rather than ConnectAsync: it is available on every Mono version this
                // project might build against, and the Rfc2898DeriveBytes round is the reminder
                // that "modern overload" and "present at runtime" are different questions.
                IAsyncResult pending = client.BeginConnect(settings.socketServerIp,
                    settings.socketServerPort, null, null);

                if (!pending.AsyncWaitHandle.WaitOne(ConnectTimeoutMs))
                {
                    _lastError = $"connect to {settings.socketServerIp}:{settings.socketServerPort} timed "
                        + $"out after {ConnectTimeoutMs} ms";

                    // Wrapped: an exception from Close here would be caught below and would overwrite
                    // _lastError, replacing the timeout - the one useful diagnostic - with a close error.
                    try { client.Close(); } catch (Exception) { }

                    return;
                }

                client.EndConnect(pending);

                NetworkStream stream = client.GetStream();

                lock (_ioLock)
                {
                    if (_stopping)
                    {
                        client.Close();
                        return;
                    }

                    _client = client;
                    _stream = stream;
                }

                _connected = true;

                ReceiveUntilClosed(stream);
            }
            catch (Exception e)
            {
                _lastError = e.Message;
            }
            finally
            {
                lock (_ioLock)
                {
                    // Inside the guard, with the fields. An abandoned worker must not clear _connected
                    // for a connection it no longer owns: when the OS finally released a stale blocked
                    // read, this line was reporting the *current, healthy* socket as dead.
                    if (ReferenceEquals(_client, client))
                    {
                        _connected = false;
                        _stream = null;
                        _client = null;
                    }
                }

                try { client?.Close(); } catch (Exception) { /* already gone; nothing to salvage */ }
            }
        }

        /// <summary>
        /// Blocking read loop. Frames on <see cref="MessageDelimiter"/> and enqueues complete messages.
        /// </summary>
        private void ReceiveUntilClosed(NetworkStream stream)
        {
            byte[] buffer = new byte[ReceiveBufferBytes];

            // One stateful decoder for the whole connection: a UTF-8 character can straddle two reads,
            // and Encoding.UTF8.GetString per read would corrupt it.
            Decoder decoder = Encoding.UTF8.GetDecoder();
            char[] chars = new char[ReceiveBufferBytes];
            StringBuilder partial = new StringBuilder();

            while (!_stopping)
            {
                int read = stream.Read(buffer, 0, buffer.Length);

                // 0 from a blocking Read is the peer closing cleanly, not an idle socket.
                if (read <= 0)
                {
                    _lastError = "the server closed the connection";
                    return;
                }

                int decoded = decoder.GetChars(buffer, 0, read, chars, 0);

                for (int i = 0; i < decoded; i++)
                {
                    char c = chars[i];

                    if (c == MessageDelimiter)
                    {
                        Enqueue(partial.ToString());
                        partial.Length = 0;
                    }
                    else
                    {
                        partial.Append(c);
                    }
                }

                // A partial message stays in the builder for the next read. The old code dropped it,
                // which is why a message split across packets was lost rather than reassembled.
            }
        }

        private void Enqueue(string message)
        {
            // The delimiter is the frame; a server that also sends newlines should not leak them into
            // the message, and the old callers each stripped them differently.
            message = message.Trim('\r', '\n');

            if (message.Length == 0)
                return;

            if (Volatile.Read(ref _queued) >= MaxQueuedMessages)
            {
                string dropped;

                if (_inbox.TryDequeue(out dropped))
                    Interlocked.Decrement(ref _queued);

                // A flag Update drains, not _lastError: that is only read on a disconnect transition, so
                // dropping messages while perfectly connected was silent, and the message then resurfaced
                // later pretending to be the reason the connection died.
                _queueOverflowed = true;
            }

            _inbox.Enqueue(message);
            Interlocked.Increment(ref _queued);
        }

        private void Teardown()
        {
            lock (_ioLock)
            {
                // Closing the stream is what unblocks the worker's Read; each in its own try, because a
                // half-built connection must still release the parts that did get built.
                try { _stream?.Close(); } catch (Exception) { }
                try { _client?.Close(); } catch (Exception) { }

                _stream = null;
                _client = null;
            }

            _connected = false;
            JoinWorker();

            // Messages queued just before the drop are stale by the time anyone reconnects, and OneCore
            // stops draining while disconnected - so without this they replay minutes later as if fresh,
            // which for a sign-in response or a door command is worse than losing them.
            int stale = 0;
            string discarded;
            while (_inbox.TryDequeue(out discarded))
                stale++;

            Interlocked.Exchange(ref _queued, 0);

            if (stale > 0 && settings != null && settings.doLogs)
                Development.Log($"socket closed with {stale} undelivered message(s); discarded rather "
                    + "than replayed after reconnect. (C6)");
        }

        private void JoinWorker()
        {
            if (_worker == null)
                return;

            // Bounded: the worker is a background thread, so a stuck one cannot keep the process alive,
            // and blocking the main thread on it for longer than this would be the worse failure.
            if (_worker.IsAlive && !_worker.Join(ConnectTimeoutMs + 1000))
                Development.LogError("the socket worker thread did not exit in time; abandoning it. The "
                    + "socket has been closed, so it should end on its next read. (C6)");

            _worker = null;
        }

        /// <summary>Logs the reason a single connect attempt failed, on the main thread.</summary>
        private void ReportAttemptFailed()
        {
            string reason = string.IsNullOrEmpty(_lastError) ? "no error reported" : _lastError.Replace("\n", " ");

            Development.LogError($"{connectionName} ({settings.socketServerIp}:"
                + $"{settings.socketServerPort}) connect attempt failed: {reason}. Retrying in "
                + $"{Mathf.Max(1, settings.reconnectTime)}s. (C6)");
        }

        /// <summary>Reports a dropped-message overflow once per connection.</summary>
        private void ReportQueueOverflow()
        {
            if (!_queueOverflowed || _queueOverflowReported)
                return;

            _queueOverflowReported = true;

            Development.LogError($"more than {MaxQueuedMessages} socket messages were left undrained, so "
                + "the oldest are being discarded. Whatever owns this SocketConnection is not calling "
                + "TryGetMessage - a component that only sends still has to drain. (C6)");
        }

        private void OnConnectionSuccess()
        {
            _queueOverflowed = false;
            _queueOverflowReported = false;

            if (settings.showNotifications)
                Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Success,
                    $"Connected to {connectionName}",
                    settings.socketServerIp + ":" + settings.socketServerPort);

            if (settings.doLogs)
                Development.Log($"Connected to {connectionName} ({settings.socketServerIp}:"
                    + $"{settings.socketServerPort})");
        }

        private void OnConnectionError(string message)
        {
            // Never null: the old OnConnectionError did message.Replace on whatever errorMessage held,
            // which was null on any path that lost the connection without an exception.
            string reason = string.IsNullOrEmpty(message) ? "no error reported" : message.Replace("\n", " ");

            // ShowNotification logs on its own, so this pairing double-reports when notifications are
            // on. Deliberate, for the same reason Core's settings-parse catches do it: a lost socket is
            // what people grep for, and an explicit log here survives any later change to the notifier.
            if (settings.showNotifications)
                Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error,
                    $"Lost connection to {connectionName} ({settings.socketServerIp}:"
                    + $"{settings.socketServerPort})", reason);

            // "Lost", not "failed to connect": this only runs on a connected -> disconnected transition,
            // so the connection existed. ReportAttemptFailed owns the never-connected case, and calling
            // both of them the same thing sent people to check an address that had been fine for hours.
            Development.LogError($"Lost connection to {connectionName} ({settings.socketServerIp}:"
                + $"{settings.socketServerPort}): {reason} (C6)");
        }

        private void OnDestroy()
        {
            CloseSocket();
        }

        private void OnApplicationQuit()
        {
            _quitting = true;
            CloseSocket();
        }
    }
}
