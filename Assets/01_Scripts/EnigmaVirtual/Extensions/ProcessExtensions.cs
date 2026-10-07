using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace EnigmaVirtual
{
    public class ProcessExtensions : MonoBehaviour
    {

        /// <summary>
        /// Checks whether a process with the specified name is currently running on the system.
        /// </summary>
        /// <param name="processName">
        /// The name of the process to check, without the ".exe" extension. 
        /// For example, use "chrome" for "chrome.exe".
        /// </param>
        /// <returns>
        /// Returns true if one or more instances of the process are found running; otherwise, false.
        /// </returns>
        public static bool IsProgramOpen(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            return processes.Length > 0;
        }

        /// <summary>
        /// Attempts to terminate all running instances of the specified processes by name.
        /// </summary>
        /// <param name="processNames">
        /// An array of process names (without the ".exe" extension) to be terminated.
        /// For example, use "chrome" instead of "chrome.exe".
        /// </param>
        public static void KillPrograms(params string[] processNames)
        {
            if (processNames.IsNullOrEmpty())
                return;

            foreach (string name in processNames)
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(name);

                    if (processes.Length > 0)
                    {
                        Development.Log($"Attempting to terminate process(es) named [{name}]...");

                        foreach (Process process in processes)
                        {
                            if (process != null && !process.HasExited)
                            {
                                process.Kill();
                                process.WaitForExit();
                            }

                            process?.Dispose();
                        }
                    }
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
                {
                    Development.LogError($"Failed to terminate process [{name}]: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Attempts to start all processes specified by their executable paths or registered application names.
        /// </summary>
        /// <param name="processPaths">
        /// An array of executable names or file paths to launch.
        /// For example, "notepad" or "C:\\Program Files\\App\\app.exe".
        /// </param>
        public static void StartProcesses(params string[] processPaths)
        {
            if (processPaths.IsNullOrEmpty())
                return;

            foreach (string path in processPaths)
            {
                try
                {
                    Process.Start(path);
                    Development.Log($"Started process: {path}");
                }
                catch (Exception e) when (e is Win32Exception || e is FileNotFoundException)
                {
                    Development.LogError($"Failed to start process [{path}]: {e.Message}");
                }
            }
        }
    }
}
