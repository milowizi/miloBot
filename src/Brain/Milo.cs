using MiloBot.Audio;
using MiloBot.Gfx;
using MiloBot.World;

namespace MiloBot.Brain;

public enum Posture { Stand, Sit, Lie, Sleep, Front, Belly, Air, Held }

public class Particle
{
    public string Icon;
    public double X, Y, VX, VY, Life, MaxLife;
    public bool OnHead, Dream, World;
    public double Wobble;
}

public partial class Milo
{
    public readonly Sprites Sprites;
    public readonly Animator Anim;
    public readonly Body Body = new();
    public readonly WorldState W;
    public readonly Sound Sound;
    public readonly Toys Toys;
    public readonly Memory Memory = new();
    public SaveData Save = new();
    public Home Home = new();
    public Laser Laser = new();
    public Critters Critters = new();
    public double RenderLift;
    public readonly Random Rng = new();
    public readonly List<Particle> Particles = new();

    public int Scale = 3;
    public int Facing = 1;
    public Posture Posture = Posture.Stand;
    public Behavior Current;
    readonly Stack<Behavior> suspended = new();
    public Func<IntPtr, RectD?> WindowRect = _ => null;
    public bool Hidden;
    public string DebugText = "";

    public double Energy = 0.8, Social = 0.3, Boredom = 0.2, Happiness = 0.4, Excitement = 0.0;

    public Milo(Sprites sprites, WorldState world, Sound sound, Toys toys)
    {
        Sprites = sprites;
        W = world;
        Sound = sound;
        Toys = toys;
        Anim = new Animator(sprites);
    }

    public double Now => W.Now;
    public double S => Scale;
    public double U(double spritePx) => spritePx * Scale;

    public void Spawn()
    {
        var floor = W.Platforms.Where(p => p.IsFloor).OrderByDescending(p => p.Width).FirstOrDefault();
        if (floor == null) return;
        Body.X = floor.X1 + floor.Width * (0.55 + Rng.NextDouble() * 0.3);
        Body.Y = floor.Y - U(60);
        Body.VX = 0;
        Body.VY = 0;
        Body.Detach();
        Posture = Posture.Air;
        UpdateBodySize();
    }

    public void UpdateBodySize()
    {
        Body.HalfWidth = U(18);
        Body.Height = U(34);
    }

    public void Update(double dt)
    {
        UpdateNeeds(dt);
        UpdateTyping(dt);
        UpdateParticles(dt);
        UpdateSenses(dt);
        UpdateCursorStill(dt);
        if (ContentPlay) UpdateContent(dt);

        if (TopDown && Posture != Posture.Held)
        {
            UpdateTop(dt);
            return;
        }

        if (Posture == Posture.Held)
        {
            if (Current is not Held) Set(new Held());
            Current?.Update(this, dt);
            Anim.Update(dt);
            return;
        }

        if (Body.Climbing)
        {
            // On a wall or the ceiling only Crawl knows how to move him; anything else (a click, a scare) makes him let go.
            if (Current is Crawl crawl)
            {
                if (!crawl.Update(this, dt) && Current == crawl)
                {
                    crawl.Exit(this);
                    Current = null;
                }
            }
            if (Body.Climbing && Current is not Crawl) LetGo();
            Anim.Update(dt);
            return;
        }

        var res = Physics.Step(Body, W, dt, U(900), 0.0, WindowRect);
        if (res == StepResult.Detached) OnDetached();
        if (!Body.OnGround)
        {
            if (Posture != Posture.Air) ForcePosture(Posture.Air);
            if (hopping)
            {
                Anim.Play(pouncing ? "zoom" : "run");
                Anim.SetFrame(Body.VY < 0 ? 1 : pouncing ? 0 : 3);
            }
            else Anim.Play("fall");
            Anim.Update(dt);
            return;
        }
        if (Posture == Posture.Air) OnLanded();
        UpdateRide(dt);
        if (!Body.OnGround) { Anim.Update(dt); return; }
        if (Body.Shove > U(14) && Posture != Posture.Sleep && Current is not Ride) Startle(false);

        if (Current == null) Choose();
        var cur = Current;
        if (cur != null && !cur.Update(this, dt) && Current == cur)
        {
            cur.Exit(this);
            Current = suspended.Count > 0 ? suspended.Pop() : null;
            Current?.Resume(this);
        }
        Anim.Update(dt);
    }

    public void Set(Behavior b, bool keepCurrent = false)
    {
        if (Current != null)
        {
            if (keepCurrent) suspended.Push(Current);
            else
            {
                Current.Exit(this);
                while (suspended.Count > 0) suspended.Pop().Exit(this);
            }
        }
        Current = b;
        b.Enter(this);
    }

    public bool TryInterrupt(Behavior b, bool keep = false)
    {
        if (Current != null && Current.Priority >= b.Priority) return false;
        Set(b, keep);
        return true;
    }

    public void Say(string name, double cooldown = 3)
    {
        if (!Memory.Cool("snd:" + name, cooldown, Now)) return;
        Sound.Play(name, Now);
        lastOwnSound = Now;
    }

    public void Emote(string icon, double life = 1.8, bool onHead = true)
    {
        if (onHead) Particles.RemoveAll(p => p.OnHead);
        Particles.Add(new Particle { Icon = icon, Life = life, MaxLife = life, OnHead = onHead });
    }

    public void Float(string icon, double life = 1.6)
    {
        Particles.Add(new Particle
        {
            Icon = icon,
            X = (Rng.NextDouble() - 0.5) * 10,
            Y = 0,
            VX = (Rng.NextDouble() - 0.5) * 6,
            VY = -10 - Rng.NextDouble() * 6,
            Life = life,
            MaxLife = life,
            Wobble = Rng.NextDouble() * 6
        });
    }

    void UpdateParticles(double dt)
    {
        foreach (var p in Particles)
        {
            p.Life -= dt;
            if (p.World)
            {
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                p.VY += U(40) * dt;
                continue;
            }
            p.X += p.VX * dt + Math.Sin((p.MaxLife - p.Life) * 4 + p.Wobble) * 0.15;
            p.Y += p.VY * dt;
        }
        Particles.RemoveAll(p => p.Life <= 0);
    }

    public void Dream(string icon)
    {
        Particles.RemoveAll(p => p.Dream);
        Particles.Add(new Particle { Icon = icon, Life = 3.6, MaxLife = 3.6, Dream = true });
    }

    public void Dust()
    {
        Particles.Add(new Particle
        {
            Icon = Rng.NextDouble() < 0.5 ? "puff" : "dirt",
            World = true,
            X = Body.X - Facing * U(8) + (Rng.NextDouble() - 0.5) * U(6),
            Y = Body.Y - U(2),
            VX = -Facing * U(20 + Rng.NextDouble() * 20),
            VY = -U(25 + Rng.NextDouble() * 20),
            Life = 0.45,
            MaxLife = 0.45
        });
    }

    public void Face(double x, double deadZone = 6)
    {
        double d = x - Body.X;
        if (Math.Abs(d) > U(deadZone)) Facing = d > 0 ? 1 : -1;
    }

    public (double x, double y) FramePoint(double fx, double fy)
    {
        double x = Body.X + (fx - Sprites.FrameW / 2.0) * S * Facing;
        double y = Body.Y - (Sprites.Ground - fy) * S;
        return (x, y);
    }

    public (double x, double y) MouthPoint()
    {
        var a = Sprites.Anchors[Anim.SheetFrame];
        return FramePoint(a[0], a[1]);
    }

    public (double x, double y) HeadPoint()
    {
        var a = Sprites.Anchors[Anim.SheetFrame];
        return FramePoint(a[2], a[3]);
    }

    public RectD HitRect()
    {
        switch (Body.Surface)
        {
            case Surface.Ceiling: return new RectD(Body.X - U(19), Body.Y, Body.X + U(19), Body.Y + U(30));
            case Surface.WallLeft: return new RectD(Body.X, Body.Y - U(19), Body.X + U(30), Body.Y + U(19));
            case Surface.WallRight: return new RectD(Body.X - U(30), Body.Y - U(19), Body.X, Body.Y + U(19));
        }
        double top = Posture switch
        {
            Posture.Sleep or Posture.Lie or Posture.Belly => Body.Y - U(22),
            Posture.Held => Body.Y - U(40),
            _ => Body.Y - U(32)
        };
        return new RectD(Body.X - U(19), top, Body.X + U(19), Body.Y);
    }

    /// <summary>Sprite rotation in quarter turns clockwise: 0 upright, 1 feet on a left wall, 2 upside down, 3 feet on a right wall.</summary>
    public int Quarter => Now < spinUntil ? ((spinFrom + spinDir * (int)((Now - spinStart) / 0.08)) % 4 + 4) % 4 : (int)Body.Surface;
    double spinUntil = -1, spinStart;
    int spinFrom, spinDir;

    /// <summary>Stop climbing and fall, turning right side up on the way down.</summary>
    public void LetGo(double vx = 0)
    {
        var s = Body.Surface;
        if (s == Surface.Floor) return;
        switch (s)
        {
            case Surface.Ceiling: Body.Y += U(30); break;
            case Surface.WallLeft: Body.X += U(18); Body.Y += U(16); break;
            case Surface.WallRight: Body.X -= U(18); Body.Y += U(16); break;
        }
        Body.Surface = Surface.Floor;
        Body.Detach();
        Body.VX = vx;
        Body.VY = 0;
        ForcePosture(Posture.Air);
        spinFrom = (int)s;
        spinDir = s == Surface.WallLeft ? -1 : 1;
        spinStart = Now;
        spinUntil = Now + (s == Surface.Ceiling ? 0.16 : 0.08);
        Emote("exclaim", 1.0);
    }

    /// <summary>Put him on another screen's floor (used when a fullscreen game takes over his screen).</summary>
    public void MoveTo(Platform floor, double x)
    {
        if (Body.Climbing) Body.Surface = Surface.Floor;
        if (Current != null && Current.Priority < 4) { Current.Exit(this); Current = null; while (suspended.Count > 0) suspended.Pop().Exit(this); }
        Body.X = Math.Clamp(x, floor.X1 + Body.HalfWidth, floor.X2 - Body.HalfWidth);
        Body.Land(floor, WindowRect);
        if (Posture == Posture.Air) ForcePosture(Posture.Stand);
    }

    public bool IsNight => W.Hour >= 23 || W.Hour < 6;
    public bool Asleep => Posture == Posture.Sleep;
}
