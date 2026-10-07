using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace EnigmaVirtual
{
    public class ForegroundEnforcer : MonoBehaviour
    {
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private IntPtr unityWindow;

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        const int ALT = 0xA4;
        const int EXTENDEDKEY = 0x1;
        const int KEYUP = 0x2;

        // bring to front -- 

        private void Start()
        {
            Development.CheckPoint();
            unityWindow = GetActiveWindow();
            StartCoroutine(RefocusWindow(Core.settings.System.Windows.Presentation.bringToFrontTimeInterval));
        }

        private IEnumerator RefocusWindow(float waitSeconds)
        {
            Development.Log("RefocusWindow");

            // wait for new window to appear
            yield return new WaitWhile(() => unityWindow == GetActiveWindow());

            for (; ; )
            {
                Development.Log("Refocussing Window");

                // Simulate alt press
                keybd_event((byte)ALT, 0x45, EXTENDEDKEY | 0, 0);

                // Simulate alt release
                keybd_event((byte)ALT, 0x45, EXTENDEDKEY | KEYUP, 0);

                SetForegroundWindow(unityWindow);

                if (waitSeconds == 0)
                    yield break;

                yield return new WaitForSeconds(waitSeconds);
            }
        }
#endif
    }
}