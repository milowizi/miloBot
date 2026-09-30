using MiloBot.World;

namespace MiloBot.Brain;

public class Home
{
    public bool Enabled = true;
    public bool Valid;
    public bool Blocked;
    public double BedX, BowlX, Y;
    int scale = 3;

    public void Place(WorldState w, int scale)
    {
        this.scale = scale;
        var s = w.Screens.FirstOrDefault(x => x.Bounds.Contains(1, 1)) ?? w.Screens.FirstOrDefault();
        if (s == null) { Valid = false; return; }
        Y = s.Work.Bottom;
        BedX = s.Work.Right - 70 * scale;
        BowlX = BedX - 34 * scale;
        Valid = true;
    }

    public bool Active => Enabled && Valid && !Blocked;
    public double Left => BowlX - 16 * scale;
    public double Right => BedX + 16 * scale;
    public double Top => Y - 23 * scale;
    public RectD Rect => new(Left, Top, Right, Y + scale);
    public double BedStandX => BedX;
    public double BowlStandX(int side) => BowlX + side * 14 * scale;
}

public class Laser
{
    public bool On;
    public double StartedAt;
    public double X, Y;
}
