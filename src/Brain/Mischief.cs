using MiloBot.Win32;
using MiloBot.World;

namespace MiloBot.Brain;

/// <summary>
/// A light version of Desktop Goose: now and then Milo tugs a small window a little to the side, or bats your mouse
/// when it has been lying still for a while. Never much, never off the screen, and never while you're holding a button.
/// </summary>
public partial class Milo
{
    public bool Mischief = true;
    double cursorStill, stillX, stillY;

    void UpdateCursorStill(double dt)
    {
        if (Math.Abs(W.CursorX - stillX) > 3 || Math.Abs(W.CursorY - stillY) > 3)
        {
            stillX = W.CursorX;
            stillY = W.CursorY;
            cursorStill = 0;
        }
        else cursorStill += dt;
    }

    public static bool MouseButtonDown() =>
        (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0 || (Native.GetAsyncKeyState(Native.VK_RBUTTON) & 0x8000) != 0 || (Native.GetAsyncKeyState(Native.VK_MBUTTON) & 0x8000) != 0;

    bool CanBatCursor =>
        Mischief && cursorStill > 20 && W.UserIdle > 8 && W.UserIdle < 90 && W.KeysPerSecond < 0.1 && !W.ForegroundFullscreen
        && !W.Blocked(W.CursorX, W.CursorY) && W.ScreenAt(Body.X, Body.Y - 10) is World.Screen s && s.Bounds.Contains(W.CursorX, W.CursorY)
        && Memory.Ready("batcursor", Now) && Energy > 0.35;

    Platform NudgeCandidate()
    {
        if (!Mischief || !Memory.Ready("nudge", Now) || W.UserIdle < 4 || W.KeysPerSecond > 0.2 || Energy < 0.4) return null;
        var s = W.ScreenAt(Body.X, Body.Y - 10);
        if (s == null) return null;
        Platform best = null;
        foreach (var p in W.Platforms)
        {
            if (p.IsFloor || p.IsContent || p.Width < U(60)) continue;
            if (p.Hwnd == W.ForegroundHwnd && W.UserIdle < 30) continue;
            if (Native.IsZoomed(p.Hwnd) || WindowRect(p.Hwnd) is not RectD r) continue;
            if (!s.Bounds.Contains(r.CenterX, r.Top + 5)) continue;
            if (r.Width > s.Work.Width * 0.7 || r.Height > s.Work.Height * 0.85) continue;
            if (Math.Abs(p.Y - r.Top) > 2 || Math.Abs(p.Y - Body.Y) > MaxJumpUp * 2) continue;
            if (best == null || Math.Abs(p.Y - Body.Y) < Math.Abs(best.Y - Body.Y)) best = p;
        }
        return best;
    }
}

public class WindowNudge : Behavior
{
    readonly IntPtr hwnd;
    readonly int dir;
    readonly double amount;
    readonly GoTo go;
    double moved, nextStep, standX;
    int phase;

    public override int Priority => 3;
    public override string Name => "Nudge";

    WindowNudge(IntPtr hwnd, int dir, double amount, double standX, double y)
    {
        this.hwnd = hwnd;
        this.dir = dir;
        this.amount = amount;
        this.standX = standX;
        go = new GoTo(standX, y, Gait.Happy, 3, 3);
    }

    public static Behavior TryMake(Milo m, Platform p)
    {
        if (m.WindowRect(p.Hwnd) is not RectD r) return null;
        var s = m.W.ScreenAt(r.CenterX, r.CenterY);
        if (s == null) return null;
        double roomRight = s.Work.Right - r.Right, roomLeft = r.Left - s.Work.Left;
        int dir = roomRight > roomLeft ? 1 : -1;
        double room = Math.Max(roomRight, roomLeft) - 10;
        if (room < 60) return null;
        double amount = Math.Min(room, 60 + m.Rng.NextDouble() * 110);
        // He grabs the top edge near the end he's pulling towards.
        double standX = dir > 0 ? Math.Min(p.X2, r.Right) - m.U(22) : Math.Max(p.X1, r.Left) + m.U(22);
        m.Memory.Cool("nudge", 1200 + m.Rng.NextDouble() * 900, m.Now);
        return new WindowNudge(p.Hwnd, dir, amount, standX, p.Y);
    }

    bool OnWindow(Milo m) => m.Body.OnGround && m.Body.Platform != null && !m.Body.Platform.IsContent && m.Body.Platform.Hwnd == hwnd;

    bool Allowed(Milo m) =>
        Native.IsWindow(hwnd) && Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd) && !Native.IsZoomed(hwnd)
        && !Milo.MouseButtonDown() && m.W.KeysPerSecond < 0.5 && !(m.W.ForegroundHwnd == hwnd && m.W.UserIdle < 1);

    protected override bool Tick(Milo m, double dt)
    {
        switch (phase)
        {
            case 0:
                if (go.Update(m, dt)) return T < 25;
                if (!OnWindow(m) || !Allowed(m)) return false;
                phase = 1;
                m.Facing = -dir;
                m.Emote("sparkle", 1.2);
                m.Say("growl", 5);
                return true;
            case 1:
                if (!OnWindow(m) || !Allowed(m)) { phase = 2; return true; }
                m.Facing = -dir;
                m.Anim.Play("tug");
                if (m.Now < nextStep) return true;
                // Little jerks, like pulling on a toy.
                nextStep = m.Now + 0.09 + (m.Rng.NextDouble() < 0.15 ? 0.35 : 0);
                double step = Math.Min(amount - moved, 5 + m.Rng.NextDouble() * 6);
                if (step <= 0.5) { phase = 2; return true; }
                if (!Native.GetWindowRect(hwnd, out var wr)) { phase = 2; return true; }
                Native.SetWindowPos(hwnd, IntPtr.Zero, wr.Left + (int)Math.Round(step * dir), wr.Top, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | Native.SWP_ASYNCWINDOWPOS);
                moved += step;
                return T < 40;
            default:
                if (moved > 20) m.Save.Stat("nudges");
                m.Set(new Sequence(1, "Proud",
                    new PlayOnce("happy", Posture.Stand, 1.2, moved > 20 ? "happy" : null, moved > 20 ? "heart" : "question", 1),
                    new Rest(Posture.Sit, 2, m.W.CursorX, 1)));
                return false;
        }
    }
}

public class CursorBat : Behavior
{
    readonly double startX, startY;
    readonly GoTo go;
    int phase;
    double phaseT, fromX, fromY, toX, toY, setX, setY;

    public override int Priority => 1;
    public override string Name => "BatCursor:" + phase;

    public CursorBat(Milo m)
    {
        startX = m.W.CursorX;
        startY = m.W.CursorY;
        double side = m.Body.X < startX ? -1 : 1;
        go = new GoTo(startX + side * m.U(16), startY, Gait.Walk, 1, 6);
        m.Memory.Cool("batcursor", 420 + m.Rng.NextDouble() * 480, m.Now);
    }

    bool CursorMoved(Milo m) => Math.Abs(m.W.CursorX - startX) > 12 || Math.Abs(m.W.CursorY - startY) > 12;

    protected override bool Tick(Milo m, double dt)
    {
        phaseT += dt;
        if (phase < 3 && CursorMoved(m))
        {
            m.Face(m.W.CursorX, 0);
            m.Emote(m.Rng.NextDouble() < 0.5 ? "question" : "exclaim", 1.2);
            return false;
        }
        switch (phase)
        {
            case 0:
                if (go.Update(m, dt)) return T < 20;
                m.Face(startX, 0);
                phase = 1;
                phaseT = 0;
                return true;
            case 1:
            {
                // Stalk it for a moment.
                double above = m.Body.Y - startY;
                m.Face(startX, 0);
                if (!m.To(Posture.Stand)) return true;
                m.Anim.Play(above > m.U(30) ? "idle_look" : "alert");
                if (phaseT < 1.2) return true;
                bool reach = above > -m.U(4) && above < m.U(70) && Math.Abs(startX - m.Body.X) < m.U(45);
                if (!reach)
                {
                    m.Emote("question", 1.5);
                    m.Set(new Sequence(1, "StareAtCursor", new Rest(Posture.Sit, 3, startX, 1)));
                    return false;
                }
                phase = 2;
                phaseT = 0;
                m.Anim.Play("stretch", true);
                return true;
            }
            case 2:
                m.Face(startX, 0);
                if (phaseT < 0.7) return true;
                if (Milo.MouseButtonDown()) return false;
                // Swat!
                var s = m.W.ScreenAt(startX, startY);
                int dir = m.Facing;
                double dist = m.U(20) + m.Rng.NextDouble() * m.U(20);
                fromX = setX = startX;
                fromY = setY = startY;
                toX = s == null ? startX + dir * dist : Math.Clamp(startX + dir * dist, s.Bounds.Left + 5, s.Bounds.Right - 5);
                toY = s == null ? startY - m.U(6) : Math.Clamp(startY - m.U(6), s.Bounds.Top + 5, s.Bounds.Bottom - 5);
                if (m.Body.Y - startY > m.U(20)) m.Hop(0, 120, 0);
                else m.Anim.Play("bark", true);
                m.Say("yip", 1);
                m.Save.Stat("bats");
                phase = 3;
                phaseT = 0;
                return true;
            case 3:
            {
                double k = Math.Min(1, phaseT / 0.25);
                double e = 1 - (1 - k) * (1 - k);
                // Stop the moment you grab the mouse yourself.
                bool userTookOver = Math.Abs(m.W.CursorX - setX) > 25 || Math.Abs(m.W.CursorY - setY) > 25;
                if (Milo.MouseButtonDown() || phaseT > 0.05 && userTookOver) k = 1;
                else
                {
                    setX = Math.Round(fromX + (toX - fromX) * e);
                    setY = Math.Round(fromY + (toY - fromY) * e);
                    Native.SetCursorPos((int)setX, (int)setY);
                }
                if (k < 1) return true;
                m.Set(new Sequence(1, "BatCursor:Done",
                    new PlayOnce("happy", Posture.Stand, 1.2, "happy", "sparkle", 1),
                    new Rest(Posture.Sit, 1.5, toX, 1)));
                return false;
            }
        }
        return false;
    }
}
