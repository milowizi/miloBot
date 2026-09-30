using System.Runtime.InteropServices;

namespace MiloBot.Audio;

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation
{
    int GetPeakValue(out float peak);
}

public class AudioMeter
{
    IAudioMeterInformation meter;
    double nextRetry;
    double fast, slow;
    double lastOnset;
    readonly Queue<(double t, bool loud)> history = new();

    public double Level => fast;
    public double Average => slow;
    public bool Onset { get; private set; }
    public bool Startle { get; private set; }
    public bool MusicLike { get; private set; }
    public double LoudFraction { get; private set; }

    bool Connect()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type);
            enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
            var iid = typeof(IAudioMeterInformation).GUID;
            device.Activate(ref iid, 23, IntPtr.Zero, out object o);
            meter = (IAudioMeterInformation)o;
            return true;
        }
        catch (Exception e)
        {
            Log.Write("audio meter unavailable: " + e.Message);
            meter = null;
            return false;
        }
    }

    public void Update(double now)
    {
        Onset = false;
        Startle = false;
        if (meter == null)
        {
            if (now < nextRetry) return;
            nextRetry = now + 10;
            if (!Connect()) return;
        }
        float peak;
        try
        {
            if (meter.GetPeakValue(out peak) != 0) throw new Exception("peak failed");
        }
        catch
        {
            meter = null;
            nextRetry = now + 5;
            return;
        }
        double prevSlow = slow;
        fast += (peak - fast) * 0.5;
        slow += (peak - slow) * 0.02;
        if (peak > prevSlow * 1.8 + 0.08 && now - lastOnset > 0.22)
        {
            Onset = true;
            if (peak > 0.55 && prevSlow < 0.08 && now - lastOnset > 3) Startle = true;
            lastOnset = now;
        }
        history.Enqueue((now, peak > 0.02));
        while (history.Count > 0 && now - history.Peek().t > 6) history.Dequeue();
        int loud = 0;
        foreach (var h in history) if (h.loud) loud++;
        LoudFraction = history.Count > 0 ? (double)loud / history.Count : 0;
        MusicLike = LoudFraction > 0.85 && slow > 0.04;
    }
}
