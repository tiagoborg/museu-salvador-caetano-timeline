// The gate keeps System.IO.Ports out of the compile on targets that cannot use it.
//
// It does NOT fix a present compile error, which is what the first pass claimed and what the
// register said. At this project's apiCompatibilityLevel: 3 (.NET Framework) System.IO.Ports lives
// in Mono's System.dll on every target, so the real exposures are a runtime
// PlatformNotSupportedException on mobile, managed-code stripping, and a hard compile error the day
// anyone moves to .NET Standard 2.x.
//
// PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN, matching MainController's call site rather than the
// wider UNITY_STANDALONE the first pass used. Mono does provide serial on macOS and Linux, so the
// wider gate was more capable - but neither is an exercised target here (see the platform matrix),
// and a mismatched pair was worse than either: the machinery compiled on mac/Linux, the #else stub
// did not, the call site never ran, and Core.main.SerialPorts stayed null - an NRE with nothing
// explaining it, where WebGL at least got a clear message. Widening the call site is the
// alternative; narrowing wins because it does not claim capability nobody has tested.
// Widening later is one grep for this exact text.
//
// ONE gate. This file carried three overlapping ones and the first pass added a fourth, which a
// pre-existing #endif closed seven lines later - so it wrapped the usings and left the class body
// to compile without them. That was a compile error I introduced, on WebGL and UWP. Nested
// platform gates in one file are how that happens.
//
// Not dead code, contrary to the first pass: Assets/08_Examples/Arduino/Arduino_Example.unity has
// this component attached. That grep covered .cs files only, so deleting it would have left a
// missing script in that scene and in every branched copy of it.
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
//#define DO_LOGS // use this on classes to print logs.
using System;
using System.IO.Ports;
using UnityEngine;

namespace EnigmaVirtual
{
    public class ArduinoController : MonoBehaviour
    {
        /// <summary>
        /// On SerialPort message received event.
        /// Dispatches serial port message as parameter.
        /// </summary>
        public event Action<string> OnSerialPortMessageEvent;
        /// <summary>
        /// On SerialPort error message received event.
        /// Dispatches serial port error message as parameter.
        /// </summary>
        public event Action<string> OnSerialPortErrorMessageEvent;
        /// <summary>
        /// On connection success event.
        /// </summary>
        public event Action OnConnectEvent;
        /// <summary>
        /// On connection error event.
        /// Dispatches connection error message as parameter.
        /// </summary>
        public event Action<string> OnConnectionErrorEvent;
        /// <summary>
        /// On connection closed event.
        /// </summary>
        public event Action OnConnectionCloseEvent;

        [SerializeField]
        private string portName = "COM4";
        [SerializeField]
        private int baudRate = 9600;
        [SerializeField]
        private int readTimeout = -1;
        [SerializeField]
        private int writeTimeout = -1;
        [SerializeField]
        private Parity parity = Parity.None;
        [SerializeField]
        private int dataBits = 8;
        [SerializeField]
        private StopBits stopBits = StopBits.One;
        [SerializeField]
        private Handshake handshake = Handshake.None;

        [Space(20)]
        [SerializeField, Tooltip("Initialize COM connection on Start.")]
        private bool autoStart = false;

        private SerialPort serialPort;
        private bool portOpen = false; //if port is open or not
        private bool useData = false;
        private string incommingdata = "";
        private int updateslower = 0;
        private int updateinterval = 4;

        void Start()
        {
            if (autoStart)
                OpenConnection();   //start the serial connection.
        }

        void Update()
        {
            updateslower++;
            if (portOpen && serialPort.IsOpen && updateslower >= updateinterval)
            {
                ReadStream();
                updateslower = 0;
            }
        }

        /// <summary>
        /// Make sure the connection to the arduino is closed.
        /// </summary>
        void OnApplicationQuit()
        {
            Development.Log("Closing connection because of program exit");
            CloseConnection();
        }

        public void Config(string portName, int baudRate = 9600, int readTimeout = -1, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            this.portName = portName;
            this.baudRate = baudRate;
            this.readTimeout = readTimeout;
            this.parity = parity;
            this.dataBits = dataBits;
            this.stopBits = stopBits;
        }

        /// <summary>
        /// Sets port read timeout (ms).
        /// </summary>
        /// <param name="readTimeout"></param>
        public void SetPortReadTimeOut(int readTimeout)
        {
            this.readTimeout = readTimeout;
        }

        /// <summary>
        /// Sets port name.
        /// </summary>
        /// <param name="portName"></param>
        public void SetPortName(string portName)
        {
            this.portName = portName;
        }

        /// <summary>
        /// Sets port baud rate.
        /// </summary>
        /// <param name="baudRate"></param>
        public void SetPortBaudRate(int baudRate)
        {
            this.baudRate = baudRate;
        }

        /// <summary>
        /// Opens the connection.
        /// </summary>
        public void OpenConnection()
        {
            OpenConnection(portName, baudRate);
        }

        /// <summary>
        /// Open the connection.
        /// </summary>
        /// <param name="portName">COM port</param>
        /// <param name="baudRate">baudrate</param>
        public void OpenConnection(string portName, int baudRate)
        {
            OpenConnection(portName, baudRate, Parity.None, 8, StopBits.One);
        }

        /// <summary>
        /// Opens the connection.
        /// </summary>
        /// <param name="portName"></param>
        /// <param name="baudRate"></param>
        /// <param name="parity"></param>
        public void OpenConnection(string portName, int baudRate, Parity parity)
        {
            OpenConnection(portName, baudRate, parity, 8, StopBits.One);
        }

        /// <summary>
        /// Opens the connection.
        /// </summary>
        /// <param name="portName"></param>
        /// <param name="baudRate"></param>
        /// <param name="parity"></param>
        /// <param name="dataBits"></param>
        public void OpenConnection(string portName, int baudRate, Parity parity, int dataBits)
        {
            OpenConnection(portName, baudRate, parity, dataBits, StopBits.One);
        }

        /// <summary>
        /// Opens the connection.
        /// </summary>
        /// <param name="portName"></param>
        /// <param name="baudRate"></param>
        /// <param name="parity"></param>
        /// <param name="dataBits"></param>
        /// <param name="stopBits"></param>
        /// <summary>
        public void OpenConnection(string portName, int baudRate, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            this.portName = portName;
            this.baudRate = baudRate;
            this.parity = parity;
            this.dataBits = dataBits;
            this.stopBits = stopBits;

            serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits);

            if (serialPort != null)
            {
                if (serialPort.IsOpen)
                {
                    Development.LogWarning("Closing port, because it was already open!");
                    OnConnectionErrorEvent?.SafeInvoke("Port already open. Closing port.");
                    serialPort.Close();
                }
                else
                {
                    Development.Log("Trying top open the Port");

                    serialPort.Handshake = handshake;
                    if (readTimeout >= 0)
                        serialPort.ReadTimeout = readTimeout;   // sets the read timeout value (how long it takes before timeout error occurs)
                    if (writeTimeout >= 0)
                        serialPort.WriteTimeout = writeTimeout; // sets the write timeout value

                    serialPort.Open();

                    OnConnectEvent?.SafeInvoke();
                    portOpen = true;
                    Development.Log("Port open = " + serialPort.IsOpen);
                }
            }
            else
            {
                if (serialPort.IsOpen)
                {
                    OnConnectionErrorEvent?.SafeInvoke("Port already open.");
                    Development.Log("Port is already open");
                }
                else
                {
                    OnConnectionErrorEvent?.SafeInvoke("Port == null.");
                    Development.Log("Port == null");
                }
            }
        }

        /// <summary>
        /// Closes current serial port connection.
        /// </summary>
        public void CloseConnection()
        {
            serialPort.Close();
            portOpen = false;
            OnConnectionCloseEvent?.SafeInvoke();
        }

        /// <summary>
        /// Sends a data stream throught the current serial port connection.
        /// </summary>
        /// <param name="stream"></param>
        public void SendStream(char[] stream)
        {
            serialPort.Write(stream, 0, stream.Length);
        }

        /// <summary>
        /// Sends a data stream throught the current serial port connection.
        /// </summary>
        /// <param name="stream"></param>
        public void SendStream(string stream)
        {
            SendStream(stream.ToCharArray());
        }

        /// <summary>
        /// Returns available ports names.
        /// </summary>
        /// <returns></returns>
        public string[] GetPortNames()
        {
            return SerialPort.GetPortNames();
        }

        /// <summary>
        /// Polling the arduino.
        /// </summary>
        private void ReadStream()
        {
            try
            {
                if (serialPort.BytesToRead > 0)
                {
                    incommingdata = serialPort.ReadLine();  //Read the data coming from the arduino

                    Development.Log("data = " + incommingdata);

                    if (!string.IsNullOrEmpty(incommingdata))
                        OnSerialPortMessageEvent?.SafeInvoke(incommingdata);

                    useData = true;
                }
            }
            catch (Exception e)
            {
                if (useData)    // Only show error once.. Avoids misleading error spam.
                {
                    Development.LogError(e);
                    OnSerialPortErrorMessageEvent?.SafeInvoke(e.ToString());
                    useData = false;
                }
            }

            serialPort.BaseStream.Flush(); // Clear the serial information to assure we read new information on next Update.
        }
    }
}
#endif
