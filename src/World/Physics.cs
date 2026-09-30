namespace MiloBot.World;

/// <summary>Which way Milo's feet point. Floor is normal; the others are for crawling like a Terraria crawler.</summary>
public enum Surface { Floor, WallLeft, Ceiling, WallRight }

public class Body
{
    public double X, Y, VX, VY;
    public bool OnGround;
    public Surface Surface = Surface.Floor;
    public bool Climbing => Surface != Surface.Floor;
    public Platform Platform;
    public double HalfWidth = 30, Height = 60;
    public double RelX, LedgeY;
    public double LastPlatformMove;
    public double Shove, PlatformDX, PlatformDY;
    public int Lost;
    public IntPtr LostHwnd;

    public void Land(Platform p, Func<IntPtr, RectD?> windowRect)
    {
        OnGround = true;
        Platform = p;
        Y = p.Y;
        VY = 0;
        if (!p.IsFloor && windowRect(p.Hwnd) is RectD r) RelX = X - r.Left;
        if (p.Ledge != null) LedgeY = p.Ledge.Y;
    }

    public void Detach()
    {
        OnGround = false;
        Platform = null;
    }
}

public enum StepResult { None, Landed, Bounced, Detached, HitWall }

public static class Physics
{
    public static StepResult Step(Body b, WorldState w, double dt, double gravity, double restitution, Func<IntPtr, RectD?> windowRect)
    {
        b.Shove = b.PlatformDX = b.PlatformDY = 0;
        if (b.OnGround) return Stick(b, w, windowRect);
        var result = StepResult.None;
        double prevY = b.Y;
        b.VY = Math.Min(b.VY + gravity * dt, 4000);
        b.X += b.VX * dt;
        b.Y += b.VY * dt;
        var s = w.ScreenAt(b.X, b.Y - b.Height / 2);
        if (s != null)
        {
            if (b.X - b.HalfWidth < s.Bounds.Left && w.ScreenAt(b.X - b.HalfWidth - 2, b.Y - 5) == s)
            {
                b.X = s.Bounds.Left + b.HalfWidth;
                b.VX = Math.Abs(b.VX) * restitution;
                result = StepResult.HitWall;
            }
            if (b.X + b.HalfWidth > s.Bounds.Right && w.ScreenAt(b.X + b.HalfWidth + 2, b.Y - 5) == s)
            {
                b.X = s.Bounds.Right - b.HalfWidth;
                b.VX = -Math.Abs(b.VX) * restitution;
                result = StepResult.HitWall;
            }
            if (b.Y - b.Height < s.Bounds.Top && b.VY < 0)
            {
                b.Y = s.Bounds.Top + b.Height;
                b.VY = Math.Abs(b.VY) * restitution;
            }
        }
        if (b.VY > 0)
        {
            Platform hit = null;
            foreach (var p in w.Platforms)
            {
                if (p.Y < prevY - 0.5 || p.Y > b.Y || !p.Contains(b.X)) continue;
                if (hit == null || p.Y < hit.Y) hit = p;
            }
            if (hit != null)
            {
                b.Y = hit.Y;
                if (b.VY > 380 && restitution > 0.05)
                {
                    b.VY = -b.VY * restitution;
                    b.VX *= 0.8;
                    return StepResult.Bounced;
                }
                b.VX *= 0.5;
                b.Land(hit, windowRect);
                return StepResult.Landed;
            }
        }
        if (s != null && b.Y > s.Bounds.Bottom + 40)
        {
            var floor = w.FloorUnder(b.X, b.Y) ?? w.Platforms.FirstOrDefault(p => p.IsFloor);
            if (floor != null)
            {
                b.X = Math.Clamp(b.X, floor.X1 + b.HalfWidth, floor.X2 - b.HalfWidth);
                b.Y = floor.Y - 200;
                b.VY = 0;
            }
        }
        return result;
    }

    static StepResult Stick(Body b, WorldState w, Func<IntPtr, RectD?> windowRect)
    {
        var p = b.Platform;
        if (p == null) { b.Detach(); return StepResult.Detached; }
        if (p.IsFloor)
        {
            var f = w.FloorUnder(b.X, b.Y);
            if (f == null)
            {
                var near = w.Platforms.Where(q => q.IsFloor).OrderBy(q => Math.Abs(q.Y - b.Y)).FirstOrDefault();
                if (near == null) return StepResult.None;
                b.X = Math.Clamp(b.X, near.X1 + b.HalfWidth, near.X2 - b.HalfWidth);
                f = near;
            }
            b.Platform = f;
            b.Y = f.Y;
            return StepResult.None;
        }
        var rect = windowRect(p.Hwnd);
        if (rect == null) { b.Lost = 1; b.LostHwnd = p.Hwnd; b.Detach(); return StepResult.Detached; }
        var r = rect.Value;
        if (p.Ledge != null) return StickContent(b, w, p, r);
        double nx = r.Left + b.RelX;
        double dx = nx - b.X, dy = r.Top - b.Y;
        bool moving = Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5;
        if (moving)
        {
            b.LastPlatformMove = w.Now;
            b.Shove = Math.Max(Math.Abs(dx), Math.Abs(dy));
            b.PlatformDX = dx;
            b.PlatformDY = dy;
            b.X = nx;
            b.Y = r.Top;
            p.Y = r.Top;
            double width = p.X2 - p.X1;
            p.X1 += dx;
            p.X2 += dx;
            if (dy < -35) { b.Lost = 3; b.Detach(); b.VY = dy * 25; return StepResult.Detached; }
        }
        if (b.X < r.Left - 2 || b.X > r.Right + 2) { b.Lost = 2; b.Detach(); return StepResult.Detached; }
        if (w.Now - b.LastPlatformMove > 0.6)
        {
            var live = w.Find(p.Hwnd, b.X, b.Y);
            if (live == null || Math.Abs(live.Y - b.Y) > 3) { b.Lost = 2; b.LostHwnd = p.Hwnd; b.Detach(); return StepResult.Detached; }
            b.Platform = live;
        }
        return StepResult.None;
    }

    // Standing on something inside a window: moves with the window, and falls off when the page scrolls
    // the thing away or another window covers it.
    static StepResult StickContent(Body b, WorldState w, Platform p, RectD r)
    {
        var l = p.Ledge;
        double ty = r.Top + l.Y;
        if (!l.Alive || Math.Abs(l.Y - b.LedgeY) > 6)
        {
            b.Lost = 4;
            b.LostHwnd = p.Hwnd;
            b.Detach();
            return StepResult.Detached;
        }
        double nx = r.Left + b.RelX;
        double dx = nx - b.X, dy = ty - b.Y;
        if (Math.Abs(dx) > 0.5 || Math.Abs(dy) > 0.5)
        {
            b.LastPlatformMove = w.Now;
            b.Shove = Math.Max(Math.Abs(dx), Math.Abs(dy));
            b.PlatformDX = dx;
            b.PlatformDY = dy;
            b.X = nx;
            b.Y = ty;
            b.LedgeY = l.Y;
            p.Y = ty;
            p.X1 += dx;
            p.X2 += dx;
            if (dy < -35) { b.Lost = 3; b.Detach(); b.VY = dy * 25; return StepResult.Detached; }
        }
        if (b.X < r.Left + l.X1 - 2 || b.X > r.Left + l.X2 + 2) { b.Lost = 2; b.Detach(); return StepResult.Detached; }
        if (w.Now - b.LastPlatformMove > 0.6)
        {
            Platform live = null;
            foreach (var q in w.Platforms)
                if (q.Ledge == l && q.Contains(b.X)) { live = q; break; }
            if (live == null) { b.Lost = 2; b.LostHwnd = p.Hwnd; b.Detach(); return StepResult.Detached; }
            b.Platform = live;
        }
        return StepResult.None;
    }
}
