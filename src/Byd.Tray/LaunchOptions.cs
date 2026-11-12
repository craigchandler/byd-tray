namespace Byd.Tray;

internal sealed record LaunchOptions(bool Demo = false, bool StartInTray = false)
{
    internal static LaunchOptions Parse(string[] args)
    {
        return new(args.Contains("--demo"), args.Contains("--start-in-tray"));
    }
}
