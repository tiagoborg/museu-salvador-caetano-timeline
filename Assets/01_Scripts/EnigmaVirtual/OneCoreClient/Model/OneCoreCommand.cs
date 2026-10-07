namespace EnigmaVirtual.OneCoreClient.Model
{
    [System.Serializable]
    public class OneCoreCommand
    {
        public string type;
        public string tag;
        public int format;
        public string message;
        /// <summary>
        /// Arbitrary JSON payload for a <c>command</c> message, forwarded to the OneCore server as-is.
        /// Example: <c>"command_data": {"devices":[615],"options":{"command":"setChannelValue"}}</c>
        /// </summary>
        /// <remarks>
        /// <para><b><c>object</c>, not <c>dynamic</c>, since v0.49.23 - and deliberately not
        /// <c>string</c>.</b> This field is filled by <b>two different serializers</b>: Newtonsoft when
        /// it arrives inside <c>appData.json</c> (where a branched project reads it off an activity),
        /// and <c>JsonUtility</c> when it arrives inside <c>settings.json</c> under
        /// <c>ConnectCommands</c>/<c>DisconnectCommands</c>. Real project data puts a JSON <b>object</b>
        /// here in one activity and <c>""</c> in another, so the type has to accept both - measured:
        /// Newtonsoft deserializing an object into a <c>string</c> field throws
        /// <c>JsonReaderException</c>, while <c>object</c> yields a <c>JObject</c> whose
        /// <c>SerializeObject</c> round-trips the original JSON exactly.</para>
        ///
        /// <para><b>Dropping <c>dynamic</c> changes nothing about serialization</b>: <c>dynamic</c> is
        /// <c>System.Object</c> in metadata, so Newtonsoft never saw a difference. What it changes is
        /// the <i>call sites</i> - they were dynamically dispatched, which is why three
        /// <c>#pragma warning disable CS1974</c> pairs sat around them. That warning was load-bearing: a
        /// dynamic call site cannot be removed by the compiler, so <c>Development.LogIf</c> was <b>not</b>
        /// being stripped on iOS and WebGL the way <c>[Conditional("DO_LOGS")]</c> intends. It removes
        /// the DLR from <i>these</i> call sites; it does not remove it from the assembly, because
        /// <c>AppDataController.DictionaryToObject</c> still uses <c>dynamic</c>.</para>
        ///
        /// <para><b>settings.json works too, and not because of the type.</b> <c>JsonUtility</c> skips
        /// <c>object</c> exactly as it skipped <c>dynamic</c> - no single type is both
        /// <c>JsonUtility</c>-visible and able to hold a JSON object - so the <i>parser</i> is the fix:
        /// <c>Core.RereadOneCoreWithNewtonsoft</c> re-reads this one block after the main parse, and
        /// <c>FileManager.SettingsAsJObject</c> re-emits it on the way out. Both writers of the settings
        /// go through that emitter, because <c>Core.Awake</c> prefers the
        /// <c>PlayerPrefs["Settings"]</c> snapshot over the file and a snapshot missing these payloads
        /// would shadow a good file permanently.</para>
        /// </remarks>
        public object command_data;

        /// <summary>
        /// Arbitrary JSON payload for an <c>event</c> or <c>push_event</c> message.
        /// Example: <c>"event_data": {"data":{"message":"Erro na maquina de rolhas 4"},"event":3}</c>
        /// </summary>
        /// <remarks>See <see cref="command_data"/> - same two serializers, same reasons.</remarks>
        public object event_data;
    }
}