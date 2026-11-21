using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SnowOverlay
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    internal static class Native
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int WS_CAPTION = 0xC00000;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_APPWINDOW = 0x40000;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong(IntPtr h, int idx, int v);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    }

    public struct WinInfo
    {
        public IntPtr Handle;
        public double L, T, R, B;   // в DIP
        public bool Occluder;       // окно только закрывает сугробы позади себя (развёрнутое / у верхнего края экрана)
    }

    /// <summary>Опрашивает WinAPI и возвращает окна в порядке Z (сверху вниз).</summary>
    public sealed class WindowTracker
    {
        private static readonly HashSet<string> SkipClasses = new HashSet<string>
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow"
        };

        private readonly HashSet<IntPtr> _exclude = new HashSet<IntPtr>();
        private readonly StringBuilder _sb = new StringBuilder(128);
        private readonly Native.EnumWindowsProc _proc;
        private List<WinInfo> _result;
        private double _sw, _sh;

        public double Scale = 1.0;   // физические пиксели / DIP

        public WindowTracker() { _proc = OnWindow; }

        public void Exclude(IntPtr h) { if (h != IntPtr.Zero) _exclude.Add(h); }

        public List<WinInfo> Snapshot(double screenW, double screenH, out double groundY)
        {
            _result = new List<WinInfo>(32);
            _sw = screenW; _sh = screenH;
            Native.EnumWindows(_proc, IntPtr.Zero);

            groundY = screenH;
            IntPtr tb = Native.FindWindow("Shell_TrayWnd", null);
            RECT r;
            if (tb != IntPtr.Zero && Native.IsWindowVisible(tb) && Native.GetWindowRect(tb, out r))
            {
                double top = r.Top / Scale;
                if (top > screenH * 0.5 && top < screenH) groundY = top;   // панель снизу
            }
            return _result;
        }

        /// <summary>Активно ли сейчас полноэкранное приложение (игра, видео, F11, презентация).</summary>
        public bool IsFullscreenAppActive()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || _exclude.Contains(fg)) return false;

            _sb.Clear();
            Native.GetClassName(fg, _sb, _sb.Capacity);
            string cls = _sb.ToString();
            if (SkipClasses.Contains(cls)) return false;

            RECT r;
            if (Native.GetWindowRect(fg, out r))
            {
                int cx = Native.GetSystemMetrics(0), cy = Native.GetSystemMetrics(1);
                int style = Native.GetWindowLong(fg, Native.GWL_STYLE);
                bool cover = r.Left <= 0 && r.Top <= 0 && r.Right >= cx && r.Bottom >= cy;
                if (cover && (style & Native.WS_CAPTION) != Native.WS_CAPTION) return true;
            }

            int st;
            if (Native.SHQueryUserNotificationState(out st) == 0 && (st == 3 || st == 4))
                return true;   // D3D fullscreen / режим презентации
            return false;
        }

        private bool OnWindow(IntPtr h, IntPtr lParam)
        {
            if (_exclude.Contains(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h))
                return true;

            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;

            int cloaked;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0)
                return true;

            if (Native.GetWindowTextLength(h) == 0) return true;

            _sb.Clear();
            Native.GetClassName(h, _sb, _sb.Capacity);
            if (SkipClasses.Contains(_sb.ToString())) return true;

            RECT r;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) != 0)
                if (!Native.GetWindowRect(h, out r)) return true;

            double l = r.Left / Scale, t = r.Top / Scale, rr = r.Right / Scale, b = r.Bottom / Scale;
            if (rr - l < 120 || b - t < 60) return true;
            if (t >= _sh - 40 || rr <= 0 || l >= _sw) return true;

            // развёрнутые окна и окна у самого верха экрана: снега на них нет, но они закрывают сугробы позади
            bool occluder = Native.IsZoomed(h) || t <= 0;

            _result.Add(new WinInfo { Handle = h, L = l, T = t, R = rr, B = b, Occluder = occluder });
            return true;
        }
    }
}
