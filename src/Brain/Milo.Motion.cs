using MiloBot.World;

namespace MiloBot.Brain;

public enum Gait { Walk, Happy, Sniff, Run, Zoom }

public partial class Milo
{
    bool transitioning;
    Posture transitionTarget;
    string transitionAnim = "";
    double transitionStart;
    double overlayUntil;
    double nextBlink, nextMicro, nextLook, alertUntil, pantUntil, tiltUntil;
    int tiltSide = 1;
    double airMaxVY;
    bool hopping;
    double lastOwnSound = -10;

    public double MaxJumpUp => U(150);
    public double MaxJumpDist => U(170);

    public bool To(Posture target)
    {
        if (transitioning)
        {
            bool hijacked = Anim.Name != transitionAnim;
            if (!Anim.Finished && !hijacked && Now - transitionStart < 2.5) return false;
            transitioning = false;
            Posture = transitionTarget;
        }
        if (Posture == target) return true;
        if (Posture == Posture.Air || Posture == Posture.Held) Posture = Posture.Stand;
        (string anim, bool rev, Posture next) step = (Posture, target) switch
        {
            (Posture.Stand, _) => ("sit_down", false, Posture.Sit),
            (Posture.Sit, Posture.Stand) => ("stand_up", false, Posture.Stand),
            (Posture.Sit, Posture.Front) => (null, false, Posture.Front),
            (Posture.Sit, _) => ("lie_down", false, Posture.Lie),
            (Posture.Front, _) => (null, false, Posture.Sit),
            (Posture.Lie, Posture.Stand) => ("get_up", false, Posture.Stand),
            (Posture.Lie, Posture.Sleep) => (null, false, Posture.Sleep),
            (Posture.Lie, Posture.Belly) => (null, false, Posture.Belly),
            (Posture.Lie, _) => ("lie_down", true, Posture.Sit),
            (Posture.Sleep, _) => ("wake", false, Posture.Lie),
            (Posture.Belly, _) => (null, false, Posture.Lie),
            _ => (null, false, target)
        };
        if (step.anim == null)
        {
            Posture = step.next;
            return Posture == target;
        }
        Anim.Play(step.anim, true, step.rev);
        transitioning = true;
        transitionTarget = step.next;
        transitionAnim = step.anim;
        transitionStart = Now;
        return false;
    }

    public void ForcePosture(Posture p)
    {
        transitioning = false;
        Posture = p;
    }

    string BaseAnim()
    {
        bool cursorNear = CursorDistance() < U(90);
        bool cursorAbove = W.CursorY < Body.Y - U(40) && Math.Abs(W.CursorX - Body.X) < U(40);
        switch (Posture)
        {
            case Posture.Sit:
                if (Now < pantUntil) return "sit_pant";
                return cursorAbove ? "sit_look" : "sit";
            case Posture.Lie:
                if (cursorAbove) return "lie_look";
                return Energy < 0.3 ? "lie_sleepy" : "lie";
            case Posture.Sleep: return "sleep";
            case Posture.Belly: return "belly";
            case Posture.Front:
                if (Petting) return "front_petted";
                if (Now < tiltUntil) return tiltSide > 0 ? "front_tilt_l" : "front_tilt_r";
                if (Now < pantUntil || Happiness > 0.75) return "front_happy";
                return cursorAbove ? "front_look" : "front";
            default:
                if (Now < alertUntil) return "alert";
                if (cursorAbove) return "idle_look";
                if (cursorNear && Happiness > 0.35) return "idle_wag";
                return Happiness > 0.8 && Excitement > 0.4 ? "happy" : "idle";
        }
    }

    public void Live(double dt, bool followCursor = true)
    {
        if (transitioning) return;
        if (Now < overlayUntil && !Anim.Finished) return;
        if (Now >= nextBlink)
        {
            nextBlink = Now + 2 + Rng.NextDouble() * 4.5;
            string blink = Posture switch
            {
                Posture.Stand => "idle_blink",
                Posture.Sit => "sit_blink",
                Posture.Lie => "lie_blink",
                Posture.Front => "front_blink",
                _ => null
            };
            if (blink != null && !(Posture == Posture.Front && Petting))
            {
                Anim.Play(blink, true);
                overlayUntil = Now + 0.15;
                return;
            }
        }
        if (followCursor && Now >= nextLook && Posture is Posture.Stand or Posture.Sit or Posture.Lie)
        {
            double d = CursorDistance();
            if (d < U(160) && W.CursorSpeed > 40 && Math.Abs(W.CursorX - Body.X) > U(12))
            {
                int want = W.CursorX > Body.X ? 1 : -1;
                if (want != Facing) { Facing = want; nextLook = Now + 1.2; }
            }
        }
        if (Now >= nextMicro)
        {
            nextMicro = Now + 3 + Rng.NextDouble() * 6;
            double r = Rng.NextDouble();
            if (Posture == Posture.Stand && r < 0.25) { Anim.Play("idle_ear", true); overlayUntil = Now + 0.4; return; }
            if (Posture == Posture.Sit && r < 0.08 && Memory.Cool("scratch", 25, Now)) { Anim.Play("scratch", true); overlayUntil = Now + 1.4; return; }
            if (Posture == Posture.Sit && r < 0.1 + (1 - Energy) * 0.25 && Memory.Cool("yawn", 20, Now))
            {
                Anim.Play("yawn", true);
                overlayUntil = Now + 1.2;
                Say("yawn", 20);
                return;
            }
            if (Posture == Posture.Front && r < 0.3) { tiltSide = -tiltSide; tiltUntil = Now + 1.2; }
            if (Posture == Posture.Sleep && r < 0.15) { Anim.Play("sleep_twitch", true); overlayUntil = Now + 0.4; if (Rng.NextDouble() < 0.3) Say("whine", 60); return; }
            if (r > 0.97 && Memory.Cool("sneeze", 90, Now)) { Say("sneeze", 60); Emote("sweat", 1.0); }
        }
        if (Posture == Posture.Sleep && Memory.Cool("zzz", 1.7, Now)) Float("z", 2.2);
        Anim.Play(BaseAnim());
    }

    public double CursorDistance()
    {
        double dx = W.CursorX - Body.X, dy = W.CursorY - (Body.Y - U(15));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public double Speed(Gait g) => U(g switch { Gait.Zoom => 150, Gait.Run => 72, Gait.Happy => 27, Gait.Sniff => 12, _ => 22 });

    public bool WalkTo(double x, Gait gait, double dt, double tolerance = 2)
    {
        var p = Body.Platform;
        if (p == null) return true;
        double lo = p.X1 + Body.HalfWidth * 0.6, hi = p.X2 - Body.HalfWidth * 0.6;
        if (lo > hi) lo = hi = (p.X1 + p.X2) / 2;
        double tx = Math.Clamp(x, lo, hi);
        double d = tx - Body.X;
        if (Math.Abs(d) <= U(tolerance) / 3 + 1)
        {
            return true;
        }
        Facing = d > 0 ? 1 : -1;
        double sp = Speed(gait) * (gait == Gait.Run && Excitement > 0.7 ? 1.25 : 1.0);
        double step = Math.Min(Math.Abs(d), sp * dt);
        Body.X += Math.Sign(d) * step;
        if (!p.IsFloor && WindowRect(p.Hwnd) is RectD r) Body.RelX = Body.X - r.Left;
        bool carrying = Toys.InMouth != null;
        Anim.Play(gait switch { Gait.Zoom => "zoom", Gait.Run => "run", Gait.Happy => carrying ? "walk_carry" : "walk_happy", Gait.Sniff => "sniff_walk", _ => carrying ? "walk_carry" : "walk" });
        if (gait == Gait.Zoom && Rng.NextDouble() < dt * 6) Dust();
        double energyCost = gait == Gait.Zoom ? 0.005 : gait == Gait.Run ? 0.004 : 0.0008;
        Energy = Math.Max(0, Energy - energyCost * dt);
        return false;
    }

    public void Launch(double x1, double y1)
    {
        double g = U(900);
        double x0 = Body.X, y0 = Body.Y;
        double apex = Math.Min(y0, y1) - U(16);
        double vy0 = -Math.Sqrt(2 * g * (y0 - apex));
        double tUp = -vy0 / g;
        double tDown = Math.Sqrt(2 * Math.Max(0, y1 - apex) / g);
        double t = Math.Max(0.15, tUp + tDown);
        Body.VX = (x1 - x0) / t;
        Body.VY = vy0;
        Facing = x1 >= x0 ? 1 : -1;
        Body.Detach();
        ForcePosture(Posture.Air);
        hopping = true;
        Energy = Math.Max(0, Energy - 0.01);
    }

    public void Hop(int dir, double up = 150, double side = 55)
    {
        Body.VX = dir * U(side);
        Body.VY = -U(up);
        Body.Detach();
        ForcePosture(Posture.Air);
        hopping = true;
    }

    public bool CanReach(Platform from, Platform to, out double takeoff, out double landing) =>
        PlanJump(from, to, Body.X, out takeoff, out landing);

    public bool PlanJump(Platform from, Platform to, double desired, out double takeoff, out double landing)
    {
        takeoff = landing = 0;
        if (from == null || to == null) return false;
        if (from.Y - to.Y > MaxJumpUp) return false;
        double m = Body.HalfWidth;
        double lo = to.X1 + m, hi = to.X2 - m;
        if (lo > hi) return false;
        double flo = from.X1 + m * 0.6, fhi = from.X2 - m * 0.6;
        if (flo > fhi) flo = fhi = (from.X1 + from.X2) / 2;
        var options = new List<(double t, double l)>();
        double outL = to.X1 - U(12), outR = to.X2 + U(12);
        if (outL >= flo && outL <= fhi) options.Add((outL, lo));
        if (outR >= flo && outR <= fhi) options.Add((outR, hi));
        double l3 = Math.Clamp(desired, lo, hi);
        double n3 = Math.Clamp(l3, flo, fhi);
        double side = Body.X < l3 ? -1 : 1;
        options.Add((Math.Clamp(n3 + side * U(24), flo, fhi), l3));
        double best = double.MaxValue;
        bool found = false;
        foreach (var (t, l) in options)
        {
            if (Math.Abs(l - t) > MaxJumpDist) continue;
            double cost = Math.Abs(t - Body.X) + Math.Abs(l - desired) * 0.4;
            if (cost < best)
            {
                best = cost;
                takeoff = t;
                landing = l;
                found = true;
            }
        }
        return found;
    }

    void OnDetached()
    {
        airMaxVY = 0;
        hopping = false;
        pouncing = false;
        ForcePosture(Posture.Air);
        if (Body.Lost is 1 or 4)
        {
            lostPending = true;
            lostX = Body.X;
            lostY = Body.Y;
            Emote(Body.Lost == 4 ? "question" : "exclaim", 1.2);
        }
        Body.Lost = 0;
    }

    void OnLanded()
    {
        ForcePosture(Posture.Stand);
        double impact = airMaxVY;
        airMaxVY = 0;
        pouncing = false;
        if (hopping)
        {
            hopping = false;
            Current?.Landed(this);
            Anim.Play("land", true);
            overlayUntil = Now + 0.1;
            return;
        }
        if (dizzyPending)
        {
            dizzyPending = false;
            lostPending = false;
            if (impact > U(420)) Say("thud", 1);
            Anim.Play("land", true);
            Set(ScreenPlans.Dizzy(this));
            return;
        }
        if (lostPending)
        {
            lostPending = false;
            if (impact > U(420)) Say("thud", 1);
            Anim.Play("land", true);
            Set(ScreenPlans.LookBack(this, lostX, lostY));
            return;
        }
        if (impact > U(420))
        {
            Say("thud", 1);
            var seq = new Sequence(3, "Landing",
                new PlayOnce("land", null, 0.4),
                new Rest(Posture.Stand, 0.4),
                Rng.NextDouble() < 0.6 ? new PlayOnce("shake", null, 1, "sniff") : null,
                new Custom("Dazed", (m, c, dt) => { if (c.T == dt) m.Emote(m.Rng.NextDouble() < 0.5 ? "question" : "sparkle"); m.Live(dt); return c.T < 1.2; }));
            Set(seq);
            Happiness = Math.Max(-1, Happiness - 0.03);
            return;
        }
        Anim.Play("land", true);
        overlayUntil = Now + 0.15;
        if (Current == null) Set(new Rest(Posture.Stand, 0.4 + Rng.NextDouble()));
        else Current.Landed(this);
    }

    public void TrackAir()
    {
        if (!Body.OnGround) airMaxVY = Math.Max(airMaxVY, Body.VY);
    }

    public void Alert(double seconds = 1.2)
    {
        alertUntil = Now + seconds;
    }

    public void Pant(double seconds) => pantUntil = Now + seconds;

    public void Tilt(double seconds)
    {
        tiltSide = Rng.NextDouble() < 0.5 ? 1 : -1;
        tiltUntil = Now + seconds;
    }
}
