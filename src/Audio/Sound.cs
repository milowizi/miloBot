using System.Runtime.InteropServices;
using MiloBot.Win32;

namespace MiloBot.Audio;

public class Sound : IDisposable
{
    class Clip
    {
        public byte[] Original;
        public int DataOffset, DataLength;
        public bool Scalable;
        public IntPtr Memory;
    }

    static readonly double[] Pitches = { 0.93, 1.0, 1.08 };
    readonly Dictionary<string, List<Clip>> clips = new();
    readonly Random rng = new();
    double volume = 1.0;
    public bool Enabled = true;
    double lastPlay;

    public Sound(string dir, double volume)
    {
        this.volume = volume;
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*.wav"))
        {
            string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            string key = TrimVariantSuffix(name);
            try
            {
                var bytes = File.ReadAllBytes(file);
                if (!clips.TryGetValue(key, out var list)) clips[key] = list = new List<Clip>();
                if (TryParse(bytes, out int channels, out int bits, out int dataOffset, out int dataLength) && bits == 16)
                {
                    bool userVariants = name != key;
                    foreach (var p in userVariants ? new[] { 1.0 } : Pitches)
                    {
                        var pitched = p == 1.0 ? bytes : Repitch(bytes, dataOffset, dataLength, channels, p);
                        TryParse(pitched, out _, out _, out int o, out int l);
                        list.Add(Make(pitched, o, l, true));
                    }
                }
                else list.Add(Make(bytes, 0, 0, false));
            }
            catch (Exception e)
            {
                Log.Write($"sound {file}: {e.Message}");
            }
        }
    }

    static string TrimVariantSuffix(string name)
    {
        int i = name.Length;
        while (i > 0 && char.IsDigit(name[i - 1])) i--;
        if (i < name.Length && i > 0 && (name[i - 1] == '_' || name[i - 1] == '-')) return name[..(i - 1)];
        return i < name.Length && i > 0 ? name[..i] : name;
    }

    Clip Make(byte[] bytes, int offset, int length, bool scalable)
    {
        var c = new Clip { Original = bytes, DataOffset = offset, DataLength = length, Scalable = scalable };
        c.Memory = Marshal.AllocHGlobal(bytes.Length);
        Write(c);
        return c;
    }

    void Write(Clip c)
    {
        var copy = (byte[])c.Original.Clone();
        if (c.Scalable && volume < 0.999)
        {
            for (int i = c.DataOffset; i + 1 < c.DataOffset + c.DataLength; i += 2)
            {
                short s = (short)(copy[i] | (copy[i + 1] << 8));
                s = (short)Math.Clamp(s * volume, short.MinValue, short.MaxValue);
                copy[i] = (byte)s;
                copy[i + 1] = (byte)(s >> 8);
            }
        }
        Marshal.Copy(copy, 0, c.Memory, copy.Length);
    }

    public void SetVolume(double v)
    {
        volume = Math.Clamp(v, 0, 1);
        Native.PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
        foreach (var list in clips.Values)
            foreach (var c in list) Write(c);
    }

    public bool Has(string name) => clips.ContainsKey(name);

    public void Play(string name, double now)
    {
        if (!Enabled || volume <= 0.01) return;
        if (now - lastPlay < 0.25) return;
        if (!clips.TryGetValue(name, out var list) || list.Count == 0) return;
        lastPlay = now;
        var clip = list[rng.Next(list.Count)];
        Native.PlaySound(clip.Memory, IntPtr.Zero, Native.SND_MEMORY | Native.SND_ASYNC | Native.SND_NODEFAULT);
    }

    static bool TryParse(byte[] b, out int channels, out int bits, out int dataOffset, out int dataLength)
    {
        channels = bits = dataOffset = dataLength = 0;
        if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[8] != 'W') return false;
        int p = 12;
        while (p + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
            int len = BitConverter.ToInt32(b, p + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(b, p + 10);
                bits = BitConverter.ToInt16(b, p + 22);
            }
            else if (id == "data")
            {
                dataOffset = p + 8;
                dataLength = Math.Min(len, b.Length - dataOffset);
                return channels > 0;
            }
            p += 8 + len + (len & 1);
        }
        return false;
    }

    static byte[] Repitch(byte[] src, int offset, int length, int channels, double pitch)
    {
        int frames = length / 2 / channels;
        int outFrames = (int)(frames / pitch);
        var outBytes = new byte[offset + outFrames * channels * 2];
        Array.Copy(src, outBytes, offset);
        for (int f = 0; f < outFrames; f++)
        {
            double pos = f * pitch;
            int i0 = (int)pos;
            int i1 = Math.Min(i0 + 1, frames - 1);
            double t = pos - i0;
            for (int ch = 0; ch < channels; ch++)
            {
                short a = BitConverter.ToInt16(src, offset + (i0 * channels + ch) * 2);
                short b = BitConverter.ToInt16(src, offset + (i1 * channels + ch) * 2);
                short v = (short)(a + (b - a) * t);
                int o = offset + (f * channels + ch) * 2;
                outBytes[o] = (byte)v;
                outBytes[o + 1] = (byte)(v >> 8);
            }
        }
        BitConverter.GetBytes(outBytes.Length - 8).CopyTo(outBytes, 4);
        BitConverter.GetBytes(outFrames * channels * 2).CopyTo(outBytes, offset - 4);
        return outBytes;
    }

    public void Dispose()
    {
        Native.PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
        foreach (var list in clips.Values)
            foreach (var c in list)
                if (c.Memory != IntPtr.Zero) Marshal.FreeHGlobal(c.Memory);
        clips.Clear();
    }
}
