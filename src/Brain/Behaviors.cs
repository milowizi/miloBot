namespace MiloBot.Brain;

public class Nap : Behavior
{
    readonly double minTime;
    double sleepyUntil = -1, nextDream, dreamRunUntil;
    public bool AtHome;

    public Nap(double minTime = 40) { this.minTime = minTime; }

    public override string Name => "Nap";

    public override void Exit(Milo m) => m.RenderLift = 0;

    protected override bool Tick(Milo m, double dt)
    {
        if (AtHome && m.Home.Active && Math.Abs(m.Body.X - m.Home.BedX) < m.U(10) && Math.Abs(m.Body.Y - m.Home.Y) < 3) m.RenderLift = 6;
        else m.RenderLift = 0;
        if (m.Posture != Posture.Sleep)
        {
            if (!m.To(Posture.Lie)) return true;
            if (sleepyUntil < 0) sleepyUntil = T + 2.5 + m.Rng.NextDouble() * 3;
            if (T < sleepyUntil)
            {
                m.Anim.Play("lie_sleepy");
                return true;
            }
            m.ForcePosture(Posture.Sleep);
            m.Say("yawn", 30);
            nextDream = T + 12 + m.Rng.NextDouble() * 10;
        }
        m.Energy = Math.Min(1, m.Energy + dt / 240);
        if (T < dreamRunUntil)
        {
            m.Anim.Play("dream_run");
            if (m.Memory.Cool("dreamyip", 1.4, m.Now) && m.Rng.NextDouble() < 0.5) m.Say("whine", 1);
        }
        else m.Live(dt, false);
        if (T > nextDream)
        {
            nextDream = T + 14 + m.Rng.NextDouble() * 14;
            string dream = m.DreamIcon();
            m.Dream(dream);
            if (dream is "ball" or "cat" or "stick" && m.Rng.NextDouble() < 0.6) dreamRunUntil = T + 2.5;
        }
        bool userAway = m.W.UserIdle > 120;
        if (T > minTime && m.Energy > 0.97 && !userAway && !m.IsNight) return false;
        return T < 60 * 30;
    }
}

public class Held : Behavior
{
    public override int Priority => 10;
    public override string Name => "Held";

    public override void Enter(Milo m)
    {
        m.ForcePosture(Posture.Held);
        if (m.Rng.NextDouble() < 0.35) m.Say("yip", 2);
        m.Emote(m.Happiness > 0.5 ? "heart" : "exclaim", 1.2);
    }

    protected override bool Tick(Milo m, double dt)
    {
        m.Anim.Play(m.Happiness > 0.55 ? "held_happy" : "held");
        double swing = m.W.CursorVX;
        if (Math.Abs(swing) > 250) m.Anim.SetFrame(swing * m.Facing > 0 ? 3 : 1);
        return true;
    }
}

public class Petted : Behavior
{
    double lastPet;
    double nextHeart;
    bool wasAsleep;

    public override int Priority => 3;
    public override string Name => "Petted";

    public override void Enter(Milo m)
    {
        lastPet = 0;
        wasAsleep = m.Asleep;
        m.Save.Stat("pets");
        m.Save.AddFriend(1);
        m.Rewarded();
        if (!wasAsleep && m.Rng.NextDouble() < 0.5) m.Say("happy", 6);
    }

    protected override bool Tick(Milo m, double dt)
    {
        if (m.Petting) lastPet = T;
        m.Happiness = Math.Min(1, m.Happiness + dt * 0.12);
        m.Social = Math.Max(0, m.Social - dt * 0.2);
        if (T >= nextHeart && m.Petting)
        {
            nextHeart = T + 0.55;
            m.Float("heart", 1.4);
        }
        if (wasAsleep)
        {
            if (T > 2.5 && m.Posture == Posture.Sleep && m.Rng.NextDouble() < dt * 0.3) m.ForcePosture(Posture.Belly);
            if (m.Posture is Posture.Sleep or Posture.Belly) m.Anim.Play(m.Posture == Posture.Belly ? "belly" : "sleep");
            return T - lastPet < 2.5;
        }
        var want = m.Posture is Posture.Lie or Posture.Belly ? Posture.Belly : Posture.Front;
        if (!m.To(want)) return true;
        m.Live(dt, false);
        if (want == Posture.Front && !m.Petting) m.Pant(1.5);
        return T - lastPet < 1.6;
    }
}

public class Boop : Behavior
{
    readonly int clicks;
    public Boop(int clicks) { this.clicks = clicks; }
    public override int Priority => 3;
    public override string Name => "Boop";
    bool grumpy;

    public override void Enter(Milo m)
    {
        m.Social = Math.Max(0, m.Social - 0.1);
        if (m.Asleep)
        {
            grumpy = true;
            m.Emote("puff", 1.6);
            m.Happiness -= 0.05;
            return;
        }
        m.Face(m.W.CursorX, 0);
        if (m.Happiness > 0.45) { m.Emote("heart"); m.Say("happy", 4); m.Pant(3); }
        else { m.Emote("question"); m.Tilt(2); if (m.Rng.NextDouble() < 0.3) m.Say("yip", 5); }
    }

    protected override bool Tick(Milo m, double dt)
    {
        if (grumpy)
        {
            if (!m.To(Posture.Lie)) return true;
            if (T < 3) { m.Anim.Play("lie_sleepy"); return true; }
            return false;
        }
        if (!m.To(Posture.Front)) return true;
        m.Live(dt, false);
        return T < 3.5;
    }
}

public static class Plans
{
    public static Behavior Wander(Milo m)
    {
        var p = m.Body.Platform;
        if (p == null) return new Rest(Posture.Stand, 2);
        double range = Math.Min(p.Width / 2, m.U(140));
        double x = m.Body.X + (m.Rng.NextDouble() * 2 - 1) * range;
        if (m.Social > 0.4 && Math.Abs(m.W.CursorX - m.Body.X) < m.U(300)) x = (x + m.W.CursorX) / 2;
        bool sniffy = m.Rng.NextDouble() < 0.35;
        return new Sequence(0, "Wander",
            new GoTo(x, p.Y, sniffy ? Gait.Sniff : Gait.Walk),
            sniffy && m.Rng.NextDouble() < 0.5 ? new PlayOnce("sniff", Posture.Stand, 1.5 + m.Rng.NextDouble() * 1.5, "sniff") : null,
            new Rest(Posture.Stand, 1 + m.Rng.NextDouble() * 3));
    }

    public static Behavior Explore(Milo m)
    {
        var cur = m.Body.Platform;
        if (cur == null) return Wander(m);
        var options = m.W.Platforms.Where(p => p != cur && p.Width > m.Body.HalfWidth * 3 && p.Y >= cur.Y - m.MaxJumpUp * 2.2).ToList();
        if (options.Count == 0) return Wander(m);
        var fg = m.W.ForegroundHwnd;
        var pick = options.FirstOrDefault(p => p.Hwnd == fg && m.Rng.NextDouble() < 0.6)
                   ?? options[m.Rng.Next(options.Count)];
        double x = pick.X1 + m.Body.HalfWidth + m.Rng.NextDouble() * Math.Max(1, pick.Width - m.Body.HalfWidth * 2);
        return new Sequence(0, "Explore",
            new GoTo(x, pick.Y, Gait.Walk),
            new PlayOnce("sniff", Posture.Stand, 1.5, "sniff"),
            new Rest(Posture.Stand, 1 + m.Rng.NextDouble() * 2));
    }

    public static Behavior Loaf(Milo m)
    {
        double t = 8 + m.Rng.NextDouble() * 14;
        if (m.Energy < 0.5 && m.Rng.NextDouble() < 0.6)
            return new Sequence(0, "Loaf", new Rest(Posture.Sit, 3 + m.Rng.NextDouble() * 4), new Rest(Posture.Lie, t));
        return new Rest(Posture.Sit, t);
    }

    public static Behavior Nap(Milo m) => new Sequence(0, "Nap", new Rest(Posture.Lie, 3 + m.Rng.NextDouble() * 4), new Nap(m.W.UserIdle > 120 ? 20 : 45));

    public static Behavior Stretch(Milo m) => new Sequence(0, "Stretch",
        new PlayOnce("stretch", Posture.Stand, 2.5, "yawn"),
        m.Rng.NextDouble() < 0.5 ? new PlayOnce("shake", Posture.Stand, 1) : null,
        new Rest(Posture.Stand, 1));

    public static Behavior SeekUser(Milo m, bool beg = false)
    {
        return new Sequence(1, "SeekUser",
            new GoTo(mm => (mm.W.CursorX + (mm.Body.X < mm.W.CursorX ? -1 : 1) * mm.U(10), mm.W.CursorY), m.Social > 0.7 ? Gait.Run : Gait.Happy, 1, 6),
            new Custom("LookUp", (mm, c, dt) =>
            {
                if (c.T == dt) { mm.Face(mm.W.CursorX, 0); if (mm.Social > 0.6) mm.Emote("heart"); }
                if (!mm.To(Posture.Front)) return true;
                mm.Live(dt, false);
                mm.Social = Math.Max(0, mm.Social - dt * 0.03);
                if (c.N == 0 && c.T > 2 && mm.Social > 0.75 && mm.W.UserIdle < 5) { c.N = 1; mm.Say("yip", 20); mm.Emote("exclaim"); }
                return c.T < 6 + mm.Social * 6;
            }, null, 1),
            beg ? new PlayOnce("beg", Posture.Sit, 3, "whine") : null);
    }

    public static Behavior Zoomies(Milo m)
    {
        var p = m.Body.Platform;
        if (p == null) return Wander(m);
        m.Excitement = 1;
        m.Save.Stat("zoomies");
        int laps = 5 + m.Rng.Next(5);
        double span = Math.Min(p.Width / 2 - m.Body.HalfWidth, m.U(260));
        double center = Math.Clamp(m.Body.X, p.X1 + span, p.X2 - span);
        return new Sequence(1, "Zoomies",
            new PlayOnce("bark", Posture.Stand, 0.8, "yip", "exclaim", 1),
            new Custom("Zoom", (mm, c, dt) =>
            {
                if (!mm.Body.OnGround) return true;
                if (!mm.To(Posture.Stand)) return true;
                if (c.N == 0) { c.N = 1; c.A = center + (mm.Rng.NextDouble() < 0.5 ? -1 : 1) * span * (0.5 + mm.Rng.NextDouble() * 0.5); c.B = 0; }
                if (c.B > 0)
                {
                    c.B -= dt;
                    mm.Body.X += mm.Facing * mm.U(90) * (c.B / 0.22) * dt;
                    var pp = mm.Body.Platform;
                    if (pp != null) mm.Body.X = Math.Clamp(mm.Body.X, pp.X1 + mm.Body.HalfWidth * 0.6, pp.X2 - mm.Body.HalfWidth * 0.6);
                    mm.Anim.Play("skid");
                    if (mm.Rng.NextDouble() < dt * 25) mm.Dust();
                    if (c.B <= 0)
                    {
                        if (--laps <= 0) return false;
                        c.A = center + (mm.Body.X > center ? -1 : 1) * span * (0.4 + mm.Rng.NextDouble() * 0.6);
                    }
                    return true;
                }
                if (mm.Rng.NextDouble() < dt * 0.5) mm.Hop(mm.Facing, 130, 60);
                else if (mm.Rng.NextDouble() < dt * 0.25) { mm.Anim.Play("spin", true); mm.Hop(0, 80, 0); }
                if (mm.WalkTo(c.A, Gait.Zoom, dt, 4)) { c.B = 0.22; mm.Dust(); mm.Dust(); }
                return c.T < 25;
            }, null, 1),
            new Custom("Flop", (mm, c, dt) =>
            {
                if (c.N == 0) { c.N = 1; mm.Pant(6); mm.Float("sweat", 1.2); mm.Say("pant", 5); }
                if (!mm.To(Posture.Lie)) return true;
                mm.Anim.Play("lie");
                return c.T < 4;
            }, null, 1));
    }

    public static Behavior TailChase(Milo m) => new Sequence(1, "TailChase",
        new Custom("Spin", (mm, c, dt) =>
        {
            if (!mm.To(Posture.Stand)) return true;
            mm.Anim.Play("spin");
            mm.Anim.Speed = 1.4;
            if (mm.Body.OnGround && mm.Rng.NextDouble() < dt * 2) mm.Hop(0, 60, 0);
            return c.T < 2.6;
        }, null, 1),
        new Custom("Dizzy", (mm, c, dt) =>
        {
            if (c.N == 0) { c.N = 1; mm.Emote("sparkle", 1.6); }
            if (!mm.To(Posture.Sit)) return true;
            mm.Anim.Play("sit_look");
            return c.T < 2;
        }, null, 1));

    public static Behavior Howl(Milo m) => new Sequence(1, "Howl",
        new Custom("Howl", (mm, c, dt) =>
        {
            if (!mm.To(Posture.Sit)) return true;
            if (c.N == 0) { c.N = 1; mm.Say("howl", 2); mm.Float("note", 2); }
            mm.Anim.Play("howl");
            return c.T < 3.2;
        }, null, 1));

    public static Behavior EdgeScratch(Milo m)
    {
        var p = m.Body.Platform;
        var s = m.W.ScreenAt(m.Body.X, m.Body.Y - 10);
        if (p == null || s == null) return Wander(m);
        bool leftWall = p.X1 <= s.Bounds.Left + 2 && m.W.ScreenAt(p.X1 - 20, p.Y - 20) == s;
        bool rightWall = p.X2 >= s.Bounds.Right - 2 && m.W.ScreenAt(p.X2 + 20, p.Y - 20) == s;
        if (!leftWall && !rightWall) return Wander(m);
        bool goLeft = leftWall && (!rightWall || m.Body.X - p.X1 < p.X2 - m.Body.X);
        double x = goLeft ? p.X1 : p.X2;
        return new Sequence(1, "EdgeScratch",
            new GoTo(x, p.Y, Gait.Walk, 1),
            new Custom("Scratch", (mm, c, dt) =>
            {
                mm.Facing = goLeft ? -1 : 1;
                if (!mm.To(Posture.Sit)) return true;
                mm.Anim.Play("beg");
                if (mm.Memory.Cool("scratchsnd", 0.9, mm.Now)) mm.Say("sniff", 0.5);
                return c.T < 2.8;
            }, null, 1),
            new Rest(Posture.Sit, 1.5));
    }

    public static Behavior Gift(Milo m)
    {
        var d = new Deliver(null, mm => (mm.W.CursorX + (mm.Body.X < mm.W.CursorX ? -1 : 1) * mm.U(14), mm.W.CursorY), 1, false);
        d.Dropped = (mm, t) => { mm.Save.Stat("gifts"); t.ThrownAt = -100; };
        return new Sequence(1, "Gift", d);
    }

    public static Behavior Tidy(Milo m, Toy toy)
    {
        var d = new Deliver(toy, mm => (mm.Home.BedX, mm.Home.Y), 0, false);
        d.Dropped = (mm, t) => { t.Gone = true; mm.Emote("sparkle", 1.2); };
        return new Sequence(0, "Tidy", d, new Rest(Posture.Lie, 6));
    }

    public static Behavior BringLeash(Milo m)
    {
        var d = new Deliver(null, mm => (mm.W.CursorX + (mm.Body.X < mm.W.CursorX ? -1 : 1) * mm.U(16), mm.W.CursorY), 2, true) { SpawnKind = ToyKind.Leash };
        d.Wait = mm =>
        {
            if (mm.Memory.Cool("leashwhine", 14, mm.Now)) { mm.Say("whine", 5); mm.Emote("leash", 2.5); }
            return !mm.WalkAccepted && d.T < 90;
        };
        return new Sequence(2, "Leash", d, new Custom("LeashEnd", (mm, c, dt) =>
        {
            if (c.N == 0)
            {
                c.N = 1;
                var leash = d.Item;
                if (leash != null) { leash.InMouth = false; leash.Gone = true; }
                if (mm.WalkAccepted) { mm.Float("heart"); mm.Say("happy", 1); }
                else { mm.Emote("dots", 2); }
            }
            if (!mm.To(mm.WalkAccepted ? Posture.Front : Posture.Lie)) return true;
            mm.Anim.Play(mm.WalkAccepted ? "front_happy" : "lie");
            return c.T < 3;
        }, null, 2));
    }

    public static Behavior GoToBedAndNap(Milo m)
    {
        if (!m.Home.Active) return Nap(m);
        var nap = new Nap(m.W.UserIdle > 120 ? 20 : 45) { AtHome = true };
        return new Sequence(0, "Nap", new GoTo(m.Home.BedX, m.Home.Y, Gait.Walk, 0, 1), new Rest(Posture.Lie, 2), nap);
    }

    public static Behavior Dance(Milo m)
    {
        return new Custom("Dance", (mm, c, dt) =>
        {
            if (!mm.W.Music) c.A += dt; else c.A = 0;
            if (c.N == 0 && c.B == 0) { c.B = mm.Rng.NextDouble() < 0.25 ? 1 : 2; if (c.B == 1) c.N = -1; }
            if (c.N == -1)
            {
                if (!mm.To(Posture.Sit)) return true;
                if (c.B == 1) { c.B = 3; mm.Say("howl", 2); mm.Float("note", 2); }
                mm.Anim.Play("howl");
                if (c.T > 3.5) c.N = 0;
                return true;
            }
            if (!mm.To(Posture.Stand)) return true;
            mm.Anim.Play("happy");
            mm.Anim.Speed = 0.7 + Math.Min(1.2, mm.W.AudioLevel * 3);
            if (mm.W.AudioOnset)
            {
                c.N++;
                if (c.N % 3 == 0 && mm.Body.OnGround && mm.Energy > 0.3) mm.Hop(0, 70, 0);
                if (c.N % 4 == 1) mm.Float("note", 1.6);
                if (c.N % 8 == 4) mm.Facing = -mm.Facing;
            }
            mm.Happiness = Math.Min(1, mm.Happiness + dt * 0.01);
            mm.Energy = Math.Max(0, mm.Energy - dt * 0.002);
            return c.A < 3 && c.T < 45 && mm.Energy > 0.15;
        }, null, 1);
    }

    public static Behavior Watch(Milo m, double x, double y, double seconds)
    {
        double side = m.Body.X < x ? -1 : 1;
        return new Sequence(1, "Watch",
            new GoTo(x + side * m.U(40), y, Gait.Walk, 1, 8),
            new Custom("Watching", (mm, c, dt) =>
            {
                mm.Face(x, 0);
                var want = mm.Energy < 0.45 ? Posture.Lie : Posture.Sit;
                if (!mm.To(want)) return true;
                mm.Live(dt, false);
                mm.Boredom = Math.Max(0, mm.Boredom - dt * 0.01);
                return c.T < seconds;
            }, null, 1));
    }

    public static Behavior Greet(Milo m, double awaySeconds)
    {
        bool big = awaySeconds > 300;
        return new Sequence(3, "Greet",
            m.Asleep ? new Rest(Posture.Stand, 0.3, null, 3) : null,
            big ? new PlayOnce("stretch", Posture.Stand, 2, null, null, 3) : null,
            new GoTo(mm => (mm.W.CursorX, mm.W.CursorY), big ? Gait.Run : Gait.Happy, 3, 8),
            new Custom("Hello", (mm, c, dt) =>
            {
                if (c.T == dt)
                {
                    mm.Face(mm.W.CursorX, 0);
                    mm.Say(big ? "bark" : "happy", 10);
                    mm.Emote("heart", 2);
                    mm.Pant(4);
                    mm.Social = 0;
                    mm.Happiness = Math.Min(1, mm.Happiness + 0.2);
                }
                if (!mm.To(Posture.Front)) return true;
                mm.Live(dt, false);
                if (big && c.T > 1 && c.N == 0) { c.N = 1; mm.Float("heart"); }
                return c.T < 4;
            }, null, 3));
    }

    public static Behavior ChaseCursor(Milo m)
    {
        m.Say("yip", 8);
        m.Excitement = 1;
        return new Custom("Chase", (mm, c, dt) =>
        {
            if (!mm.To(Posture.Stand)) return true;
            bool arrived = mm.WalkTo(mm.W.CursorX, Gait.Zoom, dt, 6);
            double d = mm.CursorDistance();
            if (arrived && d < mm.U(120) && mm.W.CursorY < mm.Body.Y - mm.U(30) && mm.Body.OnGround && c.A <= 0 && d < mm.U(80) + mm.MaxJumpUp)
            {
                mm.Hop(mm.Facing, 200, 20);
                c.A = 1.2;
                if (mm.Rng.NextDouble() < 0.4) mm.Say("yip", 3);
            }
            c.A -= dt;
            if (mm.W.CursorSpeed > 400) c.B = c.T;
            if (arrived && !mm.Body.OnGround) return true;
            return c.T - c.B < 2.2 && c.T < 20 && mm.Energy > 0.1;
        }, null, 1);
    }

    public static Behavior Scared(Milo m, double fromX)
    {
        var p = m.Body.Platform;
        double away = p == null ? m.Body.X : (fromX < m.Body.X ? p.X2 - m.Body.HalfWidth : p.X1 + m.Body.HalfWidth);
        return new Sequence(2, "Scared",
            new PlayOnce("scared", Posture.Stand, 0.8, "whine", "sweat", 2),
            new GoTo(away, p?.Y ?? m.Body.Y, Gait.Run, 2),
            new Custom("Hide", (mm, c, dt) =>
            {
                mm.Face(fromX, 0);
                if (!mm.To(Posture.Lie)) return true;
                mm.Anim.Play("lie_look");
                return c.T < 6;
            }, null, 2));
    }
}
