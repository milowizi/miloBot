using MiloBot.World;

namespace MiloBot.Brain;

public enum CritterKind { Butterfly, Bird }
public enum CritterState { Arrive, Wander, Landed, OnMilo, Leave, Gone }

public class Critter
{
    public CritterKind Kind;
    public CritterState State;
    public double X, Y, VX, VY, T, StateT, Life;
    public double TargetX, TargetY;
    public Platform Perch;
    public int Facing = 1;
    public string Frame = "fly0";
    public bool Spooked;
}

public class Critters
{
    public readonly List<Critter> Items = new();
    readonly Random rng = new();
    double nextSpawn = 150 + Random.Shared.NextDouble() * 180;
    public Action<Critter> Arrived;

    public void Spawn(WorldState w, Milo milo, CritterKind kind)
    {
        var s = w.ScreenAt(milo.Body.X, milo.Body.Y - 10);
        if (s == null) return;
        bool left = rng.NextDouble() < 0.5;
        var c = new Critter
        {
            Kind = kind,
            State = CritterState.Arrive,
            X = left ? s.Bounds.Left - 30 : s.Bounds.Right + 30,
            Y = s.Work.Top + s.Work.Height * (0.35 + rng.NextDouble() * 0.35),
            Life = kind == CritterKind.Butterfly ? 45 + rng.NextDouble() * 40 : 30 + rng.NextDouble() * 40
        };
        if (kind == CritterKind.Bird)
        {
            var spots = w.Platforms.Where(p => p.Width > 120 && Math.Abs((p.X1 + p.X2) / 2 - milo.Body.X) > 250 && w.ScreenAt((p.X1 + p.X2) / 2, p.Y - 5) == s).ToList();
            if (spots.Count == 0) spots = w.Platforms.Where(p => p.IsFloor && w.ScreenAt((p.X1 + p.X2) / 2, p.Y - 5) == s).ToList();
            if (spots.Count == 0) return;
            var p = spots[rng.Next(spots.Count)];
            double far = milo.Body.X < (p.X1 + p.X2) / 2 ? p.X2 - 60 : p.X1 + 60;
            c.Perch = p;
            c.TargetX = Math.Clamp(far + (rng.NextDouble() - 0.5) * 120, p.X1 + 30, p.X2 - 30);
            c.TargetY = p.Y;
        }
        else
        {
            c.TargetX = milo.Body.X + (rng.NextDouble() - 0.5) * 500;
            c.TargetY = milo.Body.Y - 150 - rng.NextDouble() * 200;
        }
        Items.Add(c);
    }

    public void Update(WorldState w, Milo milo, double dt, int scale, bool allowSpawn)
    {
        if (allowSpawn && Items.Count == 0 && w.Hour >= 7 && w.Hour < 22 && w.Now > nextSpawn)
        {
            nextSpawn = w.Now + 360 + rng.NextDouble() * 540;
            Spawn(w, milo, rng.NextDouble() < 0.55 ? CritterKind.Butterfly : CritterKind.Bird);
        }
        foreach (var c in Items)
        {
            c.T += dt;
            c.StateT += dt;
            if (c.Kind == CritterKind.Butterfly) Butterfly(c, w, milo, dt, scale);
            else Bird(c, w, milo, dt, scale);
            if (c.State != CritterState.Leave && c.State != CritterState.Gone && c.T > c.Life) Leave(c, w);
        }
        Items.RemoveAll(c => c.State == CritterState.Gone);
    }

    public void Leave(Critter c, WorldState w)
    {
        if (c.State == CritterState.Leave) return;
        c.State = CritterState.Leave;
        c.StateT = 0;
        var s = w.ScreenAt(c.X, c.Y);
        double dir = s != null && c.X > s.Bounds.CenterX ? 1 : -1;
        if (c.Kind == CritterKind.Bird) { c.VX = dir * 420; c.VY = -300; }
        else { c.VX = dir * 90; c.VY = -110; }
        c.Facing = dir > 0 ? 1 : -1;
    }

    public void Scare(Critter c, WorldState w)
    {
        c.Spooked = true;
        Leave(c, w);
    }

    void Butterfly(Critter c, WorldState w, Milo milo, double dt, int scale)
    {
        double flap = Math.Sin(c.T * 18);
        c.Frame = flap > 0.3 ? "fly0" : flap > -0.3 ? "fly1" : "fly2";
        switch (c.State)
        {
            case CritterState.Arrive:
            case CritterState.Wander:
            {
                if (c.StateT > 2.5 + rng.NextDouble() * 2 || Dist(c.X, c.Y, c.TargetX, c.TargetY) < 20)
                {
                    c.StateT = 0;
                    c.State = CritterState.Wander;
                    bool visit = c.T > 15 && rng.NextDouble() < 0.3 && milo.Body.OnGround && !milo.Hidden
                                 && milo.Posture is Posture.Sit or Posture.Lie or Posture.Sleep or Posture.Stand or Posture.Front
                                 && milo.Current is not Hunt;
                    if (visit)
                    {
                        var (hx, hy) = milo.HeadPoint();
                        c.TargetX = hx;
                        c.TargetY = hy + 6 * scale;
                    }
                    else
                    {
                        c.TargetX = milo.Body.X + (rng.NextDouble() - 0.5) * 600;
                        c.TargetY = milo.Body.Y - 60 - rng.NextDouble() * 260;
                    }
                }
                double dx = c.TargetX - c.X, dy = c.TargetY - c.Y;
                double d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                double sp = 70;
                c.VX += (dx / d * sp - c.VX) * Math.Min(1, dt * 2);
                c.VY += (dy / d * sp - c.VY) * Math.Min(1, dt * 2);
                c.X += c.VX * dt;
                c.Y += c.VY * dt + Math.Sin(c.T * 7) * 30 * dt;
                if (Math.Abs(c.VX) > 5) c.Facing = c.VX > 0 ? 1 : -1;
                var (mx, my) = milo.HeadPoint();
                if (c.State == CritterState.Wander && c.T > 15 && Dist(c.X, c.Y, mx, my + 6 * scale) < 10 * scale && milo.Current is not Hunt && milo.Body.OnGround)
                {
                    c.State = CritterState.OnMilo;
                    c.StateT = 0;
                    Arrived?.Invoke(c);
                }
                break;
            }
            case CritterState.OnMilo:
            {
                var (hx, hy) = milo.HeadPoint();
                c.X = hx;
                c.Y = hy + 7 * scale;
                c.Frame = c.StateT % 1.2 < 0.15 ? "fly1" : "fly2";
                if (!milo.Body.OnGround || c.StateT > 6) Leave(c, w);
                break;
            }
            case CritterState.Leave:
                c.X += c.VX * dt;
                c.Y += c.VY * dt + Math.Sin(c.T * 7) * 40 * dt;
                if (c.StateT > 12 || w.ScreenAt(c.X, c.Y) is { } s && !s.Bounds.Contains(c.X, c.Y) && c.StateT > 1) c.State = CritterState.Gone;
                break;
        }
    }

    void Bird(Critter c, WorldState w, Milo milo, double dt, int scale)
    {
        switch (c.State)
        {
            case CritterState.Arrive:
            {
                c.Frame = (int)(c.T * 10) % 2 == 0 ? "bird3" : "bird4";
                double dx = c.TargetX - c.X, dy = c.TargetY - c.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 6)
                {
                    c.X = c.TargetX;
                    c.Y = c.TargetY;
                    c.State = CritterState.Landed;
                    c.StateT = 0;
                    Arrived?.Invoke(c);
                    break;
                }
                double sp = Math.Min(260, d * 2 + 60);
                c.X += dx / d * sp * dt;
                c.Y += dy / d * sp * dt;
                c.Facing = dx > 0 ? 1 : -1;
                break;
            }
            case CritterState.Landed:
            {
                if (c.Perch != null)
                {
                    var live = w.Platforms.FirstOrDefault(p => p.Hwnd == c.Perch.Hwnd && p.Contains(c.X) && Math.Abs(p.Y - c.Y) < 40);
                    if (live == null) { Leave(c, w); break; }
                    c.Y = live.Y;
                    c.Perch = live;
                }
                double cycle = c.StateT % 3.0;
                if (cycle < 1.2) c.Frame = "bird0";
                else if (cycle < 2.0) c.Frame = ((int)(c.StateT * 6) % 2 == 0) ? "bird1" : "bird0";
                else
                {
                    c.Frame = "bird2";
                    double nx = c.X + c.Facing * 40 * dt;
                    if (c.Perch != null && !c.Perch.Contains(nx, 20)) c.Facing = -c.Facing;
                    else c.X = nx;
                    if (rng.NextDouble() < dt * 0.3) c.Facing = -c.Facing;
                }
                double md = Dist(c.X, c.Y, milo.Body.X, milo.Body.Y);
                bool loud = milo.Current is Hunt h && h.Barking;
                if (md < 90 * scale && loud) Scare(c, w);
                if (md < 30 * scale && milo.Body.OnGround && Math.Abs(milo.Body.VX) + (milo.Current is Hunt ? 100 : 0) > 50) Scare(c, w);
                break;
            }
            case CritterState.Leave:
                c.Frame = (int)(c.T * 12) % 2 == 0 ? "bird3" : "bird4";
                c.X += c.VX * dt;
                c.Y += c.VY * dt;
                c.VY -= 60 * dt;
                if (c.StateT > 6) c.State = CritterState.Gone;
                break;
        }
    }

    static double Dist(double x1, double y1, double x2, double y2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
}
