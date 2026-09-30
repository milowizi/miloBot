namespace MiloBot.World;

public struct RectD
{
    public double Left, Top, Right, Bottom;
    public RectD(double l, double t, double r, double b) { Left = l; Top = t; Right = r; Bottom = b; }
    public RectD Offset(double dx, double dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public double CenterX => (Left + Right) / 2;
    public double CenterY => (Top + Bottom) / 2;
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

public class Platform
{
    public IntPtr Hwnd;
    public double Y, X1, X2;
    public bool IsFloor;
    public string Owner = "";
    public ContentLedge Ledge;
    public bool IsContent => Ledge != null;
    public double Width => X2 - X1;
    public bool Contains(double x, double margin = 0) => x >= X1 + margin && x <= X2 - margin;
    public override string ToString() => $"{(IsFloor ? "floor" : IsContent ? Ledge.Kind + " in " + Owner : Owner)} y={Y:0} [{X1:0},{X2:0}]";
}

/// <summary>The top edge of something inside a window (a picture, a panel, a block of text), relative to the window's top-left.</summary>
public class ContentLedge
{
    public long Id;
    public string Kind = "box";
    public double X1, X2, Y;
    public RectD Block;
    public int Hits, Misses;
    public bool Alive = true;
    public bool Confirmed => Hits >= 2 && Alive;
}

public class WindowContent
{
    public IntPtr Hwnd;
    public double Width, Height;
    public List<ContentLedge> Ledges = new();
    public double ScannedAt;
    public bool Busy;
}

public class Screen
{
    public RectD Bounds, Work;
}

public enum AppKind { Unknown, Coding, Browser, Video, Music, Chat, Game, ThreeD, Drawing, Office, Files, Terminal, Desktop }

public class WorldState
{
    public List<Platform> Platforms = new();
    public List<Screen> Screens = new();
    public double CursorX, CursorY;
    public double CursorVX, CursorVY;
    public double CursorSpeed;
    public double UserIdle;
    public double Typing, KeysPerSecond;
    public IntPtr ForegroundHwnd;
    public string ForegroundProcess = "", ForegroundTitle = "";
    public RectD ForegroundRect;
    public AppKind ForegroundKind;
    public bool ForegroundFullscreen;
    public double AudioLevel, AudioAverage;
    public bool AudioOnset, AudioStartle, Music;
    public double Now;
    public int Hour = 12;
    public int PixelScale = 3;
    public double DpiScale = 1.0;
    public Screen BlockedScreen;

    public bool Blocked(double x, double y) => BlockedScreen != null && BlockedScreen.Bounds.Contains(x, y);

    public Screen ScreenAt(double x, double y)
    {
        foreach (var s in Screens) if (s.Bounds.Contains(x, y)) return s;
        Screen best = null;
        double bd = double.MaxValue;
        foreach (var s in Screens)
        {
            double dx = Math.Max(0, Math.Max(s.Bounds.Left - x, x - s.Bounds.Right));
            double dy = Math.Max(0, Math.Max(s.Bounds.Top - y, y - s.Bounds.Bottom));
            double d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = s; }
        }
        return best;
    }

    public Platform FloorUnder(double x, double y)
    {
        Platform best = null;
        foreach (var p in Platforms)
        {
            if (!p.IsFloor || !p.Contains(x)) continue;
            if (best == null || Math.Abs(p.Y - y) < Math.Abs(best.Y - y)) best = p;
        }
        return best;
    }

    public Platform PlatformBelow(double x, double y, double margin = 0)
    {
        Platform best = null;
        foreach (var p in Platforms)
        {
            if (p.Y < y - 1 || !p.Contains(x, margin)) continue;
            if (best == null || p.Y < best.Y) best = p;
        }
        return best;
    }

    public Platform Find(IntPtr hwnd, double x, double y)
    {
        Platform best = null;
        foreach (var p in Platforms)
        {
            if (p.Hwnd != hwnd || p.Ledge != null || !p.Contains(x)) continue;
            if (best == null || Math.Abs(p.Y - y) < Math.Abs(best.Y - y)) best = p;
        }
        return best;
    }
}
