namespace EnigmaVirtual
{
    public enum SettingsGroup
	{
		None,
		Activity,
		Sockets,
		OneCore,
		Phidgets,
		OSC,
		AppData
	}

	[System.Serializable]
	public class CoreSettings
	{
		// The FIELD keeps the name EditorSettings because that name is the JSON key in
		// settings.json; only the TYPE was renamed, so no config file changes.
		public PanelAccessSettings EditorSettings = new PanelAccessSettings();
		public DebugSettings DebugSettings;
		public AppDataSettings AppData;
		public ActivitySettings Activity;
		public SocketsSettings Sockets;
		public OneCoreClient.Settings OneCore;
		public PhidgetsSettings Phidgets;
		public ApiSettings Api;
        public OSCSettings OSC;
		public SystemSettings System;
		public SerialPortsSettings SerialPorts;
    }

	[System.Serializable]
	public class SerialPortsSettings
	{
        public bool isActive = false;
		public bool doLogs = false;
		public bool showNotifications = false;
        public SerialPortSettings[] serialPorts = new SerialPortSettings[0];
    }
}
