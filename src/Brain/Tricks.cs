namespace MiloBot.Brain;

public class TrickInfo
{
    public string Id, Name;
    public double Start;
}

public static class Tricks
{
    public static readonly TrickInfo[] All =
    {
        new() { Id = "sit", Name = "Zit", Start = 0.7 },
        new() { Id = "down", Name = "Af", Start = 0.5 },
        new() { Id = "speak", Name = "Spreek", Start = 0.45 },
        new() { Id = "paw", Name = "Pootje", Start = 0.3 },
        new() { Id = "beg", Name = "Opzitten", Start = 0.3 },
        new() { Id = "spin", Name = "Draai", Start = 0.2 },
        new() { Id = "bow", Name = "Buiging", Start = 0.2 },
        new() { Id = "roll", Name = "Rol om", Start = 0.08 },
        new() { Id = "dead", Name = "Pang!", Start = 0.05 },
    };

    public static TrickInfo Get(string id) => All.First(t => t.Id == id);

    public static double Skill(Milo m, string id) => m.Save.Skill(id, Get(id).Start);

    public static string BestKnown(Milo m)
    {
        var known = All.Where(t => Skill(m, t.Id) > 0.45).ToList();
        if (known.Count == 0) return "sit";
        return known[m.Rng.Next(known.Count)].Id;
    }

    public static string Stars(double skill)
    {
        int n = (int)Math.Round(skill * 5);
        return new string('★', n) + new string('☆', 5 - n);
    }
}

public class TrickBehavior : Behavior
{
    readonly bool commanded;
    readonly int priority;
    string trick;
    enum Phase { Listen, Do, Fail, Proud }
    Phase phase;
    double phaseT, listenFor;
    bool success, flipAfter;
    string failKind;

    public TrickBehavior(string trick, bool commanded, int priority = 3)
    {
        this.trick = trick;
        this.commanded = commanded;
        this.priority = priority;
    }

    public override int Priority => priority;
    public override string Name => "Trick:" + trick;

    public override void Enter(Milo m)
    {
        listenFor = commanded ? 0.5 + m.Rng.NextDouble() * 0.6 : 0.1;
        double skill = Tricks.Skill(m, trick);
        double p = 0.08 + skill * 0.9;
        if (m.Energy < 0.25) p -= 0.15;
        if (m.Happiness > 0.6) p += 0.08;
        success = !commanded || m.Rng.NextDouble() < p;
        if (!success)
        {
            double r = m.Rng.NextDouble();
            var other = Tricks.All.Where(t => t.Id != trick && Tricks.Skill(m, t.Id) > 0.4).ToList();
            if (r < 0.35 && other.Count > 0)
            {
                failKind = "wrong";
                trick = other[m.Rng.Next(other.Count)].Id;
            }
            else failKind = r < 0.8 ? "tilt" : "distracted";
        }
    }

    protected override bool Tick(Milo m, double dt)
    {
        phaseT += dt;
        switch (phase)
        {
            case Phase.Listen:
                if (commanded) m.Face(m.W.CursorX, 0);
                if (!m.To(commanded ? Posture.Front : m.Posture == Posture.Sleep ? Posture.Lie : m.Posture)) return true;
                if (commanded) m.Anim.Play("front_look");
                if (phaseT < listenFor) return true;
                phase = success || failKind == "wrong" ? Phase.Do : Phase.Fail;
                phaseT = 0;
                return true;
            case Phase.Do:
                if (!Perform(m, dt)) return true;
                phase = Phase.Proud;
                phaseT = 0;
                if (flipAfter) m.Facing = -m.Facing;
                if (success)
                {
                    m.Float("sparkle");
                    m.Save.Stat("tricks");
                    if (commanded) m.ExpectReward(trick);
                }
                else m.Emote("sparkle", 1);
                return true;
            case Phase.Fail:
                if (failKind == "tilt")
                {
                    if (phaseT < 0.05) { m.Emote("question", 2); m.Tilt(1.5); }
                    if (!m.To(Posture.Front)) return true;
                    m.Live(dt, false);
                    if (phaseT > 2.5) { Practice(m, 0.015); return false; }
                    return true;
                }
                if (phaseT < 0.05) m.Emote("dots", 1.5);
                if (!m.To(Posture.Stand)) return true;
                m.Anim.Play("sniff");
                if (phaseT > 2.2) { Practice(m, 0.015); return false; }
                return true;
            case Phase.Proud:
                if (!m.To(Posture.Front)) return true;
                m.Anim.Play(success ? "front_happy" : "front_look");
                if (!success && phaseT < 0.05) m.Tilt(1);
                return phaseT < 1.6;
        }
        return true;
    }

    void Practice(Milo m, double amount)
    {
        double s = Tricks.Skill(m, trick);
        m.Save.Tricks[trick] = Math.Min(1, s + amount * (1 - s));
    }

    bool Perform(Milo m, double dt)
    {
        switch (trick)
        {
            case "sit":
                if (!m.To(Posture.Sit)) return false;
                m.Anim.Play("sit_look");
                return phaseT > 1.4;
            case "down":
                if (!m.To(Posture.Lie)) return false;
                m.Anim.Play("lie_look");
                return phaseT > 1.8;
            case "speak":
                if (!m.To(Posture.Front)) return false;
                if (phaseT < dt * 1.5) { m.Anim.Play("front_bark", true); m.Say("bark", 0.5); }
                return phaseT > 0.9;
            case "paw":
                if (!m.To(Posture.Front)) return false;
                m.Anim.Play("paw");
                return phaseT > 2.0;
            case "beg":
                if (!m.To(Posture.Sit)) return false;
                m.Anim.Play("beg");
                return phaseT > 2.4;
            case "spin":
                if (!m.To(Posture.Stand)) return false;
                m.Anim.Play("spin");
                if (phaseT < dt * 1.5 && m.Body.OnGround) m.Hop(0, 60, 0);
                return phaseT > 1.2;
            case "bow":
                if (!m.To(Posture.Stand)) return false;
                if (phaseT < dt * 1.5) m.Anim.Play("stretch", true);
                return m.Anim.Finished || phaseT > 2.2;
            case "roll":
                if (m.Posture != Posture.Belly && !m.To(Posture.Lie)) return false;
                if (m.Anim.Name != "roll") { m.Anim.Play("roll", true); phaseT = 0; }
                if (!m.Anim.Finished) return false;
                flipAfter = true;
                return true;
            case "dead":
                if (!m.To(Posture.Lie)) return false;
                if (m.Anim.Name != "dead") { m.Anim.Play("dead", true); phaseT = 0; m.Emote("bang", 0.8); }
                if (phaseT > 3.0 && m.Body.OnGround)
                {
                    m.ForcePosture(Posture.Stand);
                    m.Hop(0, 130, 0);
                    return true;
                }
                return false;
        }
        return true;
    }
}
