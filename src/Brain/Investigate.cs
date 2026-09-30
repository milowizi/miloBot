using MiloBot.World;

namespace MiloBot.Brain;

public enum Reaction
{
    Bark, Sniff, Beg, Tilt, Happy, Scared, Comfort, Growl, Chase, Watch, Glance,
    PlayBow, ShowOff, LieNear, BringToy, SideEye, Sigh, Howl, Unimpressed, Stare, LickScreen, Zoomies,
    Pounce, PeerDown, Cool
}

public static class Investigate
{
    static Custom Once(string name, Action<Milo> first, Func<Milo, Custom, double, bool> tick, int priority = 2) =>
        new(name, (m, c, dt) =>
        {
            if (c.N == 0) { c.N = 1; first?.Invoke(m); }
            return tick(m, c, dt);
        }, null, priority);

    static Custom Look(double x, double seconds) => new("Look", (m, c, dt) => { m.Face(x, 0); m.Alert(0.3); m.Live(dt, false); return c.T < seconds; }, null, 2);

    public static Behavior Pounce(Milo m, double x, double y) => new Custom("Pounce", (mm, c, dt) =>
    {
        switch (c.N)
        {
            case 0:
                mm.Face(x, 0);
                if (!mm.To(Posture.Stand)) return true;
                c.N = 1;
                c.A = c.T;
                mm.Anim.Play("stretch", true);
                return true;
            case 1:
                mm.Face(x, 0);
                if (c.T - c.A < 0.85) return true;
                c.N = 2;
                mm.Leap(x, y);
                mm.Say("yip", 1);
                mm.Save.Stat("pounces");
                return true;
            case 2:
                c.N = 3;
                c.A = c.T;
                c.B = mm.Rng.NextDouble() < 0.35 ? 1 : 0;
                mm.Emote(c.B > 0 ? "sparkle" : "question", 1.6);
                if (c.B > 0) mm.Say("happy", 3);
                return true;
            default:
                mm.Face(x, 0);
                if (c.B > 0) mm.Anim.Play("happy");
                else mm.Live(dt, false);
                return c.T - c.A < 1.8;
        }
    }, null, 2);

    public static Platform PerchAbove(Milo m, double x, double y)
    {
        Platform best = null;
        foreach (var p in m.W.Platforms)
        {
            if (p.IsFloor || !p.Contains(x, m.Body.HalfWidth * 0.5) || p.Y >= y || y - p.Y > m.U(170)) continue;
            if (m.WindowRect(p.Hwnd) is not RectD r || !r.Contains(x, y - 2)) continue;
            if (best == null || p.Y > best.Y) best = p;
        }
        return best;
    }

    static Behavior PeerDown(Milo m, Platform perch, double x, string icon) => new Custom("PeerDown", (mm, c, dt) =>
    {
        var p = mm.Body.Platform;
        if (p == null || p.Hwnd != perch.Hwnd) return false;
        mm.Face(x, 0);
        if (!mm.To(Posture.Lie)) return true;
        if (c.N == 0) { c.N = 1; c.A = c.T + 1.2; mm.Emote(icon, 1.8); }
        if (c.T > c.A)
        {
            c.A = c.T + 1.4 + mm.Rng.NextDouble() * 2.2;
            if (mm.Rng.NextDouble() < 0.6) { mm.Anim.Play("peek_paw", true); mm.Save.Stat("swats"); }
            else mm.Anim.Play("peek_blink", true);
        }
        else if (mm.Anim.Finished || mm.Anim.Name is not ("peek_paw" or "peek_blink")) mm.Anim.Play("peek");
        return c.T < 8 + mm.Rng.NextDouble() * 0.1 + (icon == "heart" ? 4 : 0);
    }, null, 2);

    public static Behavior Make(Milo m, double x, double y, Reaction r, string label, string kind = null)
    {
        if (r == Reaction.PeerDown)
        {
            var perch = PerchAbove(m, x, y);
            if (perch == null) r = y < m.Body.Y - m.U(30) ? Reaction.Pounce : Reaction.Sniff;
            else
            {
                string icon = kind switch { "food" => "food", "cat" => "cat", "dog" or "cute" => "heart", _ => "exclaim" };
                return new Sequence(2, "Investigate:" + label, Notice(x, r),
                    new GoTo(x, perch.Y, Gait.Happy, 2, 6),
                    PeerDown(m, perch, x, icon));
            }
        }
        if (r == Reaction.Pounce)
        {
            double pside = m.Body.X < x ? -1 : 1;
            return new Sequence(2, "Investigate:" + label, Notice(x, r),
                new GoTo(x + pside * m.U(45), y, Gait.Run, 2, 10),
                Pounce(m, x, y));
        }
        Gait gait = r is Reaction.Bark or Reaction.Chase or Reaction.Happy or Reaction.Beg or Reaction.PlayBow or Reaction.LickScreen ? Gait.Run : Gait.Walk;
        double side = m.Body.X < x ? -1 : 1;
        double near = r is Reaction.LieNear or Reaction.LickScreen ? 8 : 22;
        var go = new GoTo(x + side * m.U(near), y, gait, 2, 10);
        Behavior act = r switch
        {
            Reaction.Bark => new Sequence(2, "BarkAt",
                new PlayOnce("bark", Posture.Stand, 1.2, "bark", "exclaim", 2),
                Look(x, 1.5),
                m.Rng.NextDouble() < 0.5 ? new PlayOnce("bark", Posture.Stand, 1.2, "yip", null, 2) : null,
                new Custom("Wag", (mm, c, dt) => { mm.Anim.Play("idle_wag"); return c.T < 2; }, null, 2)),
            Reaction.Sniff => new Sequence(2, "SniffAt",
                new PlayOnce("sniff", Posture.Stand, 2.5, "sniff", null, 2),
                Look(x, 1.5)),
            Reaction.Beg => new Sequence(2, "Beg",
                Once("Drool", mm => mm.Emote("food", 2.2), (mm, c, dt) => { mm.Face(x, 0); if (!mm.To(Posture.Sit)) return true; mm.Anim.Play("beg"); return c.T < 3.5; }),
                Once("Please", mm => mm.Say("whine", 10), (mm, c, dt) => { mm.Face(mm.W.CursorX, 0); if (!mm.To(Posture.Front)) return true; mm.Anim.Play("front_look"); return c.T < 2.5; })),
            Reaction.Tilt => Once("Tilt", mm => { mm.Emote("question", 2.2); mm.Tilt(1.3); }, (mm, c, dt) =>
            {
                if (!mm.To(Posture.Front)) return true;
                if (c.T > 1.4 && c.N == 1) { c.N = 2; mm.Tilt(1.3); }
                mm.Live(dt, false);
                return c.T < 3.2;
            }),
            Reaction.Happy => Once("Yay", mm => { mm.Float("sparkle"); mm.Say("happy", 5); mm.Happiness = Math.Min(1, mm.Happiness + 0.15); }, (mm, c, dt) =>
            {
                mm.Anim.Play("happy");
                if (c.T > 0.5 && c.N == 1 && mm.Body.OnGround) { c.N = 2; mm.Hop(0, 110, 0); }
                return c.T < 2.5;
            }),
            Reaction.Scared => Plans.Scared(m, x),
            Reaction.Comfort => Plans.SeekUser(m),
            Reaction.Growl => new Sequence(2, "Growl",
                new PlayOnce("growl", Posture.Stand, 1.8, "growl", "puff", 2),
                Look(x, 1.5)),
            Reaction.Chase => new Custom("Pounce", (mm, c, dt) =>
            {
                mm.Face(x, 0);
                if (c.N < 2 && mm.Body.OnGround && c.T > c.N * 0.9) { c.N++; mm.Hop(mm.Facing, 180, 25); mm.Say("yip", 2); }
                if (mm.Body.OnGround) mm.Anim.Play("happy");
                return c.T < 2.5;
            }, null, 2),
            Reaction.Watch => Plans.Watch(m, x, y, 20 + m.Rng.NextDouble() * 20),
            Reaction.PlayBow => new Sequence(2, "PlayBow",
                new PlayOnce("stretch", Posture.Stand, 2.2, "yip", "heart", 2),
                new Custom("Wag", (mm, c, dt) => { mm.Face(x, 0); mm.Anim.Play("happy"); return c.T < 1.5; }, null, 2)),
            Reaction.ShowOff => new Sequence(2, "ShowOff", Look(x, 0.6), new TrickBehavior(Tricks.BestKnown(m), false, 2)),
            Reaction.LieNear => Once("Friends", mm => { mm.Emote("heart", 2); mm.Face(x, 0); }, (mm, c, dt) =>
            {
                mm.Face(x, 0);
                if (!mm.To(Posture.Lie)) return true;
                mm.Live(dt, false);
                if (c.T > 6 && c.N == 1) { c.N = 2; mm.Float("heart"); }
                return c.T < 18 + mm.Rng.NextDouble() * 4;
            }),
            Reaction.BringToy => new Deliver(null, mm => (x + (mm.Body.X < x ? -1 : 1) * mm.U(14), y), 2, false),
            Reaction.SideEye => new Sequence(2, "SideEye", Look(x, 1.2),
                Once("TurnAway", mm => { mm.Facing = x > mm.Body.X ? -1 : 1; mm.Emote("puff", 1.4); }, (mm, c, dt) =>
                {
                    if (!mm.To(Posture.Sit)) return true;
                    mm.Anim.Play("sit");
                    return c.T < 3;
                })),
            Reaction.Sigh => Once("Sigh", mm => mm.Emote("dots", 2.4), (mm, c, dt) =>
            {
                if (!mm.To(Posture.Lie)) return true;
                if (c.T > 1 && c.N == 1) { c.N = 2; mm.Say("whine", 20); }
                mm.Live(dt, false);
                return c.T < 6;
            }),
            Reaction.Howl => Plans.Howl(m),
            Reaction.Unimpressed => new Sequence(2, "Meh", Look(x, 1), new PlayOnce("yawn", Posture.Sit, 1.6, "yawn", null, 2),
                Once("LookAway", mm => mm.Facing = x > mm.Body.X ? -1 : 1, (mm, c, dt) => { mm.Anim.Play("sit"); return c.T < 2; })),
            Reaction.Stare => Once("Stare", mm => mm.Face(x, 0), (mm, c, dt) =>
            {
                mm.Face(x, 0);
                if (!mm.To(Posture.Sit)) return true;
                mm.Anim.Play(c.T > 4 ? "sit_pant" : "sit_look");
                if (c.T > 5 && c.N == 1) { c.N = 2; mm.Emote(label.Length > 0 && mm.Rng.NextDouble() < 0.5 ? "food" : "dots", 2); }
                return c.T < 9;
            }),
            Reaction.LickScreen => Once("Lick", mm => { mm.Face(x, 0); mm.Emote("heart", 1.5); }, (mm, c, dt) =>
            {
                if (!mm.To(Posture.Sit)) return true;
                mm.Anim.Play("beg");
                if ((int)(c.T * 3) != c.N && mm.Rng.NextDouble() < 0.4) { c.N = (int)(c.T * 3); mm.Float("sweat", 0.8); }
                return c.T < 3.5;
            }),
            Reaction.Zoomies => Plans.Zoomies(m),
            Reaction.Cool => Once("Cool", mm => { mm.ShadesUntil = mm.Now + 15; mm.Float("sparkle"); mm.Say("happy", 5); }, (mm, c, dt) =>
            {
                mm.Face(mm.W.CursorX, 0);
                if (!mm.To(Posture.Front)) return true;
                mm.Anim.Play(c.T > 0.4 && c.T < 1.4 ? "wink" : "front_happy");
                return c.T < 4;
            }),
            _ => Look(x, 1.5)
        };
        var notice = Notice(x, r);
        if (r is Reaction.Comfort or Reaction.Scared or Reaction.Watch or Reaction.Glance or Reaction.BringToy or Reaction.Howl or Reaction.Zoomies or Reaction.Unimpressed or Reaction.SideEye or Reaction.Cool)
            return new Sequence(2, "Investigate:" + label, notice, act);
        return new Sequence(2, "Investigate:" + label, notice, go, new Custom("FaceIt", (mm, c, dt) => { mm.Face(x, 0); return false; }, null, 2), act);
    }

    static Behavior Notice(double x, Reaction r)
    {
        (string icon, string sound) = r switch
        {
            Reaction.Bark => ("exclaim", "yip"),
            Reaction.Growl => ("puff", "growl"),
            Reaction.Beg or Reaction.Stare or Reaction.LickScreen => ("food", null),
            Reaction.Tilt => ("question", null),
            Reaction.Happy or Reaction.ShowOff => ("sparkle", null),
            Reaction.Chase or Reaction.PlayBow or Reaction.Zoomies or Reaction.Pounce => ("exclaim", "yip"),
            Reaction.PeerDown => ("exclaim", null),
            Reaction.Cool => ("sparkle", null),
            Reaction.Comfort or Reaction.LieNear => ("heart", null),
            Reaction.SideEye or Reaction.Unimpressed or Reaction.Sigh => ("dots", null),
            Reaction.Scared or Reaction.Howl or Reaction.BringToy => (null, null),
            _ => ("exclaim", null)
        };
        return new Custom("Notice", (m, c, dt) =>
        {
            if (c.N == 0)
            {
                c.N = 1;
                m.Face(x, 0);
                m.Alert(1);
                if (icon != null) m.Emote(icon, 1.6);
                if (sound != null) m.Say(sound, 3);
            }
            if (m.Posture == Posture.Stand) m.Anim.Play("alert");
            else if (!m.Asleep) m.Live(dt, false);
            return c.T < 0.7;
        }, null, 2);
    }

    static readonly Dictionary<string, Reaction[][]> Ladder = new()
    {
        ["dog"] = new[] { new[] { Reaction.Bark }, new[] { Reaction.Happy, Reaction.PlayBow }, new[] { Reaction.PlayBow, Reaction.ShowOff, Reaction.BringToy, Reaction.PeerDown }, new[] { Reaction.LieNear, Reaction.Howl, Reaction.ShowOff, Reaction.PeerDown } },
        ["cat"] = new[] { new[] { Reaction.Growl, Reaction.Bark }, new[] { Reaction.Bark, Reaction.Tilt, Reaction.Growl, Reaction.Pounce }, new[] { Reaction.PeerDown, Reaction.SideEye, Reaction.Stare, Reaction.Pounce }, new[] { Reaction.LieNear, Reaction.SideEye, Reaction.PeerDown } },
        ["animal"] = new[] { new[] { Reaction.Tilt, Reaction.Sniff }, new[] { Reaction.Sniff, Reaction.Stare, Reaction.PeerDown }, new[] { Reaction.PlayBow, Reaction.Pounce, Reaction.PeerDown }, new[] { Reaction.LieNear, Reaction.BringToy, Reaction.PeerDown } },
        ["food"] = new[] { new[] { Reaction.Beg }, new[] { Reaction.Beg, Reaction.Stare }, new[] { Reaction.ShowOff, Reaction.Beg, Reaction.PeerDown }, new[] { Reaction.LickScreen, Reaction.Stare, Reaction.ShowOff } },
        ["ball"] = new[] { new[] { Reaction.Pounce }, new[] { Reaction.Pounce, Reaction.PlayBow }, new[] { Reaction.BringToy, Reaction.Pounce }, new[] { Reaction.BringToy, Reaction.SideEye } },
        ["person_face"] = new[] { new[] { Reaction.Tilt }, new[] { Reaction.Happy, Reaction.Tilt }, new[] { Reaction.ShowOff, Reaction.Happy }, new[] { Reaction.LieNear, Reaction.Happy } },
        ["error"] = new[] { new[] { Reaction.Tilt }, new[] { Reaction.Tilt, Reaction.Sigh }, new[] { Reaction.Sigh, Reaction.Unimpressed }, new[] { Reaction.Comfort, Reaction.SideEye, Reaction.Unimpressed } },
        ["success"] = new[] { new[] { Reaction.Happy }, new[] { Reaction.Happy, Reaction.Zoomies, Reaction.Cool }, new[] { Reaction.Zoomies, Reaction.Howl, Reaction.Cool }, new[] { Reaction.Cool, Reaction.Howl, Reaction.ShowOff } },
        ["sad"] = new[] { new[] { Reaction.Comfort }, new[] { Reaction.Comfort }, new[] { Reaction.BringToy, Reaction.Comfort }, new[] { Reaction.BringToy, Reaction.LieNear } },
        ["scary"] = new[] { new[] { Reaction.Scared }, new[] { Reaction.Scared, Reaction.Growl }, new[] { Reaction.Growl, Reaction.Unimpressed }, new[] { Reaction.Unimpressed, Reaction.SideEye } },
        ["cute"] = new[] { new[] { Reaction.Happy }, new[] { Reaction.Tilt, Reaction.Happy, Reaction.PeerDown }, new[] { Reaction.LieNear, Reaction.PeerDown }, new[] { Reaction.LieNear, Reaction.Howl } },
        ["video_player"] = new[] { new[] { Reaction.Watch }, new[] { Reaction.Watch }, new[] { Reaction.Watch, Reaction.Unimpressed }, new[] { Reaction.Watch, Reaction.LieNear } },
        ["other"] = new[] { new[] { Reaction.Sniff, Reaction.Glance }, new[] { Reaction.Glance }, new[] { Reaction.Glance, Reaction.Stare }, new[] { Reaction.Glance } },
    };

    public static string Family(string kind) => kind switch
    {
        "toy" => "ball",
        "warning" => "error",
        "victory" => "success",
        "game_over" => "sad",
        "text" or "button" => "other",
        _ => Ladder.ContainsKey(kind) ? kind : "other"
    };

    public static Reaction Pick(string kind, int stage, Random rng)
    {
        var ladder = Ladder[Family(kind)];
        double roll = rng.NextDouble();
        if (roll < 0.08) stage = 0;
        else if (roll < 0.16) stage = Math.Min(3, stage + 1);
        var options = ladder[Math.Clamp(stage, 0, 3)];
        return options[rng.Next(options.Length)];
    }
}
