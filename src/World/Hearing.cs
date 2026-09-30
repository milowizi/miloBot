using System.Text;

namespace MiloBot.World;

public class Heard
{
    public string Id;
    public bool Addressed;
}

public class Hearing
{
    static readonly Dictionary<string, (string id, bool strong)> Words = new()
    {
        ["milo"] = ("name", true), ["miloo"] = ("name", true), ["milooo"] = ("name", true),
        ["braaf"] = ("good", true), ["braaaf"] = ("good", true), ["goodboy"] = ("good", true), ["good boy"] = ("good", true), ["goeie jongen"] = ("good", true), ["brave jongen"] = ("good", true), ["brave hond"] = ("good", true),
        ["wandelen"] = ("walk", true), ["wandeling"] = ("walk", true), ["walkies"] = ("walk", true), ["walk"] = ("walk", false), ["buiten"] = ("walk", false), ["uitlaten"] = ("walk", true),
        ["snoepje"] = ("treat", true), ["snoepjes"] = ("treat", true), ["koekje"] = ("treat", true), ["treat"] = ("treat", true), ["treats"] = ("treat", true), ["snack"] = ("treat", false),
        ["balletje"] = ("ball", true), ["bal"] = ("ball", false), ["ball"] = ("ball", false),
        ["kat"] = ("cat", false), ["katten"] = ("cat", false), ["poes"] = ("cat", false), ["cat"] = ("cat", false), ["kitten"] = ("cat", false),
        ["eten"] = ("food", false), ["voer"] = ("food", false), ["brokjes"] = ("food", true), ["food"] = ("food", false), ["dinner"] = ("food", false),
        ["slapen"] = ("sleep", false), ["bedtijd"] = ("sleep", true), ["slaapwel"] = ("sleep", true), ["sleep"] = ("sleep", false),
        ["dierenarts"] = ("vet", true), ["badje"] = ("bath", true), ["bath"] = ("bath", false), ["wassen"] = ("bath", false),
        ["eekhoorn"] = ("squirrel", true), ["squirrel"] = ("squirrel", true),
        ["zit"] = ("trick:sit", false), ["sit"] = ("trick:sit", false), ["pootje"] = ("trick:paw", false), ["paw"] = ("trick:paw", false),
        ["spreek"] = ("trick:speak", false), ["speak"] = ("trick:speak", false), ["draai"] = ("trick:spin", false), ["spin"] = ("trick:spin", false),
        ["opzitten"] = ("trick:beg", false), ["buiging"] = ("trick:bow", false), ["pang"] = ("trick:dead", false), ["rol"] = ("trick:roll", false), ["roll"] = ("trick:roll", false),
        ["liggen"] = ("trick:down", false)
    };

    readonly StringBuilder word = new();
    readonly List<string> recent = new();
    readonly Queue<double> backspaces = new();
    readonly Queue<double> clicks = new();
    readonly Queue<double> keys = new();
    readonly HashSet<int> held = new();
    public readonly List<Heard> Events = new();
    public bool Enabled = true;
    public bool ScreenshotKey, BackspaceSpam, RageClicks;
    public double KeysPerSecond;
    double lastKey = -10, now;

    public void Update(double t)
    {
        now = t;
        while (keys.Count > 0 && now - keys.Peek() > 3) keys.Dequeue();
        KeysPerSecond = keys.Count / 3.0;
        if (word.Length > 0 && now - lastKey > 1.3) FinishWord();
        if (recent.Count > 0 && now - lastKey > 12) recent.Clear();
    }

    public void Clear()
    {
        word.Clear();
        recent.Clear();
        Events.Clear();
        ScreenshotKey = BackspaceSpam = RageClicks = false;
    }

    bool Down(params int[] vks) => vks.Any(held.Contains);

    public void Key(int vk, bool down, double t)
    {
        now = t;
        if (!down) { held.Remove(vk); return; }
        bool repeat = !held.Add(vk);
        if (vk == 0x2C || (vk == 0x53 && Down(0x5B, 0x5C) && Down(0x10, 0xA0, 0xA1))) { ScreenshotKey = true; return; }
        if (repeat && vk != 0x08) return;
        keys.Enqueue(now);
        lastKey = now;
        if (vk == 0x08)
        {
            if (word.Length > 0) word.Length--;
            backspaces.Enqueue(now);
            while (backspaces.Count > 0 && now - backspaces.Peek() > 4) backspaces.Dequeue();
            if (backspaces.Count >= 12) { BackspaceSpam = true; backspaces.Clear(); }
            return;
        }
        if (!Enabled) return;
        if (Down(0x11, 0xA2, 0xA3, 0x5B, 0x5C) && !Down(0x12, 0xA5)) return;
        if (vk >= 0x41 && vk <= 0x5A)
        {
            if (word.Length < 20) word.Append((char)('a' + vk - 0x41));
            return;
        }
        if (vk is 0x10 or 0xA0 or 0xA1 or 0x14 or 0x11 or 0xA2 or 0xA3 or 0x12 or 0xA4 or 0xA5) return;
        FinishWord();
        if (vk == 0x0D) recent.Clear();
    }

    public void Mouse(int buttons, double t)
    {
        now = t;
        if ((buttons & 1) == 0) return;
        clicks.Enqueue(now);
        while (clicks.Count > 0 && now - clicks.Peek() > 2) clicks.Dequeue();
        if (clicks.Count >= 7) { RageClicks = true; clicks.Clear(); }
    }

    void FinishWord()
    {
        if (word.Length == 0) return;
        string w = word.ToString();
        word.Clear();
        recent.Add(w);
        if (recent.Count > 6) recent.RemoveAt(0);
        string pair = recent.Count >= 2 ? recent[^2] + " " + w : null;
        (string id, bool strong) hit = default;
        if (pair != null && Words.TryGetValue(pair, out var p)) hit = p;
        else if (Words.TryGetValue(w, out var s)) hit = s;
        if (hit.id == null) return;
        bool addressed = hit.id != "name" && recent.Skip(Math.Max(0, recent.Count - 3)).Any(x => x.StartsWith("milo"));
        if (hit.id.StartsWith("trick:") && !addressed) return;
        Events.Add(new Heard { Id = hit.id, Addressed = addressed || hit.strong });
    }
}
