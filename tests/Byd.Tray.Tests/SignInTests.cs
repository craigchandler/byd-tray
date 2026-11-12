using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Byd.Client;
using Xunit;

namespace Byd.Tray.Tests;

public class SignInTests
{
    // The library's production constructor stays fixed to BYD. Reflection is confined to
    // this synthetic test fixture to reuse its internal transport seam without adding app dependencies to the library.
    private sealed class Server : HttpMessageHandler
    {
        internal int VehicleCount = 1;
        private readonly object codec = Activator.CreateInstance(typeof(BydClient).Assembly.GetType("Byd.Client.Crypto.Bangcle")!, nonPublic: true)!;
        internal readonly List<string> Paths = [];
        private static readonly Type Protocol = typeof(BydClient).Assembly.GetType("Byd.Client.Crypto.Protocol")!;
        internal bool ControlsOptIn;
        internal BydClient? Instance;
        internal BydMarket? Market;
        internal BydClient Client(BydClientOptions options)
        {
            ControlsOptIn = options.EnableRemoteControls;
            Market = options.Market;
            options = options with { EnablePushTelemetry = false, PollInterval = TimeSpan.Zero };
            return Instance = (BydClient)typeof(BydClient).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke([options, this, null]);
        }
        private static string Call(string name, params object[] args) => (string)Protocol.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Assert.Equal(new Uri(Market!.ApiBaseUrl).Host, request.RequestUri!.Host);
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path);
            // Only these synthetic exchanges are permitted. Never use a real network transport.
            var json = path switch
            {
                "/app/account/login" => "{\"token\":{\"userId\":\"synthetic-user\",\"signToken\":\"synthetic-sign\",\"encryToken\":\"synthetic-encryption\"}}",
                "/app/account/getAllListByUserId" => VehicleCount == 1 ? "[{\"vin\":\"SYNTHETIC000000001\",\"modelName\":\"Synthetic Atto 3\",\"energyType\":\"0\"}]" : "[{\"vin\":\"SYNTHETIC000000001\",\"modelName\":\"Synthetic Atto 3\",\"energyType\":\"0\"},{\"vin\":\"SYNTHETIC000000002\",\"modelName\":\"Synthetic Atto 3 B\",\"energyType\":\"0\"}]",
                "/vehicleInfo/vehicle/vehicleRealTimeRequest" => "{\"requestSerial\":\"synthetic-status-1\"}",
                "/vehicleInfo/vehicle/vehicleRealTimeResult" => new JsonObject { ["requestSerial"] = "synthetic-status-1", ["elecPercent"] = 78, ["enduranceMileage"] = 325, ["onlineState"] = 1, ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }.ToJsonString(),
                "/vehicle/vehicleswitch/getLatestConfig" => "{\"SYNTHETIC000000001\":{\"cfFixedList\":[{\"functionNo\":\"1005\"},{\"functionNo\":\"1026\"},{\"functionNo\":\"1012\"}]}}",
                _ => throw new InvalidOperationException("Unexpected endpoint in sign-in test")
            };
            var key = path.EndsWith("/login") ? Call("LoginKey", "synthetic-password") : Call("Md5", "synthetic-encryption");
            var reply = new JsonObject { ["code"] = "0", ["respondData"] = Call("Encrypt", json, key) }.ToJsonString();
            var encoded = (string)codec.GetType().GetMethod("Encode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(codec, [reply])!;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["response"] = encoded }.ToJsonString(), Encoding.UTF8, "application/json") });
        }
    }
    private sealed class Preferences : IPreferencesStore
    {
        internal AppPreferences Value = new();
        public AppPreferences Load() => Value;
        public void Save(AppPreferences preferences) { Value = preferences; }
    }
    private sealed class Secrets : ISecretStore
    {
        public Task<Account?> Read() => Task.FromResult<Account?>(new("synthetic@example.invalid", "synthetic-password"));
        public Task Save(Account account) => throw new InvalidOperationException();
        public Task Delete() => throw new InvalidOperationException();
    }
    private sealed class CountrySecrets(Account? account) : ISecretStore
    {
        internal int Reads;
        public Task<Account?> Read() { Reads++; return Task.FromResult(account); }
        public Task Save(Account value) => throw new InvalidOperationException("Unexpected synthetic vault save");
        public Task Delete() => throw new InvalidOperationException("Unexpected synthetic vault delete");
    }
    private static Button Find(MainWindow window, string label) => Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content?.ToString() == label);
    private static Button SignIn(MainWindow window) => Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "SignIn");
    private static string Hint(MainWindow window) => Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), b => b.Name == "RemoteSetupHint").Text!;
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    [AvaloniaFact]
    public void Signing_in_with_controls_checked_enables_setup_after_single_vehicle_selection()
    {
        var server = new Server(); var window = new MainWindow(false, new Preferences(), new Secrets(), server.Client); window.Show(); WindowTests.ShowAdvanced(window);
        var controls = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Content?.ToString()?.StartsWith("Enable remote controls") == true);
        controls.IsChecked = true;
        Click(Find(window, "Use saved account"));
        Assert.Equal("Sign in — with controls", SignIn(window).Content);
        Click(SignIn(window));
        Assert.Equal(new[] { "/app/account/login", "/app/account/getAllListByUserId" }, server.Paths);
        Assert.True(server.ControlsOptIn); Assert.True(controls.IsChecked);
        Assert.IsType<Vehicle>(Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), c => c.Name == "Vehicles").SelectedItem);
        Assert.True(Find(window, "Check remote setup").IsEffectivelyEnabled);
        Assert.StartsWith("Ready:", Hint(window));
        window.Stop(); window.Close();
    }
    [AvaloniaFact]
    public async Task Enabling_controls_preserves_the_signed_in_client_selected_vehicle_and_status_without_another_request()
    {
        var servers = new List<Server>();
        BydClient Create(BydClientOptions options) { var server = new Server { VehicleCount = 2 }; servers.Add(server); return server.Client(options); }
        var window = new MainWindow(false, new Preferences(), new Secrets(), Create); window.Show(); WindowTests.ShowAdvanced(window);
        Click(Find(window, "Use saved account")); Click(SignIn(window));
        var vehicles = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), c => c.Name == "Vehicles");
        Assert.Null(vehicles.SelectedItem); // Never guess when the person hasn't selected one.
        Assert.StartsWith("Choose your vehicle", Hint(window));
        vehicles.SelectedIndex = 1;
        var chosen = Assert.IsType<Vehicle>(vehicles.SelectedItem);
        await window.Refresh();
        var tooltip = window.TrayStatus;
        var server = Assert.Single(servers);
        var client = server.Instance;
        Assert.False(client!.RemoteControlsEnabled);
        var before = server.Paths.ToArray();
        var controls = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Content?.ToString()?.StartsWith("Enable remote controls") == true);
        controls.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Assert.Equal(chosen.Vin, Assert.IsType<Vehicle>(vehicles.SelectedItem).Vin);
        Assert.True(Find(window, "Check remote setup").IsEffectivelyEnabled);
        Assert.True(Find(window, "Lock").IsEffectivelyEnabled);
        Assert.StartsWith("Ready:", Hint(window));
        Assert.True(client.RemoteControlsEnabled); Assert.Same(client, Assert.Single(servers).Instance);
        Assert.False(server.ControlsOptIn); // Its initial sign-in was read-only.
        Assert.Equal(tooltip, window.TrayStatus); Assert.Equal(before, server.Paths);
        controls.IsChecked = false; Dispatcher.UIThread.RunJobs();
        Assert.False(client.RemoteControlsEnabled); Assert.False(Find(window, "Check remote setup").IsEffectivelyEnabled);
        Assert.False(Find(window, "Lock").IsEffectivelyEnabled);
        controls.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Assert.True(client.RemoteControlsEnabled); Assert.True(Find(window, "Lock").IsEffectivelyEnabled);
        Assert.Equal(tooltip, window.TrayStatus); Assert.Equal(before, server.Paths);
        Assert.DoesNotContain(server.Paths, p => p.Contains("remoteControl") || p.EndsWith("verifyControlPassword"));
        window.Stop(); window.Close();
    }
    [AvaloniaFact]
    public void Missing_previously_selected_vehicle_requires_a_new_choice_after_re_sign_in()
    {
        var servers = new List<Server>();
        BydClient Create(BydClientOptions options) { var server = new Server { VehicleCount = servers.Count == 0 ? 2 : 1 }; servers.Add(server); return server.Client(options); }
        var window = new MainWindow(false, new Preferences(), new Secrets(), Create); window.Show(); WindowTests.ShowAdvanced(window);
        Click(Find(window, "Use saved account")); Click(SignIn(window));
        var vehicles = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), c => c.Name == "Vehicles"); vehicles.SelectedIndex = 1;
        var controls = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Content?.ToString()?.StartsWith("Enable remote controls") == true); controls.IsChecked = true;
        Click(Find(window, "Use saved account")); Click(SignIn(window));
        Assert.Null(vehicles.SelectedItem); Assert.StartsWith("Choose your vehicle", Hint(window));
        Assert.False(Find(window, "Check remote setup").IsEffectivelyEnabled);
        Assert.All(servers, s => Assert.Equal(new[] { "/app/account/login", "/app/account/getAllListByUserId" }, s.Paths));
        window.Stop(); window.Close();
    }
    [AvaloniaFact]
    public async Task Commands_require_a_status_refresh_after_sign_in_and_explain_the_missing_step()
    {
        var server = new Server(); var window = new MainWindow(false, new Preferences(), new Secrets(), server.Client); window.Show(); WindowTests.ShowAdvanced(window);
        var controls = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Content?.ToString()?.StartsWith("Enable remote controls") == true);
        controls.IsChecked = true;
        Click(Find(window, "Use saved account")); Click(SignIn(window));
        Assert.True(Find(window, "Check remote setup").IsEffectivelyEnabled);
        Assert.False(Find(window, "Lock").IsEffectivelyEnabled);
        var hint = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "CommandHint");
        Assert.StartsWith("Refresh status to enable Lock", hint.Text);
        await window.Refresh();
        Assert.True(Find(window, "Lock").IsEffectivelyEnabled);
        Assert.Contains("Each needs its own confirmation and PIN", hint.Text);
        Assert.Equal(new[] { "/app/account/login", "/app/account/getAllListByUserId", "/vehicleInfo/vehicle/vehicleRealTimeRequest", "/vehicleInfo/vehicle/vehicleRealTimeResult" }, server.Paths);
        window.Stop(); window.Close();
    }
    [AvaloniaFact]
    public async Task Extra_controls_need_capabilities_and_reading_them_cannot_dispatch_a_command()
    {
        var server = new Server(); var window = new MainWindow(false, new Preferences(), new Secrets(), server.Client); window.Show(); WindowTests.ShowAdvanced(window);
        var controls = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Content?.ToString()?.StartsWith("Enable remote controls") == true); controls.IsChecked = true;
        Click(Find(window, "Use saved account")); Click(SignIn(window)); await window.Refresh();
        foreach (var expander in window.GetVisualDescendants().OfType<Expander>().ToArray()) expander.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(Find(window, "Review close windows").IsEffectivelyEnabled);
        Click(Find(window, "Load supported features"));
        Assert.True(Find(window, "Review close windows").IsEffectivelyEnabled);
        Assert.False(Find(window, "Review vent windows").IsEffectivelyEnabled); // Learning flag absent.
        Assert.False(Find(window, "Review open boot").IsEffectivelyEnabled);
        Assert.True(Find(window, "Review start charging").IsEffectivelyEnabled);
        Assert.DoesNotContain(server.Paths, p => p is "/control/remoteControl" or "/control/smartCharge/changeChargeStatue" || p.EndsWith("verifyControlPassword"));
        var views = Assert.Single(window.GetVisualDescendants().OfType<TabControl>()); var count = server.Paths.Count;
        views.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); views.SelectedIndex = 1; Dispatcher.UIThread.RunJobs(); Assert.Equal(count, server.Paths.Count);
        window.Stop(); window.Close();
    }
    [AvaloniaFact]
    public void Changing_country_clears_session_inputs_and_controls_without_loading_another_countrys_secret()
    {
        var preferences = new Preferences(); var server = new Server();
        var au = new CountrySecrets(new("synthetic@example.invalid", "synthetic-password")); var gb = new CountrySecrets(null);
        var window = new MainWindow(false, preferences, createClient: server.Client, createSecretStore: m => m.CountryCode == "AU" ? au : gb); window.Show(); WindowTests.ShowAdvanced(window);
        var controls = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Content?.ToString()?.StartsWith("Enable remote controls") == true); controls.IsChecked = true;
        Click(Find(window, "Use saved account")); Click(SignIn(window)); Assert.Equal(1, au.Reads); Assert.True(server.ControlsOptIn);
        var selector = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), c => c.Name == "AccountCountry");
        selector.SelectedItem = BydMarkets.ForCountry("GB"); Dispatcher.UIThread.RunJobs();
        Assert.Equal("GB", preferences.Value.CountryCode); Assert.Equal(0, gb.Reads); Assert.False(controls.IsChecked);
        Assert.All(window.GetVisualDescendants().OfType<TextBox>().Where(b => b.PlaceholderText is "BYD account email" or "BYD password"), b => Assert.Equal("", b.Text));
        Assert.Null(Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), c => c.Name == "Vehicles").SelectedItem);
        Assert.False(Find(window, "Check remote setup").IsEffectivelyEnabled);
        Click(Find(window, "Use saved account")); Assert.Equal(1, gb.Reads); Assert.Equal(1, au.Reads);
        var startup = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Content?.ToString()?.StartsWith("Sign in automatically") == true); startup.IsChecked = true;
        Assert.Equal("GB", preferences.Value.CountryCode); Assert.True(preferences.Value.AutoSignInOnStartup);
        Assert.Equal(new[] { "/app/account/login", "/app/account/getAllListByUserId" }, server.Paths);
        window.Stop(); window.Close();
    }
    [AvaloniaFact]
    public async Task Startup_uses_the_selected_market_vault_and_client_once_without_extra_features_or_commands()
    {
        var preferences = new Preferences { Value = new(true, "GB") }; var server = new Server(); var countries = new List<string>(); var secrets = new CountrySecrets(new("synthetic@example.invalid", "synthetic-password"));
        var window = new MainWindow(false, preferences, createClient: server.Client, createSecretStore: m => { countries.Add(m.CountryCode); return secrets; }); window.Show();
        await window.StartAsync(); await window.StartAsync();
        Assert.Equal(new[] { "GB" }, countries); Assert.Equal(1, secrets.Reads); Assert.Equal("GB", server.Market!.CountryCode); Assert.False(server.ControlsOptIn);
        Assert.Equal(new[] { "/app/account/login", "/app/account/getAllListByUserId", "/vehicleInfo/vehicle/vehicleRealTimeRequest", "/vehicleInfo/vehicle/vehicleRealTimeResult" }, server.Paths);
        Assert.False(server.Instance!.RemoteControlsEnabled);
        window.Stop(); window.Close();
    }
}
