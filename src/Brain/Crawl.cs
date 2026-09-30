using MiloBot.World;

namespace MiloBot.Brain;

/// <summary>
/// Milo plays Terraria crawler: he walks to an outer edge of the screen, climbs the wall, creeps along the top of the
/// screen upside down and then lets go (sometimes right onto your cursor) or climbs back down.
/// </summary>
public class Crawl : Behavior
{
    enum Phase { ToWall, Up, Ceiling, Hang, Back, Down }
    Phase phase = Phase.ToWall;
    readonly World.Screen screen;
    readonly int side;
    readonly double wallX, floorY, top;
    readonly GoTo walk;
    double targetX, phaseT, pauseUntil = -1, nextPause, hangFor;
    bool chaseCursor;

    public override int Priority => 2;
    public override string Name => "Crawl:" + phase;

    Crawl(World.Screen screen, int side, Milo m)
    {
        this.screen = screen;
        this.side = side;
        wallX = side < 0 ? screen.Bounds.Left : screen.Bounds.Right;
        floorY = screen.Work.Bottom;
        top = screen.Bounds.Top;
        walk = new GoTo(wallX - side * m.U(16), floorY, Gait.Happy, 2, 3);
    }

    /// <summary>Only screen edges that don't continue onto another monitor can be climbed.</summary>
    public static Crawl TryMake(Milo m)
    {
        if (m.Toys.InMouth != null) return null;
        var s = m.W.ScreenAt(m.Body.X, m.Body.Y - 10);
        if (s == null || s == m.W.BlockedScreen || s.Work.Bottom - s.Bounds.Top < m.U(120)) return null;
        bool Outer(double x) => !m.W.Screens.Any(o => o != s && (o.Bounds.Contains(x, s.Work.Bottom - 20) || o.Bounds.Contains(x, s.Bounds.Top + 20)));
        bool left = Outer(s.Bounds.Left - 4), right = Outer(s.Bounds.Right + 4);
        if (!left && !right) return null;
        int side = left && right ? (m.Body.X - s.Bounds.Left < s.Bounds.Right - m.Body.X ? -1 : 1) : left ? -1 : 1;
        return new Crawl(s, side, m);
    }

    void Set(Phase p)
    {
        phase = p;
        phaseT = 0;
    }

    double Speed(Milo m) => m.Speed(Gait.Walk) * 1.15;

    bool CursorHere(Milo m) => screen.Bounds.Contains(m.W.CursorX, m.W.CursorY) && m.W.CursorY > top + m.U(40);

    // A little pause now and then so it looks like he's thinking about his footing.
    bool Pausing(Milo m, string look)
    {
        if (m.Now < pauseUntil)
        {
            m.Anim.Play(look);
            return true;
        }
        if (m.Now > nextPause)
        {
            nextPause = m.Now + 2.5 + m.Rng.NextDouble() * 3;
            if (m.Rng.NextDouble() < 0.45)
            {
                pauseUntil = m.Now + 0.5 + m.Rng.NextDouble() * 0.8;
                if (m.Rng.NextDouble() < 0.3) m.Emote(m.Rng.NextDouble() < 0.5 ? "sparkle" : "question", 1.2);
                return true;
            }
        }
        return false;
    }

    protected override bool Tick(Milo m, double dt)
    {
        phaseT += dt;
        var b = m.Body;
        if (m.W.BlockedScreen == screen || T > 90)
        {
            m.LetGo();
            return false;
        }
        m.Energy = Math.Max(0, m.Energy - 0.0015 * dt);
        switch (phase)
        {
            case Phase.ToWall:
                if (walk.Update(m, dt) && phaseT < 25) return true;
                if (walk.Failed || b.Platform == null || !b.Platform.IsFloor || Math.Abs(b.X - wallX) > m.U(40)) return false;
                if (!m.To(Posture.Stand)) return true;
                b.Detach();
                b.Surface = side < 0 ? Surface.WallLeft : Surface.WallRight;
                b.X = wallX;
                b.Y = floorY - m.U(16);
                m.Facing = side < 0 ? -1 : 1;
                m.Save.Stat("climbs");
                nextPause = m.Now + 1.5;
                Set(Phase.Up);
                return true;

            case Phase.Up:
                if (Pausing(m, "idle_look")) return true;
                b.Y -= Speed(m) * dt;
                m.Anim.Play("walk");
                if (b.Y > top + m.U(18)) return true;
                b.Surface = Surface.Ceiling;
                b.Y = top;
                b.X = wallX - side * m.U(18);
                m.Facing = side;
                chaseCursor = CursorHere(m) && m.Rng.NextDouble() < 0.5;
                targetX = chaseCursor ? m.W.CursorX : screen.Bounds.Left + m.U(60) + m.Rng.NextDouble() * (screen.Bounds.Width - m.U(120));
                Set(Phase.Ceiling);
                return true;

            case Phase.Ceiling:
            {
                if (chaseCursor && CursorHere(m)) targetX = m.W.CursorX;
                targetX = Math.Clamp(targetX, screen.Bounds.Left + m.U(20), screen.Bounds.Right - m.U(20));
                double d = targetX - b.X;
                if (Math.Abs(d) < m.U(3))
                {
                    hangFor = chaseCursor ? 0.6 + m.Rng.NextDouble() * 0.6 : 2 + m.Rng.NextDouble() * 3;
                    m.Emote(chaseCursor ? "exclaim" : "sparkle", 1.2);
                    Set(Phase.Hang);
                    return true;
                }
                if (!chaseCursor && Pausing(m, "idle_look")) return true;
                m.Facing = d > 0 ? -1 : 1;
                b.X += Math.Sign(d) * Math.Min(Math.Abs(d), Speed(m) * (chaseCursor ? 1.6 : 1) * dt);
                m.Anim.Play(chaseCursor ? "walk_happy" : "sniff_walk");
                return phaseT < 40;
            }

            case Phase.Hang:
                m.Anim.Play(phaseT < hangFor * 0.5 ? "idle_look" : "idle_wag");
                if (phaseT < hangFor) return true;
                if (!chaseCursor && m.Rng.NextDouble() < 0.35)
                {
                    Set(Phase.Back);
                    return true;
                }
                m.Say(chaseCursor ? "yip" : "happy", 2);
                m.LetGo();
                return false;

            case Phase.Back:
            {
                double gx = wallX - side * m.U(18);
                double d = gx - b.X;
                m.Facing = d > 0 ? -1 : 1;
                b.X += Math.Sign(d) * Math.Min(Math.Abs(d), Speed(m) * dt);
                m.Anim.Play("walk");
                if (Math.Abs(d) > m.U(2)) return phaseT < 40;
                b.Surface = side < 0 ? Surface.WallLeft : Surface.WallRight;
                b.X = wallX;
                b.Y = top + m.U(18);
                m.Facing = side < 0 ? 1 : -1;
                Set(Phase.Down);
                return true;
            }

            case Phase.Down:
                if (Pausing(m, "idle")) return true;
                b.Y += Speed(m) * 1.2 * dt;
                m.Anim.Play("walk");
                if (b.Y < floorY - m.U(16)) return true;
                b.Surface = Surface.Floor;
                b.X = wallX - side * m.U(18);
                var floor = m.W.FloorUnder(b.X, floorY);
                if (floor == null) { b.Detach(); m.ForcePosture(Posture.Air); return false; }
                b.Land(floor, m.WindowRect);
                m.ForcePosture(Posture.Stand);
                m.Facing = -side;
                m.Set(new PlayOnce("shake", Posture.Stand, 1.2, null, "sparkle", 1));
                return false;
        }
        return false;
    }
}

public partial class Milo
{
    public void StartCrawl()
    {
        if (TopDown) return;
        if (Posture == Posture.Held || !Body.OnGround || Body.Climbing) return;
        var c = Crawl.TryMake(this);
        if (c == null) { Emote("question", 1.5); return; }
        Emote("exclaim", 1.2);
        Set(c);
    }
}
