using MiloBot.World;

namespace MiloBot.Brain;

public class Hunt : Behavior
{
    readonly Critter critter;
    double nextHop;
    GoTo go;
    public bool Barking { get; private set; }

    public Hunt(Critter critter) { this.critter = critter; }
    public override int Priority => 2;
    public override string Name => "Hunt:" + critter.Kind;
    public override bool Jumping => go != null && go.Jumping;
    public override void Landed(Milo m) => go?.Landed(m);

    protected override bool Tick(Milo m, double dt)
    {
        if (T < 0.8)
        {
            m.Face(critter.X, 0);
            m.Alert(0.5);
            if (m.Posture == Posture.Stand) m.Anim.Play("alert"); else m.Live(dt, false);
            return true;
        }
        if (!m.Body.OnGround) return true;
        if (critter.State == CritterState.Gone || critter.State == CritterState.Leave)
        {
            Barking = false;
            m.Face(critter.X, 0);
            if (!m.To(Posture.Sit)) return true;
            m.Anim.Play("sit_look");
            if (critter.Spooked && m.Memory.Cool("proud", 20, m.Now)) m.Emote("sparkle", 1.4);
            return T < 12 && critter.State != CritterState.Gone;
        }
        if (critter.Kind == CritterKind.Bird)
        {
            double d = Math.Abs(critter.X - m.Body.X);
            bool rush = d < m.U(55);
            if (rush && !Barking)
            {
                Barking = true;
                m.Say("bark", 1);
                m.Emote("exclaim", 1);
            }
            if (!m.To(Posture.Stand)) return true;
            if (go == null || m.Now > nextHop)
            {
                go = new GoTo(mm => (critter.X, critter.Y), rush ? Gait.Run : Gait.Sniff, 2, 4);
                go.Enter(m);
                nextHop = m.Now + 1.5;
            }
            if (!go.Update(m, dt)) go = null;
            if (Barking && m.Anim.Name != "run") m.Anim.Play("bark");
            return T < 40;
        }
        if (critter.State == CritterState.OnMilo) return false;
        if (!m.To(Posture.Stand)) return true;
        bool arrived = m.WalkTo(critter.X, Gait.Happy, dt, 6);
        double above = m.Body.Y - critter.Y;
        if (arrived)
        {
            m.Face(critter.X, 0);
            m.Anim.Play("idle_look");
            if (above < m.MaxJumpUp * 0.8 && above > m.U(20) && m.Now > nextHop && m.Energy > 0.2)
            {
                nextHop = m.Now + 1.8;
                m.Hop(critter.X > m.Body.X ? 1 : -1, Math.Sqrt(2 * 900 * (above / m.S + 6)), 15);
                if (m.Rng.NextDouble() < 0.5) m.Say("yip", 2);
                critter.TargetY = critter.Y - 140;
                critter.TargetX = critter.X + (m.Rng.NextDouble() - 0.5) * 300;
            }
        }
        return T < 16;
    }
}

public class LaserChase : Behavior
{
    readonly Laser laser;
    double nextJump, nextYip;
    bool tiredShown;

    public LaserChase(Laser laser) { this.laser = laser; }
    public override int Priority => 3;
    public override string Name => "Laser";

    protected override bool Tick(Milo m, double dt)
    {
        if (!laser.On) return false;
        if (!m.Body.OnGround) return true;
        if (m.Energy < 0.1)
        {
            if (!tiredShown) { tiredShown = true; m.Emote("sweat", 2); m.Say("pant", 3); }
            if (!m.To(Posture.Lie)) return true;
            m.Anim.Play("lie_sleepy");
            return true;
        }
        m.Excitement = 1;
        m.Energy = Math.Max(0, m.Energy - dt * 0.002);
        if (!m.To(Posture.Stand)) return true;
        var hit = m.HitRect();
        if (hit.Contains(laser.X, laser.Y) && m.Now > nextJump)
        {
            nextJump = m.Now + 0.7;
            m.Hop(0, 150, 0);
            if (m.Now > nextYip) { nextYip = m.Now + 1.2; m.Say("yip", 0.8); }
            return true;
        }
        double above = m.Body.Y - laser.Y;
        if (Math.Abs(laser.X - m.Body.X) < m.U(40) && above > m.U(35) && above < m.MaxJumpUp && m.Now > nextJump)
        {
            nextJump = m.Now + 1.0;
            double up = Math.Sqrt(2 * 900 * (above / m.S + 4));
            m.Hop(laser.X > m.Body.X ? 1 : -1, up, Math.Abs(laser.X - m.Body.X) / m.S * 1.5);
            if (m.Now > nextYip) { nextYip = m.Now + 1.5; m.Say("yip", 1); m.Emote("exclaim", 0.8); }
            return true;
        }
        bool arrived = m.WalkTo(laser.X, Gait.Zoom, dt, 3);
        if (arrived)
        {
            m.Face(laser.X, 0);
            m.Anim.Play(above > m.U(30) ? "idle_look" : "happy");
        }
        return true;
    }
}

public class Tug : Behavior
{
    readonly Toy toy;
    readonly double duration;
    public bool UserHolding = true;
    public int Outcome;
    double nextGrowl, runT;
    int runDir;

    public Tug(Toy toy, double duration)
    {
        this.toy = toy;
        this.duration = duration;
    }

    public override int Priority => 4;
    public override string Name => "Tug";

    protected override bool Tick(Milo m, double dt)
    {
        if (Outcome == 1)
        {
            runT += dt;
            if (runT < 0.05) { runDir = m.W.CursorX > m.Body.X ? -1 : 1; m.Emote("sparkle", 1.2); m.Say("happy", 1); }
            var p = m.Body.Platform;
            double target = p == null ? m.Body.X : Math.Clamp(m.Body.X + runDir * m.U(200), p.X1, p.X2);
            if (m.WalkTo(target, Gait.Zoom, dt, 4) || runT > 2.5)
            {
                if (!toy.Gone && toy.InMouth) m.Set(new Fetch(toy, true));
                return false;
            }
            return true;
        }
        if (Outcome == 2)
        {
            if (runT == 0) { m.Emote("heart", 1.4); m.Say("yip", 1); }
            runT += dt;
            if (!m.To(Posture.Stand)) return true;
            if (runT < 0.1) m.Anim.Play("stretch", true);
            return runT < 1.8;
        }
        if (!UserHolding)
        {
            Outcome = 1;
            Finish(m);
            return true;
        }
        if (!m.Body.OnGround) return true;
        m.ForcePosture(Posture.Stand);
        m.Face(m.W.CursorX, 0);
        m.Anim.Play("tug");
        var (mx, _) = m.MouthPoint();
        double pull = m.W.CursorX - mx;
        if (Math.Abs(pull) > m.U(6))
        {
            var p = m.Body.Platform;
            double step = Math.Sign(pull) * Math.Min(Math.Abs(pull), m.U(50)) * 0.7 * dt;
            if (p != null) m.Body.X = Math.Clamp(m.Body.X + step, p.X1 + m.Body.HalfWidth * 0.6, p.X2 - m.Body.HalfWidth * 0.6);
            if (p != null && !p.IsFloor && m.WindowRect(p.Hwnd) is RectD r) m.Body.RelX = m.Body.X - r.Left;
        }
        if (m.Now > nextGrowl)
        {
            nextGrowl = m.Now + 1.4 + m.Rng.NextDouble();
            m.Say("growl", 1);
        }
        m.Happiness = Math.Min(1, m.Happiness + dt * 0.05);
        if (T > duration)
        {
            Outcome = m.Rng.NextDouble() < 0.5 ? 1 : 2;
            Finish(m);
        }
        return true;
    }

    void Finish(Milo m)
    {
        m.Save.Stat("tugs");
        m.Save.AddFriend(2);
        if (Outcome == 2)
        {
            toy.InMouth = false;
            runT = 0;
        }
        else toy.InMouth = true;
    }
}

public class Deliver : Behavior
{
    Toy toy;
    readonly Func<Milo, (double x, double y)> dest;
    readonly int priority;
    readonly bool holdAtEnd;
    public Action<Milo, Toy> Dropped;
    public Func<Milo, bool> Wait;
    GoTo go;
    enum Phase { Get, Carry, Drop, Proud, Hold }
    Phase phase;
    double phaseT;
    public ToyKind SpawnKind = ToyKind.Sock;

    public Deliver(Toy toy, Func<Milo, (double x, double y)> dest, int priority, bool holdAtEnd)
    {
        this.toy = toy;
        this.dest = dest;
        this.priority = priority;
        this.holdAtEnd = holdAtEnd;
    }

    public override int Priority => priority;
    public override string Name => "Deliver:" + phase;
    public override bool Jumping => go != null && go.Jumping;
    public override void Landed(Milo m) => go?.Landed(m);
    public Toy Item => toy;

    public override void Enter(Milo m)
    {
        if (toy == null) toy = m.Toys.Items.FirstOrDefault(t => t.Fetchable && !t.HeldByUser && !t.InMouth && !t.Hidden);
        if (toy == null)
        {
            var kinds = new[] { ToyKind.Sock, ToyKind.Stick, ToyKind.Bone };
            var kind = SpawnKind == ToyKind.Leash ? ToyKind.Leash : kinds[m.Rng.Next(kinds.Length)];
            double x = m.Home.Active ? m.Home.BedX : m.Body.X + m.Facing * m.U(80);
            double y = m.Home.Active ? m.Home.Y : m.Body.Y;
            toy = m.Toys.Spawn(kind, x, y - m.U(4), m.Now, m.Scale);
            toy.Body.Detach();
        }
    }

    public override void Exit(Milo m)
    {
        if (toy != null && toy.InMouth && phase != Phase.Hold) DropHere(m);
    }

    void DropHere(Milo m)
    {
        var (x, y) = m.MouthPoint();
        toy.InMouth = false;
        toy.Body.X = x;
        toy.Body.Y = y;
        toy.Body.VX = m.Facing * m.U(15);
        toy.Body.VY = -m.U(30);
        toy.Body.Detach();
        toy.LastTouched = m.Now;
    }

    protected override bool Tick(Milo m, double dt)
    {
        phaseT += dt;
        if (toy == null || toy.Gone) return false;
        if (toy.HeldByUser) return false;
        if (!m.Body.OnGround) return true;
        switch (phase)
        {
            case Phase.Get:
            {
                if (toy.InMouth) { phase = Phase.Carry; phaseT = 0; return true; }
                if (!toy.Body.OnGround) { m.Face(toy.Body.X, 0); return true; }
                if (!m.To(Posture.Stand)) return true;
                double mouthX = m.Body.X + m.Facing * m.U(12);
                bool same = m.Body.Platform != null && toy.Body.Platform != null && m.Body.Platform.Hwnd == toy.Body.Platform.Hwnd && Math.Abs(toy.Body.Y - m.Body.Y) < m.U(4);
                if (same && Math.Abs(toy.Body.X - mouthX) < m.U(8))
                {
                    toy.InMouth = true;
                    toy.LastTouched = m.Now;
                    m.Say(toy.Kind == ToyKind.Ball ? "squeak" : "sniff", 1);
                    phase = Phase.Carry;
                    phaseT = 0;
                    go = null;
                    return true;
                }
                if (go == null) { go = new GoTo(mm => (toy.Body.X - mm.Facing * mm.U(10), toy.Body.Y), Gait.Happy, priority, 3); go.Enter(m); }
                if (!go.Update(m, dt)) go = null;
                return phaseT < 40;
            }
            case Phase.Carry:
            {
                if (!m.To(Posture.Stand)) return true;
                if (go == null) { go = new GoTo(dest, Gait.Happy, priority, 6); go.Enter(m); }
                if (go.Update(m, dt) && phaseT < 30) return true;
                go = null;
                phase = holdAtEnd ? Phase.Hold : Phase.Drop;
                phaseT = 0;
                return true;
            }
            case Phase.Drop:
                m.Face(dest(m).x, 0);
                DropHere(m);
                Dropped?.Invoke(m, toy);
                phase = Phase.Proud;
                phaseT = 0;
                m.Emote("heart", 1.5);
                return true;
            case Phase.Proud:
                if (!m.To(Posture.Front)) return true;
                m.Anim.Play("front_happy");
                return phaseT < 2.5;
            case Phase.Hold:
                if (!m.To(Posture.Front)) return true;
                m.Face(m.W.CursorX, 0);
                m.Anim.Play("front_look");
                if (Wait != null) return Wait(m);
                return phaseT < 10;
        }
        return true;
    }

    public void Release(Milo m)
    {
        if (toy != null && toy.InMouth) DropHere(m);
        phase = Phase.Proud;
        phaseT = 0;
    }
}

public class Meal : Behavior
{
    GoTo go;
    double eatT = -1;
    bool begged;
    public override int Priority => 1;
    public override string Name => "Meal";
    public override bool Jumping => go != null && go.Jumping;
    public override void Landed(Milo m) => go?.Landed(m);

    protected override bool Tick(Milo m, double dt)
    {
        var h = m.Home;
        if (!h.Active) return false;
        if (!m.Body.OnGround) return true;
        int side = m.Body.X < h.BowlX ? -1 : 1;
        if (eatT < 0)
        {
            if (!m.To(Posture.Stand)) return true;
            if (go == null) { go = new GoTo(h.BowlStandX(side), h.Y, m.Save.Hunger > 0.8 ? Gait.Run : Gait.Walk, 1, 3); go.Enter(m); }
            if (go.Update(m, dt) && T < 40) return true;
            m.Facing = side > 0 ? -1 : 1;
            eatT = 0;
            if (m.Save.BowlFood < 0.15)
            {
                begged = true;
                m.Emote("food", 2.2);
                m.Say("whine", 8);
            }
            return true;
        }
        eatT += dt;
        if (begged)
        {
            m.Anim.Play(eatT < 1.5 ? "sniff" : "idle_look");
            if (eatT > 3)
            {
                m.Memory.Cool("meal", 300, m.Now);
                m.Set(Plans.SeekUser(m, true));
            }
            return eatT <= 3;
        }
        m.Anim.Play("eat");
        if ((int)(eatT / 0.45) != (int)((eatT - dt) / 0.45)) m.Say("crunch", 0.3);
        if (eatT > 3.2)
        {
            m.Save.BowlFood = Math.Max(0, m.Save.BowlFood - 0.34);
            m.Save.Hunger = 0;
            m.Save.Stat("meals");
            m.Save.AddFriend(0.5);
            m.Happiness = Math.Min(1, m.Happiness + 0.15);
            m.Energy = Math.Min(1, m.Energy + 0.1);
            m.Float("heart");
            m.Pant(3);
            return false;
        }
        return true;
    }
}
