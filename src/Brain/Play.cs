namespace MiloBot.Brain;

public class Fetch : Behavior
{
    readonly Toy ball;
    enum Phase { Chase, Carry, Present, Wait }
    Phase phase;
    double phaseT;
    GoTo carry;
    double thrownAt;
    int failures;

    public Fetch(Toy ball, bool carrying = false)
    {
        this.ball = ball;
        thrownAt = ball.ThrownAt;
        if (carrying) phase = Phase.Carry;
    }

    public override void Enter(Milo m)
    {
        if (phase == Phase.Carry)
        {
            carry = new GoTo(mm => (mm.W.CursorX + (mm.Body.X < mm.W.CursorX ? -1 : 1) * mm.U(16), mm.W.CursorY), Gait.Happy, 2, 8);
            carry.Enter(m);
        }
    }

    public override int Priority => 2;
    public override bool Jumping => carry != null && carry.Jumping;
    public override string Name => "Fetch:" + phase;

    public override void Landed(Milo m) => carry?.Landed(m);

    public override void Exit(Milo m)
    {
        if (ball.InMouth) Drop(m);
    }

    void Drop(Milo m)
    {
        var (x, y) = m.MouthPoint();
        ball.InMouth = false;
        ball.Body.X = x;
        ball.Body.Y = y;
        ball.Body.VX = m.Facing * m.U(20);
        ball.Body.VY = -m.U(40);
        ball.Body.Detach();
    }

    protected override bool Tick(Milo m, double dt)
    {
        phaseT += dt;
        if (ball.Gone) return false;
        if (ball.HeldByUser)
        {
            if (ball.InMouth) ball.InMouth = false;
            m.Face(ball.Body.X, 0);
            if (!m.To(Posture.Stand)) return true;
            m.Alert(0.5);
            m.Live(dt, false);
            phase = Phase.Wait;
            phaseT = 0;
            return true;
        }
        if (ball.ThrownAt > thrownAt)
        {
            thrownAt = ball.ThrownAt;
            phase = Phase.Chase;
            phaseT = 0;
            carry = null;
            m.Excitement = 1;
            if (m.Rng.NextDouble() < 0.5) m.Say("yip", 3);
        }
        switch (phase)
        {
            case Phase.Chase:
            {
                if (!m.Body.OnGround) return true;
                if (!m.To(Posture.Stand)) return true;
                var b = ball.Body;
                double mouthX = m.Body.X + m.Facing * m.U(12);
                bool samePlatform = b.OnGround && m.Body.Platform != null && b.Platform != null &&
                                    b.Platform.Hwnd == m.Body.Platform.Hwnd && Math.Abs(b.Y - m.Body.Y) < m.U(4);
                if (samePlatform && Math.Abs(b.X - mouthX) < m.U(8) && Math.Abs(b.VX) < m.U(70))
                {
                    ball.InMouth = true;
                    ball.LastTouched = m.Now;
                    m.Say(ball.Kind == ToyKind.Ball ? "squeak" : "sniff", 2);
                    m.Happiness = Math.Min(1, m.Happiness + 0.1);
                    phase = Phase.Carry;
                    phaseT = 0;
                    carry = new GoTo(mm => (mm.W.CursorX + (mm.Body.X < mm.W.CursorX ? -1 : 1) * mm.U(16), mm.W.CursorY), Gait.Happy, 2, 8);
                    carry.Enter(m);
                    return true;
                }
                if (carry == null || phaseT > 1.5)
                {
                    carry = new GoTo(mm => (ball.Body.X - mm.Facing * mm.U(10), ball.Body.Y), Gait.Run, 2, 3);
                    carry.Enter(m);
                    phaseT = 0;
                }
                if (!carry.Update(m, dt))
                {
                    if (carry.Failed && ++failures >= 3)
                    {
                        m.Face(b.X, 0);
                        m.Emote("question");
                        m.Say("whine", 10);
                        m.Memory.Cool("fetch-fail", 40, m.Now);
                        return false;
                    }
                    carry = null;
                }
                if (!b.OnGround && m.Body.OnGround)
                    m.Face(b.X, 0);
                return m.Energy > 0.05 && T < 90;
            }
            case Phase.Carry:
                if (carry.Update(m, dt) && phaseT < 25) return true;
                phase = Phase.Present;
                phaseT = 0;
                m.Face(m.W.CursorX, 0);
                return true;
            case Phase.Present:
                if (!m.To(Posture.Front)) return true;
                m.Live(dt, false);
                if (phaseT > 1.5)
                {
                    Drop(m);
                    m.Emote("heart");
                    m.Pant(6);
                    m.Save.Stat("fetches");
                    m.Save.AddFriend(2);
                    phase = Phase.Wait;
                    phaseT = 0;
                }
                return true;
            case Phase.Wait:
                if (!m.To(Posture.Front)) return true;
                m.Live(dt, false);
                return phaseT < 14;
        }
        return true;
    }
}

public class Eat : Behavior
{
    readonly Toy treat;
    GoTo go;
    double eatT = -1;
    int failures;

    public Eat(Toy treat) { this.treat = treat; }
    public override int Priority => 2;
    public override bool Jumping => go != null && go.Jumping;
    public override string Name => "Eat";
    public override void Landed(Milo m) => go?.Landed(m);

    protected override bool Tick(Milo m, double dt)
    {
        if (treat.Gone) return false;
        if (treat.HeldByUser)
        {
            m.Face(treat.Body.X, 0);
            if (!m.To(Posture.Sit)) return true;
            m.Anim.Play("beg");
            return T < 30;
        }
        if (eatT >= 0)
        {
            eatT += dt;
            m.Anim.Play("eat");
            if ((int)(eatT / 0.4) != (int)((eatT - dt) / 0.4)) m.Say("crunch", 0.3);
            if (eatT > 1.1)
            {
                treat.Gone = true;
                m.Save.Stat("treats");
                m.Save.AddFriend(1.5);
                m.Rewarded();
                m.Say("happy", 2);
                m.Float("heart");
                m.Float("sparkle");
                m.Happiness = Math.Min(1, m.Happiness + 0.3);
                m.Energy = Math.Min(1, m.Energy + 0.1);
                m.Pant(5);
                return false;
            }
            return true;
        }
        if (!m.Body.OnGround || !treat.Body.OnGround) { if (m.Body.OnGround) m.Face(treat.Body.X, 0); return true; }
        if (go == null)
        {
            go = new GoTo(mm => (treat.Body.X - mm.Facing * mm.U(12), treat.Body.Y), m.Energy > 0.3 ? Gait.Run : Gait.Walk, 2, 3);
            go.Enter(m);
        }
        bool same = m.Body.Platform != null && treat.Body.Platform != null && m.Body.Platform.Hwnd == treat.Body.Platform.Hwnd && Math.Abs(m.Body.Y - treat.Body.Y) < m.U(4);
        if (same && Math.Abs(treat.Body.X - (m.Body.X + m.Facing * m.U(12))) < m.U(8))
        {
            m.Face(treat.Body.X, 0);
            eatT = 0;
            m.Say("sniff", 1);
            return true;
        }
        if (!go.Update(m, dt))
        {
            if (go.Failed && ++failures >= 2)
            {
                m.Face(treat.Body.X, 0);
                m.Emote("food");
                m.Say("whine", 10);
                m.Memory.Cool("eat-fail", 45, m.Now);
                return false;
            }
            go = null;
            if (same) m.Face(treat.Body.X, 0);
        }
        if (T > 40) m.Memory.Cool("eat-fail", 45, m.Now);
        return T < 40;
    }
}
