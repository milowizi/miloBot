using MiloBot.Gfx;
using MiloBot.World;

namespace MiloBot.Brain;

public class PawPrint
{
    public double X, Y, Angle, Life;
}

/// <summary>
/// Top-view mode: we look down on Milo and the whole screen is his floor. No gravity, no platforms; he wanders,
/// sniffs, follows you, naps curled up and leaves little paw prints that fade after a second.
/// </summary>
public partial class Milo
{
    public bool TopDown { get; private set; }
    /// <summary>Direction he faces in radians (0 = right, pi/2 = down).</summary>
    public double Heading = -Math.PI / 2;
    public Animator TopAnim;
    public readonly List<PawPrint> Prints = new();

    enum Top { Idle, Walk, Sniff, Sit, Lie, Sleep, Follow, Chase, Zoom, Petted, Slide, Laser, Investigate }
    Top top;
    double topT, topDur, goalX, goalY, strideAcc, lastPetTop = -10, nextTopHeart;
    int zoomLeft;
    bool leftFoot;
    string arriveIcon;

    public void SetTopDown(bool on)
    {
        if (on == TopDown || on && Sprites.Top == null) return;
        Current?.Exit(this);
        Current = null;
        while (suspended.Count > 0) suspended.Pop().Exit(this);
        Body.Surface = Surface.Floor;
        Body.Detach();
        Body.VX = Body.VY = 0;
        TopDown = on;
        Prints.Clear();
        if (on)
        {
            TopAnim ??= new Animator(Sprites.Top.Get);
            Body.Y -= U(14);
            Heading = Facing > 0 ? 0 : Math.PI;
            TopSet(Top.Idle, 1.5, Posture.Stand);
            Emote("sparkle", 1.2);
        }
        else ForcePosture(Posture.Air);
    }

    void TopSet(Top s, double duration, Posture posture = Posture.Stand)
    {
        top = s;
        topT = 0;
        topDur = duration;
        ForcePosture(posture);
    }

    public RectD TopHitRect() => new(Body.X - U(13), Body.Y - U(13), Body.X + U(13), Body.Y + U(13));

    World.Screen TopScreen() => W.ScreenAt(Body.X, Body.Y) is World.Screen s && s != W.BlockedScreen ? s : W.Screens.FirstOrDefault(x => x != W.BlockedScreen);

    bool Walkable(double x, double y) => W.Screens.Any(s => s != W.BlockedScreen && s.Work.Contains(x, y));

    (double x, double y) RandomSpot(double range)
    {
        var s = TopScreen();
        if (s == null) return (Body.X, Body.Y);
        var r = s.Work;
        double m = U(20);
        for (int i = 0; i < 8; i++)
        {
            double x = Body.X + (Rng.NextDouble() * 2 - 1) * range, y = Body.Y + (Rng.NextDouble() * 2 - 1) * range * 0.7;
            if (Walkable(x, y) && x > r.Left + m && x < r.Right - m && y > r.Top + m && y < r.Bottom - m) return (x, y);
        }
        return (Math.Clamp(Body.X, r.Left + m, r.Right - m), Math.Clamp(Body.Y, r.Top + m, r.Bottom - m));
    }

    static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    void TurnTo(double angle, double rate, double dt)
    {
        double d = Wrap(angle - Heading);
        Heading = Wrap(Heading + Math.Clamp(d, -rate * dt, rate * dt));
    }

    /// <summary>Walk towards a point, turning like a dog does. Returns true when he's there.</summary>
    bool TopMove(double tx, double ty, double speed, double dt, double stop = 3)
    {
        double dx = tx - Body.X, dy = ty - Body.Y, dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist <= stop) return true;
        double want = Math.Atan2(dy, dx);
        TurnTo(want, speed > U(60) ? 9 : 5, dt);
        double align = Math.Cos(Wrap(want - Heading));
        double step = Math.Min(dist, speed * dt * Math.Max(0.15, align));
        double nx = Body.X + Math.Cos(Heading) * step, ny = Body.Y + Math.Sin(Heading) * step;
        if (!Walkable(nx, ny)) return true;
        Body.X = nx;
        Body.Y = ny;
        strideAcc += step;
        if (strideAcc >= U(6))
        {
            strideAcc = 0;
            leftFoot = !leftFoot;
            double side = (leftFoot ? -1 : 1) * U(3), px = -Math.Sin(Heading) * side, py = Math.Cos(Heading) * side;
            Prints.Add(new PawPrint { X = Body.X + px, Y = Body.Y + py, Angle = Heading, Life = 1.0 });
        }
        Energy = Math.Max(0, Energy - (speed > U(100) ? 0.005 : speed > U(40) ? 0.003 : 0.0008) * dt);
        return false;
    }

    void FaceCursor(double dt, double rate = 4)
    {
        if (CursorDistance() > U(10)) TurnTo(Math.Atan2(W.CursorY - Body.Y, W.CursorX - Body.X), rate, dt);
    }

    void UpdateTop(double dt)
    {
        // Side-view behaviours set by events don't run here; don't let them block anything else.
        Current = null;
        foreach (var p in Prints) p.Life -= dt;
        Prints.RemoveAll(p => p.Life <= 0);
        if (Posture == Posture.Air)
        {
            TopSet(Top.Slide, 3);
            Say("yip", 3);
        }
        topT += dt;
        bool near = CursorDistance() < U(90);

        if (Petting)
        {
            lastPetTop = Now;
            if (top != Top.Petted) { TopSet(Top.Petted, 999, Posture.Sit); Save.Stat("pets"); Save.AddFriend(1); }
        }
        if (Laser.On && top != Top.Laser && top != Top.Slide) TopSet(Top.Laser, 999);
        if (top is not (Top.Chase or Top.Slide or Top.Laser or Top.Petted) && !Asleep && W.CursorSpeed > 2200 && CursorDistance() < U(220)
            && Energy > 0.4 && Memory.Cool("chase", 25, Now) && Rng.NextDouble() < 0.6)
        {
            Emote("exclaim", 1);
            TopSet(Top.Chase, 3 + Rng.NextDouble() * 3);
        }

        string anim = "idle";
        switch (top)
        {
            case Top.Idle:
                if (near) FaceCursor(dt);
                anim = near && Happiness > 0.35 ? "wag" : "idle";
                if (topT > topDur) TopChoose();
                break;
            case Top.Walk:
                anim = "walk";
                if (TopMove(goalX, goalY, Speed(Gait.Walk), dt) || topT > 25)
                {
                    if (Rng.NextDouble() < 0.35) TopSet(Top.Sniff, 1.5 + Rng.NextDouble() * 1.5);
                    else TopChoose();
                }
                break;
            case Top.Sniff:
                anim = "sniff";
                if (topT < 0.1) Say("sniff", 8);
                if (topT > topDur) TopChoose();
                break;
            case Top.Sit:
                if (near) FaceCursor(dt);
                anim = near && Happiness > 0.4 ? "sit_wag" : "sit";
                if (topT > topDur) TopChoose();
                break;
            case Top.Lie:
                anim = "lie";
                if (topT > topDur) TopChoose();
                break;
            case Top.Sleep:
                anim = "sleep";
                Energy = Math.Min(1, Energy + dt / 240);
                if (Memory.Cool("zzz", 1.7, Now)) Float("z", 2.2);
                if (topT > topDur && Energy > 0.9 || W.UserIdle < 1 && topT > 20 && Rng.NextDouble() < dt * 0.05)
                {
                    Emote("sparkle", 1.2);
                    TopSet(Top.Sit, 2, Posture.Sit);
                }
                break;
            case Top.Follow:
            {
                double side = Body.X < W.CursorX ? -1 : 1;
                anim = "walk";
                if (TopMove(W.CursorX + side * U(22), W.CursorY + U(6), Speed(Gait.Happy) * 1.3, dt, U(6)))
                {
                    Emote(arriveIcon ?? "heart", 1.6);
                    arriveIcon = null;
                    TopSet(Top.Sit, 3 + Rng.NextDouble() * 4, Posture.Sit);
                }
                else if (topT > 20) TopChoose();
                break;
            }
            case Top.Chase:
                anim = "run";
                TopMove(W.CursorX, W.CursorY, Speed(Gait.Run) * 1.3, dt, U(10));
                if (topT > topDur) { Pant(3); TopSet(Top.Sit, 3, Posture.Sit); Emote("sweat", 1.2); }
                break;
            case Top.Zoom:
                anim = "run";
                if (TopMove(goalX, goalY, Speed(Gait.Zoom), dt, U(8)))
                {
                    if (--zoomLeft <= 0) { TopSet(Top.Lie, 4, Posture.Lie); Emote("sweat", 1.4); Say("pant", 6); }
                    else (goalX, goalY) = RandomSpot(U(260));
                }
                break;
            case Top.Petted:
                FaceCursor(dt, 2);
                anim = "sit_wag";
                Happiness = Math.Min(1, Happiness + dt * 0.12);
                Social = Math.Max(0, Social - dt * 0.2);
                if (Now > nextTopHeart) { nextTopHeart = Now + 0.8 + Rng.NextDouble() * 0.6; Float("heart"); }
                if (Now - lastPetTop > 1.5) TopSet(Top.Sit, 2, Posture.Sit);
                break;
            case Top.Slide:
            {
                anim = "held";
                double k = Math.Exp(-3.5 * dt);
                Body.VX *= k;
                Body.VY *= k;
                double nx = Body.X + Body.VX * dt, ny = Body.Y + Body.VY * dt;
                if (Walkable(nx, ny)) { Body.X = nx; Body.Y = ny; }
                else { Body.VX = -Body.VX * 0.4; Body.VY = -Body.VY * 0.4; }
                if (Math.Abs(Body.VX) + Math.Abs(Body.VY) < U(15) || topT > topDur)
                {
                    Body.VX = Body.VY = 0;
                    Emote(Rng.NextDouble() < 0.5 ? "question" : "sparkle", 1.3);
                    TopSet(Top.Idle, 1.5);
                }
                break;
            }
            case Top.Laser:
                if (!Laser.On) { Pant(3); TopSet(Top.Sit, 3, Posture.Sit); break; }
                anim = "run";
                if (TopMove(Laser.X, Laser.Y, Speed(Gait.Run) * 1.4, dt, U(6)))
                {
                    anim = "sit_wag";
                    FaceCursor(dt, 8);
                    if (Rng.NextDouble() < dt * 0.8) Emote("exclaim", 0.8);
                }
                break;
            case Top.Investigate:
                anim = topT < topDur ? "walk" : "sniff";
                if (topT < topDur && TopMove(goalX, goalY, Speed(Gait.Run), dt, U(20)))
                {
                    topDur = topT;
                    Emote(arriveIcon ?? "question", 1.8);
                    Say("sniff", 4);
                }
                if (topT > topDur + 2.5) TopChoose();
                break;
        }
        TopAnim.Play(anim);
        TopAnim.Update(dt);
    }

    void TopChoose()
    {
        bool userAway = W.UserIdle > 150;
        var options = new List<(Top s, double w)>
        {
            (Top.Idle, 0.8),
            (Top.Walk, 1.0 + Boredom * 1.4 + Energy * 0.4),
            (Top.Sniff, 0.35),
            (Top.Sit, 0.6 + (1 - Energy) * 0.6),
            (Top.Lie, 0.4 + (1 - Energy) * 1.2),
            (Top.Sleep, Math.Pow(1 - Energy, 2) * 3.5 + (userAway ? 3 : 0) + (IsNight ? 0.5 : 0)),
            (Top.Follow, userAway ? 0.05 : Social * 2.2),
            (Top.Zoom, Energy > 0.7 && Happiness > 0.55 && !userAway ? 0.25 + Excitement * 0.8 : 0),
        };
        double total = options.Sum(o => o.w * o.w), r = Rng.NextDouble() * total;
        var pick = options[0].s;
        foreach (var o in options)
        {
            r -= o.w * o.w;
            if (r <= 0) { pick = o.s; break; }
        }
        switch (pick)
        {
            case Top.Walk:
                (goalX, goalY) = RandomSpot(U(Rng.NextDouble() < 0.3 ? 400 : 180));
                TopSet(Top.Walk, 0);
                break;
            case Top.Sniff: TopSet(Top.Sniff, 1.5 + Rng.NextDouble() * 2); break;
            case Top.Sit: TopSet(Top.Sit, 3 + Rng.NextDouble() * 6, Posture.Sit); break;
            case Top.Lie: TopSet(Top.Lie, 6 + Rng.NextDouble() * 12, Posture.Lie); break;
            case Top.Sleep: TopSet(Top.Sleep, 40 + Rng.NextDouble() * 60, Posture.Sleep); break;
            case Top.Follow: TopSet(Top.Follow, 0); break;
            case Top.Zoom:
                zoomLeft = 4 + Rng.NextDouble() switch { < 0.5 => 1, _ => 3 };
                (goalX, goalY) = RandomSpot(U(260));
                Save.Stat("zoomies");
                Say("yip", 3);
                TopSet(Top.Zoom, 0);
                break;
            default: TopSet(Top.Idle, 1.5 + Rng.NextDouble() * 3); break;
        }
    }

    // ---- things that happen to him in top view

    void TopClick()
    {
        if (Asleep)
        {
            Emote("puff", 1.4);
            TopSet(Top.Sit, 2, Posture.Sit);
            return;
        }
        if (clickCount >= 4 && Energy > 0.3)
        {
            clickCount = 0;
            zoomLeft = 5;
            (goalX, goalY) = RandomSpot(U(260));
            Save.Stat("zoomies");
            TopSet(Top.Zoom, 0);
            return;
        }
        Say("boop", 0.3);
        Emote(Happiness > 0.4 ? "heart" : "question", 1.4);
        TopSet(Top.Sit, 2.5, Posture.Sit);
    }

    public void TopCome(string icon = "heart")
    {
        if (!TopDown) return;
        arriveIcon = icon;
        TopSet(Top.Follow, 0);
    }

    void TopInvestigate(double x, double y, string icon)
    {
        var s = TopScreen();
        if (s == null) return;
        goalX = Math.Clamp(x, s.Work.Left + U(20), s.Work.Right - U(20));
        goalY = Math.Clamp(y, s.Work.Top + U(20), s.Work.Bottom - U(20));
        if (!Walkable(goalX, goalY)) return;
        arriveIcon = icon;
        Emote("exclaim", 1);
        TopSet(Top.Investigate, 20);
    }

    public void TopMoveTo(World.Screen s)
    {
        Body.X = s.Work.CenterX;
        Body.Y = s.Work.CenterY;
        Prints.Clear();
    }
}
