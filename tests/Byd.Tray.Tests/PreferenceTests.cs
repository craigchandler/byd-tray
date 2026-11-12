using System.Text.Json;
using Xunit;

namespace Byd.Tray.Tests;

public class PreferenceTests
{
    [Fact]
    public void Preferences_persist_only_startup_and_country_with_safe_legacy_defaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "byd-preferences-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "preferences.json");
        try
        {
            var store = new PreferencesStore(path); Assert.False(store.Load().AutoSignInOnStartup);
            store.Save(new(true)); Assert.True(new PreferencesStore(path).Load().AutoSignInOnStartup);
            var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(2, json.RootElement.EnumerateObject().Count()); Assert.True(json.RootElement.GetProperty("AutoSignInOnStartup").GetBoolean()); Assert.Equal("AU", json.RootElement.GetProperty("CountryCode").GetString());
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            File.WriteAllText(path, "invalid JSON"); Assert.False(store.Load().AutoSignInOnStartup);
            File.WriteAllText(path, "{\"AutoSignInOnStartup\":true}"); Assert.True(store.Load().AutoSignInOnStartup); Assert.Equal("AU", store.Load().CountryCode);
            File.WriteAllText(path, "{\"AutoSignInOnStartup\":true,\"CountryCode\":\"XX\"}"); Assert.False(store.Load().AutoSignInOnStartup); Assert.Equal("AU", store.Load().CountryCode);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void Tray_first_launch_is_an_explicit_flag_and_demo_remains_separate()
    {
        Assert.Equal(new LaunchOptions(), LaunchOptions.Parse([]));
        Assert.Equal(new LaunchOptions(StartInTray: true), LaunchOptions.Parse(["--start-in-tray"]));
        Assert.Equal(new LaunchOptions(Demo: true, StartInTray: true), LaunchOptions.Parse(["--demo", "--start-in-tray"]));
    }
    [Fact]
    public void Vault_keys_are_country_scoped_and_keep_the_existing_australian_entry()
    {
        var au = Byd.Client.BydMarkets.ForCountry("AU"); var gb = Byd.Client.BydMarkets.ForCountry("GB"); var nz = Byd.Client.BydMarkets.ForCountry("NZ");
        Assert.Equal("Australian account", SecretStore.WalletEntry(au)); Assert.Equal("BydTray/Australia/Account", SecretStore.WindowsTarget(au));
        Assert.NotEqual(SecretStore.WalletEntry(au), SecretStore.WalletEntry(nz)); Assert.NotEqual(SecretStore.WindowsTarget(au), SecretStore.WindowsTarget(nz));
        Assert.Equal("Account/GB", SecretStore.WalletEntry(gb)); Assert.Equal("BydTray/GB/Account", SecretStore.WindowsTarget(gb));
    }
    [Fact]
    public void Device_time_zone_preserves_Iana_and_converts_Windows_ids_for_the_cloud_protocol()
    {
        Assert.Equal("Australia/Adelaide", MainWindow.IanaTimeZone("Australia/Adelaide"));
        Assert.Equal("Australia/Adelaide", MainWindow.IanaTimeZone("Cen. Australia Standard Time"));
        Assert.Equal("UTC", MainWindow.IanaTimeZone("invalid-synthetic-time-zone"));
    }
}
