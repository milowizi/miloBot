using MiloBot.World;

namespace MiloBot.Vision;

public class MotionEvent
{
    public double X, Y;
    public double Strength;
    public bool Persistent;
}

public class MotionWatcher
{
    const int GW = 48, GH = 27;
    float[] prev;
    RectD prevSource;
    readonly int[] streak = new int[GW * GH];
    public double GlobalChange { get; private set; }
    public double ChangeSinceLook { get; set; }

    public MotionEvent Sample(RectD screen, IEnumerable<RectD> ignore)
    {
        var cap = ScreenCapture.Grab(screen, GW, GH);
        var cur = new float[GW * GH];
        for (int i = 0; i < cur.Length; i++)
        {
            int o = i * 4;
            cur[i] = (cap.Bgra[o] * 0.11f + cap.Bgra[o + 1] * 0.59f + cap.Bgra[o + 2] * 0.3f) / 255f;
        }
        if (prev == null || prevSource.Left != screen.Left || prevSource.Top != screen.Top || prevSource.Width != screen.Width)
        {
            prev = cur;
            prevSource = screen;
            return null;
        }
        var mask = new bool[GW * GH];
        foreach (var r in ignore)
        {
            int x0 = (int)Math.Floor((r.Left - screen.Left) / screen.Width * GW) - 1;
            int x1 = (int)Math.Ceiling((r.Right - screen.Left) / screen.Width * GW) + 1;
            int y0 = (int)Math.Floor((r.Top - screen.Top) / screen.Height * GH) - 1;
            int y1 = (int)Math.Ceiling((r.Bottom - screen.Top) / screen.Height * GH) + 1;
            for (int y = Math.Max(0, y0); y < Math.Min(GH, y1); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(GW, x1); x++)
                    mask[y * GW + x] = true;
        }
        double total = 0;
        int changed = 0;
        double sx = 0, sy = 0, sw = 0;
        int persistentCells = 0;
        for (int i = 0; i < cur.Length; i++)
        {
            if (mask[i]) { streak[i] = 0; continue; }
            float d = Math.Abs(cur[i] - prev[i]);
            total += d;
            if (d > 0.06)
            {
                changed++;
                streak[i]++;
                if (streak[i] >= 5) persistentCells++;
                double wgt = d;
                sx += (i % GW + 0.5) * wgt;
                sy += (i / GW + 0.5) * wgt;
                sw += wgt;
            }
            else streak[i] = Math.Max(0, streak[i] - 1);
        }
        prev = cur;
        GlobalChange = total / cur.Length;
        ChangeSinceLook += GlobalChange;
        double frac = (double)changed / cur.Length;
        if (sw <= 0 || frac < 0.004 || frac > 0.6) return null;
        return new MotionEvent
        {
            X = screen.Left + sx / sw / GW * screen.Width,
            Y = screen.Top + sy / sw / GH * screen.Height,
            Strength = frac,
            Persistent = persistentCells >= 4
        };
    }
}
