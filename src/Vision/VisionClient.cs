using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiloBot.Vision;

public class SeenThing
{
    public string Label = "";
    public string Kind = "other";
    public double X1, Y1, X2, Y2;
    public int Salience;
}

public class VisionResult
{
    public string Activity = "other";
    public string Mood = "calm";
    public int Interest;
    public List<SeenThing> Things = new();
    public double Seconds;
}

public class VisionClient
{
    static readonly string[] Families = { "qwen3-vl", "qwen2.5vl", "gemma4", "gemma3", "minicpm-v", "llama3.2-vision", "llava", "moondream", "granite3.2-vision", "mistral-small3" };
    public static readonly string[] Kinds = { "dog", "cat", "animal", "food", "ball", "toy", "person_face", "error", "warning", "success", "victory", "game_over", "sad", "scary", "cute", "video_player", "text", "button", "other" };
    static readonly string[] Activities = { "coding", "gaming", "watching_video", "browsing", "reading", "writing", "chatting", "music", "modeling_3d", "drawing", "files", "desktop", "other" };
    static readonly string[] Moods = { "calm", "focused", "fun", "tense", "scary", "sad", "celebratory" };

    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
    readonly string baseUrl;
    readonly string preferred;
    public string Model { get; private set; }
    public string Status { get; private set; } = "zoeken...";
    public bool Ready => Model != null;
    bool sendThink = true;

    public VisionClient(string baseUrl, string preferred)
    {
        this.baseUrl = baseUrl.TrimEnd('/');
        this.preferred = preferred;
    }

    public async Task<bool> Discover()
    {
        try
        {
            var json = await http.GetFromJsonAsync<JsonObject>(baseUrl + "/api/tags");
            var names = json?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToList() ?? new List<string>();
            string pick = names.FirstOrDefault(n => n == preferred || n == preferred + ":latest");
            if (pick == null)
                foreach (var fam in Families)
                {
                    pick = names.Where(n => n.StartsWith(fam, StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(n => n.Contains("instruct")).ThenBy(n => n.Contains("thinking")).FirstOrDefault();
                    if (pick != null) break;
                }
            Model = pick;
            Status = pick != null ? "aan (" + pick + ")" : "geen model: ollama pull " + preferred;
            return pick != null;
        }
        catch (Exception e)
        {
            Model = null;
            Status = "Ollama niet gevonden";
            Log.Write("ollama discover: " + e.Message);
            return false;
        }
    }

    public async Task Warm()
    {
        if (Model == null) return;
        try
        {
            var body = new JsonObject { ["model"] = Model, ["keep_alive"] = "30m" };
            await http.PostAsync(baseUrl + "/api/generate", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
            Log.Write("vision model loaded: " + Model);
        }
        catch (Exception e)
        {
            Log.Write("warm failed: " + e.Message);
        }
    }

    public async Task Unload()
    {
        if (Model == null) return;
        try
        {
            var body = new JsonObject { ["model"] = Model, ["keep_alive"] = 0 };
            await http.PostAsync(baseUrl + "/api/generate", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        }
        catch { }
    }

    static JsonObject Schema()
    {
        JsonArray Enum(string[] v) => new(v.Select(x => (JsonNode)x).ToArray());
        var thing = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["label"] = new JsonObject { ["type"] = "string" },
                ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(Kinds) },
                ["box"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" }, ["minItems"] = 4, ["maxItems"] = 4 },
                ["salience"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 10 }
            },
            ["required"] = new JsonArray("label", "kind", "box", "salience")
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["activity"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(Activities) },
                ["mood"] = new JsonObject { ["type"] = "string", ["enum"] = Enum(Moods) },
                ["things"] = new JsonObject { ["type"] = "array", ["items"] = thing, ["maxItems"] = 4 },
                ["interest"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 10 }
            },
            ["required"] = new JsonArray("activity", "mood", "things", "interest")
        };
    }

    const string SystemPrompt =
        "You are the eyes of Milo, a small fluffy Pomeranian who lives on this computer screen as a tiny pixel-art pet. " +
        "Look at the screenshot like a curious dog: notice animals, food, balls and toys, faces, red error popups, wins and celebrations, " +
        "game-over or sad moments, scary or horror things, and videos that are playing. " +
        "Ignore Milo himself (the small orange pixel-art dog), his red ball, his treats, the taskbar, desktop icons, app icons and the desktop wallpaper or background image. " +
        "Use kind 'cat' for any cat or kitten and 'dog' for any dog or puppy, also in photos, thumbnails and videos. Only use 'scary' for real horror content like monsters, jumpscares or creepy faces. " +
        "List at most 4 things, most eye-catching first, each with a short English label, a kind, a tight bounding box and a salience from 0 (boring) to 10 (a dog would go crazy). " +
        "Boxes use relative coordinates from 0 to 1000 on both axes with the origin at the top-left: [x1, y1, x2, y2]. " +
        "Plain text, code, menus and buttons are boring (salience 0-2). Answer with JSON only.";

    public async Task<VisionResult> Look(byte[] png, int imgW, int imgH, string app, string title, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string user = $"Active window: {app} \"{Trim(title, 120)}\". What would catch a dog's attention on this screen?";
        var body = new JsonObject
        {
            ["model"] = Model,
            ["stream"] = false,
            ["keep_alive"] = "30m",
            ["format"] = Schema(),
            ["options"] = new JsonObject { ["temperature"] = 0.2, ["num_predict"] = 400 },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = user, ["images"] = new JsonArray(Convert.ToBase64String(png)) })
        };
        if (sendThink) body["think"] = false;
        var resp = await http.PostAsync(baseUrl + "/api/chat", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
        string text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            if (sendThink && text.Contains("think", StringComparison.OrdinalIgnoreCase))
            {
                sendThink = false;
                return await Look(png, imgW, imgH, app, title, ct);
            }
            throw new Exception($"ollama {(int)resp.StatusCode}: {Trim(text, 200)}");
        }
        var root = JsonNode.Parse(text);
        string content = root?["message"]?["content"]?.GetValue<string>() ?? "{}";
        var r = Parse(content, imgW, imgH);
        r.Seconds = sw.Elapsed.TotalSeconds;
        return r;
    }

    public static VisionResult Parse(string content, int imgW, int imgH)
    {
        var r = new VisionResult();
        int s = content.IndexOf('{'), e = content.LastIndexOf('}');
        if (s < 0 || e <= s) return r;
        using var doc = JsonDocument.Parse(content.Substring(s, e - s + 1));
        var root = doc.RootElement;
        if (root.TryGetProperty("activity", out var a) && a.ValueKind == JsonValueKind.String) r.Activity = a.GetString();
        if (root.TryGetProperty("mood", out var m) && m.ValueKind == JsonValueKind.String) r.Mood = m.GetString();
        if (root.TryGetProperty("interest", out var i) && i.ValueKind == JsonValueKind.Number) r.Interest = Math.Clamp(i.GetInt32(), 0, 10);
        if (root.TryGetProperty("things", out var things) && things.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in things.EnumerateArray())
            {
                if (!t.TryGetProperty("box", out var box) || box.ValueKind != JsonValueKind.Array || box.GetArrayLength() < 4) continue;
                var v = box.EnumerateArray().Take(4).Select(x => x.ValueKind == JsonValueKind.Number ? x.GetDouble() : 0).ToArray();
                bool absolute = v.Any(x => x > 1000.5);
                double nx(double x) => absolute ? x / imgW : x / 1000.0;
                double ny(double y) => absolute ? y / imgH : y / 1000.0;
                var st = new SeenThing
                {
                    Label = t.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : "",
                    Kind = t.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : "other",
                    Salience = t.TryGetProperty("salience", out var sa) && sa.ValueKind == JsonValueKind.Number ? Math.Clamp(sa.GetInt32(), 0, 10) : 3,
                    X1 = Math.Clamp(Math.Min(nx(v[0]), nx(v[2])), 0, 1),
                    Y1 = Math.Clamp(Math.Min(ny(v[1]), ny(v[3])), 0, 1),
                    X2 = Math.Clamp(Math.Max(nx(v[0]), nx(v[2])), 0, 1),
                    Y2 = Math.Clamp(Math.Max(ny(v[1]), ny(v[3])), 0, 1)
                };
                if (st.X2 - st.X1 < 0.002 || st.Y2 - st.Y1 < 0.002) continue;
                r.Things.Add(st);
            }
        }
        return r;
    }

    static string Trim(string s, int n) => s.Length <= n ? s : s[..n];
}
