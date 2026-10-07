using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EnigmaVirtual;

namespace UnityRawInput
{
    public static class RawKeyInput
    {
        /// <summary>
        /// Event invoked when user presses a key.
        /// </summary>
        public static event Action<RawKey> OnKeyDown;
        /// <summary>
        /// Event invoked when user releases a key.
        /// </summary>
        public static event Action<RawKey> OnKeyUp;

        /// <summary>
        /// Whether the service is running and input messages are being processed.
        /// </summary>
        public static bool IsRunning => hookPtr != IntPtr.Zero;
        /// <summary>
        /// Whether any key is currently pressed.
        /// </summary>
        public static bool AnyKeyDown => pressedKeys.Count > 0;
        /// <summary>
        /// Whether input messages should be handled when the application is not in focus.
        /// </summary>
        public static bool WorkInBackground { get; private set; }
        /// <summary>
        /// Whether handled input messages should not be propagated further.
        /// </summary>
        public static bool InterceptMessages { get; set; }

        private static IntPtr hookPtr = IntPtr.Zero;
        private static HashSet<RawKey> pressedKeys = new HashSet<RawKey>();

#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
        // Rooted for the lifetime of the hook, and this is not a style preference. Passing the
        // method group to SetWindowsHookEx allocates a delegate at the call site whose only
        // reference is held by Windows - which the GC cannot see. Once collected, the next keystroke
        // calls freed memory: a hard crash inside user32.dll with no managed stack, minutes or hours
        // after startup and never in a short test. A static field keeps it alive.
        private static readonly Win32API.HookProc LowLevelProc = HandleLowLevelHookProc;
        private static readonly Win32API.HookProc StandardProc = HandleHookProc;
#endif

        /// <summary>
        /// Initializes the service and starts processing input messages.
        /// </summary>
        /// <param name="workInBackround">Whether input messages should be handled when the application is not in focus.</param>
        /// <returns>Whether the service started successfully.</returns>
        public static bool Start (bool workInBackround)
        {
            if (IsRunning) return false;
            WorkInBackground = workInBackround;
            return SetHook();
        }

        /// <summary>
        /// Terminates the service and stops processing input messages.
        /// </summary>
        public static void Stop ()
        {
            RemoveHook();
            pressedKeys.Clear();
        }

        /// <summary>
        /// Checks whether provided key is currently pressed.
        /// </summary>
        public static bool IsKeyDown (RawKey key)
        {
            return pressedKeys.Contains(key);
        }

        private static bool SetHook ()
        {
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (hookPtr == IntPtr.Zero)
            {
                if (WorkInBackground) hookPtr = Win32API.SetWindowsHookEx(HookType.WH_KEYBOARD_LL, LowLevelProc, IntPtr.Zero, 0);
                else hookPtr = Win32API.SetWindowsHookEx(HookType.WH_KEYBOARD, StandardProc, IntPtr.Zero, (int)Win32API.GetCurrentThreadId());

                // Read IMMEDIATELY after the P/Invoke: GetLastWin32Error reflects the last call on
                // this thread, and anything at all in between - including a log - overwrites it.
                if (hookPtr == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();

                    // Always compiled. A global hook is refused on a hardened kiosk image and
                    // by an elevated foreground window, and until now that returned false in
                    // silence, so a dead key logger looked exactly like a working one.
                    Development.LogError($"SetWindowsHookEx failed (Win32 error {err}) for the "
                        + (WorkInBackground ? "WH_KEYBOARD_LL (background)" : "WH_KEYBOARD (foreground)")
                        + " hook. No keys will be reported. Error 5 is access denied - a global "
                        + "low-level hook needs the process to be at least as privileged as the "
                        + "window it hooks.");
                }
            }
#endif

            if (hookPtr == IntPtr.Zero) return false;

            return true;
        }

        private static void RemoveHook ()
        {
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (hookPtr != IntPtr.Zero)
            {
                Win32API.UnhookWindowsHookEx(hookPtr);
                hookPtr = IntPtr.Zero;
            }
#endif
        }

#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN
        [AOT.MonoPInvokeCallback(typeof(Win32API.HookProc))]
        private static int HandleHookProc (int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) return Win32API.CallNextHookEx(hookPtr, code, wParam, lParam);

            var isKeyDown = ((int)lParam & (1 << 31)) == 0;
            var key = (RawKey)wParam;

            if (isKeyDown) HandleKeyDown(key);
            else HandleKeyUp(key);

            return InterceptMessages ? 1 : Win32API.CallNextHookEx(hookPtr, 0, wParam, lParam);
        }

        [AOT.MonoPInvokeCallback(typeof(Win32API.HookProc))]
        private static int HandleLowLevelHookProc (int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) return Win32API.CallNextHookEx(hookPtr, code, wParam, lParam);

            var kbd = KBDLLHOOKSTRUCT.CreateFromPtr(lParam);
            var keyState = (RawKeyState)wParam;
            var key = (RawKey)kbd.vkCode;

            if (keyState == RawKeyState.KeyDown || keyState == RawKeyState.SysKeyDown) HandleKeyDown(key);
            else HandleKeyUp(key);

            return InterceptMessages ? 1 : Win32API.CallNextHookEx(hookPtr, 0, wParam, lParam);
        }
#endif

        private static void HandleKeyDown (RawKey key)
        {
            var added = pressedKeys.Add(key);
            if (added && OnKeyDown != null) OnKeyDown.Invoke(key);
        }

        private static void HandleKeyUp (RawKey key)
        {
            pressedKeys.Remove(key);
            if (OnKeyUp != null) OnKeyUp.Invoke(key);
        }
    }
}
