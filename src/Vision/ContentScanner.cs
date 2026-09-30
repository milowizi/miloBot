using MiloBot.World;

namespace MiloBot.Vision;

/// <summary>
/// Looks at the pixels of the window you're using and finds the things in it: pictures, panels/buttons and blocks
/// of text. Their top edges become ledges Milo can stand on. No model involved, it's plain pixel counting on a
/// small screenshot, about once a second, so it costs next to nothing.
/// </summary>
public class ContentScanner
{
    class Found
    {
        public string Kind;
        public RectD R;
    }

    readonly Dictionary<IntPtr, WindowContent> cache = new();
    Task<List<Found>> pending;
    IntPtr pendingHwnd, lastHwnd;
    RectD pendingRect, lastRect;
    double nextScan;
    long nextId = 1;
    public readonly List<RectD> NewPictures = new();
    public string Summary { get; private set; } = "";
    public ContentLedge Standing;

    public WindowContent Get(IntPtr hwnd) => cache.TryGetValue(hwnd, out var c) ? c : null;

    public void Update(double now, WorldState w, bool enabled, bool hurry, List<RectD> ignore)
    {
        if (pending != null && pending.IsCompleted)
        {
            if (pending.IsCompletedSuccessfully) Merge(pendingHwnd, pendingRect, pending.Result, now);
            else Log.Write("content scan failed: " + pending.Exception?.GetBaseException().Message);
            pending = null;
        }
        if (!enabled)
        {
            cache.Clear();
            return;
        }
        if (pending != null || now < nextScan) return;
        nextScan = now + (hurry ? 0.7 : 1.4);
        foreach (var h in cache.Keys.Where(h => !Win32.Native.IsWindow(h) || !Win32.Native.IsWindowVisible(h)).ToList()) cache.Remove(h);
        var hwnd = w.ForegroundHwnd;
        var r = w.ForegroundRect;
        if (hwnd == IntPtr.Zero || w.ForegroundFullscreen || w.ForegroundKind is AppKind.Desktop or AppKind.Game) return;
        if (r.Width < 300 || r.Height < 200) return;
        var screen = w.ScreenAt(r.CenterX, r.CenterY);
        if (screen == null || screen == w.BlockedScreen) return;
        // Wait until a window stops moving before looking inside it.
        bool moved = hwnd != lastHwnd || Math.Abs(r.Left - lastRect.Left) > 1 || Math.Abs(r.Top - lastRect.Top) > 1 || Math.Abs(r.Width - lastRect.Width) > 1 || Math.Abs(r.Height - lastRect.Height) > 1;
        lastHwnd = hwnd;
        lastRect = r;
        if (moved) { nextScan = now + 0.4; return; }
        var clip = new RectD(Math.Max(r.Left, screen.Bounds.Left), Math.Max(r.Top, screen.Bounds.Top), Math.Min(r.Right, screen.Bounds.Right), Math.Min(r.Bottom, screen.Bounds.Bottom));
        if (clip.Width < 200 || clip.Height < 150) return;
        double f = Math.Min(1.0, Math.Min(960 / clip.Width, 720 / clip.Height));
        int cw = Math.Max(40, (int)(clip.Width * f)), ch = Math.Max(30, (int)(clip.Height * f));
        double ox = clip.Left - r.Left, oy = clip.Top - r.Top;
        double sx = clip.Width / cw, sy = clip.Height / ch;
        pendingHwnd = hwnd;
        pendingRect = r;
        // Milo, his toys and critters show up in screenshots; blank them out so he doesn't stand on himself.
        var mask = ignore.Select(i => new RectD((i.Left - clip.Left) / sx, (i.Top - clip.Top) / sy, (i.Right - clip.Left) / sx, (i.Bottom - clip.Top) / sy)).ToList();
        pending = Task.Run(() => Analyze(ScreenCapture.Grab(clip, cw, ch), sx, sy, ox, oy, r.Width, r.Height, mask));
    }

    // ------------------------------------------------------------------ pixel analysis

    const int Cell = 4;

    static List<Found> Analyze(Capture cap, double sx, double sy, double ox, double oy, double winW, double winH, List<RectD> mask)
    {
        int w = cap.Width, h = cap.Height;
        var px = cap.Bgra;

        // The page background is the most common colour.
        var hist = new int[4096];
        for (int i = 0; i < w * h; i += 2)
        {
            int o = i * 4;
            hist[(px[o + 2] >> 4 << 8) | (px[o + 1] >> 4 << 4) | (px[o] >> 4)]++;
        }
        int bgBin = Array.IndexOf(hist, hist.Max());
        int bgR = ((bgBin >> 8) & 15) * 16 + 8, bgG = ((bgBin >> 4) & 15) * 16 + 8, bgB = (bgBin & 15) * 16 + 8;

        int gw = w / Cell, gh = h / Cell;
        var ink = new byte[gw * gh];
        for (int cy = 0; cy < gh; cy++)
            for (int cx = 0; cx < gw; cx++)
            {
                int n = 0;
                for (int y = cy * Cell; y < cy * Cell + Cell; y++)
                    for (int x = cx * Cell; x < cx * Cell + Cell; x++)
                    {
                        int o = (y * w + x) * 4;
                        int d = Math.Max(Math.Abs(px[o + 2] - bgR), Math.Max(Math.Abs(px[o + 1] - bgG), Math.Abs(px[o] - bgB)));
                        if (d > 30) n++;
                    }
                ink[cy * gw + cx] = (byte)n;
            }
        foreach (var m in mask)
            for (int cy = Math.Max(0, (int)(m.Top / Cell)); cy < Math.Min(gh, (int)Math.Ceiling(m.Bottom / Cell)); cy++)
                for (int cx = Math.Max(0, (int)(m.Left / Cell)); cx < Math.Min(gw, (int)Math.Ceiling(m.Right / Cell)); cx++)
                    ink[cy * gw + cx] = 0;

        // Group cells: words on a line join up (gap up to 2 cells), lines join into paragraphs (next row).
        var parent = new int[gw * gh];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
        bool On(int cx, int cy) => cx >= 0 && cy >= 0 && cx < gw && cy < gh && ink[cy * gw + cx] >= 3;
        for (int cy = 0; cy < gh; cy++)
            for (int cx = 0; cx < gw; cx++)
            {
                if (!On(cx, cy)) continue;
                int i = cy * gw + cx;
                for (int dx = 1; dx <= 2; dx++) if (On(cx + dx, cy)) { Union(i, cy * gw + cx + dx); break; }
                for (int dx = -1; dx <= 1; dx++) if (On(cx + dx, cy + 1)) Union(i, (cy + 1) * gw + cx + dx);
            }

        var boxes = new Dictionary<int, (int x0, int y0, int x1, int y1, int cells, int inkSum)>();
        for (int cy = 0; cy < gh; cy++)
            for (int cx = 0; cx < gw; cx++)
            {
                if (!On(cx, cy)) continue;
                int root = Find(cy * gw + cx);
                var b = boxes.TryGetValue(root, out var v) ? v : (x0: cx, y0: cy, x1: cx, y1: cy, cells: 0, inkSum: 0);
                boxes[root] = (Math.Min(b.x0, cx), Math.Min(b.y0, cy), Math.Max(b.x1, cx), Math.Max(b.y1, cy), b.cells + 1, b.inkSum + ink[cy * gw + cx]);
            }

        var found = new List<Found>();
        double winArea = winW * winH;
        foreach (var b in boxes.Values)
        {
            int bw = (b.x1 - b.x0 + 1) * Cell, bh = (b.y1 - b.y0 + 1) * Cell;
            var rect = new RectD(ox + b.x0 * Cell * sx, oy + b.y0 * Cell * sy, ox + (b.x1 + 1) * Cell * sx, oy + (b.y1 + 1) * Cell * sy);
            if (rect.Width < 60 || rect.Height < 10) continue;
            if (rect.Width * rect.Height > winArea * 0.45) continue;
            double fill = (double)b.cells / ((b.x1 - b.x0 + 1) * (b.y1 - b.y0 + 1));
            double density = b.inkSum / (double)(b.cells * Cell * Cell);
            var (colors, colorful) = Palette(px, w, b.x0 * Cell, b.y0 * Cell, bw, bh);
            string kind;
            if (fill > 0.8 && density > 0.75 && rect.Width >= 80 && rect.Height >= 60 && (colors >= 45 || colorful > 0.18)) kind = "picture";
            else if (fill > 0.85 && density > 0.8 && colors < 45) kind = "box";
            else if (fill > 0.2 && colors < 60 && density < 0.75) kind = rect.Height >= 28 ? "text" : "line";
            else continue;
            found.Add(new Found { Kind = kind, R = rect });
        }
        return found;
    }

    // How many different colours a block has (pictures have lots, text and buttons very few) and how saturated it is.
    static (int colors, double colorful) Palette(byte[] px, int w, int x0, int y0, int bw, int bh)
    {
        var seen = new bool[512];
        int count = 0, n = 0;
        double sat = 0;
        int step = Math.Max(1, Math.Max(bw, bh) / 40);
        for (int y = y0; y < y0 + bh; y += step)
            for (int x = x0; x < x0 + bw; x += step)
            {
                int o = (y * w + x) * 4;
                int r = px[o + 2], g = px[o + 1], b = px[o];
                int bin = (r >> 5 << 6) | (g >> 5 << 3) | (b >> 5);
                if (!seen[bin]) { seen[bin] = true; count++; }
                sat += (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) / 255.0;
                n++;
            }
        return (count, n > 0 ? sat / n : 0);
    }

    // ------------------------------------------------------------------ keeping ledges stable between scans

    void Merge(IntPtr hwnd, RectD winRect, List<Found> found, double now)
    {
        if (!cache.TryGetValue(hwnd, out var wc) || Math.Abs(wc.Width - winRect.Width) > 2 || Math.Abs(wc.Height - winRect.Height) > 2)
        {
            wc = new WindowContent { Hwnd = hwnd, Width = winRect.Width, Height = winRect.Height };
            cache[hwnd] = wc;
            if (cache.Count > 4) cache.Remove(cache.Values.OrderBy(c => c.ScannedAt).First().Hwnd);
        }
        wc.ScannedAt = now;
        var ledges = found.Where(x => x.Kind != "line" && x.R.Top > 40 && x.R.Top < winRect.Height - 30 && x.R.Width >= 90)
            .OrderBy(x => x.Kind switch { "picture" => 0, "box" => 1, _ => 2 }).ThenByDescending(x => x.R.Width)
            .ToList();
        // Two things at nearly the same height and overlapping: keep one ledge.
        var keep = new List<Found>();
        foreach (var x in ledges)
            if (!keep.Any(k => Math.Abs(k.R.Top - x.R.Top) < 10 && k.R.Left < x.R.Right && x.R.Left < k.R.Right) && keep.Count < 18)
                keep.Add(x);

        var old = wc.Ledges;
        var matched = new HashSet<ContentLedge>();
        var fresh = new List<Found>();
        foreach (var x in keep)
        {
            // Milo himself is in the screenshot and can blur the edge he stands on, so that one gets some slack.
            var m = old.Where(l => !matched.Contains(l) && (l == Standing || l.Kind == x.Kind) && (l == Standing
                                ? Math.Abs(l.X1 - x.R.Left) < 30 && Math.Abs(l.X2 - x.R.Right) < 30 && Math.Abs(l.Y - x.R.Top) < 14
                                : Math.Abs(l.X1 - x.R.Left) < 8 && Math.Abs(l.X2 - x.R.Right) < 8 && Math.Abs(l.Y - x.R.Top) < 6))
                       .OrderBy(l => Math.Abs(l.Y - x.R.Top)).FirstOrDefault();
            if (m == null) { fresh.Add(x); continue; }
            matched.Add(m);
            m.Hits++;
            m.Misses = 0;
            if (m != Standing) { if (Math.Abs(m.Y - x.R.Top) > 2) m.Y = x.R.Top; m.Block = x.R; }
        }
        // The page scrolled: the same things came back higher or lower. Move those ledges (Milo falls off if he was on one).
        var votes = new Dictionary<int, int>();
        foreach (var x in fresh)
            foreach (var l in old.Where(l => !matched.Contains(l) && l.Kind == x.Kind && Math.Abs(l.X1 - x.R.Left) < 6 && Math.Abs(l.X2 - x.R.Right) < 6))
            {
                int dy = (int)Math.Round((x.R.Top - l.Y) / 4) * 4;
                votes[dy] = votes.GetValueOrDefault(dy) + 1;
            }
        if (votes.Count > 0)
        {
            var best = votes.OrderByDescending(v => v.Value).First();
            if (best.Value >= 1 && best.Key != 0)
                foreach (var x in fresh.ToList())
                {
                    var l = old.FirstOrDefault(l => !matched.Contains(l) && l.Kind == x.Kind && Math.Abs(l.X1 - x.R.Left) < 6 && Math.Abs(l.X2 - x.R.Right) < 6 && Math.Abs(x.R.Top - l.Y - best.Key) < 6);
                    if (l == null) continue;
                    matched.Add(l);
                    fresh.Remove(x);
                    l.Y = x.R.Top;
                    l.Block = x.R;
                    l.Misses = 0;
                }
        }
        foreach (var l in old.Where(l => !matched.Contains(l)))
            if (++l.Misses >= (l == Standing ? 4 : 2)) l.Alive = false;
        old.RemoveAll(l => !l.Alive);
        foreach (var x in fresh)
            old.Add(new ContentLedge { Id = nextId++, Kind = x.Kind, X1 = x.R.Left, X2 = x.R.Right, Y = x.R.Top, Block = x.R, Hits = 1 });
        foreach (var l in old.Where(l => l.Kind == "picture" && l.Hits == 2))
            NewPictures.Add(new RectD(winRect.Left + l.Block.Left, winRect.Top + l.Block.Top, winRect.Left + l.Block.Right, winRect.Top + l.Block.Bottom));
        string summary = $"{old.Count(l => l.Kind == "picture")} plaatjes, {old.Count(l => l.Kind == "box")} vakjes, {old.Count(l => l.Kind == "text")} tekstblokken";
        if (summary != Summary)
            Log.Write("content: " + summary + " | " + string.Join(" ", old.Where(l => l.Confirmed).Select(l => $"{l.Kind}@{l.X1:0}-{l.X2:0},{l.Y:0}")));
        Summary = summary;
    }
}
