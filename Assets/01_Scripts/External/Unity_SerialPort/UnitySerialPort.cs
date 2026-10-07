// <copyright file="UnitySerialPort.cs" company="dyadica.co.uk">
// Copyright (c) 2010, 2014 All Right Reserved, http://www.dyadica.co.uk

// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY 
// KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
// PARTICULAR PURPOSE.
//
// </copyright>

// <author>SJB</author>
// <email>github@dyadica.co.uk</email>
// <date>04.09.2013</date>
// <summary>A MonoBehaviour type class containing several functions which can be utilised 
// to perform serial communication within Unity3D</summary>

// This code was updated 04.03.2014 to include Notification Events. Please see:
// http://www.dyadica.co.uk/journal/adding-events-to-the-serialport-script for
// more information.

// This code was updated 03.02.2022 to include Notification Events. My website is
// down so please see the readme.md for more information!

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
// explaining it, where WebGL at least got a clear message. Widening
// the call site instead; narrowing wins because it does not claim capability nobody has tested.
// Widening later is one grep for this exact text.
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine;
using System.Collections;

using System.IO.Ports;
using System;

using System.Threading;

using System.Collections.Generic;

// new Text Mesh Pro text
using TMPro;
using EnigmaVirtual;
using EnigmaVirtual.Notifications;

public class UnitySerialPort : MonoBehaviour
{
    // Init a static reference if script is to be accessed by others when used in a 
    // none static nature eg. its dropped onto a gameObject. The use of "Instance"
    // allows access to public vars as such as those available to the unity editor.

    //public static UnitySerialPort Instance;

    #region Properties

    // The serial port

    public SerialPort SerialPort;

    // Thread for thread version of port
    Thread SerialLoopThread;

    [Header("SerialPort")]

    public string SerialPortName = "";

    // Current com port and set of default
    public string ComPort = "COM5";

    public bool doLogs = false;
    public bool showNotifications = false;

    // Current baud rate and set of default
    // 300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 28800, 38400, 57600, 115200
    public int BaudRate = 115200;

    // The parity-checking protocol.
    public Parity Parity = Parity.None;

    // The standard number of stopbits per byte.
    public StopBits StopBits = StopBits.One;

    // The standard length of data bits per byte.
    public int DataBits = 8;

    // The state of the Data Terminal Ready(DTR) signal during serial communication.
    public bool DtrEnable;

    // Whether or not the Request to Send(RTS) signal is enabled during serial communication.
    public bool RtsEnable;

    // Holder for status report information

    private string portStatus = "";
    public string PortStatus
    {
        get { return portStatus; }
        set { portStatus = value; }
    }

    // Read and write timeouts

    public int ReadTimeout = 10;
    public int WriteTimeout = 10;

    // Property used to run/keep alive the serial thread loop

    /// <summary>True once <see cref="OpenSerialPort"/> has succeeded at least once. Lets
    /// <see cref="CanWrite"/> tell "never opened" from "opened and since closed", which the
    /// teardown paths make indistinguishable by nulling the field without closing it.</summary>
    private bool _wasEverOpen;

    /// <summary>
    /// Suppresses repeat write-failure logs until a write actually succeeds.
    ///
    /// Cleared on a successful write, NOT on a successful precondition - which is where the first
    /// version of this got it wrong. CanWrite cleared it whenever the port was merely *open*, so a
    /// port that opens fine but whose writes fail - a semaphore timeout, exactly what COM7 produced -
    /// reset the flag on every attempt and logged every attempt. With the keep-alive at its 30s
    /// default that is an error twice a minute, forever, which is the spam this flag exists to stop.
    /// </summary>
    private bool _writeFailureReported;

    /// <summary>
    /// Reports a write failure once per run of failures.
    ///
    /// Callers pass the message WITHOUT a class-name prefix. <see cref="Development"/> already
    /// formats the calling class in, and adding one by hand produced a doubled prefix in the real
    /// log on COM7 - the line read "UnitySerialPort :: UnitySerialPort :: Write failed...".
    /// </summary>
    private void ReportWriteFailure(string message)
    {
        if (_writeFailureReported)
            return;

        _writeFailureReported = true;

        Development.LogError(message + " Further failures on this port stay silent until a write "
            + "succeeds.");
    }

    private bool isRunning = false;

    /// <summary>
    /// Consecutive non-timeout read failures before the loop gives up and clears
    /// <see cref="IsRunning"/>.
    /// </summary>
    /// <remarks>
    /// Counted rather than reset on any exception, which is the whole difficulty: a
    /// <c>TimeoutException</c> is *normal traffic* on a serial port - it means nothing arrived this
    /// cycle - so stopping on the first exception would stop a healthy port. Only non-timeout failures
    /// count, and one good read resets the run, so this distinguishes "the device went away" from
    /// "nothing arrived".
    ///
    /// <b>This number is reasoned, not measured</b>, unlike <c>AssetManager</c>'s
    /// <c>ConsecutiveFailureLimit</c> of 3, which came from a real run against an unroutable host.
    /// There is no serial hardware here to unplug. 5 is chosen to survive a USB re-enumeration - which
    /// can throw a few times and then recover - while still giving up in well under a second, since a
    /// genuinely absent device fails immediately rather than waiting for the read timeout. Someone with
    /// an adapter should pull it mid-session and check the count that actually occurs.
    /// </remarks>
    private const int ConsecutiveReadFailureLimit = 5;

    /// <summary>
    /// How long a run of failures must also span before the loop gives up.
    /// </summary>
    /// <remarks>
    /// The count alone is not a duration. Five iterations is microseconds on a thread and about 83 ms
    /// over five frames as a coroutine - so the first version of this fix claimed to "survive a USB
    /// re-enumeration" while giving up an order of magnitude too fast. Both conditions must hold.
    /// </remarks>
    private const float FailureWindowSeconds = 2f;

    /// <summary>
    /// Non-timeout read failures since the last successful read.
    /// </summary>
    /// <remarks>
    /// Written by the read loop and cleared by the two loop starts, which run on the main thread - so
    /// this is shared across threads in <c>LoopMethods.Threading</c>, as <c>isRunning</c> already was.
    /// An earlier version of this comment claimed the loop was the only writer, which was wrong.
    /// </remarks>
    private int consecutiveReadFailures = 0;

    /// <summary>When the current run of failures began, or 0 when there is no run.</summary>
    private float firstReadFailureTime = 0f;

    /// <summary>
    /// Set by the stop methods so the read loop can tell a deliberate close from a device that went
    /// away.
    /// </summary>
    /// <remarks>
    /// The distinction cannot be inferred from <c>SerialPort.IsOpen</c>, which is what the first version
    /// of this fix tried: a removed adapter's stream disposes itself, so a failure and a deliberate
    /// close look identical from inside the loop. Getting that wrong meant the loop stayed silent for
    /// the one case it was changed to report.
    /// </remarks>
    private volatile bool stopRequested = false;
    public bool IsRunning
    {
        get { return isRunning; }
        set { isRunning = value; }
    }

    // Set the gui to show ready

    private string rawData = "Ready";
    public string RawData
    {
        get { return rawData; }
        set { rawData = value; }
    }

    // Storage for parsed incoming data

    private string[] chunkData;
    public string[] ChunkData
    {
        get { return chunkData; }
        set { chunkData = value; }
    }

    [Header("GUI Fields")]

    // Refs populated by the editor inspector for default gui
    // functionality if script is to be used in a non-static
    // context.

    public TMP_Text ComStatusText;
    public TMP_Text RawDataText;
    public TMP_Text StatusMsgBox;

    // public TMP_InputField OutputString;

    // Define a delegate for our event to use. Delegates 
    // encapsulate both an object instance and a method 
    // and are similar to c++ pointers.

    public delegate void SerialDataParseEventHandler(string[] data, string rawData, string comPort);

    // Define the event that utilizes the delegate to
    // fire off a notification to all registered objs 

    public event SerialDataParseEventHandler SerialDataParseEvent;

    // Delegate and event for serialport open notification

    public delegate void SerialPortOpenEventHandler(string comPort);
    public event SerialPortOpenEventHandler SerialPortOpenEvent;

    // Delegate and event for serialport close notification

    public delegate void SerialPortCloseEventHandler(string comPort);
    public event SerialPortCloseEventHandler SerialPortCloseEvent;

    // Delegate and event for serialport sentData notification

    public delegate void SerialPortSentDataEventHandler(string data);
    public event SerialPortSentDataEventHandler SerialPortSentDataEvent;

    // Delegate and event for serialport sentLineData notification

    public delegate void SerialPortSentLineDataEventHandler(string data);
    public event SerialPortSentLineDataEventHandler SerialPortSentLineDataEvent;

    public enum LoopMethods
    { Threading, Coroutine }

    [Header("Options")]
    [SerializeField]
    public LoopMethods LoopMethod =
        LoopMethods.Coroutine;

    // If set to true then open the port when the start
    // event is called.

    public bool OpenPortOnStart = false;

    // List of all com ports available on the system

    private ArrayList comPorts =
        new ArrayList();

    [Header("Misc")]
    public List<string> ComPorts = new List<string>();

    [Header("Data Read")]

    public ReadMethod ReadDataMethod = ReadMethod.ReadLine;
    public enum ReadMethod
    {
        ReadLine,
        ReadToChar
    }

    public string Delimiter;
    public char Separator;

    // --- add these two fields near the other properties ---
    public float KeepAliveInterval = 0f;   // 0 = disabled; set via SerialPortSettings
    public string KeepAliveCommand = "STATUS";

    private Coroutine _keepAliveCoroutine;

    #endregion Properties

    #region Unity Frame Events

    /// <summary>
    /// The awake call is used to populate refs to the gui elements used in this 
    /// example. These can be removed or replaced if needed with bespoke elements.
    /// This will not affect the functionality of the system. If we are using awake
    /// then the script is being run non staticaly ie. its initiated and run by 
    /// being dropped onto a gameObject, thus enabling the game loop events to be 
    /// called e.g. start, update etc.
    /// </summary>
    void Awake()
    {
        // Define the script Instance

        //Instance = this;

        // If we have used the editor inspector to populate any included gui
        // elements then lets initiate them and set some default values.

        // Details if the port is open or closed

        if (ComStatusText != null)
        {
            ComStatusText.text = "ComStatus: Closed";
        }
    }

    /// <summary>
    /// The start call is used to populate a list of available com ports on the
    /// system. The correct port can then be selected via the respective guitext
    /// or a call to UpdateComPort();
    /// </summary>
    void Start()
    {
        // Register for a notification of the open port event

        SerialPortOpenEvent +=
            new SerialPortOpenEventHandler(UnitySerialPort_SerialPortOpenEvent);

        // Register for a notification of the close port event

        SerialPortCloseEvent +=
            new SerialPortCloseEventHandler(UnitySerialPort_SerialPortCloseEvent);

        // Register for a notification of data sent

        SerialPortSentDataEvent +=
            new SerialPortSentDataEventHandler(UnitySerialPort_SerialPortSentDataEvent);

        // Register for a notification of data sent

        SerialPortSentLineDataEvent +=
            new SerialPortSentLineDataEventHandler(UnitySerialPort_SerialPortSentLineDataEvent);

        // Register for a notification of the SerialDataParseEvent

        SerialDataParseEvent +=
            new SerialDataParseEventHandler(UnitySerialPort_SerialDataParseEvent);

        // Population of comport list via system.io.ports

        PopulateComPorts();

        // If set to true then open the port. You must 
        // ensure that the port is valid etc. for this! 

        if (OpenPortOnStart) { OpenSerialPort(); }
    }

    /// <summary>
    /// This function is called when the MonoBehaviour will be destroyed.
    /// OnDestroy will only be called on game objects that have previously
    /// been active.
    /// </summary>
    void OnDestroy()
    {
        // If we are registered for a notification of the 
        // SerialPort events then remove the registration

        if (SerialDataParseEvent != null)
            SerialDataParseEvent -= UnitySerialPort_SerialDataParseEvent;

        if (SerialPortOpenEvent != null)
            SerialPortOpenEvent -= UnitySerialPort_SerialPortOpenEvent;

        if (SerialPortCloseEvent != null)
            SerialPortCloseEvent -= UnitySerialPort_SerialPortCloseEvent;

        if (SerialPortSentDataEvent != null)
            SerialPortSentDataEvent -= UnitySerialPort_SerialPortSentDataEvent;

        if (SerialPortSentLineDataEvent != null)
            SerialPortSentLineDataEvent -= UnitySerialPort_SerialPortSentLineDataEvent;

        // Unsubscribing was all this did, which left the read thread running and the COM port held
        // by a component that no longer exists - so the next open, in this run or the next scene,
        // failed with access denied and nothing said why. CloseSerialPort stops whichever loop is in
        // use and closes the handle.
        //
        // OnDestroy, not OnDisable, is where this belongs *here*: the loop keys off isRunning rather
        // than on the component being enabled, and a deactivated-but-alive port is a legitimate state
        // for this class. A destroyed one is not.
        if (SerialPort != null)
            CloseSerialPort();
        else if (LoopMethod == LoopMethods.Threading)
            StopSerialThreading();
    }

    /// <summary>
    /// The update frame call is used to provide caps for sending data to the arduino
    /// triggered via keypress. This can be replaced via use of the static functions
    /// SendSerialData() & SendSerialDataAsLine(). Additionaly this update uses the
    /// RawData property to update the gui. Again this can be removed etc.
    /// </summary>
    void Update()
    {
        // Check if the serial port exists and is open
        if (SerialPort == null || SerialPort.IsOpen == false) { return; }

        // Example calls from system to the arduino. For more detail on the
        // structure of the calls see:
        // http://www.dyadica.co.uk/journal/simple-serial-string-parsing/

        try
        {
            // If we have set a GUI Text object then update it. This can only be
            // run on the thread that initialised the object thus cnnot be run
            // in the ParseSerialData() call below... Unless run as a coroutine!

            // I have also included other raw data examples in GUIManager.cs         

            // RawDataText is null/none by default for examples (see GUIManager.cs)
            if (RawDataText != null)
                RawDataText.text = RawData;
        }
        catch (Exception ex)
        {
            // Failed to update serial data
            Development.LogError("Error 7: " + ex.Message.ToString());

            if (showNotifications)
                Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error, "Error 7", ex.Message.ToString());
        }
    }

    /// <summary>
    /// Clean up the thread and close the port on application close event.
    /// </summary>
    void OnApplicationQuit()
    {
        // Call to cloase the serial port
        CloseSerialPort();

        Thread.Sleep(100);

        if (LoopMethod == LoopMethods.Coroutine)
            StopSerialCoroutine();

        if (LoopMethod == LoopMethods.Threading)
            StopSerialThreading();

        Thread.Sleep(100);
    }

    #endregion Unity Frame Events

    #region Notification Events

    /// <summary>
    /// Data parsed serialport notification event
    /// </summary>
    /// <param name="Data">string</param>
    /// <param name="RawData">string[]</param>
    void UnitySerialPort_SerialDataParseEvent(string[] Data, string RawData, string comPort)
    {
        // Not fired via portStatus to avoid hiding other messages from the GUI
        if (doLogs)
            Development.LogIf(doLogs, "Data received via port: " + RawData);
    }

    /// <summary>
    /// Open serialport notification event
    /// </summary>
    void UnitySerialPort_SerialPortOpenEvent(string comPort)
    {
        portStatus = "The serialport:" + ComPort + " is now open!";

        if (showNotifications)
            Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Success, "Serial Port", ComPort + " is now open!");

        if (doLogs)
            ShowDebugMessages(portStatus);

        if (KeepAliveInterval > 0f)
        {
            if (_keepAliveCoroutine != null)
                StopCoroutine(_keepAliveCoroutine);

            _keepAliveCoroutine = StartCoroutine(KeepAliveLoop());
        }
    }

    /// <summary>
    /// Close serialport notification event
    /// </summary>
    void UnitySerialPort_SerialPortCloseEvent(string comPort)
    {
        portStatus = "The serialport:" + ComPort + " is now closed!";

        if (showNotifications)
            Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Info, "Serial Port", ComPort + " is now closed!");

        if (doLogs)
            ShowDebugMessages(portStatus);

        if (_keepAliveCoroutine != null)
        {
            StopCoroutine(_keepAliveCoroutine);
            _keepAliveCoroutine = null;
        }
    }

    /// <summary>
    /// Send data serialport notification event
    /// </summary>
    /// <param name="Data">string</param>
    void UnitySerialPort_SerialPortSentDataEvent(string Data)
    {
        portStatus = "Sent data: " + Data;

        if (doLogs)
            ShowDebugMessages(portStatus);
    }

    /// <summary>
    /// Send data with "\n" serialport notification event
    /// </summary>
    /// <param name="Data">string</param>
    void UnitySerialPort_SerialPortSentLineDataEvent(string Data)
    {
        portStatus = "Sent data as line: " + Data;

        if (doLogs)
            ShowDebugMessages(portStatus);
    }

    #endregion Notification Events

    #region Object Serial Port

    /// <summary>
    /// Opens the defined serial port and starts the serial thread used
    /// to catch and deal with serial events.
    /// </summary>
    public void OpenSerialPort()
    {
        if (showNotifications)
            Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Info, "Trying to connect to Serial Port ", ComPort + ":" + BaudRate);

        // A REOPEN has to release the previous handle first. The assignment below simply
        // replaced the old SerialPort, so it stayed open holding COM3 - and the reopen then failed
        // with "access denied" from the port the same process was already using.
        //
        // Its OWN try, for two reasons. CloseSerialPort has three unguarded throw paths - the
        // Thread.Abort in StopSerialThreading, an unguarded Core.notificationManager in its catch,
        // and SerialPortCloseEvent invoking subscriber code - and SerialPortsManager.InitSerialPorts
        // iterates ports with no try of its own, so one escaping here abandons every port after this
        // one. And it is a separate mechanism from the open below: folding it into that catch would
        // report a failed CLOSE as "could not open the port".
        //
        // Closed only if it is actually OPEN, and cleared otherwise. Neither guard alone is
        // right. Closing unconditionally fires
        // SerialPortCloseEvent and an operator-facing "COM3 is now closed!" for a port that never
        // opened (a retry after a failed open reaches here with a non-null, never-opened object).
        // Guarding on IsOpen alone would leave that same object referenced forever, which is the
        // handle leak this fix exists to remove. So: close what is open, drop what is not.
        if (SerialPort != null)
        {
            if (SerialPort.IsOpen)
            {
                try
                {
                    CloseSerialPort();
                }
                catch (Exception closeEx)
                {
                    Development.LogError("Could not close the previous handle on " + ComPort
                        + " before reopening it; the reopen will probably fail with access denied: "
                        + closeEx.Message);
                }
            }
            else
            {
                SerialPort = null;
            }
        }

        // Cleared BEFORE the try, and that is the whole point rather than tidiness. If the open
        // throws, control leaves the try and nothing after it runs - so isRunning kept the TRUE
        // from the previous session while pointing at a port that is now closed. The manager's open check
        // in SerialPortsManager reads IsRunning, so that stale true is a false OK: a dead port
        // reported as healthy. Failing closed is the only safe direction here.
        isRunning = false;

        try
        {
            // Initialise the serial port
            SerialPort = new SerialPort(ComPort, BaudRate, Parity, DataBits, StopBits);

            SerialPort.ReadTimeout = ReadTimeout;
            SerialPort.WriteTimeout = WriteTimeout;

            SerialPort.DtrEnable = DtrEnable;
            SerialPort.RtsEnable = RtsEnable;

            // Open the serial port
            SerialPort.Open();

            // Update the gui if applicable
            if (ComStatusText != null)
            { ComStatusText.text = "ComStatus: Open"; }

            //if (Instance != null && Instance.ComStatusText != null)
            //{ Instance.ComStatusText.text = "ComStatus: Open"; }

            // The `if (isRunning) StopSerialCoroutine();` that used to guard this is gone, and
            // removing it fixed a second defect rather than merely tidying. isRunning is
            // cleared at the top of this method now, so the guard could never fire - but on the OLD
            // reopen path it was true, and StopSerialCoroutine nulls SerialPort: it ran AFTER the
            // new port had been created and opened above, so the loop below started against a null
            // handle. Closing the previous port up front is what makes the stop unnecessary here.
            if (LoopMethod == LoopMethods.Coroutine)
                StartSerialCoroutine();

            // Same as the coroutine branch above: the stop is done up front by
            // CloseSerialPort, and doing it here nulled the handle this loop was about to read.
            if (LoopMethod == LoopMethods.Threading)
                StartSerialThread();

            portStatus = "The serialport is now open!";

            if (showNotifications && Core.notificationManager != null)
                Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Success, "Serial Port", ComPort + " is now open!");

            if (doLogs)
                ShowDebugMessages(portStatus);

            // Start keepalive here — port is guaranteed open at this point
            if (KeepAliveInterval > 0f)
            {
                if (_keepAliveCoroutine != null)
                    StopCoroutine(_keepAliveCoroutine);

                _keepAliveCoroutine = StartCoroutine(KeepAliveLoop());
            }

            _wasEverOpen = true;

            // Raised INSIDE the try, so it only fires for an open that actually happened. Outside
            // it, a port that failed to open still announced itself, and SerialPortsManager's
            // subscriber logged "SerialPort [COM3] Open!" on the very next line after the error
            // saying it had not opened.
            if (SerialPortOpenEvent != null)
                SerialPortOpenEvent(ComPort);
        }
        catch (Exception ex)
        {
            // FIRST, before anything that can itself throw. StartSerialThread and
            // StartSerialCoroutine set isRunning = true as their first statement - before the loop
            // they promise actually exists - so a throw anywhere after that point lands here with
            // isRunning still true and nothing reading the port. Clearing it only before the try
            // covered "Open() itself threw" and left the wider hole: SerialPortsManager's health
            // check reads IsRunning and would report that port healthy.
            isRunning = false;

            // Failed to open com port or start serial thread. LogError, not Debug.Log: this is the
            // only place the *reason* appears - "Access to the port 'COM3' is denied" - and at Log
            // level it sat in ordinary noise while the caller reported the failure at Error with no
            // cause attached.
            Development.LogError($"could not open {ComPort}: {ex.Message}");

            // Null-guarded, unlike before: Core.notificationManager can legitimately be null (see
            // Core.Awake), and throwing from a catch here would propagate out of OpenSerialPort
            // into the InitSerialPorts coroutine and silently abandon every remaining port.
            if (showNotifications && Core.notificationManager != null)
                Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error, "Error 1", ex.Message.ToString());
        }
    }

    /// <summary>
    /// Cloases the serial port so that changes can be made or communication
    /// ended.
    /// </summary>
    public void CloseSerialPort()
    {
        if (_keepAliveCoroutine != null)
        {
            StopCoroutine(_keepAliveCoroutine);
            _keepAliveCoroutine = null;
        }

        // Stop the loop FIRST. This used to sit after the catch below, so a Close() that threw -
        // which is what a removed adapter does - escaped the method with the read loop still running
        // and the handle still held. That defeated the OnDestroy change in precisely the case it was
        // added for. Stopping first is also the right order regardless: nothing should be reading a
        // port that is being closed.
        if (LoopMethod == LoopMethods.Coroutine)
            StopSerialCoroutine();

        if (LoopMethod == LoopMethods.Threading)
            StopSerialThreading();

        try
        {
            // Close the serial port
            if (SerialPort != null)
                SerialPort.Close();

            // Update the gui if applicable
            if (ComStatusText != null)
            { ComStatusText.text = "ComStatus: Closed"; }

            //if (Instance.ComStatusText != null)
            //{ Instance.ComStatusText.text = "ComStatus: Closed"; }
        }
        catch (Exception ex)
        {
            if (SerialPort == null || SerialPort.IsOpen == false)
            {
                // Failed to close the serial port. Uncomment if
                // you wish but this is triggered as the port is
                // already closed and or null.

                // Debug.Log("Error 2A: " + "Port already closed!");
            }
            else
            {
                // LogError, not Debug.Log: this is newly reachable from the reopen path added for
                // this, and a close that genuinely failed is what makes the next open fail with
                // access denied. Always compiled, unlike the trace-level line it replaced.
                Development.LogError("Could not close " + ComPort + ": " + ex.Message.ToString());

                // Null-guarded like the sibling at the top of this file: the manager is a nullable
                // static, and an NRE raised while reporting a failed close would replace the real
                // error with a less useful one.
                if (showNotifications && Core.notificationManager != null)
                    Core.notificationManager.ShowNotification(NotificationManager.NotificationType.Error, "Error 2B", ex.Message.ToString());
            }
        }

        // The loop was already stopped at the top of this method, before the close.
        portStatus = "Serial port closed!";

        if (doLogs)
            ShowDebugMessages(portStatus);

        // Trigger a port closed notification

        if (SerialPortCloseEvent != null)
            SerialPortCloseEvent(ComPort);
    }

    #endregion Object Serial Port

    #region Serial Threading

    void StartSerialThread()
    {
        // A fresh run starts with a clean failure history; otherwise a re-open inherits the count
        // that stopped the previous one and gives up early.
        consecutiveReadFailures = 0;
        firstReadFailureTime = 0f;
        stopRequested = false;
        isRunning = true;

        SerialLoopThread = new Thread(SerialThreadLoop);
        SerialLoopThread.Start();
    }

    void SerialThreadLoop()
    {
        while (isRunning)
        {
            if (isRunning == false)
                break;

            // Run the generic loop
            GenericSerialLoop();
        }

        portStatus = "Ending Serial Thread!";

        if (doLogs)
            ShowDebugMessages(portStatus);
    }

    /// <summary>
    /// Function used to stop the thread and "over" kill
    /// off any instance
    /// </summary>
    public void StopSerialThreading()
    {
        stopRequested = true;
        isRunning = false;

        // this should timeout the thread

        Thread.Sleep(100);

        // otherwise...

        if (SerialLoopThread != null && SerialLoopThread.IsAlive)
            SerialLoopThread.Abort();

        Thread.Sleep(100);

        if (SerialLoopThread != null)
            SerialLoopThread = null;

        // Reset the serial port to null

        if (SerialPort != null)
        { SerialPort = null; }

        // Update the port status... just in case :)

        portStatus = "Ended Serial Loop Thread!";

        if (doLogs)
            ShowDebugMessages(portStatus);
    }

    #endregion Serial Threading

    #region Serial Coroutine

    /// <summary>
    /// Loop used to send a keep alive command at a defined interval. 
    /// </summary>
    /// <returns></returns>
    private IEnumerator KeepAliveLoop()
    {
        Development.LogIf(doLogs, "Starting KeepAliveLoop with interval: " + KeepAliveInterval + " and command: " + KeepAliveCommand);

        while (isRunning)
        {
            yield return new WaitForSeconds(KeepAliveInterval);

            // No pre-guard here any more. It duplicated CanWrite's condition and, by returning
            // early, meant the one path in this project that writes unattended - the keep-alive -
            // was the one path that never reported a dead port. It just did nothing, every
            // interval, forever.
            if (SendSerialDataAsLine(KeepAliveCommand))
                Development.LogIf(doLogs, $"KeepAlive sent [{KeepAliveCommand}] on {ComPort}");
        }
    }

    /// <summary>
    /// Function used to start coroutine for reading serial 
    /// data.
    /// </summary>
    public void StartSerialCoroutine()
    {
        // A fresh run starts with a clean failure history; otherwise a re-open inherits the
        // count that stopped the previous one and gives up early.
        consecutiveReadFailures = 0;
        firstReadFailureTime = 0f;
        stopRequested = false;

        isRunning = true;

        StartCoroutine("SerialCoroutineLoop");
    }

    /// <summary>
    /// A Coroutine used to recieve serial data thus not 
    /// affecting generic unity playback etc.
    /// </summary>
    public IEnumerator SerialCoroutineLoop()
    {
        while (isRunning)
        {
            GenericSerialLoop();
            yield return null;
        }

        portStatus = "Ending Coroutine!";

        if (doLogs)
            ShowDebugMessages(portStatus);
    }

    /// <summary>
    /// Function used to stop the coroutine and kill
    /// off any instance
    /// </summary>
    public void StopSerialCoroutine()
    {
        stopRequested = true;

        isRunning = false;

        Thread.Sleep(100);

        try
        {
            StopCoroutine("SerialCoroutineLoop");
        }
        catch (Exception ex)
        {
            portStatus = "Error 2A: " + ex.Message.ToString();

            if (doLogs)
                ShowDebugMessages(portStatus);
        }

        // Reset the serial port to null
        if (SerialPort != null)
        { SerialPort = null; }

        // Update the port status... just in case :)
        portStatus = "Ended Serial Loop Coroutine!";

        if (doLogs)
            ShowDebugMessages(portStatus);
    }

    #endregion Serial Coroutine

    /// <summary>
    /// The serial thread loop & the coroutine loop both utilise
    /// the same code with the exception of the null return on 
    /// the coroutine, so we share it here.
    /// </summary>
    private void GenericSerialLoop()
    {
        // Intent is read from a flag, never inferred from the port's state. That inference is what made
        // the first version of this fix useless: a removed USB adapter's stream disposes itself, so
        // IsOpen goes false with no exception - indistinguishable from a deliberate close if IsOpen is
        // all you look at. The loop then returned quietly and spun forever with isRunning still true,
        // which is the exact failure this method was changed to report.
        SerialPort port = SerialPort;

        if (port == null || !port.IsOpen)
        {
            if (stopRequested)
                return;

            FailReadLoop("the port is no longer open, and no stop was requested - the device has "
                + "probably been unplugged", null);
            return;
        }

        string rData = null;

        try
        {
            // The try wraps THE READ AND NOTHING ELSE. ParseSerialData used to sit inside it, and it
            // raises SerialDataParseEvent - so an App subscriber throwing was reported as
            // "Serial read failed", pointing an operator at the cable for someone else's bug. It could
            // not even trip the limit, because the success reset ran before the parse: the count stuck
            // at 1 and the error repeated once per message forever.
            switch (ReadDataMethod)
            {
                case ReadMethod.ReadLine:
                    rData = port.ReadLine();
                    break;
                case ReadMethod.ReadToChar:
                    rData = port.ReadTo(Delimiter);
                    break;
            }
        }
        catch (TimeoutException)
        {
            // Normal traffic, and deliberately NOT counted: a timeout means nothing arrived this cycle,
            // which is the resting state of most serial devices. Counting it would stop a healthy port,
            // which is the blind patch this entry refused for a year.
            return;
        }
        catch (Exception ex)
        {
            if (stopRequested)
                return;

            SerialPort during = SerialPort;

            if (during == null || !during.IsOpen)
            {
                // Closed under us with no stop requested: same conclusion as above, reached through an
                // exception instead of a state check.
                FailReadLoop("the port closed while a read was in flight", ex);
                return;
            }

            consecutiveReadFailures++;

            if (firstReadFailureTime <= 0f)
                firstReadFailureTime = Time.realtimeSinceStartup;

            float failingFor = Time.realtimeSinceStartup - firstReadFailureTime;

            // BOTH conditions, and the second is the one review added. Five iterations is ~83 ms over
            // five frames in Coroutine mode and microseconds on a thread - far too fast to "survive a
            // USB re-enumeration", which is what the first version of this comment claimed. Requiring
            // the run to also span a wall-clock window makes that claim true.
            if (consecutiveReadFailures < ConsecutiveReadFailureLimit
                || failingFor < FailureWindowSeconds)
            {
                Development.LogError($"Serial read failed on {ComPort} "
                    + $"({consecutiveReadFailures} of {ConsecutiveReadFailureLimit} consecutive, "
                    + $"{failingFor:F1}s of {FailureWindowSeconds}s): {ex.GetType().Name}: "
                    + $"{ex.Message}. Still reading.");

                return;
            }

            FailReadLoop($"{consecutiveReadFailures} consecutive reads failed over {failingFor:F1}s",
                ex);
            return;
        }

        // A completed read means the device answered, whatever it sent - so the failure run ends here
        // rather than only on non-empty data.
        consecutiveReadFailures = 0;
        firstReadFailureTime = 0f;

        if (string.IsNullOrEmpty(rData))
            return;

        RawData = rData;
        ChunkData = RawData.Split(Separator);

        // Outside the read's try, with its own guard, so a subscriber's exception is reported as what
        // it is and cannot be mistaken for a hardware fault or stop the port.
        try
        {
            ParseSerialData(ChunkData, RawData);
        }
        catch (Exception ex)
        {
            Development.LogException(ex);
            Development.LogError($"A handler of serial data from {ComPort} threw "
                + $"({ex.GetType().Name}: {ex.Message}). The port is still being read - this is a "
                + "consumer fault, not a connection one.");
        }
    }

    /// <summary>
    /// Stops the read loop and reports why, once.
    /// </summary>
    /// <remarks>
    /// Clearing <see cref="IsRunning"/> is the whole point: it was never cleared here, so a dead port
    /// kept reporting itself as running. <b>Be honest about what that buys, though</b> - nothing
    /// re-reads <c>IsRunning</c> after boot (<c>SerialPortsManager</c> checks it once inside
    /// <c>InitSerialPorts</c>), so the log lines below are the actual report and the flag only matters
    /// to a project that polls it. The register says so rather than calling this a health signal.
    ///
    /// The port handle is deliberately left as it is: closing it from the read loop would race whatever
    /// the main thread is doing with it, and <c>CanWrite</c>'s existing diagnosis covers the write side.
    /// </remarks>
    private void FailReadLoop(string reason, Exception ex)
    {
        isRunning = false;

        string detail = ex == null ? "" : $" ({ex.GetType().Name}: {ex.Message})";

        Development.LogError($"Serial port {ComPort} has stopped being read: {reason}{detail}. "
            + "IsRunning is now false. Nothing re-opens it automatically, so reads stay stopped for "
            + "the rest of the session - re-open the port to resume. Unplugging the adapter is the "
            + "usual cause.");

        // Marshalled because in LoopMethods.Threading this runs on the read thread and ShowNotification
        // touches the UI. That mode is unreachable today - LoopMethod is never assigned, so the
        // Coroutine initializer always wins - which makes this a guard against a future assignment
        // rather than a live fix, and it costs one frame's delay on the path that does run.
        if (showNotifications)
        {
            string message = reason;

            MainThreadDispatcher.RunOnMainThread(() =>
            {
                if (Core.notificationManager != null)
                {
                    Core.notificationManager.ShowNotification(
                        NotificationManager.NotificationType.Error, "SERIAL PORT LOST", message);
                }
            });
        }
    }

    #region Methods

    /// <summary>
    /// Function used to send string data over serial with
    /// an included line return
    /// </summary>
    /// <param name="data">string</param>
    public bool SendSerialDataAsLine(string data)
    {
        // Was a silent `return` on the same condition. Guarding was right; saying nothing was not -
        // a dropped command is indistinguishable from a delivered one in any log.
        if (!CanWrite(nameof(SendSerialDataAsLine), data))
            return false;

        try
        {
            SerialPort.WriteLine(data);

            portStatus = "Sent data: " + data;

            if (doLogs)
                ShowDebugMessages(portStatus);

            if (SerialPortSentLineDataEvent != null)
                SerialPortSentLineDataEvent(data);

            // The one place recovery is real: bytes went out and the call returned.
            _writeFailureReported = false;

            return true;
        }
        catch (System.IO.IOException ex)
        {
            // Semaphore timeout or device not ready — recover gracefully, but say so at Error.
            // A failed write is a command the hardware never received: the projector did not
            // switch on, the relay did not fire. At Warning this was indistinguishable from a
            // successful send in any log, and stripped entirely on a build without DO_LOGS.
            ReportWriteFailure($"WriteLine failed on {ComPort}, the command was NOT sent: {ex.Message}");
        }
        catch (Exception ex)
        {
            ReportWriteFailure($"Unexpected WriteLine error on {ComPort}, the command was NOT sent: "
                + ex.Message);
        }

        return false;
    }

    /// <summary>
    /// Function used to send string data over serial without
    /// a line return included.
    /// </summary>
    /// <param name="data"></param>
    public bool SendSerialData(string data)
    {
        // Structurally identical to SendSerialDataAsLine above now, and it was not: this method
        // wrote only `if (SerialPort != null)` but then set portStatus and raised
        // SerialPortSentDataEvent **unconditionally**, so a null port produced a complete "Sent
        // data" report with nothing written - a command the hardware never received, announced as
        // delivered. It also had no IsOpen check and no try, so a *closed* port made
        // SerialPort.Write throw an unhandled InvalidOperationException that propagated out
        // through SerialPortsManager into App code.
        if (!CanWrite(nameof(SendSerialData), data))
            return false;

        try
        {
            SerialPort.Write(data);

            // Inside the try and after the write, which is the whole of the fix: these two only
            // mean anything once a write has actually returned.
            portStatus = "Sent data: " + data;

            if (doLogs)
                ShowDebugMessages(portStatus);

            // throw a sent data notification
            if (SerialPortSentDataEvent != null)
                SerialPortSentDataEvent(data);

            _writeFailureReported = false;

            return true;
        }
        catch (System.IO.IOException ex)
        {
            ReportWriteFailure($"Write failed on {ComPort}, the command was NOT sent: {ex.Message}");
        }
        catch (Exception ex)
        {
            ReportWriteFailure($"Unexpected write error on {ComPort}, the command was NOT sent: "
                + ex.Message);
        }

        return false;
    }

    /// <summary>
    /// Whether a write can actually reach the hardware, reporting at Error when it cannot.
    ///
    /// A dropped serial command is a projector that did not switch on or a relay that did not
    /// fire, and both send methods used to make that invisible - in opposite directions.
    /// <see cref="SendSerialData"/> announced a successful send it had not performed;
    /// <see cref="SendSerialDataAsLine"/> returned in silence. Error level because
    /// <c>Development.LogError</c> is always compiled, while the <c>doLogs</c>-gated
    /// <c>ShowDebugMessages</c> around it is not.
    /// </summary>
    private bool CanWrite(string caller, string data)
    {
        // Deliberately does NOT clear _writeFailureReported here. An open port is not a working
        // one: COM7 opens and then fails every write with a semaphore timeout, so clearing on a
        // passing precondition reset the suppression on every attempt and logged every attempt.
        // Only a write that returns counts as recovery - see the send methods.
        if (SerialPort != null && SerialPort.IsOpen)
            return true;

        // The distinction matters to whoever reads this: the teardown paths null the field without
        // closing, so "never opened" and "opened and since closed" are genuinely different states
        // and point at different causes - a config or cabling problem versus a device that went
        // away. Saying "never opened" for the second sends someone to settings.json for a port that
        // was configured perfectly well.
        string state = SerialPort == null
            ? (_wasEverOpen ? "was open and has since been closed" : "was never opened")
            : "is closed";

        ReportWriteFailure($"{caller} on {ComPort}: the port {state}, so [{data}] was NOT sent to "
            + "the hardware.");

        return false;
    }

    /// <summary>
    /// Function used to filter and act upon the data recieved. You can add
    /// bespoke functionality here.
    /// </summary>
    /// <param name="data">string[] of raw data seperated into chunks via ','</param>
    /// <param name="rawData">string of raw data</param>
    private void ParseSerialData(string[] data, string rawData)
    {

        // Fire a notification to all registered objects. Before we do
        // this however, first double check that we have some valid
        // data here so this only has to be performed once and not on
        // each object notified.

        if (data != null && rawData != string.Empty)
        {
            if (SerialDataParseEvent != null)
                SerialDataParseEvent(data, rawData, ComPort);
        }
    }

    /// <summary>
    /// Function that utilises system.io.ports.getportnames() to populate
    /// a list of com ports available on the system.
    /// </summary>
    public void PopulateComPorts()
    {
        // Loop through all available ports and add them to the list
        foreach (string cPort in SerialPort.GetPortNames())
        {
            ComPorts.Add(cPort);

            comPorts.Add(cPort);

            // Debug.Log(cPort.ToString());
        }

        // Update the port status just in case :)
        portStatus = "ComPort list population complete";

        if (doLogs)
            ShowDebugMessages(portStatus);
    }

    /// <summary>
    /// Function used to update the current selected com port
    /// </summary>
    public string UpdateComPort()
    {
        // If open close the existing port
        if (SerialPort != null && SerialPort.IsOpen)
        { CloseSerialPort(); }

        // Find the current id of the existing port within the
        // list of available ports
        int currentComPort = comPorts.IndexOf(ComPort);

        // check against the list of ports and get the next one.
        // If we have reached the end of the list then reset to zero.
        if (currentComPort + 1 <= comPorts.Count - 1)
        {
            // Inc the port by 1 to get the next port
            ComPort = (string)comPorts[currentComPort + 1];
        }
        else
        {
            // We have reached the end of the list reset to the
            // first available port.
            ComPort = (string)comPorts[0];
        }

        // Update the port status just in case :)
        portStatus = "ComPort set to: " + ComPort.ToString();

        if (doLogs)
            ShowDebugMessages(portStatus);

        // Return the new ComPort just in case
        return ComPort;
    }

    /// <summary>
    /// What it says on the tin!
    /// </summary>
    /// <param name="portStatus">string</param>
    public void ShowDebugMessages(string portStatus)
    {
        if (StatusMsgBox != null)
            StatusMsgBox.text = portStatus;

        Development.LogIf(doLogs, portStatus);
    }

    #endregion Methods
}
#endif
