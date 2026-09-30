using MiloBot.World;

namespace MiloBot.Brain;

public partial class Milo
{
    public double ShadesUntil = -1;
    double rideSpeed, typingStreak, lostX, lostY;
    int lastRideSign;
    bool dizzyPending, lostPending, pouncing;
    readonly Queue<double> reversals = new();
    readonly Queue<double> frustrations = new();

    public int StageOf(string kind, string label, bool bump = true)
    {
        var info = Save.SeenOf(kind, label);
        double days = (DateTime.Now - info.Last).TotalDays;
        double eff = info.Count * Math.Pow(0.5, days / 7);
        int stage = eff < 1 ? 0 : eff < 3 ? 1 : eff < 7 ? 2 : 3;
        if (bump)
        {
            info.Count++;
            info.Last = DateTime.Now;
        }
        return stage;
    }

    void UpdateRide(double dt)
    {
        var p = Body.Platform;
        if (!Body.OnGround || p == null || p.IsFloor)
        {
            rideSpeed = 0;
            lastRideSign = 0;
            return;
        }
        double v = Math.Sqrt(Body.PlatformDX * Body.PlatformDX + Body.PlatformDY * Body.PlatformDY) / Math.Max(dt, 0.001);
        rideSpeed += (v - rideSpeed) * Math.Min(1, dt * 8);
        if (Math.Abs(Body.PlatformDX) > 2)
        {
            int sign = Math.Sign(Body.PlatformDX);
            if (lastRideSign != 0 && sign != lastRideSign) reversals.Enqueue(Now);
            lastRideSign = sign;
        }
        while (reversals.Count > 0 && Now - reversals.Peek() > 1.8) reversals.Dequeue();
        if (Current is Ride && reversals.Count >= 4)
        {
            reversals.Clear();
            Fling();
            return;
        }
        if (rideSpeed > U(50) && Current is not Ride && Current is not Held && (Current == null || Current.Priority < 3 || Current is Nap))
            Set(new Ride());
    }

    void Fling()
    {
        double dir = lastRideSign != 0 ? lastRideSign : Facing;
        Current?.Exit(this);
        Current = null;
        Body.Detach();
        Body.VX = dir * U(90 + Rng.NextDouble() * 60);
        Body.VY = -U(200);
        ForcePosture(Posture.Air);
        airMaxVY = 0;
        hopping = false;
        dizzyPending = true;
        Emote("exclaim", 1);
        Say("yip", 1);
        Save.Stat("dizzy");
    }

    public void Leap(double tx, double ty)
    {
        double g = U(900);
        double h = Math.Clamp(Body.Y - ty, U(30), MaxJumpUp * 0.95);
        double vy = Math.Sqrt(2 * g * h);
        double tUp = vy / g;
        Body.VX = Math.Clamp((tx - Body.X) / tUp, -U(220), U(220));
        Body.VY = -vy;
        Facing = tx >= Body.X ? 1 : -1;
        Body.Detach();
        ForcePosture(Posture.Air);
        hopping = true;
        pouncing = true;
        Energy = Math.Max(0, Energy - 0.01);
    }

    public bool CursorUnderMe(double maxDy)
    {
        var p = Body.Platform;
        if (p == null || p.IsFloor || !Body.OnGround) return false;
        if (WindowRect(p.Hwnd) is not RectD r) return false;
        return r.Contains(W.CursorX, W.CursorY) && W.CursorY > p.Y && W.CursorY - p.Y < maxDy && p.Contains(W.CursorX);
    }

    void UpdateTyping(double dt)
    {
        if (W.KeysPerSecond > 1.5) typingStreak += dt;
        else typingStreak = Math.Max(0, typingStreak - dt * 3);
    }

    public void OnHeard(string id, bool addressed)
    {
        if (Hidden || Posture == Posture.Held) return;
        bool coding = W.ForegroundKind is AppKind.Coding or AppKind.Terminal;
        if (coding && id == "name")
        {
            if (Rng.NextDouble() < 0.7) return;
            addressed = false;
        }
        if (!Memory.Cool("heard:" + id, id == "name" ? 6 : addressed ? 30 : 240, Now)) return;
        Log.Write("heard: " + id + (addressed ? " (for me)" : ""));
        Save.Stat("words");
        if (id == "good") Rewarded();
        if (Asleep)
        {
            bool wake = addressed && id is "name" or "walk" or "treat" or "squirrel" && Rng.NextDouble() < (id == "name" ? 0.35 : 0.8);
            if (!wake)
            {
                Anim.Play("sleep_twitch", true);
                return;
            }
            Emote("exclaim", 1.2);
            ForcePosture(Posture.Lie);
            Energy = Math.Max(Energy, 0.6);
        }
        if (!addressed)
        {
            if (Current != null && Current.Priority >= 2) return;
            Alert(1.4);
            Face(W.CursorX, 10);
            Emote(HeardIcon(id), 1.4);
            return;
        }
        if (Current != null && Current.Priority >= 3 && Current is not Nap) return;
        Boredom = Math.Max(0, Boredom - 0.2);
        switch (id)
        {
            case "name":
                Emote(Social > 0.5 ? "heart" : "question", 1.8);
                Set(Rng.NextDouble() < 0.55 ? Plans.SeekUser(this) : ScreenPlans.TiltAtCursor(this));
                break;
            case "good":
                Happiness = Math.Min(1, Happiness + 0.15);
                Save.AddFriend(0.5);
                Set(ScreenPlans.Proud(this));
                break;
            case "walk":
                Excitement = 1;
                Emote("leash", 2);
                Say("yip", 2);
                if (Home.Active && Memory.Cool("leash", 600, Now))
                {
                    WalkAccepted = false;
                    Set(new Sequence(2, "Leash", new PlayOnce("spin", Posture.Stand, 1.2, "happy", null, 2), Plans.BringLeash(this)));
                }
                else Set(Plans.Zoomies(this));
                break;
            case "treat":
                Emote("food", 2);
                Set(Plans.SeekUser(this, true));
                break;
            case "ball":
                Emote("ball", 2);
                var ball = Toys.Ball;
                if (ball != null && !ball.InMouth && !ball.HeldByUser) Set(new Fetch(ball));
                else Set(ScreenPlans.PlayBowAt(this, W.CursorX));
                break;
            case "cat":
                Set(ScreenPlans.WhereCat(this));
                break;
            case "food":
                Emote("food", 2);
                if (Home.Active) Set(new Meal());
                else Set(Plans.SeekUser(this, true));
                break;
            case "sleep":
                Say("yawn", 5);
                if (Energy < 0.7) GoSleep();
                else Set(new PlayOnce("yawn", Posture.Sit, 1.6, null, "z", 1));
                break;
            case "vet" or "bath":
                Set(Plans.Scared(this, W.CursorX));
                break;
            case "squirrel":
                Set(ScreenPlans.Squirrel(this));
                break;
            default:
                if (id.StartsWith("trick:")) DoTrick(id[6..]);
                break;
        }
    }

    static string HeardIcon(string id) => id switch
    {
        "walk" => "leash",
        "treat" or "food" => "food",
        "ball" => "ball",
        "cat" => "cat",
        "sleep" => "z",
        "good" => "heart",
        _ => "question"
    };

    public void OnFrustrated()
    {
        if (Asleep || Hidden || !Body.OnGround) return;
        if (!Memory.Cool("frustrated", 45, Now)) return;
        while (frustrations.Count > 0 && Now - frustrations.Peek() > 600) frustrations.Dequeue();
        frustrations.Enqueue(Now);
        if (Current != null && Current.Priority >= 2) return;
        if (frustrations.Count >= 3)
        {
            frustrations.Clear();
            Emote("heart", 2);
            Set(Plans.SeekUser(this));
            return;
        }
        Set(ScreenPlans.TiltAtCursor(this));
    }

    public void OnRageClicks()
    {
        if (Asleep || Hidden || !Body.OnGround || Hover) return;
        if (!Memory.Cool("rage", 25, Now)) return;
        Face(W.CursorX, 0);
        Alert(1.5);
        Emote(Rng.NextDouble() < 0.5 ? "exclaim" : "sweat", 1.4);
        if (CursorDistance() < U(90) && (Current == null || Current.Priority < 2))
            Set(new PlayOnce("scared", Posture.Stand, 0.8, "whine", null, 1));
    }

    public void OnScreenshotKey()
    {
        if (Hidden || Asleep || !Body.OnGround) return;
        if (!Memory.Cool("screenshot", 8, Now)) return;
        if (Current != null && Current.Priority >= 3) return;
        Emote("sparkle", 1.2);
        PosePhoto();
    }

    public void OnFlash(WorldEvent e)
    {
        if (Hidden || !Memory.Cool("flash", 90, Now)) return;
        if (Asleep)
        {
            if (Rng.NextDouble() < 0.4) Anim.Play("sleep_twitch", true);
            return;
        }
        if (Current != null && Current.Priority >= 2) return;
        Alert(1.5);
        Emote("mail", 2);
        if (W.UserIdle > 30 && Rng.NextDouble() < 0.6) Set(ScreenPlans.TellUser(this));
    }

    public void OnToast(RectD r)
    {
        if (Hidden || !Memory.Cool("toast", 20, Now)) return;
        if (Asleep)
        {
            Anim.Play("sleep_twitch", true);
            return;
        }
        if (Current != null && Current.Priority >= 2) return;
        int stage = StageOf("notify", "toast");
        Log.Write($"toast at {r.CenterX:0},{r.Bottom:0} stage {stage}");
        switch (stage)
        {
            case 0 or 1:
                Set(ScreenPlans.CheckToast(this, r, stage == 0));
                break;
            case 2:
                Face(r.CenterX, 0);
                Alert(1.5);
                Emote("mail", 1.6);
                break;
            default:
                if (Rng.NextDouble() < 0.4) { Face(r.CenterX, 0); Emote("dots", 1.4); }
                break;
        }
    }

    void OnDesktopShown()
    {
        if (W.Platforms.Any(p => !p.IsFloor) || !Memory.Cool("desktop", 600, Now)) return;
        if (Current != null && Current.Priority >= 1) return;
        Emote("sparkle", 1.5);
        if (Energy > 0.55 && Rng.NextDouble() < 0.5) Set(Plans.Zoomies(this));
        else Set(Plans.Wander(this));
    }
}

public class Ride : Behavior
{
    int stage;
    double quiet, fx;

    public override int Priority => 3;
    public override string Name => "Ride";

    public override void Enter(Milo m)
    {
        stage = m.StageOf("window", "ride");
        m.Save.Stat("rides");
        m.ForcePosture(Posture.Stand);
        m.Emote(stage == 0 ? "exclaim" : stage == 1 ? "question" : "sparkle", 1.2);
        if (stage == 0) m.Say("yip", 2);
    }

    protected override bool Tick(Milo m, double dt)
    {
        if (!m.Body.OnGround) return false;
        m.ForcePosture(Posture.Stand);
        bool moving = m.Now - m.Body.LastPlatformMove < 0.25;
        if (Math.Abs(m.Body.PlatformDX) > 1) m.Facing = m.Body.PlatformDX > 0 ? 1 : -1;
        quiet = moving ? 0 : quiet + dt;
        m.Anim.Play(stage == 0 ? "scared" : moving ? "surf" : "idle");
        fx -= dt;
        if (moving && fx <= 0)
        {
            fx = 1 + m.Rng.NextDouble() * 0.6;
            if (stage == 0) m.Float("sweat", 0.8);
            else if (m.Rng.NextDouble() < 0.5) m.Float(stage >= 2 ? "heart" : "sparkle", 1);
        }
        if (quiet > 0.9)
        {
            m.Set(ScreenPlans.AfterRide(m, stage));
            return false;
        }
        return T < 90;
    }
}

public class Perch : Behavior
{
    double swipeCd, duration, outside, blinkAt = 3;

    public override int Priority => 1;
    public override string Name => "Perch";

    public override void Enter(Milo m)
    {
        duration = 10 + m.Rng.NextDouble() * 14;
        m.Emote("exclaim", 1);
    }

    public void Swipe(Milo m)
    {
        if (swipeCd > 0 || m.Posture != Posture.Lie) return;
        swipeCd = 0.8 + m.Rng.NextDouble() * 0.8;
        m.Anim.Play("peek_paw", true);
        m.Save.Stat("swats");
        if (m.Rng.NextDouble() < 0.25) m.Say("yip", 3);
    }

    protected override bool Tick(Milo m, double dt)
    {
        var p = m.Body.Platform;
        if (p == null || p.IsFloor) return false;
        double cx = m.W.CursorX, cy = m.W.CursorY;
        bool inside = m.WindowRect(p.Hwnd) is RectD r && r.Contains(cx, cy) && cy > p.Y;
        outside = inside ? 0 : outside + dt;
        if (outside > 4) return false;
        double dx = cx - m.Body.X;
        if (inside && Math.Abs(dx) > m.U(50) && p.Contains(cx, m.Body.HalfWidth))
        {
            if (!m.To(Posture.Stand)) return true;
            m.WalkTo(cx - Math.Sign(dx) * m.U(12), Gait.Happy, dt, 3);
            return T < duration + 15;
        }
        if (inside) m.Face(cx, 0);
        if (!m.To(Posture.Lie)) return true;
        swipeCd -= dt;
        bool close = inside && Math.Abs(dx) < m.U(32) && cy - p.Y < m.U(75) && (dx > 0) == (m.Facing > 0);
        if (close && m.W.CursorSpeed > 30) Swipe(m);
        bool pawing = m.Anim.Name == "peek_paw" && !m.Anim.Finished;
        if (!pawing)
        {
            if (T > blinkAt) { blinkAt = T + 2 + m.Rng.NextDouble() * 4; m.Anim.Play("peek_blink", true); }
            else if (m.Anim.Name != "peek_blink" || m.Anim.Finished) m.Anim.Play("peek");
        }
        return T < duration || close;
    }
}

public static class ScreenPlans
{
    static Custom Once(string name, Action<Milo> first, Func<Milo, Custom, double, bool> tick, int priority = 2) =>
        new(name, (m, c, dt) =>
        {
            if (c.N == 0) { c.N = 1; first?.Invoke(m); }
            return tick(m, c, dt);
        }, null, priority);

    public static Behavior AfterRide(Milo m, int stage) => stage switch
    {
        0 => new Sequence(2, "AfterRide", new PlayOnce("shake", Posture.Stand, 1.2, "sniff", null, 2),
            Once("Grumpy", mm => { mm.Face(mm.W.CursorX, 0); mm.Emote("puff", 1.6); }, (mm, c, dt) => { if (!mm.To(Posture.Sit)) return true; mm.Anim.Play("sit"); return c.T < 2.5; })),
        1 => new Sequence(2, "AfterRide", new PlayOnce("shake", Posture.Stand, 1.2, null, null, 2),
            Once("Huh", mm => mm.Emote("question", 1.5), (mm, c, dt) => { mm.Anim.Play("idle_wag"); return c.T < 1.5; })),
        _ => Once("Again", mm => { mm.Face(mm.W.CursorX, 0); mm.Emote("heart", 1.6); mm.Say("happy", 5); }, (mm, c, dt) =>
        {
            mm.Face(mm.W.CursorX, 0);
            mm.Anim.Play("happy");
            if (c.N == 1 && c.T > 0.4 && mm.Body.OnGround) { c.N = 2; mm.Hop(0, 110, 0); }
            return c.T < 2.2;
        })
    };

    public static Behavior Dizzy(Milo m) => new Sequence(3, "Dizzy",
        Once("Spinning", mm => { mm.Emote("dizzy", 3.2); mm.Say("whine", 4); }, (mm, c, dt) =>
        {
            if (!mm.To(Posture.Sit)) return true;
            mm.Anim.Play("dizzy");
            return c.T < 3.2;
        }, 3),
        new PlayOnce("shake", Posture.Stand, 1.2, "sneeze", null, 3),
        Once("Recover", mm => mm.Emote(mm.Rng.NextDouble() < 0.5 ? "puff" : "question", 1.4), (mm, c, dt) => { mm.Anim.Play("idle"); return c.T < 1.2; }, 3));

    public static Behavior LookBack(Milo m, double x, double y)
    {
        int stage = m.StageOf("window", "vanish");
        m.Save.Stat("falls");
        if (stage >= 2)
            return new Sequence(2, "LookBack", Once("Annoyed", mm => { mm.Face(mm.W.CursorX, 0); mm.Emote(mm.Rng.NextDouble() < 0.5 ? "puff" : "dots", 1.8); }, (mm, c, dt) =>
            {
                if (!mm.To(Posture.Sit)) return true;
                mm.Anim.Play("sit");
                return c.T < 2.5;
            }));
        return new Sequence(2, "LookBack",
            Once("WhereDidItGo", mm => { mm.Face(x, 0); mm.Emote("question", 2.2); }, (mm, c, dt) =>
            {
                mm.Face(x, 0);
                mm.Anim.Play("idle_look");
                return c.T < 2.2;
            }),
            stage == 0 ? new PlayOnce("sniff", Posture.Stand, 1.8, "sniff", null, 2) : new PlayOnce("shake", Posture.Stand, 1.2, null, null, 2));
    }

    public static Behavior TiltAtCursor(Milo m) => Once("TiltAt", mm => { mm.Face(mm.W.CursorX, 0); mm.Emote("question", 2); mm.Tilt(1.3); }, (mm, c, dt) =>
    {
        mm.Face(mm.W.CursorX, 0);
        if (!mm.To(Posture.Front)) return true;
        if (c.N == 1 && c.T > 1.4) { c.N = 2; mm.Tilt(1.3); }
        mm.Live(dt, false);
        return c.T < 3.2;
    }, 1);

    public static Behavior Proud(Milo m) => Once("Proud", mm => { mm.Float("heart"); mm.Float("heart"); mm.Say("happy", 4); }, (mm, c, dt) =>
    {
        mm.Face(mm.W.CursorX, 0);
        if (!mm.To(Posture.Front)) return true;
        mm.Anim.Play(c.T < 1 ? "front_happy" : "wink");
        return c.T < 2.4;
    }, 1);

    public static Behavior PlayBowAt(Milo m, double x) => new Sequence(1, "PlayBow",
        new Custom("Face", (mm, c, dt) => { mm.Face(x, 0); return false; }, null, 1),
        new PlayOnce("stretch", Posture.Stand, 2.2, "yip", null, 1),
        new Custom("Wag", (mm, c, dt) => { mm.Face(x, 0); mm.Anim.Play("happy"); return c.T < 1.5; }, null, 1));

    public static Behavior WhereCat(Milo m) => Once("WhereCat", mm => { mm.Emote("exclaim", 1.4); mm.Say("growl", 5); }, (mm, c, dt) =>
    {
        if (!mm.To(Posture.Stand)) return true;
        int look = (int)(c.T / 0.8);
        if (look != (int)c.A) { c.A = look; mm.Facing = -mm.Facing; }
        mm.Anim.Play(c.T < 1.2 ? "growl" : "alert");
        return c.T < 3.4;
    }, 1);

    public static Behavior Squirrel(Milo m)
    {
        var s = m.W.ScreenAt(m.Body.X, m.Body.Y - 10);
        double edge = s == null ? m.Body.X : (m.Rng.NextDouble() < 0.5 ? s.Work.Left + m.U(30) : s.Work.Right - m.U(30));
        return new Sequence(2, "Squirrel",
            Once("What", mm => { mm.Emote("exclaim", 1.2); mm.Say("yip", 1); }, (mm, c, dt) => { mm.Anim.Play("alert"); return c.T < 0.5; }),
            new GoTo(edge, m.Body.Y, Gait.Zoom, 2, 6),
            new Custom("FaceEdge", (mm, c, dt) => { mm.Face(edge < mm.Body.X + 1 ? edge - 100 : edge + 100, 0); return false; }, null, 2),
            new PlayOnce("bark", Posture.Stand, 1.2, "bark", "exclaim", 2),
            new PlayOnce("bark", Posture.Stand, 1.2, "bark", null, 2),
            Once("Gone", mm => mm.Emote("question", 1.6), (mm, c, dt) => { mm.Anim.Play("idle_look"); return c.T < 1.6; }));
    }

    public static Behavior TellUser(Milo m) => new Sequence(2, "TellUser",
        new GoTo(mm => (mm.W.CursorX + (mm.Body.X < mm.W.CursorX ? -1 : 1) * mm.U(14), mm.W.CursorY), Gait.Run, 2, 8),
        Once("Hey", mm => { mm.Face(mm.W.CursorX, 0); mm.Emote("mail", 2); mm.Say("yip", 3); }, (mm, c, dt) =>
        {
            if (!mm.To(Posture.Front)) return true;
            mm.Anim.Play(c.T < 0.8 ? "front_bark" : "front_look");
            return c.T < 3;
        }));

    public static Behavior CheckToast(Milo m, RectD r, bool first)
    {
        double x = Math.Clamp(r.CenterX, r.Left + m.U(20), r.Right - m.U(20));
        return new Sequence(2, "Toast",
            Once("Notice", mm => { mm.Face(x, 0); mm.Emote("mail", 1.6); if (first) mm.Say("yip", 3); }, (mm, c, dt) => { mm.Anim.Play("alert"); return c.T < 0.6; }),
            new GoTo(x, r.Bottom + 4, first ? Gait.Run : Gait.Happy, 2, 10),
            Once("LookUp", mm => mm.Face(x, 0), (mm, c, dt) =>
            {
                if (!mm.To(Posture.Sit)) return true;
                mm.Anim.Play("sit_look");
                if (c.N == 1 && c.T > 2) { c.N = 2; mm.Emote(first ? "question" : "dots", 1.4); }
                return c.T < 3.5;
            }),
            first ? new PlayOnce("sniff", Posture.Stand, 1.6, "sniff", null, 2) : null);
    }

    public static Behavior TypingBuddy(Milo m)
    {
        var fg = m.W.ForegroundRect;
        double side = m.Rng.NextDouble() < 0.5 ? -1 : 1;
        double x = Math.Clamp(fg.CenterX + side * fg.Width * 0.3, fg.Left + m.U(20), fg.Right - m.U(20));
        return new Sequence(1, "TypingBuddy",
            new GoTo(x, fg.Top, Gait.Happy, 1, 10),
            new Custom("Company", (mm, c, dt) =>
            {
                mm.Face(fg.CenterX, 0);
                var want = c.T > 12 && mm.Energy < 0.7 ? Posture.Lie : Posture.Sit;
                if (!mm.To(want)) return true;
                mm.Live(dt, false);
                if (mm.W.KeysPerSecond > 3 && mm.Memory.Cool("buddy-dots", 9, mm.Now) && mm.Rng.NextDouble() < 0.5) mm.Emote("dots", 1.4);
                if (mm.W.KeysPerSecond < 0.2) c.A += dt; else c.A = 0;
                return c.T < 35 && c.A < 10;
            }, null, 1));
    }
}
