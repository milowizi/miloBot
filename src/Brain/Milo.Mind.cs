using MiloBot.World;

namespace MiloBot.Brain;

public partial class Milo
{
    public bool Petting { get; private set; }
    public bool Hover { get; private set; }
    public bool BreakReminder = true;
    public double BreakMinutes = 75;
    public bool WalkAccepted { get; private set; }
    readonly Queue<(double t, double x)> strokes = new();
    double lastStrokeX, strokeDir, strokeAcc, lastStrokeTime = -10;
    int clickCount;
    double lastClick = -10;
    string lastChoice = "";
    double activeStreak, walkUntil = -1;
    string rewardTrick;
    double rewardUntil = -1;

    void UpdateNeeds(double dt)
    {
        bool sleeping = Posture == Posture.Sleep;
        double drain = 1.0 / 2400 * (IsNight ? 1.8 : 1.0);
        if (!sleeping) Energy = Math.Max(0, Energy - drain * dt);
        Social = Math.Clamp(Social + dt / (sleeping ? 2400 : 700), 0, 1);
        if (Hover && !sleeping) Social = Math.Max(0, Social - dt * 0.01);
        bool busy = Current != null && Current.Priority >= 1;
        Boredom = Math.Clamp(Boredom + dt / (busy ? 900 : 260), 0, 1);
        if (sleeping) Boredom = Math.Max(0, Boredom - dt / 200);
        double baseline = 0.35 - Social * 0.15 - (Save.Hunger > 0.7 ? 0.1 : 0);
        Happiness += (baseline - Happiness) * Math.Min(1, dt * 0.004);
        Excitement = Math.Max(0, Excitement - dt * 0.08);
        Save.Hunger = Math.Min(1, Save.Hunger + dt / (sleeping ? 25200 : 12600));
        if (W.UserIdle < 120)
        {
            Save.MinutesTogether += dt / 60;
            Save.AddFriend(dt / 7200);
        }
        if (W.UserIdle > 300) activeStreak = 0;
        else if (W.UserIdle < 240) activeStreak += dt;
        if (rewardUntil > 0 && Now > rewardUntil)
        {
            rewardUntil = -1;
            if (rewardTrick != null)
            {
                double s = Tricks.Skill(this, rewardTrick);
                Save.Tricks[rewardTrick] = Math.Min(1, s + 0.03 * (1 - s));
            }
        }
        if (walkUntil > 0 && W.UserIdle > 180)
        {
            walkUntil = -1;
            Save.Stat("walks");
            Save.AddFriend(1);
            Memory.Cool("afterwalk", 1, Now);
            afterWalk = true;
        }
        if (walkUntil > 0 && Now > walkUntil) walkUntil = -1;
    }

    bool afterWalk;

    void UpdateSenses(double dt)
    {
        TrackAir();
        var r = HitRect();
        Hover = !Hidden && r.Contains(W.CursorX, W.CursorY);
        bool canPet = Hover && Posture != Posture.Held && Posture != Posture.Air && Math.Abs(W.CursorVY) < 3000;
        if (canPet)
        {
            double dx = W.CursorX - lastStrokeX;
            if (Math.Abs(dx) > 0.5)
            {
                double dir = Math.Sign(dx);
                strokeAcc += Math.Abs(dx);
                if (dir != strokeDir && strokeAcc > U(4))
                {
                    strokes.Enqueue((Now, W.CursorX));
                    strokeAcc = 0;
                }
                strokeDir = dir;
            }
        }
        lastStrokeX = W.CursorX;
        while (strokes.Count > 0 && Now - strokes.Peek().t > 1.6) strokes.Dequeue();
        bool petNow = canPet && strokes.Count >= 3;
        if (petNow) lastStrokeTime = Now;
        Petting = Now - lastStrokeTime < 0.7 && Hover;
        if (Petting && Current is not Petted && Current is not Held && Current is not Tug)
        {
            if (Current == null || Current.Priority < 3) Set(new Petted());
        }
        if (!Asleep && Body.OnGround && (Current == null || Current.Priority < 1) && W.CursorSpeed > 40 && W.CursorSpeed < 1500
            && CursorUnderMe(U(70)) && Math.Abs(W.CursorX - Body.X) < U(60) && Memory.Cool("perch", 30, Now))
        {
            Set(new Perch());
        }
        if (!Asleep && Body.OnGround && (Current == null || Current.Priority < 1) && W.CursorSpeed > 2200
            && CursorDistance() < U(170) && Energy > 0.4 && Happiness > 0.4 && Memory.Cool("chase", 35, Now) && Rng.NextDouble() < 0.6)
        {
            Set(Plans.ChaseCursor(this));
        }
    }

    void Choose()
    {
        if (!Body.OnGround) return;
        var treat = Toys.Treat;
        if (treat != null && !treat.InMouth && Memory.Ready("eat-fail", Now)) { Set(new Eat(treat)); return; }
        var thrown = Toys.Items.FirstOrDefault(t => t.Fetchable && Now - t.ThrownAt < 8 && !t.InMouth && !t.HeldByUser);
        if (thrown != null && Energy > 0.12 && Memory.Ready("fetch-fail", Now)) { Set(new Fetch(thrown)); return; }
        if (afterWalk)
        {
            afterWalk = false;
            Set(Plans.Greet(this, 600));
            return;
        }

        bool userAway = W.UserIdle > 150;
        double night = IsNight ? 1 : 0;
        var lying = Toys.Items.FirstOrDefault(t => t.Fetchable && !t.InMouth && !t.HeldByUser && t.Body.OnGround && Now - t.LastTouched > 180 && Now - t.ThrownAt > 60);
        bool leashDue = BreakReminder && activeStreak > BreakMinutes * 60 && !userAway && Memory.Ready("leash", Now) && Home.Active;
        var options = new List<(string name, double score, Func<Behavior> make)>
        {
            ("idle", 0.8, () => new Rest(Posture.Stand, 2 + Rng.NextDouble() * 4)),
            ("wander", 0.9 + Boredom * 1.4 + Energy * 0.4 - night * 0.3, () => Plans.Wander(this)),
            ("loaf", 0.8 + (1 - Energy) * 1.0 + (W.Typing > 0.3 ? 0.8 : 0) + night * 0.4, () => Plans.Loaf(this)),
            ("nap", Math.Pow(1 - Energy, 2) * 3.5 + (userAway ? 3 : 0) + night * (1 - Energy) * 2, () => Home.Active && Rng.NextDouble() < 0.65 ? Plans.GoToBedAndNap(this) : Plans.Nap(this)),
            ("explore", W.Platforms.Count > 1 ? 0.3 + Boredom * 1.3 + Energy * 0.3 - night * 0.2 : 0, () => Plans.Explore(this)),
            ("seek", Social * 2.2 * (userAway ? 0.1 : 1) * (1 + Save.Level * 0.05), () => Plans.SeekUser(this, Social > 0.8 && Rng.NextDouble() < 0.4)),
            ("zoomies", Energy > 0.7 && Happiness > 0.55 && !userAway ? 0.25 + Excitement * 0.8 : 0, () => Plans.Zoomies(this)),
            ("dance", W.Music && Energy > 0.25 && W.ForegroundKind != AppKind.Video && Memory.Ready("dance", Now) ? 2.5 : 0, () => { Memory.Cool("dance", 90, Now); return Plans.Dance(this); }),
            ("stretch", lastChoice == "nap" ? 3 : 0.12, () => Plans.Stretch(this)),
            ("meal", Home.Active && Save.Hunger > 0.55 && Memory.Ready("meal", Now) ? 2 + Save.Hunger * 2 : 0, () => new Meal()),
            ("tailchase", Energy > 0.5 && !userAway ? 0.12 + Boredom * 0.35 : 0, () => Plans.TailChase(this)),
            ("howl", !userAway ? (IsNight ? 0.12 : 0.03) : 0, () => Plans.Howl(this)),
            ("edge", 0.08 + Boredom * 0.2, () => Plans.EdgeScratch(this)),
            ("gift", Save.Level >= 2 && Happiness > 0.5 && !userAway && Memory.Ready("gift", Now) ? 0.15 + Save.Level * 0.05 : 0, () => { Memory.Cool("gift", 1500 - Save.Level * 90, Now); return Plans.Gift(this); }),
            ("tidy", lying != null && Home.Active ? 0.5 : 0, () => Plans.Tidy(this, lying)),
            ("perch", CursorUnderMe(U(300)) && Math.Abs(W.CursorX - Body.X) < U(200) && !userAway ? 0.9 : 0, () => new Perch()),
            ("buddy", typingStreak > 25 && W.KeysPerSecond > 2 && !W.ForegroundFullscreen && W.ForegroundKind != AppKind.Desktop && Memory.Ready("buddy", Now) ? 1.3 : 0, () => { Memory.Cool("buddy", 240, Now); return ScreenPlans.TypingBuddy(this); }),
            ("leash", leashDue ? 6 : 0, () => { Memory.Cool("leash", 1800, Now); activeStreak = 0; WalkAccepted = false; return Plans.BringLeash(this); }),
        };
        foreach (ref var o in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(options))
            if (o.name == lastChoice && o.name != "nap") o.score *= 0.45;
        double total = 0;
        foreach (var o in options) total += Math.Max(0, o.score) * Math.Max(0, o.score);
        double pick = Rng.NextDouble() * total;
        foreach (var o in options)
        {
            double w = Math.Max(0, o.score) * Math.Max(0, o.score);
            if (pick <= w)
            {
                lastChoice = o.name;
                Boredom = Math.Max(0, Boredom - o.name switch { "wander" => 0.12, "explore" => 0.3, "zoomies" => 0.4, "dance" => 0.3, "seek" => 0.1, "tailchase" => 0.3, "edge" => 0.15, _ => 0 });
                Set(o.make());
                return;
            }
            pick -= w;
        }
        Set(new Rest(Posture.Stand, 2));
    }

    public void OnGrab()
    {
        Body.Detach();
        Body.VX = Body.VY = 0;
        Set(new Held());
        Happiness = Math.Min(1, Happiness + 0.02);
        Social = Math.Max(0, Social - 0.1);
    }

    public void HoldAt(double cx, double cy)
    {
        Body.X = cx + 2 * S * Facing;
        Body.Y = cy + (Sprites.Ground - 20) * S;
    }

    public void OnRelease(double vx, double vy)
    {
        Current?.Exit(this);
        Current = null;
        double max = U(1100);
        double sp = Math.Sqrt(vx * vx + vy * vy);
        if (sp > max) { vx *= max / sp; vy *= max / sp; }
        Body.VX = vx;
        Body.VY = vy;
        Body.Detach();
        ForcePosture(Posture.Air);
        airMaxVY = 0;
        hopping = false;
        if (sp > U(700)) { Emote("exclaim", 1); Excitement = 1; }
    }

    public void OnClick()
    {
        if (Current is Sequence seq && seq.Name.StartsWith("Leash") && !WalkAccepted)
        {
            WalkAccepted = true;
            walkUntil = Now + 1200;
            Emote("heart", 2);
            return;
        }
        if (Now - lastClick < 1.2) clickCount++; else clickCount = 1;
        lastClick = Now;
        Save.Stat("boops");
        Save.AddFriend(0.2);
        Rewarded();
        if (clickCount >= 4 && !Asleep && Energy > 0.3)
        {
            clickCount = 0;
            Set(Plans.Zoomies(this));
            return;
        }
        if (Current is Boop && clickCount > 1)
        {
            Emote(Asleep ? "puff" : (Happiness > 0.4 ? "heart" : "question"));
            return;
        }
        if (Current is TrickBehavior) return;
        Say("boop", 0.3);
        Set(new Boop(clickCount));
    }

    public void ExpectReward(string trick)
    {
        rewardTrick = trick;
        rewardUntil = Now + 6;
    }

    public void Rewarded()
    {
        if (rewardUntil < 0 || Now > rewardUntil || rewardTrick == null) return;
        double s = Tricks.Skill(this, rewardTrick);
        Save.Tricks[rewardTrick] = Math.Min(1, s + 0.1 * (1 - s));
        Save.AddFriend(2);
        rewardUntil = -1;
        Float("heart");
        Float("sparkle");
        Happiness = Math.Min(1, Happiness + 0.1);
    }

    public void Startle(bool loud)
    {
        if (!Memory.Cool("startle", 6, Now)) return;
        if (Now - lastOwnSound < 1.5 && loud) return;
        Emote("exclaim", 1.2);
        Alert(1.5);
        if (Asleep)
        {
            if (Current == null || Current.Priority < 2)
                Set(new Sequence(2, "Startled", new Rest(Posture.Stand, 0.2, null, 2), new PlayOnce("alert", Posture.Stand, 1.2, null, null, 2), new PlayOnce("shake", Posture.Stand, 1, null, null, 2)));
            return;
        }
        if (loud && Rng.NextDouble() < 0.35) Say("bark", 12);
        Excitement = Math.Min(1, Excitement + 0.4);
    }

    public void OnWorld(WorldEvent e)
    {
        switch (e.Kind)
        {
            case WorldEventKind.UserReturned:
                if (e.Value > 90 && Memory.Cool("greet", 120, Now))
                {
                    if (TryInterrupt(Plans.Greet(this, e.Value))) Save.AddFriend(1);
                }
                break;
            case WorldEventKind.ForegroundChanged:
                Boredom = Math.Max(0, Boredom - 0.08);
                if (Asleep || Current?.Priority >= 2) break;
                Face(e.Rect.CenterX, 20);
                Alert(0.9);
                ReactToApp(e);
                break;
            case WorldEventKind.Toast:
                OnToast(e.Rect);
                break;
            case WorldEventKind.Flash:
                OnFlash(e);
                break;
            case WorldEventKind.WindowOpened:
                if (Asleep) break;
                Face(e.Rect.CenterX, 20);
                Alert(1.2);
                Boredom = Math.Max(0, Boredom - 0.1);
                if (Rng.NextDouble() < 0.25 + Boredom * 0.4 && (Current == null || Current.Priority < 1))
                    Set(new Sequence(1, "CheckWindow", new GoTo(e.Rect.CenterX, e.Rect.Top, Gait.Happy, 1), new PlayOnce("sniff", Posture.Stand, 1.5, "sniff", null, 1)));
                break;
        }
    }

    void ReactToApp(WorldEvent e)
    {
        string key = "app:" + W.ForegroundProcess + ":" + e.App;
        double novelty = Memory.Novelty(key, Now, 1800);
        Memory.Remember(key, Now, 1, 1800);
        if (Current != null && Current.Priority >= 1) return;
        double roll = Rng.NextDouble();
        switch (e.App)
        {
            case AppKind.Video when roll < 0.5 * novelty + 0.15:
                Set(Plans.Watch(this, e.Rect.CenterX, e.Rect.Bottom, 25 + Rng.NextDouble() * 30));
                break;
            case AppKind.Game:
                Excitement = Math.Min(1, Excitement + 0.3);
                if (roll < 0.4 * novelty) Set(new Sequence(1, "GameTime", new PlayOnce("happy", Posture.Stand, 1.5, "yip", "exclaim", 1)));
                break;
            case AppKind.ThreeD or AppKind.Drawing when roll < 0.35 * novelty + 0.1:
                Set(Plans.Watch(this, e.Rect.CenterX, e.Rect.Bottom, 15 + Rng.NextDouble() * 15));
                break;
            case AppKind.Coding or AppKind.Office or AppKind.Terminal when roll < 0.3:
                Set(new Rest(Energy < 0.6 ? Posture.Lie : Posture.Sit, 20 + Rng.NextDouble() * 30));
                break;
            case AppKind.Music:
                Alert(1.5);
                break;
            case AppKind.Desktop:
                OnDesktopShown();
                break;
        }
    }

    public void OnToy(Toy t, bool thrown)
    {
        t.LastTouched = Now;
        if (t.Kind == ToyKind.Treat)
        {
            if (Asleep && Rng.NextDouble() < 0.3) return;
            Emote("food", 1.5);
            TryInterrupt(new Eat(t));
            return;
        }
        if (Current is Tug) return;
        if (thrown && t.Fetchable && Energy > 0.1)
        {
            if (Current is Fetch) return;
            if (Asleep && Rng.NextDouble() < 0.5) { Emote("question"); return; }
            TryInterrupt(new Fetch(t));
        }
        else if (!Asleep)
        {
            Face(t.Body.X, 0);
            Alert(1);
        }
    }

    public bool See(string kind, string label, double x, double y, double salience, bool strong)
    {
        var info = Save.SeenOf(kind, label);
        double days = (DateTime.Now - info.Last).TotalDays;
        double eff = info.Count * Math.Pow(0.5, days / 7);
        int stage = eff < 1 ? 0 : eff < 3 ? 1 : eff < 7 ? 2 : 3;
        var reaction = Investigate.Pick(kind, stage, Rng);
        if (!OnVision(x, y, reaction, label, salience, strong, kind)) return false;
        info.Count++;
        info.Last = DateTime.Now;
        if (kind == "cat") Save.Stat("cats");
        if (kind == "dog") Save.Stat("dogs");
        Log.Write($"react: {label} ({kind}, stage {stage}, seen {info.Count}x) -> {reaction}");
        return true;
    }

    public bool OnVision(double x, double y, Reaction r, string label, double interest, bool strong = false, string kind = null)
    {
        if (Posture == Posture.Held || !Body.OnGround || Hidden) return false;
        if (Asleep && !strong) return false;
        if (Current != null && Current.Priority >= 2) return false;
        if (Asleep)
        {
            Emote("exclaim", 1.5);
            Alert(2);
        }
        Boredom = Math.Max(0, Boredom - 0.2);
        Excitement = Math.Min(1, Excitement + interest * 0.05);
        Set(Investigate.Make(this, x, y, r, label, kind));
        return true;
    }

    public void OnMotion(double x, double y, bool persistent)
    {
        if (Asleep || !Body.OnGround || Posture == Posture.Held) return;
        if (Current is Perch perch)
        {
            if (y > Body.Y && Math.Abs(x - Body.X) < U(90)) { Face(x, 0); perch.Swipe(this); }
            return;
        }
        if (Current != null && Current.Name.StartsWith("Watch"))
        {
            Face(x, 10);
            if (!persistent && Energy > 0.45 && y < Body.Y - U(20) && Body.Y - y < MaxJumpUp * 1.2 && Math.Abs(x - Body.X) < U(160)
                && Rng.NextDouble() < 0.3 && Memory.Cool("motionpounce", 45, Now))
                Set(new Sequence(2, "Investigate:motion", Investigate.Pounce(this, x, y)));
            return;
        }
        if (Current != null && Current.Priority >= 1) return;
        if (!Memory.Cool("motion", persistent ? 40 : 8, Now)) return;
        Face(x, 20);
        Alert(1.0);
        if (persistent && Rng.NextDouble() < 0.35 && Energy > 0.2)
            Set(Plans.Watch(this, x, y, 15 + Rng.NextDouble() * 20));
    }

    public void OnCritter(Critter c)
    {
        if (c.State == CritterState.OnMilo)
        {
            Save.Stat("butterflies");
            Set(new Sequence(3, "ButterflyNose",
                new Custom("LookUp", (m, cc, dt) =>
                {
                    if (cc.N == 0) { cc.N = 1; m.Emote("exclaim", 1); }
                    if (m.Asleep) { if (cc.T > 3) m.ForcePosture(Posture.Lie); return cc.T < 5; }
                    if (m.Posture == Posture.Stand) m.Anim.Play("idle_look");
                    else if (m.Posture == Posture.Sit) m.Anim.Play("sit_look");
                    else if (m.Posture == Posture.Lie) m.Anim.Play("lie_look");
                    else if (m.Posture == Posture.Front) m.Anim.Play("front_look");
                    return cc.T < 2.5 + m.Rng.NextDouble();
                }, null, 3),
                new Custom("Sneeze", (m, cc, dt) =>
                {
                    if (cc.N == 0) { cc.N = 1; m.Say("sneeze", 1); m.Critters.Leave(c, m.W); m.Emote("sweat", 1); }
                    if (!m.To(Posture.Stand)) return true;
                    m.Anim.Play("shake");
                    return cc.T < 0.8;
                }, null, 3),
                new Rest(Posture.Sit, 2, null, 1)));
            return;
        }
        if (c.Kind == CritterKind.Bird) Save.Stat("birds"); else Save.Stat("butterflies");
        if (Asleep || Hidden || !Body.OnGround) return;
        if (Current != null && Current.Priority >= 2) return;
        if (Energy < 0.15) { Face(c.X, 0); Alert(2); return; }
        Set(new Hunt(c));
    }

    public void StartTug(Toy toy) => Set(new Tug(toy, 2.5 + Rng.NextDouble() * 4));

    public void EndTugByUser()
    {
        if (Current is Tug t && t.Outcome == 0) t.UserHolding = false;
    }

    public void StartLaser()
    {
        Laser.On = true;
        Laser.StartedAt = Now;
        Emote("exclaim", 1.5);
        Say("yip", 1);
        Set(new LaserChase(Laser));
    }

    public void DoTrick(string id)
    {
        if (Posture == Posture.Held || !Body.OnGround) return;
        Set(new TrickBehavior(id, true));
    }

    public void PosePhoto()
    {
        if (Posture == Posture.Held || !Body.OnGround) return;
        Set(new Custom("Photo", (m, c, dt) =>
        {
            m.Face(m.W.CursorX, 0);
            if (!m.To(Posture.Front)) return true;
            m.Anim.Play(c.T > 0.3 && c.T < 1.2 ? "wink" : "front_happy");
            return c.T < 2.5;
        }, null, 3));
    }

    public string DreamIcon()
    {
        var d = Save.Today;
        var pool = new List<(string icon, double w)>
        {
            ("bone", 1), ("heart", 1 + d.Pets * 0.5), ("ball", d.Fetches * 1.0), ("food", (d.Treats + d.Meals) * 0.8),
            ("cat", d.Cats * 1.2), ("sock", d.Gifts * 0.8), ("stick", d.Tugs * 0.8), ("sparkle", (d.Butterflies + d.Birds) * 0.8), ("leash", d.Walks * 1.0)
        };
        double total = pool.Sum(p => p.w);
        double r = Rng.NextDouble() * total;
        foreach (var p in pool)
        {
            if (r < p.w) return p.icon;
            r -= p.w;
        }
        return "bone";
    }

    public void Call()
    {
        if (Asleep) Emote("exclaim");
        Social = Math.Max(Social, 0.5);
        Set(Plans.SeekUser(this));
    }

    public void GoSleep()
    {
        Energy = Math.Min(Energy, 0.3);
        Set(Home.Active ? Plans.GoToBedAndNap(this) : Plans.Nap(this));
    }

    public void WakeUp()
    {
        if (!Asleep && Posture != Posture.Lie) return;
        Energy = Math.Max(Energy, 0.7);
        Set(Plans.Stretch(this));
    }

    public void OnShown(double hiddenSeconds)
    {
        if (hiddenSeconds > 60)
        {
            Energy = Math.Min(1, Energy + hiddenSeconds / 600);
            ForcePosture(Posture.Sleep);
            Set(new Nap(3));
        }
    }
}
