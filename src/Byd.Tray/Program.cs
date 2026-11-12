using Avalonia;

namespace Byd.Tray;

internal static class Program
{
    internal static LaunchOptions Options { get; private set; } = new();
    internal static bool Demo => Options.Demo;
    [STAThread]
    public static int Main(string[] args)
    {
        try { Options = LaunchOptions.Parse(args); }
        catch (ArgumentException) { Audit.Write(AuditEvent.UnexpectedFailure); return 2; }
        using var instance = new Mutex(true, "BydTray-" + Environment.UserName + (Demo ? "-demo" : ""), out var first);
        if (!first) return 0;
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Audit.Write(AuditEvent.UnexpectedFailure);
        try { return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args); }
        catch { Audit.Write(AuditEvent.UnexpectedFailure); return 1; }
    }
}
