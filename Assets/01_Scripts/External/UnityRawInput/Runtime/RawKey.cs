
using EnigmaVirtual;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityRawInput
{
    public static class RawKeyExtensions
    {
#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        public static extern short GetKeyState(int keyCode);
#endif

        private static Dictionary<RawKey, char> rawKeyToCharDicCaps = new Dictionary<RawKey, char>();
        private static Dictionary<RawKey, char> rawKeyToCharDicNoCaps = new Dictionary<RawKey, char>();
        private static Dictionary<RawKey, char> rawKeyToNumberDic = new Dictionary<RawKey, char>();
        private static Dictionary<RawKey, char> rawKeyToSymbolDic = new Dictionary<RawKey, char>();

        private static bool _capsLock;
        //private static bool _numLock;
        //private static bool _scrollLock;

        static RawKeyExtensions()
        {
            // CAPS
            rawKeyToCharDicCaps.Add(RawKey.A, 'A');
            rawKeyToCharDicCaps.Add(RawKey.B, 'B');
            rawKeyToCharDicCaps.Add(RawKey.C, 'C');
            rawKeyToCharDicCaps.Add(RawKey.D, 'D');
            rawKeyToCharDicCaps.Add(RawKey.E, 'E');
            rawKeyToCharDicCaps.Add(RawKey.F, 'F');
            rawKeyToCharDicCaps.Add(RawKey.G, 'G');
            rawKeyToCharDicCaps.Add(RawKey.H, 'H');
            rawKeyToCharDicCaps.Add(RawKey.I, 'I');
            rawKeyToCharDicCaps.Add(RawKey.J, 'J');
            rawKeyToCharDicCaps.Add(RawKey.K, 'K');
            rawKeyToCharDicCaps.Add(RawKey.L, 'L');
            rawKeyToCharDicCaps.Add(RawKey.M, 'M');
            rawKeyToCharDicCaps.Add(RawKey.N, 'N');
            rawKeyToCharDicCaps.Add(RawKey.O, 'O');
            rawKeyToCharDicCaps.Add(RawKey.P, 'P');
            rawKeyToCharDicCaps.Add(RawKey.Q, 'Q');
            rawKeyToCharDicCaps.Add(RawKey.R, 'R');
            rawKeyToCharDicCaps.Add(RawKey.S, 'S');
            rawKeyToCharDicCaps.Add(RawKey.T, 'T');
            rawKeyToCharDicCaps.Add(RawKey.U, 'U');
            rawKeyToCharDicCaps.Add(RawKey.V, 'V');
            rawKeyToCharDicCaps.Add(RawKey.W, 'W');
            rawKeyToCharDicCaps.Add(RawKey.X, 'X');
            rawKeyToCharDicCaps.Add(RawKey.Y, 'Y');
            rawKeyToCharDicCaps.Add(RawKey.Z, 'Z');

            // NO CAPS
            rawKeyToCharDicNoCaps.Add(RawKey.A, 'a');
            rawKeyToCharDicNoCaps.Add(RawKey.B, 'b');
            rawKeyToCharDicNoCaps.Add(RawKey.C, 'c');
            rawKeyToCharDicNoCaps.Add(RawKey.D, 'd');
            rawKeyToCharDicNoCaps.Add(RawKey.E, 'e');
            rawKeyToCharDicNoCaps.Add(RawKey.F, 'f');
            rawKeyToCharDicNoCaps.Add(RawKey.G, 'g');
            rawKeyToCharDicNoCaps.Add(RawKey.H, 'h');
            rawKeyToCharDicNoCaps.Add(RawKey.I, 'i');
            rawKeyToCharDicNoCaps.Add(RawKey.J, 'j');
            rawKeyToCharDicNoCaps.Add(RawKey.K, 'k');
            rawKeyToCharDicNoCaps.Add(RawKey.L, 'l');
            rawKeyToCharDicNoCaps.Add(RawKey.M, 'm');
            rawKeyToCharDicNoCaps.Add(RawKey.N, 'n');
            rawKeyToCharDicNoCaps.Add(RawKey.O, 'o');
            rawKeyToCharDicNoCaps.Add(RawKey.P, 'p');
            rawKeyToCharDicNoCaps.Add(RawKey.Q, 'q');
            rawKeyToCharDicNoCaps.Add(RawKey.R, 'r');
            rawKeyToCharDicNoCaps.Add(RawKey.S, 's');
            rawKeyToCharDicNoCaps.Add(RawKey.T, 't');
            rawKeyToCharDicNoCaps.Add(RawKey.U, 'u');
            rawKeyToCharDicNoCaps.Add(RawKey.V, 'v');
            rawKeyToCharDicNoCaps.Add(RawKey.W, 'w');
            rawKeyToCharDicNoCaps.Add(RawKey.X, 'x');
            rawKeyToCharDicNoCaps.Add(RawKey.Y, 'y');
            rawKeyToCharDicNoCaps.Add(RawKey.Z, 'z');

            // NUMBERS
            rawKeyToNumberDic.Add(RawKey.N0, '0');
            rawKeyToNumberDic.Add(RawKey.N1, '1');
            rawKeyToNumberDic.Add(RawKey.N2, '2');
            rawKeyToNumberDic.Add(RawKey.N3, '3');
            rawKeyToNumberDic.Add(RawKey.N4, '4');
            rawKeyToNumberDic.Add(RawKey.N5, '5');
            rawKeyToNumberDic.Add(RawKey.N6, '6');
            rawKeyToNumberDic.Add(RawKey.N7, '7');
            rawKeyToNumberDic.Add(RawKey.N8, '8');
            rawKeyToNumberDic.Add(RawKey.N9, '9');
            rawKeyToNumberDic.Add(RawKey.Numpad0, '0');
            rawKeyToNumberDic.Add(RawKey.Numpad1, '1');
            rawKeyToNumberDic.Add(RawKey.Numpad2, '2');
            rawKeyToNumberDic.Add(RawKey.Numpad3, '3');
            rawKeyToNumberDic.Add(RawKey.Numpad4, '4');
            rawKeyToNumberDic.Add(RawKey.Numpad5, '5');
            rawKeyToNumberDic.Add(RawKey.Numpad6, '6');
            rawKeyToNumberDic.Add(RawKey.Numpad7, '7');
            rawKeyToNumberDic.Add(RawKey.Numpad8, '8');
            rawKeyToNumberDic.Add(RawKey.Numpad9, '9');

            // SYMBOLS
            rawKeyToSymbolDic.Add(RawKey.Space, ' ');
            rawKeyToSymbolDic.Add(RawKey.Return, '\n');
            rawKeyToSymbolDic.Add(RawKey.Multiply, '*');
            rawKeyToSymbolDic.Add(RawKey.Add, '+');
            rawKeyToSymbolDic.Add(RawKey.Subtract, '-');
            rawKeyToSymbolDic.Add(RawKey.Decimal, '.');
            rawKeyToSymbolDic.Add(RawKey.Divide, '/');
            rawKeyToSymbolDic.Add(RawKey.OEMPlus, '+');
            rawKeyToSymbolDic.Add(RawKey.OEMComma, ',');
            rawKeyToSymbolDic.Add(RawKey.OEMPeriod, '.');

        }

        public static bool IsLetter(this RawKey key)
        {
            switch (key)
            {
                case RawKey.A: return true;
                case RawKey.B: return true;
                case RawKey.C: return true;
                case RawKey.D: return true;
                case RawKey.E: return true;
                case RawKey.F: return true;
                case RawKey.G: return true;
                case RawKey.H: return true;
                case RawKey.I: return true;
                case RawKey.J: return true;
                case RawKey.K: return true;
                case RawKey.L: return true;
                case RawKey.M: return true;
                case RawKey.N: return true;
                case RawKey.O: return true;
                case RawKey.P: return true;
                case RawKey.Q: return true;
                case RawKey.R: return true;
                case RawKey.S: return true;
                case RawKey.T: return true;
                case RawKey.U: return true;
                case RawKey.V: return true;
                case RawKey.W: return true;
                case RawKey.X: return true;
                case RawKey.Y: return true;
                case RawKey.Z: return true;
                default: return false;
            }
        }

        public static bool IsNumber(this RawKey key)
        {
            switch (key)
            {
                case RawKey.N0: return true;
                case RawKey.N1: return true;
                case RawKey.N2: return true;
                case RawKey.N3: return true;
                case RawKey.N4: return true;
                case RawKey.N5: return true;
                case RawKey.N6: return true;
                case RawKey.N7: return true;
                case RawKey.N8: return true;
                case RawKey.N9: return true;
                case RawKey.Numpad0: return true;
                case RawKey.Numpad1: return true;
                case RawKey.Numpad2: return true;
                case RawKey.Numpad3: return true;
                case RawKey.Numpad4: return true;
                case RawKey.Numpad5: return true;
                case RawKey.Numpad6: return true;
                case RawKey.Numpad7: return true;
                case RawKey.Numpad8: return true;
                case RawKey.Numpad9: return true;
                default: return false;
            }
        }

        public static char ToKeyCharacter(this RawKey key)
        {
            _capsLock = false;
            //_numLock = false;
            //_scrollLock = false;

#if PLATFORM_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            _capsLock = (((ushort)GetKeyState(0x14)) & 0xffff) != 0;
            //_numLock = (((ushort)GetKeyState(0x90)) & 0xffff) != 0;
            //_scrollLock = (((ushort)GetKeyState(0x91)) & 0xffff) != 0;
#endif

            if (IsLetter(key))
            {
                if (_capsLock)
                    return rawKeyToCharDicCaps[key];
                else
                    return rawKeyToCharDicNoCaps[key];
            }
            else if (IsNumber(key))
                return rawKeyToNumberDic[key];
            else if (rawKeyToSymbolDic.ContainsKey(key))
                return rawKeyToSymbolDic[key];

            return '\0';
        }

        public static string ToKeyString(this RawKey key)
        {
            char c = ToKeyCharacter(key);
            if (c == '\0')
                return "";
            else
                return c.ToString();
        }

        public static KeyCode ToKeyCode(this RawKey key)
        {
            switch (key)
            {
                case RawKey.Back:
                    return KeyCode.Backspace;
                case RawKey.Tab:
                    return KeyCode.Tab;
                case RawKey.Return:
                    return KeyCode.Return;
                case RawKey.CapsLock:
                    return KeyCode.CapsLock;
                case RawKey.Escape:
                    return KeyCode.Escape;
                case RawKey.Space:
                    return KeyCode.Space;
                case RawKey.End:
                    return KeyCode.End;
                case RawKey.Home:
                    return KeyCode.Home;
                case RawKey.Left:
                    return KeyCode.LeftArrow;
                case RawKey.Up:
                    return KeyCode.UpArrow;
                case RawKey.Right:
                    return KeyCode.RightArrow;
                case RawKey.Down:
                    return KeyCode.DownArrow;
                case RawKey.Insert:
                    return KeyCode.Insert;
                case RawKey.Delete:
                    return KeyCode.Delete;
                case RawKey.N0:
                    return KeyCode.Alpha0;
                case RawKey.N1:
                    return KeyCode.Alpha1;
                case RawKey.N2:
                    return KeyCode.Alpha2;
                case RawKey.N3:
                    return KeyCode.Alpha3;
                case RawKey.N4:
                    return KeyCode.Alpha4;
                case RawKey.N5:
                    return KeyCode.Alpha5;
                case RawKey.N6:
                    return KeyCode.Alpha6;
                case RawKey.N7:
                    return KeyCode.Alpha7;
                case RawKey.N8:
                    return KeyCode.Alpha8;
                case RawKey.N9:
                    return KeyCode.Alpha9;
                case RawKey.A:
                    return KeyCode.A;
                case RawKey.B:
                    return KeyCode.B;
                case RawKey.C:
                    return KeyCode.C;
                case RawKey.D:
                    return KeyCode.D;
                case RawKey.E:
                    return KeyCode.E;
                case RawKey.F:
                    return KeyCode.F;
                case RawKey.G:
                    return KeyCode.G;
                case RawKey.H:
                    return KeyCode.H;
                case RawKey.I:
                    return KeyCode.I;
                case RawKey.J:
                    return KeyCode.J;
                case RawKey.K:
                    return KeyCode.K;
                case RawKey.L:
                    return KeyCode.L;
                case RawKey.M:
                    return KeyCode.M;
                case RawKey.N:
                    return KeyCode.N;
                case RawKey.O:
                    return KeyCode.O;
                case RawKey.P:
                    return KeyCode.P;
                case RawKey.Q:
                    return KeyCode.Q;
                case RawKey.R:
                    return KeyCode.R;
                case RawKey.S:
                    return KeyCode.S;
                case RawKey.T:
                    return KeyCode.T;
                case RawKey.U:
                    return KeyCode.U;
                case RawKey.V:
                    return KeyCode.V;
                case RawKey.W:
                    return KeyCode.W;
                case RawKey.X:
                    return KeyCode.X;
                case RawKey.Y:
                    return KeyCode.Y;
                case RawKey.Z:
                    return KeyCode.Z;
                case RawKey.LeftWindows:
                    return KeyCode.LeftWindows;
                case RawKey.RightWindows:
                    return KeyCode.RightWindows;
                case RawKey.Numpad0:
                    return KeyCode.Keypad0;
                case RawKey.Numpad1:
                    return KeyCode.Keypad1;
                case RawKey.Numpad2:
                    return KeyCode.Keypad2;
                case RawKey.Numpad3:
                    return KeyCode.Keypad3;
                case RawKey.Numpad4:
                    return KeyCode.Keypad4;
                case RawKey.Numpad5:
                    return KeyCode.Keypad5;
                case RawKey.Numpad6:
                    return KeyCode.Keypad6;
                case RawKey.Numpad7:
                    return KeyCode.Keypad7;
                case RawKey.Numpad8:
                    return KeyCode.Keypad8;
                case RawKey.Numpad9:
                    return KeyCode.Keypad9;
                case RawKey.Multiply:
                    return KeyCode.KeypadMultiply;
                case RawKey.Add:
                    return KeyCode.KeypadPlus;
                case RawKey.Subtract:
                    return KeyCode.KeypadMinus;
                case RawKey.Decimal:
                    return KeyCode.KeypadPeriod;
                case RawKey.Divide:
                    return KeyCode.KeypadDivide;
                case RawKey.OEMPlus:
                    return KeyCode.Plus;
                case RawKey.OEMComma:
                    return KeyCode.Comma;
                case RawKey.OEMMinus:
                    return KeyCode.Minus;
                case RawKey.OEMPeriod:
                    return KeyCode.Period;
                case RawKey.F1:
                    return KeyCode.F1;
                case RawKey.F2:
                    return KeyCode.F2;
                case RawKey.F3:
                    return KeyCode.F3;
                case RawKey.F4:
                    return KeyCode.F4;
                case RawKey.F5:
                    return KeyCode.F5;
                case RawKey.F6:
                    return KeyCode.F6;
                case RawKey.F7:
                    return KeyCode.F7;
                case RawKey.F8:
                    return KeyCode.F8;
                case RawKey.F9:
                    return KeyCode.F9;
                case RawKey.F10:
                    return KeyCode.F10;
                case RawKey.F11:
                    return KeyCode.F11;
                case RawKey.F12:
                    return KeyCode.F2;
                case RawKey.F13:
                    return KeyCode.F13;
                case RawKey.F14:
                    return KeyCode.F14;
                case RawKey.F15:
                    return KeyCode.F15;
                case RawKey.NumLock:
                    return KeyCode.Numlock;
                case RawKey.ScrollLock:
                    return KeyCode.ScrollLock;
                case RawKey.LeftShift:
                    return KeyCode.LeftShift;
                case RawKey.RightShift:
                    return KeyCode.RightShift;
                case RawKey.LeftControl:
                    return KeyCode.LeftControl;
                case RawKey.RightControl:
                    return KeyCode.RightControl;
                case RawKey.OEM4:
                    return KeyCode.Quote;
                case RawKey.OEM5:
                    return KeyCode.Backslash;

                case RawKey.Kana:
                case RawKey.Junja:
                case RawKey.Final:
                case RawKey.Hanja:
                case RawKey.VolumeMute:
                case RawKey.VolumeDown:
                case RawKey.VolumeUp:
                case RawKey.Application:
                case RawKey.Play:
                case RawKey.Zoom:
                case RawKey.Noname:
                case RawKey.PA1:
                case RawKey.OEMClear:
                case RawKey.LeftMenu:
                case RawKey.RightMenu:
                case RawKey.LeftButton:
                case RawKey.RightButton:
                case RawKey.Cancel:
                case RawKey.MiddleButton:
                case RawKey.ExtraButton1:
                case RawKey.ExtraButton2:
                case RawKey.Clear:
                case RawKey.Shift:
                case RawKey.Control:
                case RawKey.Menu:
                case RawKey.Pause:
                case RawKey.Convert:
                case RawKey.NonConvert:
                case RawKey.Accept:
                case RawKey.ModeChange:
                case RawKey.Prior:
                case RawKey.Next:
                case RawKey.Select:
                case RawKey.Print:
                case RawKey.Execute:
                case RawKey.Snapshot:
                case RawKey.Help:
                case RawKey.BrowserBack:
                case RawKey.BrowserForward:
                case RawKey.BrowserRefresh:
                case RawKey.BrowserStop:
                case RawKey.BrowserSearch:
                case RawKey.BrowserFavorites:
                case RawKey.BrowserHome:
                case RawKey.Sleep:
                case RawKey.NEC_Equal:
                case RawKey.Fujitsu_Masshou:
                case RawKey.Fujitsu_Touroku:
                case RawKey.Fujitsu_Loya:
                case RawKey.Fujitsu_Roya:
                case RawKey.MediaNextTrack:
                case RawKey.MediaPrevTrack:
                case RawKey.MediaStop:
                case RawKey.MediaPlayPause:
                case RawKey.LaunchMail:
                case RawKey.LaunchMediaSelect:
                case RawKey.LaunchApplication1:
                case RawKey.LaunchApplication2:
                case RawKey.OEM1:
                case RawKey.OEM2:
                case RawKey.OEM3:
                case RawKey.OEM6:
                case RawKey.OEM7:
                case RawKey.OEM8:
                case RawKey.OEMAX:
                case RawKey.OEM102:
                case RawKey.ICOHelp:
                case RawKey.ICO00:
                case RawKey.ProcessKey:
                case RawKey.ICOClear:
                case RawKey.Packet:
                case RawKey.OEMReset:
                case RawKey.OEMJump:
                case RawKey.OEMPA1:
                case RawKey.OEMPA2:
                case RawKey.OEMPA3:
                case RawKey.OEMWSCtrl:
                case RawKey.OEMCUSel:
                case RawKey.OEMATTN:
                case RawKey.OEMFinish:
                case RawKey.OEMCopy:
                case RawKey.OEMAuto:
                case RawKey.OEMENLW:
                case RawKey.OEMBackTab:
                case RawKey.ATTN:
                case RawKey.CRSel:
                case RawKey.EXSel:
                case RawKey.EREOF:
                case RawKey.Separator:
                case RawKey.F16:
                case RawKey.F17:
                case RawKey.F18:
                case RawKey.F19:
                case RawKey.F20:
                case RawKey.F21:
                case RawKey.F22:
                case RawKey.F23:
                case RawKey.F24:
                default:
                    Development.LogWarning("Key not mapped!");
                    return KeyCode.None;
            }
        }
    }

    public enum RawKey : ushort
    {
        LeftButton = 0x01,
        RightButton = 0x02,
        Cancel = 0x03,
        MiddleButton = 0x04,
        ExtraButton1 = 0x05,
        ExtraButton2 = 0x06,
        Back = 0x08,
        Tab = 0x09,
        Clear = 0x0C,
        Return = 0x0D,
        Shift = 0x10,
        Control = 0x11,
        Menu = 0x12,
        Pause = 0x13,
        CapsLock = 0x14,
        Kana = 0x15,
        Hangeul = 0x15,
        Hangul = 0x15,
        Junja = 0x17,
        Final = 0x18,
        Hanja = 0x19,
        Kanji = 0x19,
        Escape = 0x1B,
        Convert = 0x1C,
        NonConvert = 0x1D,
        Accept = 0x1E,
        ModeChange = 0x1F,
        Space = 0x20,
        Prior = 0x21,
        Next = 0x22,
        End = 0x23,
        Home = 0x24,
        Left = 0x25,
        Up = 0x26,
        Right = 0x27,
        Down = 0x28,
        Select = 0x29,
        Print = 0x2A,
        Execute = 0x2B,
        Snapshot = 0x2C,
        Insert = 0x2D,
        Delete = 0x2E,
        Help = 0x2F,
        N0 = 0x30,
        N1 = 0x31,
        N2 = 0x32,
        N3 = 0x33,
        N4 = 0x34,
        N5 = 0x35,
        N6 = 0x36,
        N7 = 0x37,
        N8 = 0x38,
        N9 = 0x39,
        A = 0x41,
        B = 0x42,
        C = 0x43,
        D = 0x44,
        E = 0x45,
        F = 0x46,
        G = 0x47,
        H = 0x48,
        I = 0x49,
        J = 0x4A,
        K = 0x4B,
        L = 0x4C,
        M = 0x4D,
        N = 0x4E,
        O = 0x4F,
        P = 0x50,
        Q = 0x51,
        R = 0x52,
        S = 0x53,
        T = 0x54,
        U = 0x55,
        V = 0x56,
        W = 0x57,
        X = 0x58,
        Y = 0x59,
        Z = 0x5A,
        LeftWindows = 0x5B,
        RightWindows = 0x5C,
        Application = 0x5D,
        Sleep = 0x5F,
        Numpad0 = 0x60,
        Numpad1 = 0x61,
        Numpad2 = 0x62,
        Numpad3 = 0x63,
        Numpad4 = 0x64,
        Numpad5 = 0x65,
        Numpad6 = 0x66,
        Numpad7 = 0x67,
        Numpad8 = 0x68,
        Numpad9 = 0x69,
        Multiply = 0x6A,
        Add = 0x6B,
        Separator = 0x6C,
        Subtract = 0x6D,
        Decimal = 0x6E,
        Divide = 0x6F,
        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,
        F13 = 0x7C,
        F14 = 0x7D,
        F15 = 0x7E,
        F16 = 0x7F,
        F17 = 0x80,
        F18 = 0x81,
        F19 = 0x82,
        F20 = 0x83,
        F21 = 0x84,
        F22 = 0x85,
        F23 = 0x86,
        F24 = 0x87,
        NumLock = 0x90,
        ScrollLock = 0x91,
        NEC_Equal = 0x92,
        Fujitsu_Jisho = 0x92,
        Fujitsu_Masshou = 0x93,
        Fujitsu_Touroku = 0x94,
        Fujitsu_Loya = 0x95,
        Fujitsu_Roya = 0x96,
        LeftShift = 0xA0,
        RightShift = 0xA1,
        LeftControl = 0xA2,
        RightControl = 0xA3,
        LeftMenu = 0xA4,
        RightMenu = 0xA5,
        BrowserBack = 0xA6,
        BrowserForward = 0xA7,
        BrowserRefresh = 0xA8,
        BrowserStop = 0xA9,
        BrowserSearch = 0xAA,
        BrowserFavorites = 0xAB,
        BrowserHome = 0xAC,
        VolumeMute = 0xAD,
        VolumeDown = 0xAE,
        VolumeUp = 0xAF,
        MediaNextTrack = 0xB0,
        MediaPrevTrack = 0xB1,
        MediaStop = 0xB2,
        MediaPlayPause = 0xB3,
        LaunchMail = 0xB4,
        LaunchMediaSelect = 0xB5,
        LaunchApplication1 = 0xB6,
        LaunchApplication2 = 0xB7,
        OEM1 = 0xBA,
        OEMPlus = 0xBB,
        OEMComma = 0xBC,
        OEMMinus = 0xBD,
        OEMPeriod = 0xBE,
        OEM2 = 0xBF,
        OEM3 = 0xC0,
        OEM4 = 0xDB,
        OEM5 = 0xDC,
        OEM6 = 0xDD,
        OEM7 = 0xDE,
        OEM8 = 0xDF,
        OEMAX = 0xE1,
        OEM102 = 0xE2,
        ICOHelp = 0xE3,
        ICO00 = 0xE4,
        ProcessKey = 0xE5,
        ICOClear = 0xE6,
        Packet = 0xE7,
        OEMReset = 0xE9,
        OEMJump = 0xEA,
        OEMPA1 = 0xEB,
        OEMPA2 = 0xEC,
        OEMPA3 = 0xED,
        OEMWSCtrl = 0xEE,
        OEMCUSel = 0xEF,
        OEMATTN = 0xF0,
        OEMFinish = 0xF1,
        OEMCopy = 0xF2,
        OEMAuto = 0xF3,
        OEMENLW = 0xF4,
        OEMBackTab = 0xF5,
        ATTN = 0xF6,
        CRSel = 0xF7,
        EXSel = 0xF8,
        EREOF = 0xF9,
        Play = 0xFA,
        Zoom = 0xFB,
        Noname = 0xFC,
        PA1 = 0xFD,
        OEMClear = 0xFE
    }
}
