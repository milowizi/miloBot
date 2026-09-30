using System.Diagnostics;
using MiloBot.Brain;
using MiloBot.Gfx;
using MiloBot.Vision;
using MiloBot.Win32;
using MiloBot.World;

namespace MiloBot;

public partial class App
{
    string lastRenderKey = "", homeKey = "";
    int winX, winY;

    string CurrentHat()
    {
        if (milo.ShadesUntil > Now) return "shades";
        string h = settings.Hat;
        if (h != "auto") return h == "none" ? null : h;
        var d = DateTime.Now;
        if (d.Month == 12 && d.Day <= 30) return "santa";
        if ((d.Month == 12 && d.Day == 31) || (d.Month == 1 && d.Day == 1)) return "party";
        var met = save.FirstMet;
        if (d.Month == met.Month && d.Day == met.Day && d.Year > met.Year) return "party";
        return null;
    }

    // Where Milo's feet go on the (square) pet canvas for each quarter turn, in sprite pixels.
    (double x, double y) FeetOnCanvas(int quarter)
    {
        double cw = sprites.FrameW + 2 * SideMargin, ch = sprites.FrameH + TopMargin;
        return quarter switch
        {
            1 => (3, ch / 2),
            2 => (cw / 2, 3),
            3 => (cw - 3, ch / 2),
            _ => (SideMargin + sprites.FrameW / 2.0, TopMargin + sprites.Ground)
        };
    }

    // In top view a soft shadow under his paws makes the screen read as a floor.
    void DrawShadow(Bitmap c, double cx, double cy)
    {
        double rx = 13 * scale, ry = 3 * scale;
        cx *= scale;
        cy *= scale;
        uint col = Bitmap.Premultiply(0, 0, 0, 60);
        for (int y = (int)(cy - ry); y <= (int)(cy + ry); y++)
            for (int x = (int)(cx - rx); x <= (int)(cx + rx); x++)
            {
                if (x < 0 || y < 0 || x >= c.Width || y >= c.Height) continue;
                double dx = (x + 0.5 - cx) / rx, dy = (y + 0.5 - cy) / ry;
                if (dx * dx + dy * dy <= 1) c.Pixels[y * c.Width + x] = col;
            }
    }

    const int TrailSize = 640;
    LayeredWindow trailWindow;
    Bitmap pawPrint;
    double nextTrail;

    // The paw prints live in their own click-through window around Milo, redrawn ~20 times a second while they fade.
    void RenderTrail()
    {
        if (!milo.TopDown || milo.Hidden || milo.Prints.Count == 0)
        {
            trailWindow?.Show(false);
            return;
        }
        if (Now < nextTrail) return;
        nextTrail = Now + 0.05;
        if (trailWindow == null)
        {
            trailWindow = new LayeredWindow(TrailSize, TrailSize, true);
            own.Add(trailWindow.Hwnd);
        }
        pawPrint ??= MakePawPrint();
        int ox = (int)Math.Round(milo.Body.X) - TrailSize / 2, oy = (int)Math.Round(milo.Body.Y) - TrailSize / 2;
        var c = trailWindow.Canvas;
        c.Clear();
        int s = scale;
        foreach (var p in milo.Prints)
            c.DrawRotated(pawPrint, 0, 0, pawPrint.Width, p.X - ox, p.Y - oy, p.Angle + Math.PI / 2, s, Math.Min(1, p.Life) * 0.8);
        bool shown = trailWindow.Visible;
        trailWindow.Present(ox, oy);
        if (!shown)
        {
            trailWindow.KeepOnTop();
            petWindow.KeepOnTop();
        }
    }

    static Bitmap MakePawPrint()
    {
        string[] art = { ".x..x.", "......", "x....x", "..xx..", ".xxxx.", ".xxxx." };
        var b = new Bitmap(6, 6);
        uint col = Bitmap.Premultiply(196, 142, 104, 210), edge = Bitmap.Premultiply(70, 42, 28, 190);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
                if (art[y][x] == 'x') b.Pixels[y * 6 + x] = y >= 4 && x is 2 or 3 ? edge : col;
        return b;
    }

    void Render()
    {
        if (milo.Hidden) return;
        int frame = milo.Anim.SheetFrame;
        bool flip = milo.Facing < 0;
        int q = milo.Quarter;
        var feet = FeetOnCanvas(q);
        double lift = q == 0 ? milo.RenderLift : 0;
        int x = (int)Math.Round(milo.Body.X - feet.x * scale);
        int y = (int)Math.Round(milo.Body.Y - (feet.y + lift) * scale);
        var held = toys.InMouth;
        string hat = CurrentHat();
        string key = $"{frame}|{flip}|{q}|{milo.TopDown}|{x}|{y}|{held?.Kind}|{hat}|{milo.Particles.Count}";
        if (key == lastRenderKey && milo.Particles.Count == 0) return;
        lastRenderKey = key;
        winX = x;
        winY = y;
        var c = petWindow.Canvas;
        c.Clear();
        var (fx, fy) = sprites.FrameOrigin(frame);
        double ax = sprites.FrameW / 2.0, ay = sprites.Ground;
        if (milo.TopDown && milo.Posture != Posture.Held) DrawShadow(c, feet.x, feet.y - 0.5);
        c.DrawTurned(sprites.Sheet, fx, fy, sprites.FrameW, sprites.FrameH, ax, ay, feet.x, feet.y, q, scale, flip);
        if (hat != null && sprites.Hats.TryGetValue(hat, out var hatSheet))
            c.DrawTurned(hatSheet, fx, fy, sprites.FrameW, sprites.FrameH, ax, ay, feet.x, feet.y, q, scale, flip);
        var a = sprites.Anchors[frame];
        (double x, double y) OnCanvas(double sx, double sy)
        {
            if (flip) sx = sprites.FrameW - sx;
            var (rx, ry) = Bitmap.Turn(sx - ax, sy - ay, q);
            return (feet.x + rx, feet.y + ry);
        }
        var mouth = OnCanvas(a[0], a[1] + 1);
        var head = OnCanvas(a[2], a[3]);
        // Emotes always float "above" him, which is away from whatever he's standing on.
        var (ux, uy) = Bitmap.Turn(0, -1, q);
        if (q != 0) head = (Math.Clamp(head.x + ux * 6, 8, sprites.FrameW + 2 * SideMargin - 8), Math.Clamp(head.y + uy * 6, 8, sprites.FrameH + TopMargin - 8));
        if (held != null) DrawIcon(c, held.Icon, mouth.x, mouth.y, 1);
        foreach (var p in milo.Particles)
        {
            double age = p.MaxLife - p.Life;
            double alpha = Math.Min(1, age / 0.15) * Math.Min(1, p.Life / 0.4);
            if (p.World)
            {
                DrawIcon(c, p.Icon, (p.X - x) / scale, (p.Y - y) / scale, alpha * 0.9);
                continue;
            }
            if (p.Dream)
            {
                double bx = Math.Clamp(head.x, SideMargin + 16, SideMargin + sprites.FrameW - 16), by = head.y - 10 + Math.Sin(age * 2) * 0.6;
                DrawProp(c, "bubble", bx, by, alpha);
                DrawIcon(c, p.Icon, bx + 1, by - 2, alpha);
                continue;
            }
            double px = head.x + p.X, py = head.y + p.Y - 4;
            if (p.OnHead) py += Math.Sin(age * 6) * 0.8 - 3;
            DrawIcon(c, p.Icon, px, py, alpha);
        }
        petWindow.Present(x, y);
    }

    void DrawIcon(Bitmap c, string icon, double cx, double cy, double alpha)
    {
        if (!sprites.IconIndex.TryGetValue(icon, out int i)) return;
        int s = sprites.IconSize;
        c.Draw(sprites.Icons, i * s, 0, s, s, (int)Math.Round((cx - s / 2.0) * scale), (int)Math.Round((cy - s / 2.0) * scale), scale, false, alpha);
    }

    void DrawProp(Bitmap c, string prop, double cx, double cy, double alpha)
    {
        if (sprites.Props == null || !sprites.PropIndex.TryGetValue(prop, out int i)) return;
        int w = sprites.PropW, h = sprites.PropH;
        c.Draw(sprites.Props, i * w, 0, w, h, (int)Math.Round((cx - w / 2.0) * scale), (int)Math.Round((cy - h / 2.0) * scale), scale, false, alpha);
    }

    void RenderToys()
    {
        foreach (var t in toys.Items)
        {
            if (!toyWindows.TryGetValue(t, out var w))
            {
                w = new LayeredWindow(16 * scale, 16 * scale) { Target = toyMouse };
                own.Add(w.Hwnd);
                toyWindows[t] = w;
            }
            if (t.InMouth || t.Hidden || milo.Hidden || milo.TopDown || world.Blocked(t.Body.X, t.Body.Y - 5)) { w.Show(false); continue; }
            w.Canvas.Clear();
            w.Canvas.Draw(sprites.Icons, sprites.IconIndex[t.Icon] * 16, 0, 16, 16, 0, 0, scale);
            int x = (int)Math.Round(t.Body.X - 8 * scale);
            int y = (int)Math.Round(t.Body.Y - 11 * scale);
            w.Present(x, y);
        }
        foreach (var kv in toyWindows.Where(kv => !toys.Items.Contains(kv.Key)).ToList())
        {
            own.Remove(kv.Value.Hwnd);
            kv.Value.Dispose();
            toyWindows.Remove(kv.Key);
        }
    }

    void RenderHome()
    {
        if (!home.Active || milo.Hidden || milo.TopDown || sprites.Props == null)
        {
            homeWindow?.Show(false);
            return;
        }
        int w = (int)Math.Round(home.Right - home.Left), h = sprites.PropH * scale;
        if (homeWindow == null)
        {
            homeWindow = new LayeredWindow(w, h) { Target = homeMouse };
            own.Add(homeWindow.Hwnd);
        }
        bool full = save.BowlFood >= 0.15;
        string key = $"{w}|{h}|{full}|{home.Left}|{home.Top}";
        if (key == homeKey && homeWindow.Visible) return;
        homeKey = key;
        if (homeWindow.Width != w || homeWindow.Height != h) homeWindow.Resize(w, h);
        var c = homeWindow.Canvas;
        c.Clear();
        int pw = sprites.PropW;
        c.Draw(sprites.Props, sprites.PropIndex[full ? "bowl_full" : "bowl_empty"] * pw, 0, pw, sprites.PropH, 0, 0, scale);
        c.Draw(sprites.Props, sprites.PropIndex["bed"] * pw, 0, pw, sprites.PropH, (int)Math.Round(home.BedX - 16 * scale - home.Left), 0, scale);
        homeWindow.Present((int)Math.Round(home.Left), (int)Math.Round(home.Top));
    }

    void RenderCritters()
    {
        foreach (var cr in critters.Items)
        {
            if (!critterWindows.TryGetValue(cr, out var w))
            {
                w = new LayeredWindow(16 * scale, 16 * scale) { Target = critterMouse };
                own.Add(w.Hwnd);
                critterWindows[cr] = w;
            }
            if (milo.Hidden || milo.TopDown || world.Blocked(cr.X, cr.Y - 5) || sprites.CritterSheet == null || !sprites.CritterIndex.TryGetValue(cr.Frame, out int i)) { w.Show(false); continue; }
            int cs = sprites.CritterSize;
            w.Canvas.Clear();
            w.Canvas.Draw(sprites.CritterSheet, i * cs, 0, cs, cs, 0, 0, scale, cr.Facing < 0);
            w.Present((int)Math.Round(cr.X - cs / 2.0 * scale), (int)Math.Round(cr.Y - (cs - 1) * scale));
        }
        foreach (var kv in critterWindows.Where(kv => !critters.Items.Contains(kv.Key)).ToList())
        {
            own.Remove(kv.Value.Hwnd);
            kv.Value.Dispose();
            critterWindows.Remove(kv.Key);
        }
    }

    void RenderLaser()
    {
        var l = milo.Laser;
        if (!l.On)
        {
            laserWindow?.Show(false);
            return;
        }
        if (laserWindow == null)
        {
            laserWindow = new LayeredWindow(16 * scale, 16 * scale, true);
            own.Add(laserWindow.Hwnd);
        }
        var c = laserWindow.Canvas;
        c.Clear();
        double pulse = 0.75 + 0.25 * Math.Sin(Now * 14);
        c.Draw(sprites.Icons, sprites.IconIndex["laser"] * 16, 0, 16, 16, 0, 0, scale, false, pulse);
        laserWindow.Present((int)Math.Round(l.X - 8 * scale), (int)Math.Round(l.Y - 8 * scale));
    }

    void TakePhoto()
    {
        try
        {
            int pad = 40 * scale;
            var win = new RectD(winX - pad, winY - pad / 2, winX + petWindow.Width + pad, winY + petWindow.Height + pad / 2);
            var s = world.ScreenAt(milo.Body.X, milo.Body.Y - 10);
            if (s != null)
                win = new RectD(Math.Max(win.Left, s.Bounds.Left), Math.Max(win.Top, s.Bounds.Top), Math.Min(win.Right, s.Bounds.Right), Math.Min(win.Bottom, s.Bounds.Bottom));
            int w = (int)win.Width, h = (int)win.Height;
            var cap = ScreenCapture.Grab(win, w, h);
            var px = cap.Bgra;
            var mc = petWindow.Canvas;
            int ox = winX - (int)win.Left, oy = winY - (int)win.Top;
            for (int y = 0; y < mc.Height; y++)
            {
                int ty = y + oy;
                if (ty < 0 || ty >= h) continue;
                for (int x = 0; x < mc.Width; x++)
                {
                    int tx = x + ox;
                    if (tx < 0 || tx >= w) continue;
                    uint p = mc.Pixels[y * mc.Width + x];
                    uint a = p >> 24;
                    if (a == 0) continue;
                    int o = (ty * w + tx) * 4;
                    uint inv = 255 - a;
                    px[o] = (byte)((p & 0xFF) + px[o] * inv / 255);
                    px[o + 1] = (byte)(((p >> 8) & 0xFF) + px[o + 1] * inv / 255);
                    px[o + 2] = (byte)(((p >> 16) & 0xFF) + px[o + 2] * inv / 255);
                }
            }
            int border = 6 * scale / 3 + 4, bottom = border * 4;
            int fw = w + border * 2, fh = h + border + bottom;
            var framed = new byte[fw * fh * 4];
            for (int i = 0; i < framed.Length; i += 4) { framed[i] = 245; framed[i + 1] = 248; framed[i + 2] = 250; framed[i + 3] = 255; }
            for (int y = 0; y < h; y++)
                Array.Copy(px, y * w * 4, framed, ((y + border) * fw + border) * 4, w * 4);
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Milo");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"milo_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
            File.WriteAllBytes(file, Png.EncodeRgb(framed, fw, fh));
            sound.Play("shutter", Now);
            milo.Float("sparkle");
            Log.Write("photo saved: " + file);
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Write("photo failed: " + e.Message);
        }
    }
}
