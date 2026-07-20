using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace Horimiya.Utils
{
    public static class Win32
    {
        // ── Cursor / Input ──────────────────────────────────────────────────
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, uint dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

        public const byte VK_LSHIFT = 0xA0;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_SCANCODE = 0x0008;

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public static void SendHardwareKey(byte vk, bool down)
        {
            uint scanCode = MapVirtualKey(vk, 0);
            uint flags = KEYEVENTF_SCANCODE;
            if (!down) flags |= KEYEVENTF_KEYUP;
            keybd_event(0, (byte)scanCode, flags, 0);
        }

        [DllImport("user32.dll")]
        public static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll")]
        public static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        // ── Window Management ────────────────────────────────────────────────
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        // ── SendInput support (for future use) ────────────────────────────────────────────────
        [DllImport("user32.dll")]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type; // 0 = INPUT_MOUSE
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        // ── Click helpers: use mouse_event for reliable aim-assist compatible clicks ─────────
        // mouse_event with dx=0, dy=0 sends ONLY button events without touching cursor position,
        // so external aim assists can freely move the mouse without interference.
        // dwExtraInfo=0x1337 tags synthetic clicks so the low-level hook can filter them.

        public static void SendLeftDown()
        {
            mouse_event(0x0002, 0, 0, 0, 0x1337);
        }

        public static void SendLeftUp()
        {
            mouse_event(0x0004, 0, 0, 0, 0x1337);
        }

        // Versiones sin tag 0x1337: el hook no las filtra, XClient las ve como clicks reales.
        // Usadas en Toggle/Always mode para que el aim assist de XClient las reconozca.
        public static void SendLeftDownNative()
        {
            mouse_event(0x0002, 0, 0, 0, 0);
        }

        public static void SendLeftUpNative()
        {
            mouse_event(0x0004, 0, 0, 0, 0);
        }

        // ── PostMessage Clicks (Bypass Windows global queue) ─────────────────
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);

        public static bool IsCursorInClientArea(IntPtr hwnd, out Point clientPos)
        {
            clientPos = new Point(0, 0);
            if (GetCursorPos(out clientPos))
            {
                ScreenToClient(hwnd, ref clientPos);
                RECT rect;
                if (GetClientRect(hwnd, out rect))
                {
                    // Si el Y es negativo (Title bar) o fuera de los límites, devolver falso
                    if (clientPos.X >= 0 && clientPos.Y >= 0 && clientPos.X <= rect.right && clientPos.Y <= rect.bottom)
                        return true;
                }
            }
            return false;
        }

        public static IntPtr PostLeftDown(IntPtr hwnd)
        {
            Point p;
            if (IsCursorInClientArea(hwnd, out p))
            {
                IntPtr lParam = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
                PostMessage(hwnd, 0x0201, (IntPtr)1, lParam); // WM_LBUTTONDOWN, MK_LBUTTON
                return lParam;
            }
            return IntPtr.Zero;
        }

        public static void PostLeftUp(IntPtr hwnd, IntPtr lParam)
        {
            if (lParam != IntPtr.Zero)
                PostMessage(hwnd, 0x0202, (IntPtr)0, lParam); // WM_LBUTTONUP
        }

        /// <summary>
        /// Sends WM_LBUTTONUP with a FRESH cursor position instead of reusing the stale
        /// position from PostLeftDown. This is critical when XClient aim assist is active:
        /// aim assist moves the cursor during the hold time between DOWN and UP.
        /// If UP arrives with the old position, Minecraft may discard the click.
        /// </summary>
        public static void PostLeftUpFresh(IntPtr hwnd, IntPtr fallbackLParam)
        {
            Point p;
            if (IsCursorInClientArea(hwnd, out p))
            {
                IntPtr lParam = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
                PostMessage(hwnd, 0x0202, (IntPtr)0, lParam);
            }
            else if (fallbackLParam != IntPtr.Zero)
            {
                // Si el mouse salió de la ventana durante el hold, mandamos el UP
                // en la última posición válida (fallback) para que no se quede pegado.
                PostMessage(hwnd, 0x0202, (IntPtr)0, fallbackLParam);
            }
        }

        public static void SendRightDown()
        {
            mouse_event(0x0008, 0, 0, 0, 0x1337);
        }

        public static void SendRightUp()
        {
            mouse_event(0x0010, 0, 0, 0, 0x1337);
        }

        public static IntPtr PostRightDown(IntPtr hwnd)
        {
            Point p;
            if (IsCursorInClientArea(hwnd, out p))
            {
                IntPtr lParam = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
                PostMessage(hwnd, 0x0204, (IntPtr)2, lParam); // WM_RBUTTONDOWN, MK_RBUTTON
                return lParam;
            }
            return IntPtr.Zero;
        }

        public static void PostRightUp(IntPtr hwnd, IntPtr lParam)
        {
            if (lParam != IntPtr.Zero)
                PostMessage(hwnd, 0x0205, (IntPtr)0, lParam); // WM_RBUTTONUP
        }

        public static void PostRightUpFresh(IntPtr hwnd, IntPtr fallbackLParam)
        {
            Point p;
            if (IsCursorInClientArea(hwnd, out p))
            {
                IntPtr lParam = (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));
                PostMessage(hwnd, 0x0205, (IntPtr)0, lParam);
            }
            else if (fallbackLParam != IntPtr.Zero)
            {
                PostMessage(hwnd, 0x0205, (IntPtr)0, fallbackLParam);
            }
        }

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        // ── Graphics / DC (for Pixel Color checks) ───────────────────────────
        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        // ── Windows Timer Resolution ─────────────────────────────────────────
        [DllImport("ntdll.dll")]
        public static extern uint NtSetTimerResolution(uint DesiredResolution, bool SetResolution, ref uint CurrentResolution);

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint uPeriod);

        // ── Form Dragging Helpers ────────────────────────────────────────────
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        // ── Mouse Hook ───────────────────────────────────────────────────────
        public static volatile bool IsLeftDown = false;
        public static volatile bool IsRightDown = false;

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP   = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP   = 0x0205;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelMouseProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static Thread _hookThread;

        public static void StartMouseHook()
        {
            if (_hookThread != null) return;
            
            _hookThread = new Thread(() =>
            {
                using (var curProcess = Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    _hookID = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                }
                
                // Run a message loop on this dedicated thread so the hook isn't starved
                // by the main UI thread blocking on VSync
                Application.Run();
                
                if (_hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                }
            });
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.IsBackground = true;
            _hookThread.Start();
        }

        public static void StopMouseHook()
        {
            // The thread is background, so it will die automatically on exit,
            // but we can politely unhook if we want, though Application.Run is blocking it.
            // Since we exit soon after, leaving it is harmless, but for completeness:
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                
                // Ignore our own injected clicks by checking the custom dwExtraInfo tag
                if (hookStruct.dwExtraInfo != (IntPtr)0x1337)
                {
                    if (wParam == (IntPtr)WM_LBUTTONDOWN) IsLeftDown = true;
                    if (wParam == (IntPtr)WM_LBUTTONUP)   IsLeftDown = false;
                    if (wParam == (IntPtr)WM_RBUTTONDOWN)  IsRightDown = true;
                    if (wParam == (IntPtr)WM_RBUTTONUP)    IsRightDown = false;
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // ── BSOD ─────────────────────────────────────────────────────────────
        [DllImport("ntdll.dll")]
        private static extern uint RtlAdjustPrivilege(int Privilege, bool bEnablePrivilege, bool IsThreadPrivilege, out bool PreviousValue);

        [DllImport("ntdll.dll")]
        private static extern uint NtRaiseHardError(uint ErrorStatus, uint NumberOfParameters, uint UnicodeStringParameterMask, IntPtr Parameters, uint ValidResponseOption, out uint Response);

        public static void TriggerBSOD()
        {
            bool prev;
            RtlAdjustPrivilege(19, true, false, out prev);
            uint resp;
            NtRaiseHardError(0xC0000022, 0, 0, IntPtr.Zero, 6, out resp);
        }

        // ── Structs ──────────────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public Point pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public Point ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }
    }
}
