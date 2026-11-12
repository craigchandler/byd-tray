namespace Byd.Tray;

internal enum AuditEvent { Started, LoginSucceeded, LoginFailed, StatusSucceeded, StatusFailed, CommandRequested, CommandConfirmed, CommandFailed, CommandUnknown, WalletSaved, WalletDeleted, WalletFailed, UnexpectedFailure, StatusFromLivePush, StatusFromHttp, LocationSucceeded, LocationFailed, PreferencesFailed, TrayTooltipReady, TrayTooltipFailed, RemoteSetupPassed, RemoteSetupSupportFailed, RemoteSetupPinFailed, RemoteSetupFailed, CommandNotSent, AdvancedReadSucceeded, AdvancedReadFailed }
internal static class Audit
{
    internal static bool Disabled { get; set; }
    // Only compile-time event names can enter the log; no free text or exception details.
    internal static void Write(AuditEvent value)
    {
        if (Disabled) return; // Synthetic demos/tests must not enter the live user's validation log.
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BydTray");
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var path = Path.Combine(directory, "events.log");
            if (File.Exists(path) && new FileInfo(path).Length > 128 * 1024) File.Move(path, path + ".1", true);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {value}\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* Logging failure must not expose exception data or prevent normal exit. */ }
    }
}
