using System.Runtime.InteropServices;
using MiloBot.Gfx;

namespace MiloBot.Win32;

public interface IMouseTarget
{
    void MouseDown();
    void MouseMove();
    void MouseUp();
    void RightClick();
    void CaptureLost();
}

public class LayeredWindow : IDisposable
{
    const string ClassName = "MiloBotLayered";
    static WndProc procRef;
    static bool registered;
    static readonly Dictionary<IntPtr, LayeredWindow> windows = new();
    static IntPtr handCursor;

    public IntPtr Hwnd { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public Bitmap Canvas { get; private set; }
    public IMouseTarget Target;
    public bool Visible { get; private set; }
    IntPtr memDC, dib, oldObj, bits;
    int lastX = int.MinValue, lastY = int.MinValue;

    public LayeredWindow(int width, int height, bool clickThrough = false)
    {
        Register();
        Hwnd = Native.CreateWindowExW(
            Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | (clickThrough ? Native.WS_EX_TRANSPARENT : 0),
            ClassName, "Milo", Native.WS_POPUP, 0, 0, width, height, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandleW(null), IntPtr.Zero);
        if (Hwnd == IntPtr.Zero) throw new Exception("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        windows[Hwnd] = this;
        Resize(width, height);
    }

    static void Register()
    {
        if (registered) return;
        procRef = Proc;
        handCursor = Native.LoadCursorW(IntPtr.Zero, new IntPtr(Native.IDC_HAND));
        var wc = new WNDCLASSEX
        {
            Size = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(procRef),
            Instance = Native.GetModuleHandleW(null),
            Cursor = handCursor,
            ClassName = ClassName
        };
        Native.RegisterClassExW(ref wc);
        registered = true;
    }

    public void Resize(int width, int height)
    {
        FreeSurface();
        Width = width;
        Height = height;
        Canvas = new Bitmap(width, height);
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        memDC = Native.CreateCompatibleDC(screen);
        Native.ReleaseDC(IntPtr.Zero, screen);
        var bi = new BITMAPINFO
        {
            Header = new BITMAPINFOHEADER
            {
                Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            }
        };
        dib = Native.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
        oldObj = Native.SelectObject(memDC, dib);
        lastX = int.MinValue;
    }

    public unsafe void Present(int x, int y)
    {
        fixed (uint* src = Canvas.Pixels)
            Buffer.MemoryCopy(src, (void*)bits, (long)Width * Height * 4, (long)Width * Height * 4);
        var pt = new POINT(x, y);
        var size = new SIZE(Width, Height);
        var zero = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        Native.UpdateLayeredWindow(Hwnd, screen, ref pt, ref size, memDC, ref zero, 0, ref blend, Native.ULW_ALPHA);
        Native.ReleaseDC(IntPtr.Zero, screen);
        lastX = x;
        lastY = y;
        if (!Visible) Show(true);
    }

    public void Show(bool show)
    {
        if (show == Visible) return;
        Visible = show;
        Native.ShowWindow(Hwnd, show ? Native.SW_SHOWNOACTIVATE : Native.SW_HIDE);
    }

    public void KeepOnTop()
    {
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    static IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp)
    {
        windows.TryGetValue(hwnd, out var w);
        var t = w?.Target;
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                return new IntPtr(Native.MA_NOACTIVATE);
            case Native.WM_SETCURSOR:
                Native.SetCursor(handCursor);
                return new IntPtr(1);
            case Native.WM_LBUTTONDOWN:
                Native.SetCapture(hwnd);
                t?.MouseDown();
                return IntPtr.Zero;
            case Native.WM_MOUSEMOVE:
                t?.MouseMove();
                return IntPtr.Zero;
            case Native.WM_LBUTTONUP:
                t?.MouseUp();
                Native.ReleaseCapture();
                return IntPtr.Zero;
            case Native.WM_RBUTTONUP:
                t?.RightClick();
                return IntPtr.Zero;
            case Native.WM_CAPTURECHANGED:
                t?.CaptureLost();
                return IntPtr.Zero;
            case Native.WM_CLOSE:
                Native.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return Native.DefWindowProcW(hwnd, msg, wp, lp);
    }

    void FreeSurface()
    {
        if (memDC != IntPtr.Zero)
        {
            Native.SelectObject(memDC, oldObj);
            Native.DeleteObject(dib);
            Native.DeleteDC(memDC);
            memDC = dib = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        FreeSurface();
        if (Hwnd != IntPtr.Zero)
        {
            windows.Remove(Hwnd);
            Native.DestroyWindow(Hwnd);
            Hwnd = IntPtr.Zero;
        }
    }
}
