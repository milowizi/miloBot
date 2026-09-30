using System.Diagnostics;
using Microsoft.Win32;
using MiloBot.Audio;
using MiloBot.Brain;
using MiloBot.Gfx;
using MiloBot.Vision;
using MiloBot.Win32;
using MiloBot.World;

namespace MiloBot;

public partial class App : IMouseTarget
{
    const int TopMargin = 24, SideMargin = 12;
    readonly Settings settings = Settings.Load();
    readonly SaveData save = SaveData.Load();
    readonly string assets = Path.Combine(AppContext.BaseDirectory, "assets");
    readonly WorldState world = new();
    readonly HashSet<IntPtr> own = new();
    readonly Toys toys = new();
    readonly Critters critters = new();
    readonly Home home = new();
    readonly Hearing hearing = new();
    readonly Dictionary<Toy, LayeredWindow> toyWindows = new();
    readonly Dictionary<Critter, LayeredWindow> critterWindows = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    Sprites sprites;
    Sensor sensor;
    Sound sound;
    Milo milo;
    Eyes eyes;
    readonly ContentScanner scanner = new();
    LayeredWindow petWindow, homeWindow, laserWindow;
    Host host;
    int scale;
    bool running = true;
    double hiddenSince = -1;
    double nextTopmost, nextTip, nextLog, nextSave, nextPerf, photoAt = -1;
    TimeSpan lastCpu;
    double lastPerfT;
    int screenSignature;

    public void Run()
    {
        sprites = Sprites.Load(assets);
        sound = new Sound(Path.Combine(assets, "sounds"), settings.Volume) { Enabled = settings.Sound };
        host = new Host(Path.Combine(assets, "milo.ico"));
        own.Add(host.Hwnd);
        host.TrayClicked = ShowMenu;
        host.DisplayChanged = () => sensor.ForceRefresh();
        host.Key = (vk, down) => hearing.Key(vk, down, Now);
        host.MouseButtons = b => hearing.Mouse(b, Now);
        host.WindowFlashed = h => sensor.Flashed(h);
        hearing.Enabled = settings.Hearing;
        sensor = new Sensor(world, own) { ContentOf = scanner.Get };
        milo = new Milo(sprites, world, sound, toys)
        {
            WindowRect = WindowRect,
            Save = save,
            Home = home,
            Critters = critters,
            BreakReminder = settings.BreakReminder,
            BreakMinutes = settings.BreakMinutes,
            Mischief = settings.Mischief,
            ContentPlay = settings.ContentPlay
        };
        home.Enabled = settings.ShowHome;
        critters.Arrived = c => milo.OnCritter(c);
        eyes = new Eyes(settings);
        milo.Memory.Cool("nudge", 600, 0);
        milo.Memory.Cool("batcursor", 240, 0);
        milo.Memory.Cool("crawl", 90, 0);
        toyMouse = new ToyMouse(this);
        homeMouse = new HomeMouse(this);
        critterMouse = new CritterMouse(this);
        save.RollDay();
        ApplyScale();
        sensor.Update(Now);
        sensor.ForceRefresh();
        sensor.Update(Now + 0.001);
        home.Place(world, scale);
        milo.Spawn();
        if (settings.StartWithWindows) SetAutostart(true);
        Log.Write($"started, scale {scale}, dpi {world.DpiScale:0.00}, friendship {save.Friendship:0} (lvl {save.Level}), screens: " + string.Join(" ", world.Screens.Select(s => $"[{s.Bounds.Left},{s.Bounds.Top},{s.Bounds.Right},{s.Bounds.Bottom} work {s.Work.Bottom}]")));
        lastCpu = Process.GetCurrentProcess().TotalProcessorTime;

        double last = Now;
        while (running)
        {
            while (Native.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, Native.PM_REMOVE))
            {
                if (msg.Message == Native.WM_QUIT) { running = false; break; }
                Native.TranslateMessage(ref msg);
                Native.DispatchMessageW(ref msg);
            }
            if (!running) break;
            double now = Now;
            double dt = Math.Clamp(now - last, 0, 0.05);
            last = now;
            try
            {
                Tick(now, dt);
            }
            catch (Exception e)
            {
                Log.Write("tick error: " + e);
                Thread.Sleep(200);
            }
            double spent = Now - now;
            uint wait = (uint)Math.Max(1, (1.0 / 60 - spent) * 1000);
            Native.MsgWaitForMultipleObjects(0, IntPtr.Zero, false, wait, Native.QS_ALLINPUT);
        }
        Shutdown();
    }

    double Now => clock.Elapsed.TotalSeconds;

    void ApplyScale()
    {
        int baseScale = settings.Size switch { 1 => 2, 3 => 4, _ => 3 };
        scale = Math.Max(1, (int)Math.Round(baseScale * Math.Max(1, world.DpiScale)));
        world.PixelScale = scale;
        milo.Scale = scale;
        milo.UpdateBodySize();
        int w = (sprites.FrameW + 2 * SideMargin) * scale, h = (sprites.FrameH + TopMargin) * scale;
        if (petWindow == null)
        {
            petWindow = new LayeredWindow(w, h) { Target = this };
            own.Add(petWindow.Hwnd);
        }
        else petWindow.Resize(w, h);
        foreach (var kv in toyWindows) kv.Value.Resize(16 * scale, 16 * scale);
        foreach (var kv in critterWindows) kv.Value.Resize(16 * scale, 16 * scale);
        foreach (var t in toys.Items)
        {
            t.Body.HalfWidth = 4 * scale;
            t.Body.Height = 7 * scale;
        }
        laserWindow?.Resize(16 * scale, 16 * scale);
        home.Place(world, scale);
        homeKey = "";
        lastRenderKey = "";
    }

    RectD? WindowRect(IntPtr hwnd)
    {
        var r = Sensor.WindowRect(hwnd);
        if (r == null) return null;
        var v = r.Value;
        return new RectD(v.Left, v.Top, v.Right, v.Bottom);
    }

    void Tick(double now, double dt)
    {
        double oldDpi = world.DpiScale;
        sensor.Update(now);
        if (Math.Abs(world.DpiScale - oldDpi) > 0.01) ApplyScale();
        int sig = world.Screens.Aggregate(17, (h, s) => h * 31 + (int)s.Work.Bottom * 7 + (int)s.Work.Right);
        if (sig != screenSignature) { screenSignature = sig; home.Place(world, scale); homeKey = ""; }
        foreach (var e in sensor.Events)
        {
            if (e.Kind == WorldEventKind.FullscreenChanged) continue;
            milo.OnWorld(e);
        }
        UpdateHearing(now);
        if (world.AudioStartle) milo.Startle(true);
        UpdateHidden(now);
        if (dragging) milo.HoldAt(world.CursorX, world.CursorY);
        if (draggedToy != null)
        {
            draggedToy.Body.X = world.CursorX;
            draggedToy.Body.Y = world.CursorY + 4 * scale;
        }
        UpdateTug();
        UpdateLaser(now);
        milo.Update(dt);
        toys.Update(world, dt, scale, WindowRect);
        critters.Update(world, milo, dt, scale, settings.Critters && !milo.Hidden);
        eyes.Update(now, world, milo, IgnoreRects());
        scanner.Standing = milo.Body.OnGround ? milo.Body.Platform?.Ledge : null;
        scanner.Update(now, world, settings.ContentPlay && !milo.Hidden, scanner.Standing != null, IgnoreRects());
        if (scanner.NewPictures.Count > 0)
        {
            milo.OnNewPicture(scanner.NewPictures[0]);
            scanner.NewPictures.Clear();
        }
        sensor.ConsumeAudioFlags();
        world.AudioOnset = false;
        Render();
        RenderToys();
        RenderHome();
        RenderCritters();
        RenderLaser();
        if (photoAt > 0 && now >= photoAt) { photoAt = -1; TakePhoto(); }
        if (now >= nextTopmost)
        {
            nextTopmost = now + 2;
            homeWindow?.KeepOnTop();
            foreach (var w in toyWindows.Values) if (w.Visible) w.KeepOnTop();
            foreach (var w in critterWindows.Values) if (w.Visible) w.KeepOnTop();
            if (!milo.Hidden) petWindow.KeepOnTop();
            if (laserWindow != null && laserWindow.Visible) laserWindow.KeepOnTop();
        }
        if (now >= nextSave)
        {
            nextSave = now + 60;
            save.Store();
        }
        if (now >= nextLog)
        {
            nextLog = now + (now < 180 ? 10 : 60);
            Log.Write($"state {milo.Current?.Name ?? "-"} {milo.Posture} {milo.Anim.Name} at {milo.Body.X:0},{milo.Body.Y:0} on={milo.Body.Platform} hidden={milo.Hidden} fg={world.ForegroundProcess}/{world.ForegroundKind} E={milo.Energy:0.00} H={milo.Happiness:0.00} hunger={save.Hunger:0.00} friend={save.Friendship:0} vision={eyes.Status}");
        }
        if (now >= nextPerf)
        {
            nextPerf = now + 60;
            var proc = Process.GetCurrentProcess();
            var cpu = proc.TotalProcessorTime;
            double span = now - lastPerfT;
            if (lastPerfT > 0 && span > 1)
            {
                double core = (cpu - lastCpu).TotalSeconds / span * 100;
                Log.Write($"perf cpu={core / Environment.ProcessorCount:0.00}% of pc ({core:0.0}% of 1 core), ram={proc.WorkingSet64 / 1048576}MB");
            }
            lastCpu = cpu;
            lastPerfT = now;
        }
        if (settings.Debug && now >= nextCommand)
        {
            nextCommand = now + 1;
            RunDebugCommand();
        }
        if (now >= nextTip)
        {
            nextTip = now + 1.5;
            host.SetTip($"Milo - {Describe()}\nEnergie {milo.Energy * 100:0}% | Blij {milo.Happiness * 100:0}% | Honger {save.Hunger * 100:0}%\nVisie: {eyes.Status}");
        }
    }

    void UpdateHearing(double now)
    {
        hearing.Update(now);
        world.KeysPerSecond = hearing.KeysPerSecond;
        foreach (var h in hearing.Events) milo.OnHeard(h.Id, h.Addressed);
        hearing.Events.Clear();
        if (hearing.BackspaceSpam) milo.OnFrustrated();
        if (hearing.RageClicks && !pressed) milo.OnRageClicks();
        if (hearing.ScreenshotKey) milo.OnScreenshotKey();
        hearing.BackspaceSpam = hearing.RageClicks = hearing.ScreenshotKey = false;
        if (sensor.Events.Any(e => e.Kind == WorldEventKind.ForegroundChanged)) hearing.Clear();
    }

    void UpdateLaser(double now)
    {
        var l = milo.Laser;
        if (!l.On) return;
        l.X = world.CursorX;
        l.Y = world.CursorY;
        if (now - l.StartedAt > 120 || milo.Hidden || world.Blocked(l.X, l.Y)) l.On = false;
    }

    string Describe()
    {
        if (milo.Hidden) return "slaapt (fullscreen)";
        return milo.Current switch
        {
            null => "chillt",
            Nap => "slaapt",
            Fetch => "speelt met de bal",
            Eat => "eet een snoepje",
            Meal => "eet uit zijn bak",
            Petted => "wordt geaaid",
            Held => "wordt vastgehouden",
            Tug => "trekt aan het speeltje",
            LaserChase => "jaagt op het laserpuntje",
            Hunt h => h.Name.EndsWith("Bird") ? "besluipt een vogel" : "jaagt op een vlinder",
            TrickBehavior => "doet een trucje",
            _ => milo.Current.Name.Split('/', ':')[0].ToLowerInvariant() switch
            {
                "wander" => "wandelt rond",
                "explore" => "gaat op ontdekking",
                "loaf" or "rest" => "rust uit",
                "nap" => "gaat slapen",
                "seekuser" => "komt naar jou",
                "zoomies" => "heeft de zoomies",
                "dance" => "danst",
                "watch" => "kijkt mee",
                "greet" => "zegt hallo",
                "chase" => "jaagt op je muis",
                "scared" => "is bang",
                "tailchase" => "bijt in zijn staart",
                "howl" => "huilt awoo",
                "gift" => "brengt je een cadeautje",
                "tidy" => "ruimt speelgoed op",
                "leash" => "wil wandelen",
                "edgescratch" => "krabt aan de rand",
                "butterflynose" => "heeft een vlinder op zijn neus",
                "ride" => "surft op je venster",
                "afterride" => "komt bij van het surfen",
                "perch" => "loert over de rand",
                "dizzy" => "is duizelig",
                "lookback" => "zoekt waar zijn venster heen is",
                "squirrel" => "EEKHOORN!",
                "telluser" => "zegt dat je een bericht hebt",
                "toast" => "bekijkt een melding",
                "typingbuddy" => "houdt je gezelschap",
                "proud" => "is trots",
                "tiltat" => "kijkt je vragend aan",
                "wherecat" => "zoekt de kat",
                "playbow" => "wil spelen",
                "photo" => "poseert",
                "crawl" => "kruipt over de rand van je scherm",
                "content" => "bekijkt iets in je app",
                "nudge" => "trekt aan een venster",
                "batcursor" => "loert op je muis",
                "staretcursor" or "stareatcursor" => "staart naar je muis",
                var s when s.StartsWith("investigate") => "onderzoekt iets",
                var s => s
            }
        };
    }

    List<RectD> IgnoreRects()
    {
        var list = new List<RectD>
        {
            Grow(milo.HitRect(), 6 * scale, 12 * scale)
        };
        foreach (var t in toys.Items)
            list.Add(new RectD(t.Body.X - 8 * scale, t.Body.Y - 12 * scale, t.Body.X + 8 * scale, t.Body.Y + 2 * scale));
        foreach (var c in critters.Items)
            list.Add(new RectD(c.X - 8 * scale, c.Y - 16 * scale, c.X + 8 * scale, c.Y + 2 * scale));
        if (home.Active) list.Add(home.Rect);
        return list;
    }

    double nextCommand;

    // Only with "Debug": true in settings.json. Write a command into command.txt next to it to make Milo do
    // something right away (handy for testing): crawl, nudge, bat, content, state.
    void RunDebugCommand()
    {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiloBot", "command.txt");
        if (!File.Exists(file)) return;
        string cmd;
        try { cmd = File.ReadAllText(file).Trim().ToLowerInvariant(); File.Delete(file); }
        catch { return; }
        Log.Write("debug command: " + cmd);
        switch (cmd)
        {
            case "crawl": milo.StartCrawl(); break;
            case "nudge":
                var p = world.Platforms.FirstOrDefault(q => !q.IsFloor && !q.IsContent && !Native.IsZoomed(q.Hwnd));
                var b = p == null ? null : WindowNudge.TryMake(milo, p);
                if (b != null) milo.Set(b); else Log.Write("nothing to nudge");
                break;
            case "bat": milo.Set(new CursorBat(milo)); break;
            case "content":
                var c = milo.ContentHere().ToList();
                Log.Write("content here: " + string.Join(" ", c.Select(x => x.ToString())));
                if (c.Count > 0) milo.Set(ContentPlans.Visit(milo, c[Random.Shared.Next(c.Count)]));
                break;
            case "state":
                Log.Write($"state {milo.Current?.Name ?? "-"} {milo.Posture} q={milo.Quarter} at {milo.Body.X:0},{milo.Body.Y:0} on={milo.Body.Platform} platforms={world.Platforms.Count} content={world.Platforms.Count(x => x.IsContent)}");
                break;
        }
    }

    static RectD Grow(RectD r, double dx, double dy) => new(r.Left - dx, r.Top - dy, r.Right + dx, r.Bottom + dy);

    void UpdateHidden(double now)
    {
        bool hide = false;
        World.Screen game = null;
        if (settings.HideInFullscreen && world.ForegroundFullscreen && world.ForegroundKind != AppKind.Desktop)
            game = world.ScreenAt(world.ForegroundRect.CenterX, world.ForegroundRect.CenterY);
        // With more than one screen the game's screen is simply off-limits and Milo keeps you company on the other one.
        var blocked = game != null && world.Screens.Count > 1 ? game : null;
        if (blocked != world.BlockedScreen)
        {
            world.BlockedScreen = blocked;
            sensor.ForceRefresh();
            home.Blocked = blocked != null && blocked.Bounds.Contains(home.BedX, home.Y - 5);
            homeKey = "";
        }
        if (game != null)
        {
            var ms = world.ScreenAt(milo.Body.X, milo.Body.Y - 10);
            hide = game == ms;
            if (hide && blocked != null && !dragging && milo.Posture != Posture.Held)
            {
                var floor = world.Platforms.Where(p => p.IsFloor && !blocked.Bounds.Contains(p.X1 + 1, p.Y - 5))
                    .OrderBy(p => Math.Min(Math.Abs(p.X1 - milo.Body.X), Math.Abs(p.X2 - milo.Body.X))).FirstOrDefault();
                if (floor != null)
                {
                    double edge = Math.Abs(floor.X2 - milo.Body.X) < Math.Abs(floor.X1 - milo.Body.X) ? floor.X2 - 30 * scale : floor.X1 + 30 * scale;
                    milo.MoveTo(floor, edge);
                    Log.Write($"moved to the other screen for fullscreen {world.ForegroundProcess}");
                    hide = false;
                }
            }
        }
        if (hide && !milo.Hidden)
        {
            Log.Write($"hiding for fullscreen {world.ForegroundProcess}");
            milo.Hidden = true;
            hiddenSince = now;
            petWindow.Show(false);
            foreach (var w in toyWindows.Values) w.Show(false);
            foreach (var w in critterWindows.Values) w.Show(false);
            homeWindow?.Show(false);
            if (dragging) EndDrag(false);
        }
        else if (!hide && milo.Hidden)
        {
            milo.Hidden = false;
            milo.OnShown(now - hiddenSince);
            lastRenderKey = "";
            homeKey = "";
        }
    }

    static void SetAutostart(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (on) key.SetValue("MiloBot", "\"" + Environment.ProcessPath + "\"");
            else key.DeleteValue("MiloBot", false);
        }
        catch (Exception e)
        {
            Log.Write("autostart: " + e.Message);
        }
    }

    void Shutdown()
    {
        save.Store();
        foreach (var w in toyWindows.Values) w.Dispose();
        foreach (var w in critterWindows.Values) w.Dispose();
        homeWindow?.Dispose();
        laserWindow?.Dispose();
        petWindow?.Dispose();
        host?.Dispose();
        sound?.Dispose();
        Log.Write("stopped");
    }
}
