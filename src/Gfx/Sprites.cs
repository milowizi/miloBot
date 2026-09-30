using System.Text.Json;

namespace MiloBot.Gfx;

public class Anim
{
    public string Name;
    public int[] Frames;
    public double Fps;
    public bool Loop;
    public double Duration => Frames.Length / Fps;
}

public class Sprites
{
    public Bitmap Sheet, Icons, Props, CritterSheet;
    public readonly Dictionary<string, Bitmap> Hats = new();
    public readonly Dictionary<string, int> PropIndex = new();
    public readonly Dictionary<string, int> CritterIndex = new();
    public int PropW = 32, PropH = 24, CritterSize = 16;
    public int FrameW, FrameH, Ground, Columns, IconSize;
    public readonly Dictionary<string, Anim> Anims = new();
    public readonly Dictionary<string, int> IconIndex = new();
    public float[][] Anchors;

    public static Sprites Load(string dir)
    {
        var s = new Sprites
        {
            Sheet = Png.Load(Path.Combine(dir, "milo.png")),
            Icons = Png.Load(Path.Combine(dir, "icons.png"))
        };
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "milo.json")));
        var root = doc.RootElement;
        s.FrameW = root.GetProperty("frameW").GetInt32();
        s.FrameH = root.GetProperty("frameH").GetInt32();
        s.Ground = root.GetProperty("ground").GetInt32();
        s.Columns = root.GetProperty("columns").GetInt32();
        s.IconSize = root.GetProperty("iconSize").GetInt32();
        foreach (var a in root.GetProperty("anims").EnumerateObject())
        {
            s.Anims[a.Name] = new Anim
            {
                Name = a.Name,
                Frames = a.Value.GetProperty("frames").EnumerateArray().Select(e => e.GetInt32()).ToArray(),
                Fps = a.Value.GetProperty("fps").GetDouble(),
                Loop = a.Value.GetProperty("loop").GetBoolean()
            };
        }
        s.Anchors = root.GetProperty("anchors").EnumerateArray()
            .Select(e => e.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray()).ToArray();
        int i = 0;
        foreach (var n in root.GetProperty("icons").EnumerateArray()) s.IconIndex[n.GetString()] = i++;
        if (root.TryGetProperty("hats", out var hats))
            foreach (var h in hats.EnumerateArray())
            {
                string file = Path.Combine(dir, "hat_" + h.GetString() + ".png");
                if (File.Exists(file)) s.Hats[h.GetString()] = Png.Load(file);
            }
        if (root.TryGetProperty("props", out var props) && File.Exists(Path.Combine(dir, "props.png")))
        {
            s.Props = Png.Load(Path.Combine(dir, "props.png"));
            s.PropW = root.GetProperty("propW").GetInt32();
            s.PropH = root.GetProperty("propH").GetInt32();
            i = 0;
            foreach (var n in props.EnumerateArray()) s.PropIndex[n.GetString()] = i++;
        }
        if (root.TryGetProperty("critters", out var crit) && File.Exists(Path.Combine(dir, "critters.png")))
        {
            s.CritterSheet = Png.Load(Path.Combine(dir, "critters.png"));
            s.CritterSize = root.GetProperty("critterSize").GetInt32();
            i = 0;
            foreach (var n in crit.EnumerateArray()) s.CritterIndex[n.GetString()] = i++;
        }
        return s;
    }

    public Anim Get(string name) => Anims.TryGetValue(name, out var a) ? a : Anims["idle"];

    public (int x, int y) FrameOrigin(int frame) => (frame % Columns * FrameW, frame / Columns * FrameH);
}
