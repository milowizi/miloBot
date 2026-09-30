using MiloBot.World;

namespace MiloBot.Brain;

public class GoTo : Behavior
{
    readonly Func<Milo, (double x, double y)> target;
    readonly Gait gait;
    readonly int priority;
    readonly double tolerance;
    readonly bool whine;
    Platform goal;
    Platform hop;
    double takeoff, landing;
    enum Phase { Plan, Walk, ToTakeoff, Crouch, Air, ToEdge, Peer, GiveUp }
    Phase phase = Phase.Plan;
    int attempts;
    double phaseT;
    int edgeDir;
    public bool Failed { get; private set; }

    public GoTo(double x, double y, Gait gait, int priority = 0, double tolerance = 2, bool whine = false)
        : this(_ => (x, y), gait, priority, tolerance, whine) { }

    public GoTo(Func<Milo, (double x, double y)> target, Gait gait, int priority = 0, double tolerance = 2, bool whine = false)
    {
        this.target = target;
        this.gait = gait;
        this.priority = priority;
        this.tolerance = tolerance;
        this.whine = whine;
    }

    public override int Priority => priority;
    public override bool Jumping => phase == Phase.Air;
    public override string Name => "GoTo:" + phase;

    public override void Landed(Milo m)
    {
        phase = Phase.Plan;
    }

    public override void Resume(Milo m)
    {
        phase = Phase.Plan;
    }

    void Set(Phase p)
    {
        phase = p;
        phaseT = 0;
    }

    protected override bool Tick(Milo m, double dt)
    {
        phaseT += dt;
        if (!m.Body.OnGround) return true;
        if (!m.To(Posture.Stand)) return true;
        var (tx, ty) = target(m);
        var cur = m.Body.Platform;
        if (cur == null) return true;
        switch (phase)
        {
            case Phase.Plan:
                if (++attempts > 7) { Set(Phase.GiveUp); return true; }
                goal = ResolveGoal(m, tx, ty);
                if (goal == null || Same(cur, goal))
                {
                    Set(Phase.Walk);
                    return true;
                }
                hop = goal;
                if (goal.Y > cur.Y + m.U(4) && !(m.CanReach(cur, goal, out _, out _) && Math.Abs(goal.Y - cur.Y) < m.U(20)))
                {
                    double leftEdge = cur.X1, rightEdge = cur.X2;
                    bool goalLeft = tx < m.Body.X;
                    if (tx < cur.X1) edgeDir = -1;
                    else if (tx > cur.X2) edgeDir = 1;
                    else edgeDir = (tx - leftEdge) < (rightEdge - tx) ? -1 : 1;
                    if (goalLeft && tx >= cur.X1 && tx <= cur.X2 && Math.Abs(tx - leftEdge) < Math.Abs(tx - rightEdge) * 1.5) edgeDir = -1;
                    Set(Phase.ToEdge);
                    return true;
                }
                if (m.PlanJump(cur, goal, tx, out takeoff, out landing))
                {
                    Set(Phase.ToTakeoff);
                    return true;
                }
                hop = FindStep(m, cur, goal);
                if (hop != null && m.CanReach(cur, hop, out takeoff, out landing))
                {
                    Set(Phase.ToTakeoff);
                    return true;
                }
                if (goal.Y > cur.Y)
                {
                    edgeDir = tx < m.Body.X ? -1 : 1;
                    Set(Phase.ToEdge);
                    return true;
                }
                Set(Phase.GiveUp);
                return true;

            case Phase.Walk:
                if (m.WalkTo(tx, gait, dt, tolerance))
                    return false;
                if (phaseT > 1.0 && Math.Abs(m.Body.X - Math.Clamp(tx, cur.X1 + m.Body.HalfWidth * 0.6, cur.X2 - m.Body.HalfWidth * 0.6)) < 2) return false;
                if (phaseT > 3 && !Same(cur, ResolveGoal(m, tx, ty) ?? cur)) Set(Phase.Plan);
                return phaseT < 30;

            case Phase.ToTakeoff:
                if (m.WalkTo(takeoff, gait == Gait.Sniff ? Gait.Walk : gait, dt, 1))
                {
                    m.Face(landing, 0);
                    m.Anim.Play("land", true);
                    Set(Phase.Crouch);
                }
                return phaseT < 20;

            case Phase.Crouch:
                if (phaseT >= 0.14)
                {
                    m.Launch(landing, hop.Y);
                    Set(Phase.Air);
                }
                return true;

            case Phase.ToEdge:
                double ex = edgeDir < 0 ? cur.X1 + m.Body.HalfWidth * 0.6 : cur.X2 - m.Body.HalfWidth * 0.6;
                if (m.WalkTo(ex, gait == Gait.Sniff ? Gait.Walk : gait, dt, 1))
                {
                    m.Facing = edgeDir;
                    var below = m.W.PlatformBelow(ex + edgeDir * m.U(30), cur.Y + 2);
                    double drop = below != null ? below.Y - cur.Y : m.U(300);
                    if (drop > m.U(90) && gait is not (Gait.Run or Gait.Zoom) && m.Rng.NextDouble() < 0.7)
                    {
                        m.Anim.Play("look_down");
                        if (drop > m.U(250) && m.Rng.NextDouble() < 0.4) m.Emote("sweat", 1);
                        Set(Phase.Peer);
                        return true;
                    }
                    m.Hop(edgeDir, 120, 70);
                    Set(Phase.Air);
                }
                return phaseT < 25;

            case Phase.Peer:
                m.Anim.Play("look_down");
                if (phaseT > 0.6 + m.Rng.NextDouble() * 0.02)
                {
                    m.Hop(edgeDir, 120, 70);
                    Set(Phase.Air);
                }
                return true;

            case Phase.GiveUp:
                if (!Failed)
                {
                    Failed = true;
                    if (whine)
                    {
                        m.Emote("question");
                        m.Say("whine", 15);
                    }
                }
                if (m.WalkTo(tx, gait == Gait.Sniff ? Gait.Walk : gait, dt, tolerance) || phaseT > 10)
                {
                    m.Face(tx, 0);
                    return false;
                }
                return true;
        }
        return true;
    }

    static bool Same(Platform a, Platform b) => a.Hwnd == b.Hwnd && Math.Abs(a.Y - b.Y) < 3 && a.X1 < b.X2 && b.X1 < a.X2;

    static Platform ResolveGoal(Milo m, double tx, double ty)
    {
        Platform best = null;
        double bestScore = double.MaxValue;
        foreach (var p in m.W.Platforms)
        {
            if (p.Width < m.Body.HalfWidth * 2) continue;
            double dx = tx < p.X1 ? p.X1 - tx : tx > p.X2 ? tx - p.X2 : 0;
            double dy = p.Y - ty;
            double score = dx * 2 + (dy >= -m.U(6) ? dy : -dy * 3 + m.U(40));
            if (score < bestScore)
            {
                bestScore = score;
                best = p;
            }
        }
        return best;
    }

    static Platform FindStep(Milo m, Platform cur, Platform goal)
    {
        Platform best = null;
        double bestScore = double.MaxValue;
        foreach (var p in m.W.Platforms)
        {
            if (p.Y >= cur.Y - m.U(4) || p.Y < goal.Y - 1) continue;
            if (Same(p, cur)) continue;
            if (!m.CanReach(cur, p, out _, out _)) continue;
            double hx = goal.X1 > p.X2 ? goal.X1 - p.X2 : p.X1 > goal.X2 ? p.X1 - goal.X2 : 0;
            double score = (p.Y - goal.Y) + hx * 0.7;
            if (score < bestScore)
            {
                bestScore = score;
                best = p;
            }
        }
        return best;
    }
}
