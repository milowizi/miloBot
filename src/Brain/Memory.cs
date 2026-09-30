namespace MiloBot.Brain;

public class Memory
{
    class Trace
    {
        public double Strength, Last;
    }

    readonly Dictionary<string, Trace> traces = new();
    readonly Dictionary<string, double> cooldowns = new();

    public double Familiarity(string key, double now, double halfLife = 900)
    {
        if (!traces.TryGetValue(key, out var t)) return 0;
        return t.Strength * Math.Pow(0.5, (now - t.Last) / halfLife);
    }

    public double Novelty(string key, double now, double halfLife = 900) => 1.0 / (1.0 + Familiarity(key, now, halfLife));

    public void Remember(string key, double now, double amount = 1, double halfLife = 900)
    {
        double f = Familiarity(key, now, halfLife);
        traces[key] = new Trace { Strength = f + amount, Last = now };
        if (traces.Count > 400)
        {
            var oldest = traces.OrderBy(kv => kv.Value.Last).Take(100).Select(kv => kv.Key).ToList();
            foreach (var k in oldest) traces.Remove(k);
        }
    }

    public bool Cool(string key, double seconds, double now)
    {
        if (cooldowns.TryGetValue(key, out var until) && now < until) return false;
        cooldowns[key] = now + seconds;
        return true;
    }

    public bool Ready(string key, double now) => !cooldowns.TryGetValue(key, out var until) || now >= until;
}
