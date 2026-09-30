using System.Runtime.InteropServices;

namespace MiloBot.Win32;

public class MenuItem
{
    public int Id;
    public string Text;
    public bool Checked, Disabled, Separator;
    public List<MenuItem> Children;
}

public class Host : IDisposable
{
    const string ClassName = "MiloBotHost";
    const uint TrayMessage = Native.WM_APP + 1;
    readonly WndProc proc;
    readonly uint taskbarCreated;
    public IntPtr Hwnd { get; }
    IntPtr icon;
    string tip = "Milo";
    bool trayAdded;
    public Action TrayClicked;
    public Action DisplayChanged;
    public Action<int, bool> Key;
    public Action<int> MouseButtons;
    public Action<IntPtr> WindowFlashed;
    readonly uint shellMessage;
    readonly IntPtr rawBuffer = Marshal.AllocHGlobal(256);

    public Host(string iconPath)
    {
        proc = Proc;
        var wc = new WNDCLASSEX
        {
            Size = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(proc),
            Instance = Native.GetModuleHandleW(null),
            ClassName = ClassName
        };
        Native.RegisterClassExW(ref wc);
        Hwnd = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW, ClassName, "MiloBot", Native.WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.Instance, IntPtr.Zero);
        taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");
        if (File.Exists(iconPath)) icon = Native.LoadImageW(IntPtr.Zero, iconPath, Native.IMAGE_ICON, 32, 32, Native.LR_LOADFROMFILE);
        AddTray();
        var devices = new[]
        {
            new RAWINPUTDEVICE { UsagePage = 1, Usage = 6, Flags = Native.RIDEV_INPUTSINK, Target = Hwnd },
            new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = Native.RIDEV_INPUTSINK, Target = Hwnd }
        };
        if (!Native.RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<RAWINPUTDEVICE>())) Log.Write("raw input not available");
        shellMessage = Native.RegisterWindowMessageW("SHELLHOOK");
        if (!Native.RegisterShellHookWindow(Hwnd)) Log.Write("shell hook not available");
    }

    void ReadRawInput(IntPtr handle)
    {
        uint headerSize = (uint)(8 + IntPtr.Size * 2);
        uint size = 256;
        if (Native.GetRawInputData(handle, Native.RID_INPUT, rawBuffer, ref size, headerSize) == uint.MaxValue) return;
        int type = Marshal.ReadInt32(rawBuffer, 0);
        int o = (int)headerSize;
        if (type == 1)
        {
            int flags = (ushort)Marshal.ReadInt16(rawBuffer, o + 2);
            int vkey = (ushort)Marshal.ReadInt16(rawBuffer, o + 6);
            Key?.Invoke(vkey, (flags & 1) == 0);
        }
        else if (type == 0)
        {
            int buttons = (ushort)Marshal.ReadInt16(rawBuffer, o + 4);
            if (buttons != 0) MouseButtons?.Invoke(buttons);
        }
    }

    NOTIFYICONDATA Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        Hwnd = Hwnd,
        Id = 1,
        Flags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP,
        CallbackMessage = TrayMessage,
        Icon = icon,
        Tip = tip,
        Info = "",
        InfoTitle = ""
    };

    void AddTray()
    {
        var d = Data();
        trayAdded = Native.Shell_NotifyIconW(Native.NIM_ADD, ref d);
    }

    public void SetTip(string text)
    {
        text = text.Length > 120 ? text[..120] : text;
        if (text == tip) return;
        tip = text;
        if (!trayAdded) return;
        var d = Data();
        Native.Shell_NotifyIconW(Native.NIM_MODIFY, ref d);
    }

    public int ShowMenu(List<MenuItem> items)
    {
        IntPtr menu = Build(items);
        Native.GetCursorPos(out var p);
        Native.SetForegroundWindow(Hwnd);
        int cmd = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON, p.X, p.Y, Hwnd, IntPtr.Zero);
        Native.PostMessageW(Hwnd, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        Native.DestroyMenu(menu);
        return cmd;
    }

    static IntPtr Build(List<MenuItem> items)
    {
        IntPtr menu = Native.CreatePopupMenu();
        foreach (var it in items)
        {
            if (it.Separator) { Native.AppendMenuW(menu, Native.MF_SEPARATOR, IntPtr.Zero, null); continue; }
            uint flags = Native.MF_STRING | (it.Checked ? Native.MF_CHECKED : 0) | (it.Disabled ? Native.MF_GRAYED : 0);
            if (it.Children != null)
                Native.AppendMenuW(menu, flags | Native.MF_POPUP, Build(it.Children), it.Text);
            else
                Native.AppendMenuW(menu, flags, new IntPtr(it.Id), it.Text);
        }
        return menu;
    }

    IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp)
    {
        if (msg == TrayMessage)
        {
            uint ev = (uint)((long)lp & 0xFFFF);
            if (ev == Native.WM_RBUTTONUP || ev == Native.WM_LBUTTONUP) TrayClicked?.Invoke();
            return IntPtr.Zero;
        }
        if (msg == Native.WM_INPUT)
        {
            ReadRawInput(lp);
            return Native.DefWindowProcW(hwnd, msg, wp, lp);
        }
        if (msg == shellMessage && shellMessage != 0)
        {
            if (((long)wp & 0x7FFF) == 6 && ((long)wp & 0x8000) != 0) WindowFlashed?.Invoke(lp);
            return IntPtr.Zero;
        }
        if (msg == taskbarCreated && taskbarCreated != 0)
        {
            AddTray();
            return IntPtr.Zero;
        }
        if (msg == Native.WM_CLOSE || msg == 0x0016)
        {
            Native.PostQuitMessage(0);
            return IntPtr.Zero;
        }
        if (msg == Native.WM_DISPLAYCHANGE || msg == Native.WM_DPICHANGED)
        {
            DisplayChanged?.Invoke();
        }
        return Native.DefWindowProcW(hwnd, msg, wp, lp);
    }

    public void Dispose()
    {
        if (trayAdded)
        {
            var d = Data();
            Native.Shell_NotifyIconW(Native.NIM_DELETE, ref d);
            trayAdded = false;
        }
        if (icon != IntPtr.Zero) Native.DestroyIcon(icon);
        Native.DeregisterShellHookWindow(Hwnd);
        Marshal.FreeHGlobal(rawBuffer);
        Native.DestroyWindow(Hwnd);
    }
}
