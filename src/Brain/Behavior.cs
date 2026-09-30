namespace MiloBot.Brain;

public abstract class Behavior
{
    public double T;
    public virtual int Priority => 0;
    public virtual bool Jumping => false;
    public virtual string Name => GetType().Name;

    public virtual void Enter(Milo m) { }
    public virtual void Resume(Milo m) { }
    public virtual void Exit(Milo m) { }
    public virtual void Landed(Milo m) { }

    public bool Update(Milo m, double dt)
    {
        T += dt;
        return Tick(m, dt);
    }

    protected abstract bool Tick(Milo m, double dt);
}

public class Sequence : Behavior
{
    readonly List<Behavior> steps;
    readonly int priority;
    readonly string name;
    int index;

    public Sequence(int priority, string name, params Behavior[] steps)
    {
        this.priority = priority;
        this.name = name;
        this.steps = steps.Where(s => s != null).ToList();
    }

    public override int Priority => priority;
    public override string Name => name + (index < steps.Count ? "/" + steps[index].Name : "");
    public override bool Jumping => index < steps.Count && steps[index].Jumping;

    public override void Enter(Milo m)
    {
        if (steps.Count > 0) steps[0].Enter(m);
    }

    public override void Resume(Milo m)
    {
        if (index < steps.Count) steps[index].Resume(m);
    }

    public override void Exit(Milo m)
    {
        if (index < steps.Count) steps[index].Exit(m);
    }

    public override void Landed(Milo m)
    {
        if (index < steps.Count) steps[index].Landed(m);
    }

    protected override bool Tick(Milo m, double dt)
    {
        while (index < steps.Count)
        {
            if (steps[index].Update(m, dt)) return true;
            steps[index].Exit(m);
            index++;
            if (index < steps.Count) steps[index].Enter(m);
            dt = 0;
        }
        return false;
    }
}

public class Custom : Behavior
{
    readonly Func<Milo, Custom, double, bool> tick;
    readonly Action<Milo> enter;
    readonly int priority;
    readonly string name;
    public double A, B;
    public int N;

    public Custom(string name, Func<Milo, Custom, double, bool> tick, Action<Milo> enter = null, int priority = 0)
    {
        this.name = name;
        this.tick = tick;
        this.enter = enter;
        this.priority = priority;
    }

    public override string Name => name;
    public override int Priority => priority;
    public override void Enter(Milo m) => enter?.Invoke(m);
    protected override bool Tick(Milo m, double dt) => tick(m, this, dt);
}

public class Rest : Behavior
{
    readonly Posture posture;
    readonly double duration;
    readonly double? faceX;
    readonly int priority;

    public Rest(Posture posture, double duration, double? faceX = null, int priority = 0)
    {
        this.posture = posture;
        this.duration = duration;
        this.faceX = faceX;
        this.priority = priority;
    }

    public override int Priority => priority;
    public override string Name => "Rest:" + posture;

    protected override bool Tick(Milo m, double dt)
    {
        if (faceX.HasValue && m.Posture != posture) m.Face(faceX.Value);
        if (!m.To(posture)) return true;
        m.Live(dt, faceX == null);
        return T < duration;
    }
}

public class PlayOnce : Behavior
{
    readonly string anim;
    readonly Posture? posture;
    readonly double maxTime;
    readonly string sound, emote;
    readonly int priority;
    bool started;
    double startT;

    public PlayOnce(string anim, Posture? posture = null, double maxTime = 4, string sound = null, string emote = null, int priority = 0)
    {
        this.anim = anim;
        this.posture = posture;
        this.maxTime = maxTime;
        this.sound = sound;
        this.emote = emote;
        this.priority = priority;
    }

    public override int Priority => priority;
    public override string Name => "Play:" + anim;

    public override void Resume(Milo m) => started = false;

    protected override bool Tick(Milo m, double dt)
    {
        if (posture.HasValue && !m.To(posture.Value)) return true;
        if (!started)
        {
            started = true;
            startT = T;
            m.Anim.Play(anim, true);
            if (sound != null) m.Say(sound, 1);
            if (emote != null) m.Emote(emote);
        }
        double el = T - startT;
        if (m.Anim.Current.Loop) return el < maxTime;
        return !m.Anim.Finished && el < maxTime;
    }
}
