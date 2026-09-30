using System.Runtime.InteropServices;
using MiloBot.Win32;
using MiloBot.World;

namespace MiloBot.Vision;

public class Capture
{
    public int Width, Height;
    public byte[] Bgra;
    public RectD Source;
}

public static class ScreenCapture
{
    public static Capture Grab(RectD src, int targetW, int targetH)
    {
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        IntPtr mem = Native.CreateCompatibleDC(screen);
        var bi = new BITMAPINFO
        {
            Header = new BITMAPINFOHEADER
            {
                Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                Width = targetW,
                Height = -targetH,
                Planes = 1,
                BitCount = 32
            }
        };
        IntPtr dib = Native.CreateDIBSection(mem, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr old = Native.SelectObject(mem, dib);
        try
        {
            Native.SetStretchBltMode(mem, Native.HALFTONE);
            Native.SetBrushOrgEx(mem, 0, 0, IntPtr.Zero);
            Native.StretchBlt(mem, 0, 0, targetW, targetH, screen, (int)src.Left, (int)src.Top, (int)src.Width, (int)src.Height, Native.SRCCOPY);
            Native.GdiFlush();
            var data = new byte[targetW * targetH * 4];
            Marshal.Copy(bits, data, 0, data.Length);
            return new Capture { Width = targetW, Height = targetH, Bgra = data, Source = src };
        }
        finally
        {
            Native.SelectObject(mem, old);
            Native.DeleteObject(dib);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
