using System.Runtime.InteropServices;

namespace MiloBot.Win32;

public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

[StructLayout(LayoutKind.Sequential)]
public struct RAWINPUTDEVICE
{
    public ushort UsagePage, Usage;
    public uint Flags;
    public IntPtr Target;
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

[StructLayout(LayoutKind.Sequential)]
public struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public POINT Pt;
    public uint Private;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WNDCLASSEX
{
    public uint Size;
    public uint Style;
    public IntPtr WndProc;
    public int ClsExtra;
    public int WndExtra;
    public IntPtr Instance;
    public IntPtr Icon;
    public IntPtr Cursor;
    public IntPtr Background;
    public string MenuName;
    public string ClassName;
    public IntPtr IconSm;
}

[StructLayout(LayoutKind.Sequential)]
public struct BLENDFUNCTION
{
    public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
}

[StructLayout(LayoutKind.Sequential)]
public struct BITMAPINFOHEADER
{
    public uint Size;
    public int Width;
    public int Height;
    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ClrUsed;
    public uint ClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
public struct BITMAPINFO
{
    public BITMAPINFOHEADER Header;
    public uint Colors;
}

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO
{
    public uint Size;
    public RECT Monitor;
    public RECT Work;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
public struct LASTINPUTINFO
{
    public uint Size;
    public uint Time;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NOTIFYICONDATA
{
    public uint Size;
    public IntPtr Hwnd;
    public uint Id;
    public uint Flags;
    public uint CallbackMessage;
    public IntPtr Icon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
    public uint State;
    public uint StateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
    public uint Version;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
    public uint InfoFlags;
    public Guid GuidItem;
    public IntPtr BalloonIcon;
}

public static class Native
{
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;
    public const uint WS_CHILD = 0x40000000;

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_NULL = 0x0000;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_APP = 0x8000;
    public const uint WM_INPUT = 0x00FF;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RID_INPUT = 0x10000003;
    public const int MA_NOACTIVATE = 3;

    public const uint PM_REMOVE = 0x0001;
    public const uint QS_ALLINPUT = 0x04FF;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    public const uint ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    public const uint SRCCOPY = 0x00CC0020;
    public const int HALFTONE = 4;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;

    public const uint MF_STRING = 0x0000;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint MF_CHECKED = 0x0008;
    public const uint MF_GRAYED = 0x0001;
    public const uint MF_POPUP = 0x0010;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_BOTTOMALIGN = 0x0020;

    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

    public const int IDC_ARROW = 32512;
    public const int IDC_HAND = 32649;
    public const int IDC_SIZEALL = 32646;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;
    public const uint GA_ROOT = 2;

    public const uint SND_ASYNC = 0x0001;
    public const uint SND_NODEFAULT = 0x0002;
    public const uint SND_MEMORY = 0x0004;
    public const uint SND_PURGE = 0x0040;

    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x0010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hwnd, char[] text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, char[] text, int max);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT ptDst, ref SIZE size,
        IntPtr hdcSrc, ref POINT ptSrc, uint key, ref BLENDFUNCTION blend, uint flags);

    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")] public static extern bool PeekMessageW(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint MsgWaitForMultipleObjects(uint count, IntPtr handles, bool waitAll, uint ms, uint wakeMask);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr LoadCursorW(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll")] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")] public static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool DeregisterShellHookWindow(IntPtr hwnd);

    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr id, string text);
    [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();

    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")] public static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string name);

    public static int LoWord(IntPtr v) => (short)((long)v & 0xFFFF);
    public static int HiWord(IntPtr v) => (short)(((long)v >> 16) & 0xFFFF);

    public static string WindowText(IntPtr hwnd)
    {
        var buf = new char[256];
        int n = GetWindowTextW(hwnd, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    public static string ClassName(IntPtr hwnd)
    {
        var buf = new char[128];
        int n = GetClassNameW(hwnd, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    public static RECT FrameBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0 && r.Width > 0)
            return r;
        GetWindowRect(hwnd, out r);
        return r;
    }

    public static bool IsCloaked(IntPtr hwnd)
    {
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int c, 4) == 0 && c != 0;
    }
}
