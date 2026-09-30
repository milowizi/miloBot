namespace MiloBot.Gfx;

public class Animator
{
    readonly Func<string, Anim> get;
    public Anim Current { get; private set; }
    public double Time { get; private set; }
    public double Speed = 1.0;
    public bool Reverse { get; private set; }
    public int FrameIndex { get; private set; }

    public Animator(Sprites sprites) : this(sprites.Get) { }

    public Animator(Func<string, Anim> get)
    {
        this.get = get;
        Current = get("idle");
    }

    public string Name => Current.Name;

    public bool Finished => !Current.Loop && Time * Current.Fps >= Current.Frames.Length;

    public void Play(string name, bool restart = false, bool reverse = false)
    {
        var a = get(name);
        if (a == Current && !restart && reverse == Reverse) return;
        Current = a;
        Reverse = reverse;
        Time = 0;
        Speed = 1.0;
        UpdateFrame();
    }

    public void Update(double dt)
    {
        Time += dt * Speed;
        UpdateFrame();
    }

    public void SetFrame(int index)
    {
        FrameIndex = Math.Clamp(index, 0, Current.Frames.Length - 1);
        Time = FrameIndex / Current.Fps;
    }

    void UpdateFrame()
    {
        int n = Current.Frames.Length;
        int i = (int)(Time * Current.Fps);
        i = Current.Loop ? i % n : Math.Min(i, n - 1);
        FrameIndex = Reverse ? n - 1 - i : i;
    }

    public int SheetFrame => Current.Frames[FrameIndex];
}
