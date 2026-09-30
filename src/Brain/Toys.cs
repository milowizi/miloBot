using MiloBot.World;

namespace MiloBot.Brain;

public enum ToyKind { Ball, Treat, Sock, Stick, Bone, Leash }

public class Toy
{
    public ToyKind Kind;
    public Body Body = new();
    public bool HeldByUser;
    public bool InMouth;
    public bool Gone;
    public bool Hidden;
    public double ThrownAt = -100;
    public double SpawnedAt;
    public double LastTouched;

    public string Icon => Kind switch
    {
        ToyKind.Ball => "ball",
        ToyKind.Treat => "food",
        ToyKind.Sock => "sock",
        ToyKind.Stick => "stick",
        ToyKind.Bone => "bone",
        _ => "leash"
    };

    public bool Fetchable => Kind is ToyKind.Ball or ToyKind.Sock or ToyKind.Stick or ToyKind.Bone;
}

public class Toys
{
    public readonly List<Toy> Items = new();

    public Toy Ball => Items.FirstOrDefault(t => t.Kind == ToyKind.Ball && !t.Gone);
    public Toy Treat => Items.FirstOrDefault(t => t.Kind == ToyKind.Treat && !t.Gone && !t.Hidden);
    public Toy InMouth => Items.FirstOrDefault(t => t.InMouth && !t.Gone);

    public Toy Spawn(ToyKind kind, double x, double y, double now, int scale)
    {
        var t = new Toy { Kind = kind, SpawnedAt = now, LastTouched = now };
        t.Body.X = x;
        t.Body.Y = y;
        t.Body.HalfWidth = 4 * scale;
        t.Body.Height = 7 * scale;
        Items.Add(t);
        return t;
    }

    public void Update(WorldState w, double dt, int scale, Func<IntPtr, RectD?> windowRect)
    {
        foreach (var t in Items)
        {
            if (t.Gone || t.HeldByUser || t.InMouth || t.Hidden) continue;
            var b = t.Body;
            bool bouncy = t.Kind == ToyKind.Ball;
            Physics.Step(b, w, dt, 900 * scale, bouncy ? 0.55 : 0.15, windowRect);
            if (b.OnGround && bouncy)
            {
                b.VX *= Math.Pow(0.35, dt);
                if (Math.Abs(b.VX) < 8) b.VX = 0;
                b.X += b.VX * dt;
                var p = b.Platform;
                if (p != null && !p.Contains(b.X))
                {
                    b.Detach();
                    b.VY = -40;
                }
                else if (p != null && !p.IsFloor && windowRect(p.Hwnd) is RectD r) b.RelX = b.X - r.Left;
                else if (p != null && p.IsFloor)
                {
                    if (b.X < p.X1 + b.HalfWidth) { b.X = p.X1 + b.HalfWidth; b.VX = Math.Abs(b.VX) * 0.5; }
                    if (b.X > p.X2 - b.HalfWidth) { b.X = p.X2 - b.HalfWidth; b.VX = -Math.Abs(b.VX) * 0.5; }
                }
            }
            else if (b.OnGround) b.VX = 0;
        }
        Items.RemoveAll(t => t.Gone);
    }
}
