using System;
using System.Runtime.InteropServices;

namespace UnityRawInput
{
    public static class Win32API
    {
        public delegate int HookProc (int code, IntPtr wParam, IntPtr lParam);

        // SetLastError, because RawKeyInput.SetHook logs Marshal.GetLastWin32Error() when this
        // returns IntPtr.Zero. Without it the CLR never captures the error and that call
        // returns a stale, unrelated value - a log that is confidently wrong is worse than the
        // silence it replaced.
        [DllImport("User32", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx (HookType code, HookProc func, IntPtr hInstance, int threadID);
        [DllImport("User32")]
        public static extern int UnhookWindowsHookEx (IntPtr hhook);
        [DllImport("User32")]
        public static extern int CallNextHookEx (IntPtr hhook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("Kernel32")]
        public static extern uint GetCurrentThreadId ();
        [DllImport("Kernel32")]
        public static extern IntPtr GetModuleHandle (string lpModuleName);
    }
}
