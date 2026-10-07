using System.Collections.Generic;
using UnityEngine;

namespace EnigmaVirtual.OneCoreClient
{
    /// <summary>
    /// One analytics submission to OneCore: the app's identity, the platform and a timestamp,
    /// plus whatever custom parameters the caller adds.
    /// </summary>
    /// <remarks>
    /// Was <c>Analitics</c> until v0.49.5. <b>Not renamed to the obvious <c>Analytics</c></b>:
    /// <c>UnityEngine.Analytics.Analytics</c> is a real type, and <c>AnalyticsSender</c> calls
    /// <c>Analytics.CustomEvent</c> - so a type of that name in a namespace anyone imports
    /// alongside <c>UnityEngine.Analytics</c> is a <c>CS0104</c> waiting to happen, in a branched
    /// project's file rather than in this one. The typo fix is not worth buying a collision, so it
    /// is named for what it is instead.
    /// </remarks>
    [System.Serializable]
    public class AnalyticsPayload
    {
        public Dictionary<string, object> data { get; private set; }

        private int capacity;

        public AnalyticsPayload(int capacity = 10)
        {
            this.capacity = capacity;
            ApplicationDescription appDescriptor = Resources.Load("Descriptor") as ApplicationDescription;

            data = new Dictionary<string, object>(capacity)
            {
                { "name", appDescriptor.FullName },
                { "namespace", appDescriptor.Slug },
                { "version", appDescriptor.Version },
                { "submit_time", System.DateTime.Now },
                { "platform", Application.platform.ToString() },
                { "custom_params", new Dictionary<string, object>(capacity) { } }
            };
        }

        internal void AddCustomParameter(string key, object value)
        {
            ((Dictionary<string, object>)data["custom_params"]).Add(key, value);
        }

        internal void ResetCustomParameters()
        {
            data["custom_params"] = new Dictionary<string, object>(capacity) { };
        }

        internal void RemoveCustomParameter(string key)
        {
            Dictionary<string, object> cParams = (Dictionary<string, object>)data["custom_params"];
            if (cParams.ContainsKey(key))
                cParams.Remove(key);
        }
    }
}