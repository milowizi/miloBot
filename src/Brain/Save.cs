using System.Text.Json;

namespace MiloBot.Brain;

public class DayStats
{
    public int Pets, Boops, Fetches, Treats, Meals, Tricks, Cats, Dogs, Gifts, Zoomies, Butterflies, Birds, Tugs, Walks, Rides, Dizzy, Falls, Words, Pounces, Swats, Climbs, Nudges, Bats, Pictures, Reads;
}

public class SeenInfo
{
    public string Kind = "", Label = "";
    public int Count;
    public DateTime Last;
}

public class SaveData
{
    public DateTime FirstMet = DateTime.Now;
    public double Friendship;
    public double Hunger = 0.2;
    public double BowlFood = 1.0;
    public Dictionary<string, double> Tricks = new();
    public Dictionary<string, SeenInfo> Seen = new();
    public string Day = "";
    public DayStats Today = new();
    public DayStats Total = new();
    public double MinutesTogether;

    static readonly int[] LevelAt = { 0, 10, 30, 60, 100, 150, 220, 300, 400, 520 };
    static readonly JsonSerializerOptions Options = new() { IncludeFields = true, WriteIndented = true };
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiloBot", "save.json");

    public int Level
    {
        get
        {
            int l = 0;
            for (int i = 0; i < LevelAt.Length; i++) if (Friendship >= LevelAt[i]) l = i;
            return l;
        }
    }

    public static SaveData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var d = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(FilePath), Options);
                if (d != null) return d;
            }
        }
        catch (Exception e)
        {
            Log.Write("save load failed: " + e.Message);
        }
        return new SaveData();
    }

    public void Store()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception e)
        {
            Log.Write("save failed: " + e.Message);
        }
    }

    public void RollDay()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (Day == today) return;
        Day = today;
        Today = new DayStats();
    }

    public void Stat(string name)
    {
        RollDay();
        foreach (var d in new[] { Today, Total })
        {
            switch (name)
            {
                case "pets": d.Pets++; break;
                case "boops": d.Boops++; break;
                case "fetches": d.Fetches++; break;
                case "treats": d.Treats++; break;
                case "meals": d.Meals++; break;
                case "tricks": d.Tricks++; break;
                case "cats": d.Cats++; break;
                case "dogs": d.Dogs++; break;
                case "gifts": d.Gifts++; break;
                case "zoomies": d.Zoomies++; break;
                case "butterflies": d.Butterflies++; break;
                case "birds": d.Birds++; break;
                case "tugs": d.Tugs++; break;
                case "walks": d.Walks++; break;
                case "rides": d.Rides++; break;
                case "dizzy": d.Dizzy++; break;
                case "falls": d.Falls++; break;
                case "words": d.Words++; break;
                case "pounces": d.Pounces++; break;
                case "swats": d.Swats++; break;
                case "climbs": d.Climbs++; break;
                case "nudges": d.Nudges++; break;
                case "bats": d.Bats++; break;
                case "pictures": d.Pictures++; break;
                case "reads": d.Reads++; break;
            }
        }
    }

    public void AddFriend(double amount) => Friendship = Math.Max(0, Friendship + amount);

    public double Skill(string trick, double start)
    {
        if (!Tricks.TryGetValue(trick, out var s)) Tricks[trick] = s = start;
        return s;
    }

    public SeenInfo SeenOf(string kind, string label)
    {
        string key = kind + ":" + Normalize(label);
        if (!Seen.TryGetValue(key, out var info))
        {
            if (Seen.Count > 300)
            {
                var old = Seen.OrderBy(kv => kv.Value.Last).Take(60).Select(kv => kv.Key).ToList();
                foreach (var k in old) Seen.Remove(k);
            }
            Seen[key] = info = new SeenInfo { Kind = kind, Label = label, Last = DateTime.Now };
        }
        return info;
    }

    public int KindCount(string kind) => Seen.Values.Where(s => s.Kind == kind).Sum(s => s.Count);

    static string Normalize(string label)
    {
        var words = label.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3);
        return string.Join(' ', words);
    }
}
