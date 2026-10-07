/*******************************************************
 * Copyright (C) 2021 Gema Digital Agency
 * 
 * SocketsSettings can not be copied and/or distributed without 
 * the express permission of Gema Digital
 *******************************************************/

namespace EnigmaVirtual
{
    [System.Serializable]
    public class SocketsSettings
    {
        public bool isActive = false;
        public bool autoConnect = true;
        public bool autoSignIn = false;
        public bool doLogs;
        public string socketServerIp;
        public int socketServerPort;
        public bool showNotifications;
        public int keepAliveTime;
        public int reconnectTime;

        /// <summary>
        /// Upper bound for <see cref="keepAliveTime"/> and <see cref="reconnectTime"/>, in seconds.
        /// </summary>
        /// <remarks>
        /// Both were clamped at the low end only. The keep-alive ping is the <b>sole</b> detector of a
        /// half-open connection - a blocking read on a socket whose peer vanished never returns - so a
        /// large keepAliveTime leaves a dead link reading as connected for that long, and a large
        /// reconnectTime means "connect once and never retry". Neither says anything while doing it, which
        /// is why the ceiling is here rather than in a comment.
        /// </remarks>
        public const int MaxIntervalSeconds = 300;

        public string keepAliveMsg = "ping";
        public string broadcastPrefix = "&";

        /// <summary>
        /// Applies one operator-edited setting from the on-device settings panel.
        /// Unparseable input keeps the previous value and reports it - see
        /// <see cref="SettingsParse"/> for why throwing here is not an option.
        /// </summary>
        /// <remarks>
        /// Subclasses must handle their own keys and only fall through to <c>base.SetData</c>
        /// for the rest - see <see cref="OneCoreClient.Settings"/>. Delegating to base *first*
        /// would make the unknown-key report below fire on every subclass key.
        /// </remarks>
        public virtual void SetData(string key, string value)
        {
            switch (key)
            {
                case "isActive":
                    isActive = SettingsParse.Bool(key, value, isActive);
                    break;
                case "socketServerIp":
                    socketServerIp = value;
                    break;
                case "socketServerPort":
                    // Port 0 asks the OS to pick one, which is never what an operator means here.
                    socketServerPort = SettingsParse.Int(key, value, socketServerPort, 1, SettingsParse.MaxPort);
                    break;
                case "keepAliveTime":
                    // Guards the panel only - settings.json still loads 0 straight through
                    // JsonUtility into the keep-alive loop. That loop was InvokeRepeating("CheckConnection",
                    // 0, 0) - one call per frame - until SocketConnection replaced it with its
                    // Lifecycle() coroutine in v0.20.0. A 0 is still wrong, it just spins the
                    // coroutine with no wait now instead.
                    keepAliveTime = SettingsParse.Int(key, value, keepAliveTime, 1, MaxIntervalSeconds);
                    break;
                case "reconnectTime":
                    reconnectTime = SettingsParse.Int(key, value, reconnectTime, 1, MaxIntervalSeconds);
                    break;
                default:
                    SettingsParse.Error($"SetData: unknown key [{key}] - nothing was saved for it.");
                    break;
            }
        }
    }
}
