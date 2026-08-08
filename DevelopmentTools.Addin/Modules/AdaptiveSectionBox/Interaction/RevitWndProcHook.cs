using System;
using System.Runtime.InteropServices;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Interaction
{
    public static class RevitWndProcHook
    {
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr _hookId = IntPtr.Zero;
        private static HookProc _hookProc;

        private const int WH_MOUSE = 7;
        private const int HC_ACTION = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEHOOKSTRUCT
        {
            public POINT pt;
            public IntPtr hwnd;
            public uint wHitTestCode;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        public static void Hook(IntPtr hwnd)
        {
            if (_hookId != IntPtr.Zero) return;
            try
            {
                _hookProc = new HookProc(HookCallback);
                uint threadId = GetCurrentThreadId();
                _hookId = SetWindowsHookEx(WH_MOUSE, _hookProc, IntPtr.Zero, threadId);
                System.Diagnostics.Debug.WriteLine($"[RevitWndProcHook] 註冊滑鼠 Hook 成功，線程 ID: {threadId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RevitWndProcHook] 註冊失敗: {ex.Message}");
            }
        }

        public static void Unhook()
        {
            if (_hookId == IntPtr.Zero) return;
            try
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                _hookProc = null;
                System.Diagnostics.Debug.WriteLine("[RevitWndProcHook] 滑鼠 Hook 註銷成功。");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RevitWndProcHook] 註銷失敗: {ex.Message}");
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }

    public static class Win32Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    }
}
