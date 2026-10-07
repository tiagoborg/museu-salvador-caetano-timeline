using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace EnigmaVirtual
{
    public static class WindowUtils
    {
        const int SW_MINIMIZE = 6;

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static void MinimizeWindow()
        {
            IntPtr handle = GetActiveWindow();
            ShowWindow(handle, SW_MINIMIZE);
        }
    }
}
