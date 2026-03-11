using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RssPlayer.Components.Utilities
{
    public class ActivityHandler
    {
        private readonly Action _onActivity;
        private bool _isRegistered = false;

        private IntPtr _keyboardHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;

        private LowLevelHookProc _keyboardProc;
        private LowLevelHookProc _mouseProc;

        public ActivityHandler(Form form, Control webView, Action onActivity)
        {
            _onActivity = onActivity;
        }

        public void Register()
        {
            if (_isRegistered) return;

            _keyboardProc = KeyboardCallback;
            _mouseProc = MouseCallback;

            _keyboardHookId = SetHook(WH_KEYBOARD_LL, _keyboardProc);
            _mouseHookId = SetHook(WH_MOUSE_LL, _mouseProc);

            _isRegistered = true;
        }

        public void Unregister()
        {
            if (!_isRegistered) return;

            UnhookWindowsHookEx(_keyboardHookId);
            UnhookWindowsHookEx(_mouseHookId);

            _keyboardHookId = IntPtr.Zero;
            _mouseHookId = IntPtr.Zero;

            _isRegistered = false;
        }

        private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                _onActivity?.Invoke();
            }
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                _onActivity?.Invoke();
            }
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private IntPtr SetHook(int idHook, LowLevelHookProc callback)
        {
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule curModule = curProcess.MainModule;
            return SetWindowsHookEx(idHook, callback, GetModuleHandle(curModule.ModuleName), 0);
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
