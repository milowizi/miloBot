using System.Text.RegularExpressions;
using MiloBot.Brain;
using MiloBot.Gfx;
using MiloBot.World;

namespace MiloBot.Vision;

public class Eyes
{
    readonly Settings settings;
    VisionClient client;
    readonly MotionWatcher motion = new();
    Task<VisionResult> pending;
    Capture pendingCapture;
    List<RectD> pendingIgnore = new();
    Task discovering;
    Task<MotionEvent> sampling;
    double nextDiscover, nextMotion, lastLook = -100, fgChangedAt = -1, fullscreenSince = -1;
    string lastLookKey = "";
    IntPtr lastFg;
    bool unloaded, warmed, wasAsleep;
    public string LastSeen { get; private set; } = "";

    public Eyes(Settings settings)
    {
        this.settings = settings;
        client = new VisionClient(settings.OllamaUrl, settings.VisionModel);
    }

    public string Status => !settings.Vision ? "uit" : client.Status;

    public void Reset()
    {
        client = new VisionClient(settings.OllamaUrl, settings.VisionModel);
        nextDiscover = 0;
        pending = null;
    }

    public void Update(double now, WorldState w, Milo milo, List<RectD> ignore)
    {
        var screen = w.ForegroundKind == AppKind.Desktop || w.ForegroundRect.Width <= 0
            ? w.ScreenAt(milo.Body.X, milo.Body.Y - 10)
            : w.ScreenAt(w.ForegroundRect.CenterX, w.ForegroundRect.CenterY);
        if (sampling != null && sampling.IsCompleted)
        {
            if (sampling.IsFaulted) { Log.Write("motion: " + sampling.Exception?.GetBaseException().Message); nextMotion = now + 10; }
            else if (sampling.Result is MotionEvent ev && !milo.Asleep && ev.Strength > 0.01) milo.OnMotion(ev.X, ev.Y, ev.Persistent);
            sampling = null;
        }
        if (screen != null && sampling == null && now >= nextMotion && !milo.Hidden)
        {
            nextMotion = now + 1.2;
            var bounds = screen.Bounds;
            var ign = new List<RectD>(ignore);
            sampling = Task.Run(() => motion.Sample(bounds, ign));
        }
        if (!settings.Vision) return;
        if (!client.Ready && (discovering == null || discovering.IsCompleted) && now >= nextDiscover)
        {
            nextDiscover = now + 45;
            discovering = client.Discover();
        }
        if (client.Ready && !warmed)
        {
            warmed = true;
            _ = client.Warm();
        }
        if (wasAsleep && !milo.Asleep) lastLook = Math.Min(lastLook, now - settings.VisionMinSeconds + 4);
        wasAsleep = milo.Asleep;
        if (w.ForegroundHwnd != lastFg)
        {
            lastFg = w.ForegroundHwnd;
            fgChangedAt = now;
        }
        if (w.ForegroundFullscreen)
        {
            if (fullscreenSince < 0) fullscreenSince = now;
            if (!unloaded && now - fullscreenSince > 15 && client.Ready)
            {
                unloaded = true;
                _ = client.Unload();
            }
        }
        else
        {
            fullscreenSince = -1;
            unloaded = false;
        }
        if (pending != null && pending.IsCompleted)
        {
            if (pending.IsFaulted)
            {
                Log.Write("vision failed: " + pending.Exception?.GetBaseException().Message);
                nextDiscover = now + 5;
                if (pending.Exception?.GetBaseException() is HttpRequestException) _ = client.Discover();
            }
            else if (pending.IsCompletedSuccessfully) Handle(pending.Result, pendingCapture, pendingIgnore, milo, w);
            pending = null;
        }
        if (pending != null || !client.Ready || milo.Asleep || milo.Hidden || w.UserIdle > 60 || w.ForegroundFullscreen || screen == null) return;
        if (milo.Current != null && milo.Current.Priority >= 2) return;
        string key = w.ForegroundProcess + "|" + w.ForegroundTitle;
        bool fgTrigger = fgChangedAt > 0 && now - fgChangedAt > 1.5 && key != lastLookKey && now - lastLook > 6;
        double gap = settings.VisionMinSeconds <= 12 ? 5 : settings.VisionMinSeconds >= 60 ? 20 : 7;
        bool bigChange = motion.ChangeSinceLook > 0.12 && now - lastLook > gap;
        bool periodic = now - lastLook > settings.VisionMinSeconds && motion.ChangeSinceLook > 0.03;
        if (!fgTrigger && !bigChange && !periodic) return;
        fgChangedAt = -1;
        lastLookKey = key;
        lastLook = now;
        motion.ChangeSinceLook = 0;
        StartLook(screen, w, milo, ignore);
    }

    void StartLook(World.Screen screen, WorldState w, Milo milo, List<RectD> ignore)
    {
        var src = screen.Bounds;
        double aspect = src.Width / src.Height;
        int tw = (int)Math.Min(1280, src.Width);
        int th = (int)Math.Round(tw / aspect);
        pendingCapture = new Capture { Width = tw, Height = th, Source = src };
        pendingIgnore = new List<RectD>(ignore);
        string app = w.ForegroundProcess, title = w.ForegroundTitle;
        var model = client;
        pending = Task.Run(async () =>
        {
            var cap = ScreenCapture.Grab(src, tw, th);
            var png = Png.EncodeRgb(cap.Bgra, cap.Width, cap.Height);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            return await model.Look(png, cap.Width, cap.Height, app, title, cts.Token);
        });
        if (!milo.Asleep && (milo.Current == null || milo.Current.Priority < 1) && milo.Rng.NextDouble() < 0.4)
        {
            milo.Face(w.ForegroundRect.CenterX, 30);
            milo.Alert(1.5);
        }
    }

    static readonly Dictionary<string, double> Weight = new()
    {
        ["dog"] = 1.0, ["cat"] = 0.9, ["animal"] = 0.8, ["food"] = 0.9, ["ball"] = 0.9, ["toy"] = 0.7,
        ["person_face"] = 0.4, ["error"] = 0.6, ["warning"] = 0.4, ["success"] = 0.6, ["victory"] = 0.8,
        ["game_over"] = 0.7, ["sad"] = 0.6, ["scary"] = 0.8, ["cute"] = 0.7, ["video_player"] = 0.35,
        ["text"] = 0.03, ["button"] = 0.03, ["other"] = 0.15
    };

    static readonly string[] CatWords = { "cat", "kitten", "kitty", "kitties", "tabby", "feline", "kat", "poes" };
    static readonly string[] DogWords = { "dog", "puppy", "puppies", "pup", "doggo", "hond", "pomeranian", "retriever", "shiba", "husky", "corgi", "labrador", "poodle", "bulldog", "beagle", "chihuahua", "terrier", "shepherd", "spitz", "canine", "pug" };
    static readonly string[] ScaryWords = { "horror", "monster", "zombie", "ghost", "creepy", "skull", "jumpscare", "jump scare", "demon", "blood", "bloody", "creature", "clown", "skeleton", "evil", "killer", "haunted", "spider" };
    static readonly string[] FoodWords = { "pizza", "burger", "food", "cake", "fries", "sushi", "cookie", "meat", "chicken", "snack", "bread", "steak", "hot dog", "hotdog", "pasta", "donut", "ice cream", "sandwich" };
    static readonly string[] AnimalWords = { "animal", "pet", "bird", "fish", "horse", "rabbit", "bunny", "hamster", "fox", "bear", "lion", "tiger", "cow", "pig", "sheep", "goat", "duck", "owl", "parrot", "snake", "lizard", "turtle", "frog", "deer", "wolf", "monkey", "elephant", "giraffe", "panda", "penguin", "mouse", "mice", "rat", "squirrel", "dinosaur", "dragon", "butterfly", "bee", "insect", "bug", "cheetah", "leopard", "koala", "otter", "seal", "whale", "dolphin", "shark", "crab", "raccoon", "hedgehog", "ferret", "chick", "creature", "critter", "capybara", "llama", "alpaca", "sloth", "zebra", "kangaroo", "pigeon", "seagull", "crow", "chicken" };
    static readonly string[] JunkWords = { "wallpaper", "background", "desktop", "icon", "taskbar", "folder", "file", "file explorer", "window", "starburst", "sunburst" };
    static readonly string[] SelfWords = { "pixel art dog", "pixel-art dog", "pixel dog", "pixelated dog", "milo" };
    // Names that contain an animal word but aren't animals (the log had "Watch Dogs 2 logo" as a dog, several times).
    static readonly string[] FakeAnimalWords = { "watch dog", "watch_dog", "watchdog", "dog tag", "dogecoin", "underdog", "top dog", "dog-ear", "catalog", "category", "cat5", "catapult", "copycat" };
    // A logo, title or piece of text that mentions an animal is still just text.
    static readonly string[] TextWords = { "logo", "title", "text", "icon", "splash", "wordmark", "banner", "font", "menu", "button", "label", "caption", "heading", "headline", "tab", "link", "name", "username", "channel", "search", "result" };
    static readonly string[] GameOverWords = { "game over", "gameover", "you died", "you lose", "you lost", "defeat", "defeated", "wasted", "died", "dead", "failed", "mission failed", "retry", "try again", "continue?" };
    static readonly string[] VictoryWords = { "victory", "win", "winner", "won", "you win", "congratulations", "congrats", "trophy", "level complete", "complete", "cleared", "success", "achievement", "gold", "champion", "first place", "1st" };
    static readonly string[] ErrorWords = { "error", "fail", "failed", "crash", "exception", "warning", "not responding", "denied", "invalid", "problem", "blue screen", "bsod" };

    static bool Has(string l, string[] words)
    {
        var parts = Regex.Split(l, "[^a-z]+");
        return words.Any(w => w.Contains(' ') || w.Contains('-') ? l.Contains(w) : parts.Any(x => x == w || x == w + "s" || x == w + "es"));
    }

    public static string FixKind(SeenThing t)
    {
        string l = t.Label.ToLowerInvariant().Replace('_', ' ');
        if (Has(l, SelfWords)) return null;
        if (FakeAnimalWords.Any(l.Contains) || l.Contains("watch dogs") || l.Contains("watchdogs")) return "other";
        if (Has(l, TextWords) && t.Kind is "dog" or "cat" or "animal" or "food" or "cute") return t.Kind is "cute" ? "cute" : "text";
        if (t.Kind == "game_over" && !Has(l, GameOverWords)) return Has(l, ErrorWords) ? "error" : "other";
        if (t.Kind is "victory" or "success" && !Has(l, VictoryWords)) return "other";
        if (t.Kind is "error" or "warning" && !Has(l, ErrorWords) && !l.Contains("popup") && !l.Contains("alert")) return "other";
        bool living = t.Kind is "dog" or "cat" or "animal" or "cute" or "scary" or "food" or "ball" or "toy" or "person_face" or "other";
        if (living && Has(l, JunkWords) && !Has(l, CatWords) && !Has(l, DogWords) && !Has(l, FoodWords)) return null;
        if (Has(l, FoodWords) && t.Kind is "animal" or "cute" or "other" or "food" or "dog") return "food";
        if (Has(l, CatWords)) return "cat";
        if (Has(l, DogWords)) return "dog";
        if (t.Kind is "cat" or "dog" or "animal") return Has(l, AnimalWords) ? "animal" : "other";
        if (t.Kind == "scary" && !Has(l, ScaryWords)) return l.Contains("error") ? "error" : "other";
        if (t.Kind is "toy" or "other" && Has(l, new[] { "ball" })) return "ball";
        return t.Kind;
    }

    void Handle(VisionResult r, Capture cap, List<RectD> ignore, Milo milo, WorldState w)
    {
        var src = cap.Source;
        var things = new List<(SeenThing t, RectD rect)>();
        foreach (var t in r.Things)
        {
            var rect = new RectD(src.Left + t.X1 * src.Width, src.Top + t.Y1 * src.Height, src.Left + t.X2 * src.Width, src.Top + t.Y2 * src.Height);
            if (ignore.Any(i => Overlap(rect, i) > 0.4)) continue;
            var kind = FixKind(t);
            if (kind == null) continue;
            t.Kind = kind;
            things.Add((t, rect));
        }
        LastSeen = $"{r.Activity}/{r.Mood}: " + string.Join(", ", things.Select(x => $"{x.t.Label}({x.t.Kind},{x.t.Salience})")) + $" [{r.Seconds:0.0}s]";
        Log.Write("saw " + LastSeen);

        switch (r.Activity)
        {
            case "gaming": milo.Excitement = Math.Min(1, milo.Excitement + 0.2); break;
            case "coding" or "writing" or "reading": milo.Boredom = Math.Max(0, milo.Boredom - 0.05); break;
        }

        (SeenThing t, RectD rect) best = default;
        double bestScore = 0;
        string bestLabelKey = "", bestSpotKey = "";
        foreach (var x in things)
        {
            if (x.t.Kind == "scary" && x.t.Salience < 8) continue;
            double wgt = Weight.TryGetValue(x.t.Kind, out var v) ? v : 0.15;
            string labelKey = "seen:" + x.t.Kind + ":" + x.t.Label.ToLowerInvariant();
            int cx = (int)((x.rect.CenterX - src.Left) / src.Width * 6), cy = (int)((x.rect.CenterY - src.Top) / src.Height * 4);
            string spotKey = $"spot:{x.t.Kind}:{cx},{cy}";
            double novelty = Math.Min(milo.Memory.Novelty(labelKey, w.Now, 600), milo.Memory.Novelty(spotKey, w.Now, 450));
            double score = wgt * (x.t.Salience / 10.0) * novelty;
            if (score > bestScore)
            {
                bestScore = score;
                best = x;
                bestLabelKey = labelKey;
                bestSpotKey = spotKey;
            }
        }
        if (best.t != null && bestScore > 0.2 && milo.Rng.NextDouble() < bestScore + 0.45)
        {
            bool strong = best.t.Salience >= 8 && best.t.Kind is "cat" or "dog" or "food";
            if (milo.See(best.t.Kind, best.t.Label, best.rect.CenterX, best.rect.Bottom, best.t.Salience, strong))
            {
                milo.Memory.Remember(bestLabelKey, w.Now, 1, 600);
                milo.Memory.Remember(bestSpotKey, w.Now, 1, 450);
            }
            else Log.Write($"no reaction to {best.t.Label}: {(milo.Asleep ? "asleep" : "busy with " + milo.Current?.Name)}");
            return;
        }
        if (best.t != null) Log.Write($"not interested (score {bestScore:0.00})");
        if (r.Mood == "celebratory" && milo.Memory.Cool("mood:celebrate", 60, w.Now)) milo.OnVision(milo.Body.X, milo.Body.Y, Reaction.Happy, "celebration", 6);
        else if (r.Mood == "sad" && milo.Memory.Cool("mood:sad", 180, w.Now) && milo.Rng.NextDouble() < 0.5) milo.OnVision(w.CursorX, w.CursorY, Reaction.Comfort, "sad", 5);
    }

    static double Overlap(RectD a, RectD b)
    {
        double w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        double h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        if (w <= 0 || h <= 0) return 0;
        double area = Math.Max(1, a.Width * a.Height);
        return w * h / area;
    }
}
