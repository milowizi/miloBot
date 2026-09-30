using MiloBot.Win32;

namespace MiloBot;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "MiloBot.SingleInstance", out bool created);
        if (!created) return;
        try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
        Native.timeBeginPeriod(1);
        try
        {
            new App().Run();
        }
        catch (Exception e)
        {
            Log.Write("fatal: " + e);
        }
        finally
        {
            Native.timeEndPeriod(1);
        }
    }
}
