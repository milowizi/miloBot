using MiloBot.World;

namespace MiloBot.Brain;

/// <summary>Playing with the things inside your apps: pictures, panels/buttons and blocks of text (see ContentScanner).</summary>
public partial class Milo
{
    public bool ContentPlay = true;
    double hoverPicture;
    Platform hoverTarget;

    Platform PickContent(List<Platform> options)
    {
        double Weight(Platform p) => p.Ledge.Kind switch { "picture" => 3, "text" => 1.5, _ => 1 } / (1 + Math.Abs(p.Y - Body.Y) / U(150) + Math.Abs((p.X1 + p.X2) / 2 - Body.X) / U(500));
        double total = options.Sum(Weight), r = Rng.NextDouble() * total;
        foreach (var p in options)
        {
            r -= Weight(p);
            if (r <= 0) return p;
        }
        return options[^1];
    }

    /// <summary>Content ledges on Milo's screen that he could stand on.</summary>
    public IEnumerable<Platform> ContentHere(string kind = null)
    {
        return W.Platforms.Where(p => p.IsContent && (kind == null || p.Ledge.Kind == kind) && p.Width >= Body.HalfWidth * 2.4
                                      && !W.Blocked(p.X1 + p.Width / 2, p.Y - 2));
    }

    public RectD? BlockRect(Platform p) => WindowRect(p.Hwnd) is RectD r ? p.Ledge.Block.Offset(r.Left, r.Top) : null;

    // You rest your mouse on a picture for a while: Milo comes to see what you're looking at.
    void UpdateContent(double dt)
    {
        if (Hidden || Asleep || !Body.OnGround || W.CursorSpeed > 80) { hoverPicture = 0; return; }
        Platform over = null;
        foreach (var p in ContentHere("picture"))
            if (BlockRect(p) is RectD r && r.Contains(W.CursorX, W.CursorY)) { over = p; break; }
        if (over == null || over != hoverTarget && hoverTarget?.Ledge != over.Ledge) hoverPicture = 0;
        hoverTarget = over;
        if (over == null) return;
        hoverPicture += dt;
        if (hoverPicture > 2.5 && (Current == null || Current.Priority < 1) && Energy > 0.25 && Memory.Cool("hoverpic", 120, Now))
        {
            hoverPicture = 0;
            Emote("exclaim", 1.2);
            Set(ContentPlans.Visit(this, over));
        }
    }

    public void OnNewPicture(RectD r)
    {
        if (TopDown) return;
        if (Hidden || Asleep || !Body.OnGround || !Memory.Cool("newpic", 30, Now)) return;
        if (Current != null && Current.Priority >= 1) return;
        Face(r.CenterX, 20);
        Alert(1.2);
        if (Rng.NextDouble() < 0.3)
        {
            var p = ContentHere("picture").FirstOrDefault(q => BlockRect(q) is RectD b && Math.Abs(b.Top - r.Top) < 8 && Math.Abs(b.Left - r.Left) < 8);
            if (p != null) Set(ContentPlans.Visit(this, p));
        }
    }
}

public static class ContentPlans
{
    public static Behavior Visit(Milo m, Platform p)
    {
        var l = p.Ledge;
        double margin = Math.Min(m.U(22), p.Width / 3);
        double x = p.X1 + margin + m.Rng.NextDouble() * Math.Max(1, p.Width - margin * 2);
        if (l.Kind == "text") x = m.Body.X < p.X1 + p.Width / 2 ? p.X1 + margin : p.X2 - margin;
        Behavior act = l.Kind switch
        {
            "picture" => m.Rng.NextDouble() < 0.65 ? LookDown(l, 6 + m.Rng.NextDouble() * 5) : SitOn(l),
            "text" => ReadAlong(l),
            _ => PressButton(l)
        };
        return new Sequence(1, "Content:" + l.Kind, new GoTo(x, p.Y, Gait.Happy, 1, 4), act);
    }

    static bool OnIt(Milo m, ContentLedge l) => m.Body.OnGround && m.Body.Platform?.Ledge == l;

    // Didn't make it up there: look at it from where he is.
    static bool Admire(Milo m, Custom c, ContentLedge l, double dt)
    {
        if (c.N == 0) { c.N = 1; m.Emote("question", 1.5); }
        if (m.Body.Platform != null && m.WindowRect(m.Body.Platform.Hwnd) is RectD r) m.Face(r.Left + (l.X1 + l.X2) / 2, 0);
        if (!m.To(Posture.Sit)) return true;
        m.Anim.Play("sit_look");
        return c.T < 2.5;
    }

    // Lying on top of a picture, peeking over the edge and pawing at it.
    static Behavior LookDown(ContentLedge l, double seconds) => new Custom("LookAtPicture", (m, c, dt) =>
    {
        if (!OnIt(m, l)) return Admire(m, c, l, dt);
        if (!m.To(Posture.Lie)) return true;
        if (c.N == 0)
        {
            c.N = 1;
            c.A = c.T + 1.5;
            m.Save.Stat("pictures");
            m.Emote(m.Rng.NextDouble() < 0.5 ? "heart" : "sparkle", 1.8);
        }
        if (c.T > c.A)
        {
            c.A = c.T + 1.2 + m.Rng.NextDouble() * 2.5;
            double r = m.Rng.NextDouble();
            if (r < 0.45) m.Anim.Play("peek_paw", true);
            else if (r < 0.7) m.Anim.Play("peek_blink", true);
            else if (r < 0.8) m.Emote("question", 1.2);
        }
        else if (m.Anim.Finished || m.Anim.Name is not ("peek_paw" or "peek_blink")) m.Anim.Play("peek");
        return c.T < seconds;
    }, null, 1);

    // Sitting on a picture like it's a shelf, looking at you.
    static Behavior SitOn(ContentLedge l) => new Custom("SitOnPicture", (m, c, dt) =>
    {
        if (!OnIt(m, l)) return Admire(m, c, l, dt);
        m.Face(m.W.CursorX, 10);
        if (!m.To(Posture.Sit)) return true;
        if (c.N == 0) { c.N = 1; m.Save.Stat("pictures"); if (m.Rng.NextDouble() < 0.5) m.Emote("sparkle", 1.4); }
        m.Live(dt, false);
        return c.T < 5 + m.Rng.NextDouble() * 0.1;
    }, null, 1);

    // Walking slowly along a block of text with his nose down, like he's reading it.
    static Behavior ReadAlong(ContentLedge l) => new Custom("Read", (m, c, dt) =>
    {
        if (!OnIt(m, l)) return Admire(m, c, l, dt);
        var p = m.Body.Platform;
        if (c.N == 0)
        {
            c.N = 1;
            c.A = m.Body.X < (p.X1 + p.X2) / 2 ? p.X2 - m.U(14) : p.X1 + m.U(14);
            m.Save.Stat("reads");
        }
        if (c.N == 1)
        {
            if (!m.To(Posture.Stand)) return true;
            if (m.Rng.NextDouble() < dt * 0.4) m.Emote("dots", 1.2);
            if (m.WalkTo(c.A, Gait.Sniff, dt, 2)) { c.N = 2; c.B = c.T; m.Emote(m.Rng.NextDouble() < 0.5 ? "question" : "sparkle", 1.5); }
            return c.T < 30;
        }
        if (!m.To(Posture.Sit)) return true;
        m.Anim.Play("sit_look");
        return c.T - c.B < 2;
    }, null, 1);

    // A button or panel: he paws at it a couple of times (he never actually clicks anything).
    static Behavior PressButton(ContentLedge l) => new Custom("Button", (m, c, dt) =>
    {
        if (!OnIt(m, l)) return Admire(m, c, l, dt);
        if (!m.To(Posture.Lie)) return true;
        if (c.N == 0) { c.N = 1; c.A = c.T + 0.6; }
        if (c.N < 4 && c.T > c.A)
        {
            c.N++;
            c.A = c.T + 0.9;
            m.Anim.Play("peek_paw", true);
            if (c.N == 3) m.Emote(m.Rng.NextDouble() < 0.5 ? "question" : "exclaim", 1.3);
        }
        else if (m.Anim.Finished || m.Anim.Name != "peek_paw") m.Anim.Play("peek");
        return c.T < 4.5;
    }, null, 1);
}
