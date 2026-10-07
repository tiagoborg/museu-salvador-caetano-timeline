using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Analytics;

namespace EnigmaVirtual
{
    public class AnalyticsSender : MonoBehaviour
    {
        /// <summary>
        /// Right-click the component to fire one test event. <b>Sends nothing</b> - see
        /// <see cref="SendAnalytics"/>.
        /// </summary>
        /// <remarks>
        /// Was a public <c>sendAnaliticsTest</c> bool polled by <c>Update</c> every frame on every
        /// instance, plus an <c>Awake</c> that loaded the descriptor whether or not anyone ever ticked
        /// it. A ContextMenu is the same affordance with no per-frame cost and no serialized state, so
        /// the bool, <c>Update</c> and <c>Awake</c> are all gone.
        /// </remarks>
        [ContextMenu("Send a test analytics event")]
        private void DoAnalyticsTest()
        {
            // Loaded here rather than in Awake: nothing else on this component needs it, so a component
            // sitting in a scene should not pay a Resources.Load at startup for a menu item nobody used.
            var data = Resources.Load("Descriptor") as ApplicationDescription;
            if (data == null)
            {
                Development.LogError("test analytics event not sent: Resources/Descriptor could not be "
                    + "loaded, so there is no project, client or version to report. (M8)");
                return;
            }

            SendAnalytics("unityTest", new Dictionary<string, object>
            {
                { "source", "framework"},
                { "project", data.FullName },
                { "client", data.Client},
                { "version", "version " + data.Version + "\n" + data.Copyright}
            });
        }

        /// <summary>
        /// Placeholder for a future analytics feature. <b>Sends nothing today.</b>
        /// </summary>
        /// <remarks>
        /// <c>Analytics.CustomEvent</c> belongs to Unity Analytics' <b>retired</b> service: it reports
        /// success and transmits nothing. Kept deliberately rather than deleted - analytics is a planned
        /// feature and this is where it will go - but the retirement means
        /// whoever builds it <b>replaces</b> this call rather than extending it: Unity Gaming Services'
        /// Analytics is a different package and a different API.
        ///
        /// The warning is why this method still earns its place. Without it there is no way to tell a
        /// placeholder from a working pipeline, which is the shape of every silent-failure defect in this
        /// register - and a number nobody sent looks exactly like a number nobody looked at.
        /// </remarks>
        public static void SendAnalytics(string name, Dictionary<string,object> data)
        {
            // LogError, not LogWarning. LogWarning is [Conditional("DO_LOGS")] and DO_LOGS ships only
            // for Android and Standalone, so on iOS, macOS, Linux and WebGL the warning compiled away
            // and this method was once again indistinguishable from a working pipeline - the exact
            // thing the message exists to prevent. One caller, so there
            // is no spam concern.
            Development.LogError($"analytics event [{name}] was NOT sent: AnalyticsSender is a "
                + "placeholder for a future feature, and Analytics.CustomEvent belongs to Unity's "
                + "retired Analytics service, which accepts events and discards them. (M8)");

            // Left in place so the intended shape stays visible to whoever implements this. It is a no-op.
            Analytics.CustomEvent(name, data);
        }
    }
}
