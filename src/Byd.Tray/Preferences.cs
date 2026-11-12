using System.Text.Json;
using Byd.Client;

namespace Byd.Tray;

internal sealed record AppPreferences(bool AutoSignInOnStartup = false, string CountryCode = "AU");
internal interface IPreferencesStore
{
    AppPreferences Load();
    void Save(AppPreferences preferences);
}
internal sealed class PreferencesStore(string path) : IPreferencesStore
{
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BydTray", "preferences.json");
    internal static IPreferencesStore Create() => new PreferencesStore(DefaultPath);
    public AppPreferences Load()
    {
        try
        {
            var preferences = File.Exists(path) ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path)) ?? new() : new();
            BydMarkets.ForCountry(preferences.CountryCode); return preferences;
        }
        catch { Audit.Write(AuditEvent.PreferencesFailed); return new(); }
    }
    public void Save(AppPreferences preferences)
    {
        BydMarkets.ForCountry(preferences.CountryCode);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporary = path + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options)) JsonSerializer.Serialize(file, preferences);
            File.Move(temporary, path, true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
