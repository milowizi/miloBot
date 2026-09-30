using System.Diagnostics;
using MiloBot.Brain;
using MiloBot.Win32;

namespace MiloBot;

public partial class App
{
    static readonly (string id, string name)[] HatOptions =
    {
        ("auto", "Automatisch (feestdagen)"), ("none", "Geen"), ("tophat", "Hoge hoed"), ("party", "Feesthoedje"), ("santa", "Kerstmuts"), ("crown", "Kroontje"), ("shades", "Zonnebril")
    };

    static readonly (string model, string name)[] Models =
    {
        ("qwen3-vl:8b-instruct", "Slim (8B, ~6 GB VRAM)"), ("qwen3-vl:4b-instruct", "Licht (4B, ~3 GB VRAM)")
    };

    static readonly (int secs, string name)[] Frequencies = { (12, "Vaak"), (25, "Normaal"), (60, "Zelden") };

    List<MenuItem> Diary()
    {
        save.RollDay();
        var t = save.Today;
        var all = save.Total;
        int days = Math.Max(1, (int)(DateTime.Now.Date - save.FirstMet.Date).TotalDays + 1);
        int lvl = save.Level;
        string hearts = new string('❤', Math.Min(5, (lvl + 1) / 2)) + new string('♡', 5 - Math.Min(5, (lvl + 1) / 2));
        var best = Tricks.All.OrderByDescending(x => Tricks.Skill(milo, x.Id)).First();
        var lines = new List<string>
        {
            $"Samen sinds {save.FirstMet:d MMM yyyy} (dag {days})",
            $"Vriendschap: {hearts}  level {lvl}",
            $"Vandaag: {t.Pets}x geaaid, {t.Fetches}x apport, {t.Treats + t.Meals} hapjes",
            $"Vandaag gezien: {t.Cats} katten, {t.Dogs} honden, {t.Butterflies} vlinders, {t.Birds} vogels",
            $"Vandaag: {t.Tricks} trucjes, {t.Zoomies}x zoomies, {t.Tugs}x touwtrekken",
            $"Vandaag: {t.Rides}x gesurfd, {t.Dizzy}x duizelig, {t.Pounces}x gesprongen, {t.Swats}x naar je muis gemept",
            $"Vandaag: {t.Words} woordjes gehoord, {t.Falls}x van een venster gevallen",
            $"Vandaag: {t.Climbs}x tegen het plafond, {t.Pictures} plaatjes bekeken, {t.Reads}x \"gelezen\", {t.Nudges}x aan een venster getrokken, {t.Bats}x je muis gemept",
            $"Totaal: {all.Pets}x geaaid, {all.Fetches}x apport, {all.Gifts} cadeautjes, {all.Walks} wandelingen",
            $"Beste trucje: {best.Name} ({Tricks.Skill(milo, best.Id) * 100:0}%)",
            $"Honger: {save.Hunger * 100:0}%   Voerbak: {save.BowlFood * 100:0}%"
        };
        return lines.Select(l => new MenuItem { Text = l, Disabled = true }).ToList();
    }

    void ShowMenu()
    {
        bool asleep = milo.Asleep || milo.Current is Nap;
        var trickItems = Tricks.All.Select((t, i) => new MenuItem { Id = 200 + i, Text = $"{t.Name}   {Tricks.Stars(Tricks.Skill(milo, t.Id))}" }).ToList();
        var hatItems = HatOptions.Select((h, i) => new MenuItem { Id = 300 + i, Text = h.name, Checked = settings.Hat == h.id }).ToList();
        var visionItems = new List<MenuItem>
        {
            new() { Id = 21, Text = "Aan", Checked = settings.Vision },
            new() { Text = "Status: " + eyes.Status, Disabled = true },
            new() { Separator = true }
        };
        visionItems.AddRange(Models.Select((m, i) => new MenuItem { Id = 400 + i, Text = m.name, Checked = settings.VisionModel == m.model }));
        visionItems.Add(new MenuItem { Separator = true });
        visionItems.AddRange(Frequencies.Select((f, i) => new MenuItem { Id = 410 + i, Text = "Kijkt: " + f.name, Checked = settings.VisionMinSeconds == f.secs }));
        if (!string.IsNullOrEmpty(eyes.LastSeen)) visionItems.Add(new MenuItem { Text = "Laatst: " + Trim(eyes.LastSeen, 90), Disabled = true });

        var items = new List<MenuItem>
        {
            new() { Id = 1, Text = "Milo roepen" },
            new()
            {
                Text = "Spelen", Children = new List<MenuItem>
                {
                    new() { Id = 2, Text = toys.Ball == null ? "Balletje gooien" : "Balletje opnieuw gooien", Disabled = milo.TopDown },
                    new() { Id = 5, Text = milo.Laser.On ? "Laserpuntje uit" : "Laserpuntje aan", Checked = milo.Laser.On },
                    new() { Id = 3, Text = "Snoepje geven", Disabled = milo.TopDown },
                    new() { Id = 6, Text = "Voerbak vullen", Disabled = !home.Active || save.BowlFood >= 0.99 },
                    new() { Id = 7, Text = "Vlinder loslaten", Disabled = milo.TopDown },
                    new() { Id = 9, Text = "Tegen het plafond kruipen", Disabled = milo.TopDown }
                }
            },
            new() { Text = "Trucjes", Children = trickItems, Disabled = milo.TopDown },
            new() { Id = 4, Text = asleep ? "Wakker maken" : "Laten slapen" },
            new() { Id = 8, Text = "📸 Foto van Milo" },
            new() { Text = "Milo's dagboek", Children = Diary() },
            new() { Separator = true },
            new() { Text = "Hoedje", Children = hatItems },
            new()
            {
                Text = "Grootte", Children = new List<MenuItem>
                {
                    new() { Id = 11, Text = "Klein", Checked = settings.Size == 1 },
                    new() { Id = 12, Text = "Normaal", Checked = settings.Size == 2 },
                    new() { Id = 13, Text = "Groot", Checked = settings.Size == 3 }
                }
            },
            new()
            {
                Text = "Geluid", Children = new List<MenuItem>
                {
                    new() { Id = 20, Text = "Aan", Checked = settings.Sound },
                    new() { Separator = true },
                    new() { Id = 25, Text = "Zacht", Checked = settings.Volume < 0.45 },
                    new() { Id = 26, Text = "Normaal", Checked = settings.Volume >= 0.45 && settings.Volume < 0.8 },
                    new() { Id = 27, Text = "Luid", Checked = settings.Volume >= 0.8 }
                }
            },
            new() { Text = "Scherm-visie", Children = visionItems },
            new()
            {
                Text = "Opties", Children = new List<MenuItem>
                {
                    new() { Id = 22, Text = "Verbergen bij fullscreen", Checked = settings.HideInFullscreen },
                    new() { Id = 23, Text = "Starten met Windows", Checked = settings.StartWithWindows },
                    new() { Id = 24, Text = $"Pauze-herinnering (na {settings.BreakMinutes} min)", Checked = settings.BreakReminder },
                    new() { Id = 28, Text = "Mandje en voerbak tonen", Checked = settings.ShowHome },
                    new() { Id = 29, Text = "Vlinders en vogels", Checked = settings.Critters },
                    new() { Id = 32, Text = "Luistert naar wat je typt", Checked = settings.Hearing },
                    new() { Id = 33, Text = "Ondeugend (trekt aan vensters, mept je muis)", Checked = settings.Mischief },
                    new() { Id = 34, Text = "Speelt met wat er in je apps staat", Checked = settings.ContentPlay },
                    new() { Id = 35, Text = "Bovenaanzicht (Milo loopt over je hele scherm)", Checked = settings.TopView },
                    new() { Text = "In je app: " + (settings.ContentPlay ? scanner.Summary : "uit"), Disabled = true }
                }
            },
            new() { Separator = true },
            new() { Id = 30, Text = "Speelgoed opruimen", Disabled = toys.Items.Count == 0 },
            new() { Id = 31, Text = "Log openen" },
            new() { Id = 99, Text = "Afsluiten" }
        };
        int cmd = host.ShowMenu(items);
        Native.GetCursorPos(out var p);
        switch (cmd)
        {
            case 1: milo.Call(); break;
            case 2:
            {
                var b = toys.Ball ?? toys.Spawn(ToyKind.Ball, p.X, p.Y, Now, scale);
                b.InMouth = false;
                b.HeldByUser = false;
                b.Hidden = false;
                b.Body.X = p.X;
                b.Body.Y = Math.Min(p.Y, (world.ScreenAt(p.X, p.Y)?.Work.Bottom ?? p.Y) - 60 * scale);
                b.Body.Detach();
                b.Body.VX = (milo.Body.X < p.X ? 1 : -1) * (250 + Random.Shared.NextDouble() * 250) * scale / 3.0;
                b.Body.VY = -400 * scale / 3.0;
                b.ThrownAt = Now;
                milo.OnToy(b, true);
                break;
            }
            case 3:
            {
                var t = toys.Spawn(ToyKind.Treat, p.X, Math.Min(p.Y, (world.ScreenAt(p.X, p.Y)?.Work.Bottom ?? p.Y) - 60 * scale), Now, scale);
                t.Body.Detach();
                milo.OnToy(t, false);
                break;
            }
            case 4: if (asleep) milo.WakeUp(); else milo.GoSleep(); break;
            case 5:
                if (milo.Laser.On) milo.Laser.On = false;
                else milo.StartLaser();
                break;
            case 6: FillBowl(); break;
            case 7: critters.Spawn(world, milo, CritterKind.Butterfly); break;
            case 8: milo.PosePhoto(); photoAt = Now + 0.9; break;
            case 11 or 12 or 13: settings.Size = cmd - 10; settings.Save(); ApplyScale(); break;
            case 20: settings.Sound = !settings.Sound; sound.Enabled = settings.Sound; settings.Save(); break;
            case 25 or 26 or 27:
                settings.Volume = cmd == 25 ? 0.3 : cmd == 26 ? 0.6 : 1.0;
                sound.SetVolume(settings.Volume);
                settings.Save();
                milo.Say("yip", 0);
                break;
            case 21: settings.Vision = !settings.Vision; settings.Save(); eyes.Reset(); break;
            case 22: settings.HideInFullscreen = !settings.HideInFullscreen; settings.Save(); break;
            case 23: settings.StartWithWindows = !settings.StartWithWindows; settings.Save(); SetAutostart(settings.StartWithWindows); break;
            case 24: settings.BreakReminder = !settings.BreakReminder; milo.BreakReminder = settings.BreakReminder; settings.Save(); break;
            case 28: settings.ShowHome = !settings.ShowHome; home.Enabled = settings.ShowHome; homeKey = ""; settings.Save(); break;
            case 29: settings.Critters = !settings.Critters; settings.Save(); break;
            case 32: settings.Hearing = !settings.Hearing; hearing.Enabled = settings.Hearing; hearing.Clear(); settings.Save(); break;
            case 33: settings.Mischief = !settings.Mischief; milo.Mischief = settings.Mischief; settings.Save(); break;
            case 34: settings.ContentPlay = !settings.ContentPlay; milo.ContentPlay = settings.ContentPlay; settings.Save(); sensor.ForceRefresh(); break;
            case 9: milo.StartCrawl(); break;
            case 35: settings.TopView = !settings.TopView; settings.Save(); milo.SetTopDown(settings.TopView); lastRenderKey = ""; homeKey = ""; break;
            case 30: foreach (var t in toys.Items) t.Gone = true; break;
            case 31: try { Process.Start(new ProcessStartInfo(Settings.LogPath) { UseShellExecute = true }); } catch { } break;
            case 99: running = false; break;
            case >= 200 and < 200 + 9: milo.DoTrick(Tricks.All[cmd - 200].Id); break;
            case >= 300 and < 307: settings.Hat = HatOptions[cmd - 300].id; settings.Save(); lastRenderKey = ""; break;
            case >= 400 and < 402: settings.VisionModel = Models[cmd - 400].model; settings.Save(); eyes.Reset(); break;
            case >= 410 and < 413: settings.VisionMinSeconds = Frequencies[cmd - 410].secs; settings.Save(); break;
        }
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "...";
}
