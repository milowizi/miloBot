using System.Diagnostics;
using MiloBot.Audio;
using MiloBot.Win32;

namespace MiloBot.World;

public enum WorldEventKind { ForegroundChanged, WindowOpened, UserReturned, FullscreenChanged, Toast, Flash }

public class WorldEvent
{
    public WorldEventKind Kind;
    public RectD Rect;
    public double Value;
    public AppKind App;
}

public class Sensor
{
    readonly WorldState w;
    readonly HashSet<IntPtr> ownWindows;
    readonly AudioMeter audio = new();
    readonly uint ownPid = (uint)Environment.ProcessId;
    readonly Dictionary<uint, string> processNames = new();
    readonly EnumWindowsProc enumProc;
    readonly MonitorEnumProc monitorProc;
    readonly List<(IntPtr hwnd, RECT r, string title, uint pid)> scratch = new();
    readonly List<Screen> screenScratch = new();
    HashSet<IntPtr> knownWindows = new();
    readonly HashSet<IntPtr> toasts = new(), toastsNow = new();
    public readonly List<WorldEvent> Events = new();
    readonly List<WorldEvent> pending = new();

    double nextWindows, nextForeground, nextAudio, nextScreens;
    double lastX, lastY, lastT;
    uint lastInputTick;
    double typingEnergy;
    double prevIdle;
    bool firstScan = true;

    public Sensor(WorldState world, HashSet<IntPtr> ownWindows)
    {
        w = world;
        this.ownWindows = ownWindows;
        enumProc = CollectWindow;
        monitorProc = CollectMonitor;
    }

    public void Update(double now)
    {
        Events.Clear();
        Events.AddRange(pending);
        pending.Clear();
        w.Now = now;
        w.Hour = DateTime.Now.Hour;
        UpdateCursorAndInput(now);
        if (now >= nextScreens) { nextScreens = now + 5; RefreshScreens(); }
        if (now >= nextForeground) { nextForeground = now + 0.25; RefreshForeground(); }
        if (now >= nextWindows) { nextWindows = now + 0.3; RefreshWindows(); }
        if (now >= nextAudio)
        {
            nextAudio = now + 0.05;
            audio.Update(now);
            w.AudioLevel = audio.Level;
            w.AudioAverage = audio.Average;
            w.AudioOnset |= audio.Onset;
            w.AudioStartle |= audio.Startle;
            w.Music = audio.MusicLike;
        }
    }

    public void ConsumeAudioFlags()
    {
        w.AudioOnset = false;
        w.AudioStartle = false;
    }

    public void Flashed(IntPtr hwnd)
    {
        if (ownWindows.Contains(hwnd) || !Native.IsWindow(hwnd)) return;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == ownPid) return;
        var r = Native.FrameBounds(hwnd);
        pending.Add(new WorldEvent { Kind = WorldEventKind.Flash, Rect = new RectD(r.Left, r.Top, r.Right, r.Bottom), App = Classify(ProcessName(pid), Native.WindowText(hwnd)) });
    }

    public void ForceRefresh()
    {
        nextScreens = nextWindows = nextForeground = 0;
    }

    void UpdateCursorAndInput(double now)
    {
        Native.GetCursorPos(out var p);
        double dt = now - lastT;
        if (dt > 0.001)
        {
            double vx = (p.X - lastX) / dt, vy = (p.Y - lastY) / dt;
            w.CursorVX += (vx - w.CursorVX) * Math.Min(1, dt * 12);
            w.CursorVY += (vy - w.CursorVY) * Math.Min(1, dt * 12);
            w.CursorSpeed = Math.Sqrt(w.CursorVX * w.CursorVX + w.CursorVY * w.CursorVY);
        }
        bool moved = p.X != (int)lastX || p.Y != (int)lastY;
        w.CursorX = p.X;
        w.CursorY = p.Y;
        lastX = p.X;
        lastY = p.Y;
        lastT = now;

        var info = new LASTINPUTINFO { Size = 8 };
        if (Native.GetLastInputInfo(ref info))
        {
            uint tick = (uint)Environment.TickCount;
            w.UserIdle = (tick - info.Time) / 1000.0;
            if (info.Time != lastInputTick && !moved) typingEnergy += 0.35;
            lastInputTick = info.Time;
        }
        typingEnergy = Math.Max(0, typingEnergy - dt * 1.2);
        w.Typing = Math.Clamp(typingEnergy / 3.0, 0, 1);
        if (prevIdle > 90 && w.UserIdle < 1)
            Events.Add(new WorldEvent { Kind = WorldEventKind.UserReturned, Value = prevIdle });
        prevIdle = w.UserIdle;
    }

    void RefreshScreens()
    {
        screenScratch.Clear();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, monitorProc, IntPtr.Zero);
        if (screenScratch.Count > 0)
            w.Screens = new List<Screen>(screenScratch);
        uint dpi = 96;
        try { dpi = Native.GetDpiForSystem(); } catch { }
        w.DpiScale = dpi / 96.0;
    }

    bool CollectMonitor(IntPtr mon, IntPtr hdc, ref RECT rect, IntPtr data)
    {
        var mi = new MONITORINFO { Size = 40 };
        if (Native.GetMonitorInfoW(mon, ref mi))
            screenScratch.Add(new Screen
            {
                Bounds = new RectD(mi.Monitor.Left, mi.Monitor.Top, mi.Monitor.Right, mi.Monitor.Bottom),
                Work = new RectD(mi.Work.Left, mi.Work.Top, mi.Work.Right, mi.Work.Bottom)
            });
        return true;
    }

    void RefreshForeground()
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || ownWindows.Contains(fg)) return;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == ownPid) return;
        string proc = ProcessName(pid);
        string title = Native.WindowText(fg);
        string cls = Native.ClassName(fg);
        var r = Native.FrameBounds(fg);
        var rect = new RectD(r.Left, r.Top, r.Right, r.Bottom);
        bool desktop = cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
        bool fullscreen = false;
        if (!desktop)
        {
            var s = w.ScreenAt(rect.CenterX, rect.CenterY);
            if (s != null && rect.Left <= s.Bounds.Left && rect.Top <= s.Bounds.Top && rect.Right >= s.Bounds.Right && rect.Bottom >= s.Bounds.Bottom)
                fullscreen = true;
        }
        var kind = desktop ? AppKind.Desktop : Classify(proc, title);
        bool changed = fg != w.ForegroundHwnd || kind != w.ForegroundKind;
        if (fullscreen != w.ForegroundFullscreen)
            Events.Add(new WorldEvent { Kind = WorldEventKind.FullscreenChanged, Value = fullscreen ? 1 : 0, Rect = rect });
        w.ForegroundHwnd = fg;
        w.ForegroundProcess = proc;
        w.ForegroundTitle = title;
        w.ForegroundRect = rect;
        w.ForegroundFullscreen = fullscreen;
        w.ForegroundKind = kind;
        if (changed) Events.Add(new WorldEvent { Kind = WorldEventKind.ForegroundChanged, Rect = rect, App = kind });
    }

    string ProcessName(uint pid)
    {
        if (processNames.TryGetValue(pid, out var n)) return n;
        try { n = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); }
        catch { n = ""; }
        if (processNames.Count > 500) processNames.Clear();
        processNames[pid] = n;
        return n;
    }

    public static AppKind Classify(string proc, string title)
    {
        string t = title.ToLowerInvariant();
        if (proc is "chrome" or "msedge" or "firefox" or "opera" or "brave" or "vivaldi" or "arc" or "zen" or "librewolf")
        {
            if (t.Contains("youtube") || t.Contains("netflix") || t.Contains("twitch") || t.Contains("disney+") || t.Contains("prime video") || t.Contains("streamz") || t.Contains("vtm go"))
                return AppKind.Video;
            if (t.Contains("spotify") || t.Contains("soundcloud") || t.Contains("deezer")) return AppKind.Music;
            if (t.Contains("github") || t.Contains("stack overflow") || t.Contains("localhost")) return AppKind.Coding;
            if (t.Contains("discord") || t.Contains("whatsapp") || t.Contains("messenger") || t.Contains("teams")) return AppKind.Chat;
            return AppKind.Browser;
        }
        return proc switch
        {
            "code" or "devenv" or "rider64" or "rider" or "idea64" or "pycharm64" or "cursor" or "sublime_text" or "notepad++" or "zed" or "windsurf" => AppKind.Coding,
            "windowsterminal" or "cmd" or "powershell" or "pwsh" or "wt" or "conhost" => AppKind.Terminal,
            "spotify" or "musicbee" or "foobar2000" or "itunes" or "applemusic" => AppKind.Music,
            "vlc" or "mpc-hc64" or "mpc-hc" or "potplayermini64" or "netflix" or "mpv" => AppKind.Video,
            "discord" or "whatsapp" or "telegram" or "slack" or "ms-teams" or "teams" or "signal" => AppKind.Chat,
            "blender" or "unity" or "unity hub" or "unrealeditor" or "godot" or "maya" or "3dsmax" or "cinema 4d" => AppKind.ThreeD,
            "photoshop" or "krita" or "aseprite" or "gimp-2.10" or "gimp" or "clipstudiopaint" or "illustrator" or "figma" or "paint.net" or "mspaint" => AppKind.Drawing,
            "winword" or "excel" or "powerpnt" or "onenote" or "notion" or "obsidian" or "outlook" => AppKind.Office,
            "explorer" => AppKind.Files,
            "steam" or "steamwebhelper" or "epicgameslauncher" or "lunarclient" or "minecraft" or "javaw" or "curseforge" or "battle.net" => AppKind.Game,
            _ => LooksLikeGame(proc) ? AppKind.Game : AppKind.Unknown
        };
    }

    static bool LooksLikeGame(string proc) =>
        proc.Contains("game") || proc.Contains("-win64-shipping") || proc.EndsWith("_x64") || proc is "terraria" or "ultrakill" or "fivenightsatfreddys" or "hollow_knight" or "valorant" or "fortniteclient-win64-shipping" or "cs2" or "roblox" or "robloxplayerbeta";

    void RefreshWindows()
    {
        scratch.Clear();
        toastsNow.Clear();
        Native.EnumWindows(enumProc, IntPtr.Zero);
        toasts.Clear();
        toasts.UnionWith(toastsNow);
        var platforms = new List<Platform>();
        double scale = Math.Max(0.5, w.PixelScale / 3.0);
        double minWidth = 60 * scale;
        var nowWindows = new HashSet<IntPtr>();
        for (int i = 0; i < scratch.Count; i++)
        {
            var (hwnd, r, title, _) = scratch[i];
            nowWindows.Add(hwnd);
            if (!firstScan && !knownWindows.Contains(hwnd) && r.Width > 200 && r.Height > 150)
                Events.Add(new WorldEvent { Kind = WorldEventKind.WindowOpened, Rect = new RectD(r.Left, r.Top, r.Right, r.Bottom) });
            var screen = w.ScreenAt((r.Left + r.Right) / 2.0, r.Top);
            if (screen == null) continue;
            double y = r.Top;
            if (y < screen.Work.Top + 70 * scale || y > screen.Work.Bottom - 40 * scale) continue;
            if (r.Width < minWidth) continue;
            var segs = new List<(double a, double b)> { (Math.Max(r.Left + 4, screen.Bounds.Left), Math.Min(r.Right - 4, screen.Bounds.Right)) };
            for (int j = 0; j < i && segs.Count > 0; j++)
            {
                var o = scratch[j].r;
                if (o.Top <= y + 2 && o.Bottom >= y - 12 * scale)
                    segs = Subtract(segs, o.Left, o.Right);
            }
            foreach (var (a, b) in segs)
                if (b - a >= 24 * scale)
                    platforms.Add(new Platform { Hwnd = hwnd, Y = y, X1 = a, X2 = b, Owner = title });
        }
        knownWindows = nowWindows;
        firstScan = false;
        foreach (var s in w.Screens)
            platforms.Add(new Platform { Hwnd = IntPtr.Zero, Y = s.Work.Bottom, X1 = s.Work.Left, X2 = s.Work.Right, IsFloor = true, Owner = "floor" });
        MergeFloors(platforms);
        w.Platforms = platforms;
    }

    static void MergeFloors(List<Platform> list)
    {
        var floors = list.Where(p => p.IsFloor).OrderBy(p => p.Y).ThenBy(p => p.X1).ToList();
        for (int i = 0; i < floors.Count; i++)
            for (int j = i + 1; j < floors.Count; j++)
            {
                var a = floors[i];
                var b = floors[j];
                if (a.Y == b.Y && (Math.Abs(a.X2 - b.X1) < 2 || Math.Abs(b.X2 - a.X1) < 2))
                {
                    a.X1 = Math.Min(a.X1, b.X1);
                    a.X2 = Math.Max(a.X2, b.X2);
                    list.Remove(b);
                    floors.RemoveAt(j);
                    j = i;
                }
            }
    }

    static List<(double, double)> Subtract(List<(double a, double b)> segs, double l, double r)
    {
        var res = new List<(double, double)>();
        foreach (var (a, b) in segs)
        {
            if (r <= a || l >= b) { res.Add((a, b)); continue; }
            if (l > a) res.Add((a, l));
            if (r < b) res.Add((r, b));
        }
        return res;
    }

    bool CollectWindow(IntPtr hwnd, IntPtr lp)
    {
        if (ownWindows.Contains(hwnd) || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
        long ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
        if ((ex & Native.WS_EX_TRANSPARENT) != 0 && (ex & Native.WS_EX_LAYERED) != 0) return true;
        if (Native.IsCloaked(hwnd)) return true;
        string title = Native.WindowText(hwnd);
        if (title.Length == 0) return true;
        string cls = Native.ClassName(hwnd);
        if (cls == "Windows.UI.Core.CoreWindow")
        {
            string t = title.ToLowerInvariant();
            if (t.Contains("melding") || t.Contains("notification"))
            {
                var tr = Native.FrameBounds(hwnd);
                if (tr.Width > 100 && tr.Width < 700 && tr.Height > 40 && tr.Height < 500)
                {
                    toastsNow.Add(hwnd);
                    if (!toasts.Contains(hwnd))
                        Events.Add(new WorldEvent { Kind = WorldEventKind.Toast, Rect = new RectD(tr.Left, tr.Top, tr.Right, tr.Bottom) });
                }
            }
            return true;
        }
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == ownPid) return true;
        var r = Native.FrameBounds(hwnd);
        if (r.Width < 40 || r.Height < 30) return true;
        scratch.Add((hwnd, r, title, pid));
        return true;
    }

    public static RECT? WindowRect(IntPtr hwnd)
    {
        if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return null;
        return Native.FrameBounds(hwnd);
    }
}
