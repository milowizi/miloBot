using System.Text.Json;

namespace MiloBot;

public class Settings
{
    public int Size { get; set; } = 2;
    public bool Sound { get; set; } = true;
    public double Volume { get; set; } = 0.6;
    public bool Vision { get; set; } = true;
    public string VisionModel { get; set; } = "qwen3-vl:8b-instruct";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public int VisionMinSeconds { get; set; } = 25;
    public bool HideInFullscreen { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool Debug { get; set; } = false;
    public string Hat { get; set; } = "auto";
    public bool BreakReminder { get; set; } = true;
    public int BreakMinutes { get; set; } = 75;
    public bool ShowHome { get; set; } = true;
    public bool Critters { get; set; } = true;
    public bool Hearing { get; set; } = true;

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiloBot");
    static string FilePath => Path.Combine(Dir, "settings.json");
    public static string LogPath => Path.Combine(AppContext.BaseDirectory, "milo.log");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception e)
        {
            Log.Write("settings load failed: " + e.Message);
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Write("settings save failed: " + e.Message);
        }
    }
}

public static class Log
{
    static readonly object gate = new();

    public static void Write(string line)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Settings.LogPath));
                var fi = new FileInfo(Settings.LogPath);
                if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                File.AppendAllText(Settings.LogPath, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
