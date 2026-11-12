using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Byd.Tray;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Byd.Tray.Tests.TestAppBuilder))]
namespace Byd.Tray.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public class TestApp : Application
{
    public override void Initialize() { Audit.Disabled = true; Styles.Add(new FluentTheme()); }
}
public class WindowTests
{
    [AvaloniaFact]
    public void Vehicle_picker_distinguishes_models_with_the_privacy_redacted_library()
    {
        var window = new MainWindow(true); window.Show();
        var picker = Assert.Single(window.GetVisualDescendants().OfType<ComboBox>(), c => c.Name == "Vehicles");
        var first = new Byd.Client.Vehicle("SYNTHETIC000000001", "Synthetic car A", 0);
        var second = new Byd.Client.Vehicle("SYNTHETIC000000002", "Synthetic car B", 0);
        picker.ItemsSource = new[] { first, second };
        picker.SelectedIndex = 0;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(picker.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == first.DisplayName);
        picker.SelectedIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(picker.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == second.DisplayName);
        Assert.DoesNotContain(picker.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == second.ToString());
        window.Stop(); window.Close();
    }

    internal static void ShowAdvanced(MainWindow window)
    {
        Assert.Single(window.GetVisualDescendants().OfType<TabControl>(), c => c.Name == "MainViews").SelectedIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        foreach (var section in window.GetVisualDescendants().OfType<Expander>().Where(e => e.Name is "AccountSettings" or "RemoteControls").ToArray()) section.IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
    private sealed class MemoryPreferences(bool auto = false) : IPreferencesStore
    {
        internal AppPreferences Value = new(auto);
        internal bool FailWrites;
        public AppPreferences Load() => Value;
        public void Save(AppPreferences preferences) { if (FailWrites) throw new IOException("Synthetic preferences failure"); Value = preferences; }
    }
    private sealed class EmptySecrets : ISecretStore
    {
        internal int Reads;
        public Task<Account?> Read() { Reads++; return Task.FromResult<Account?>(null); }
        public Task Save(Account account) => throw new InvalidOperationException("Unexpected wallet save in test");
        public Task Delete() => throw new InvalidOperationException("Unexpected wallet deletion in test");
    }
    [AvaloniaFact]
    public void Starts_disconnected_with_controls_and_polling_off()
    {
        var window = new MainWindow(false, new MemoryPreferences(), new EmptySecrets()); window.Show();
        Assert.Equal(0, Assert.Single(window.GetVisualDescendants().OfType<TabControl>()).SelectedIndex);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), box => box.PlaceholderText == "BYD password");
        ShowAdvanced(window);
        var children = window.GetVisualDescendants().ToArray();
        Assert.All(children.OfType<CheckBox>(), box => Assert.NotEqual(true, box.IsChecked));
        var refresh = Assert.Single(children.OfType<Button>(), b => b.Content?.ToString() == "Refresh status");
        Assert.False(refresh.IsEnabled);
        Assert.False(Assert.Single(children.OfType<Button>(), b => b.Content?.ToString() == "Vehicle location map").IsEnabled);
        Assert.False(Assert.Single(children.OfType<Button>(), b => b.Content?.ToString() == "Check remote setup").IsEnabled);
        Assert.All(children.OfType<Button>().Where(b => b.Content?.ToString() is "Lock" or "Unlock" or "Start climate" or "Stop climate"), button => Assert.False(button.IsEffectivelyEnabled));
        window.Stop(); window.Close();
    }
    [AvaloniaFact]
    public void Demo_is_labelled_and_cannot_sign_in_or_send_commands()
    {
        var window = new MainWindow(true); window.Show();
        var path = Environment.GetEnvironmentVariable("BYD_TEST_RENDER_PATH");
        if (!string.IsNullOrEmpty(path)) window.CaptureRenderedFrame()!.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        ShowAdvanced(window);
        var children = window.GetVisualDescendants().ToArray();
        Assert.Contains(children.OfType<TextBlock>(), x => x.Text?.Contains("Synthetic values") == true);
        Assert.All(children.OfType<Button>().Where(b => b.Content?.ToString() is "Sign in — read only" or "Unlock"), b => Assert.False(b.IsEffectivelyEnabled));
        window.Stop(); window.Close();
    }
    [Fact]
    public void Secret_container_string_representations_are_redacted()
    {
        Assert.DoesNotContain("test-password", new Account("test-user", "test-password").ToString());
        Assert.DoesNotContain("123456", new CommandInput("123456", 21, 10).ToString());
    }
    [AvaloniaFact]
    public void Status_is_default_settings_are_separate_and_demo_never_enables_extras()
    {
        var secrets = new EmptySecrets(); var window = new MainWindow(true, new MemoryPreferences(), secrets); window.Show();
        var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>());
        Assert.Equal(0, tabs.SelectedIndex); Assert.Equal(2, tabs.ItemCount);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), b => b.PlaceholderText == "BYD password");
        ShowAdvanced(window);
        foreach (var expander in window.GetVisualDescendants().OfType<Expander>().ToArray()) expander.IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Contains(buttons, b => b.Content?.ToString()?.StartsWith("Review ") == true);
        Assert.All(buttons.Where(b => b.Content?.ToString()?.StartsWith("Review ") == true), b => Assert.False(b.IsEffectivelyEnabled));
        Assert.DoesNotContain(buttons, b => b.Content?.ToString()?.Contains("Find", StringComparison.OrdinalIgnoreCase) == true || b.Content?.ToString()?.Contains("horn", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Equal(0, secrets.Reads);
        var path = Environment.GetEnvironmentVariable("BYD_ADVANCED_TEST_RENDER_PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var section in window.GetVisualDescendants().OfType<Expander>().ToArray()) section.IsExpanded = false;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        window.Stop(); window.Close();
    }
    [AvaloniaFact]
    public async Task Advanced_confirmation_shows_exact_schedule_and_returns_only_explicit_pin_confirmation()
    {
        var owner = new MainWindow(true); owner.Show();
        var settings = new Byd.Client.CommandSettings { ChargeStart = new(23, 0), ChargeUntilFull = true, Enabled = false, Repeat = Byd.Client.ChargeRepeat.Weekdays };
        var dialog = new CommandDialog(new Byd.Client.Vehicle("SYNTHETIC000000001", "Synthetic Atto 3", 0), Byd.Client.VehicleCommand.SaveChargingSchedule, settings);
        var result = dialog.ShowDialog<CommandInput?>(owner); var children = dialog.GetVisualDescendants().ToArray();
        Assert.Contains(children.OfType<TextBlock>(), t => t.Text?.Contains("23:00–until full") == true && t.Text.Contains("Weekdays") && t.Text.Contains("Enabled: False"));
        var send = Assert.Single(children.OfType<Button>(), b => b.Content?.ToString() == "Send this command"); Assert.False(send.IsEnabled); Assert.False(result.IsCompleted);
        var pin = Assert.Single(children.OfType<TextBox>(), b => b.PasswordChar == '●'); pin.Text = "123456"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        send.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(settings, (await result)!.Settings); Assert.Equal("", pin.Text);
        owner.Stop(); owner.Close();
    }

    [AvaloniaFact]
    public async Task Command_dialog_requires_pin_and_explicit_send()
    {
        var owner = new MainWindow(true);
        owner.Show();
        var dialog = new CommandDialog(new Byd.Client.Vehicle("SYNTHETIC000000001", "Synthetic Atto 3", 0), Byd.Client.VehicleCommand.StartClimate);
        var result = dialog.ShowDialog<CommandInput?>(owner);
        var controls = dialog.GetVisualDescendants().ToArray();
        var send = Assert.Single(controls.OfType<Button>(), b => b.Content?.ToString() == "Send this command");
        var pin = Assert.Single(controls.OfType<TextBox>(), b => b.PasswordChar == '●');
        Assert.False(send.IsEnabled);
        pin.Text = "123456";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(send.IsEnabled);
        Assert.False(result.IsCompleted);
        send.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var confirmed = await result;
        Assert.NotNull(confirmed);
        Assert.Equal(21, confirmed.Temperature);
        Assert.Equal(10, confirmed.Duration);
        Assert.Equal("", pin.Text);
        owner.Stop();
        owner.Close();
    }
    [AvaloniaFact]
    public async Task Remote_setup_dialog_is_separate_from_command_confirmation_and_clears_pin()
    {
        var owner = new MainWindow(true); owner.Show();
        ShowAdvanced(owner);
        Assert.False(Assert.Single(owner.GetVisualDescendants().OfType<Button>(), b => b.Content?.ToString() == "Check remote setup").IsEnabled);
        var dialog = new RemoteSetupDialog(new Byd.Client.Vehicle("SYNTHETIC000000001", "Synthetic Atto 3", 0));
        var result = dialog.ShowDialog<string?>(owner);
        var children = dialog.GetVisualDescendants().ToArray();
        Assert.DoesNotContain(children.OfType<Button>(), b => b.Content?.ToString() == "Send this command");
        Assert.Contains(children.OfType<TextBlock>(), t => t.Text?.Contains("No vehicle command will be sent") == true);
        var check = Assert.Single(children.OfType<Button>(), b => b.Content?.ToString() == "Check PIN only");
        var pin = Assert.Single(children.OfType<TextBox>(), b => b.PasswordChar == '●');
        Assert.False(check.IsEnabled);
        pin.Text = "１２３４５６"; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.False(check.IsEnabled);
        pin.Text = "123456"; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.True(check.IsEnabled); Assert.False(result.IsCompleted);
        check.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("123456", await result); Assert.Equal("", pin.Text);
        dialog = new RemoteSetupDialog(new Byd.Client.Vehicle("SYNTHETIC000000001", "Synthetic Atto 3", 0));
        result = dialog.ShowDialog<string?>(owner);
        pin = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), b => b.PasswordChar == '●'); pin.Text = "123456";
        dialog.Close(null); Assert.Null(await result); Assert.Equal("", pin.Text);
        owner.Stop(); owner.Close();
    }
    [Fact]
    public void Unexpected_command_failure_cannot_claim_no_command_was_sent_or_leak_details()
    {
        var exception = new InvalidOperationException("private-token 123456");
        Assert.Equal(AuditEvent.CommandUnknown, MainWindow.CommandFailureEvent(exception));
        var message = MainWindow.CommandFailureMessage(exception);
        Assert.Contains("Check the official app", message); Assert.DoesNotContain("private-token", message); Assert.DoesNotContain("123456", message);
        Assert.DoesNotContain("No vehicle command was sent", message);
        var unknown = new Byd.Client.BydException(Byd.Client.BydError.OutcomeUnknown);
        Assert.Equal(AuditEvent.CommandUnknown, MainWindow.CommandFailureEvent(unknown));
        Assert.Contains("outcome is unknown", MainWindow.CommandFailureMessage(unknown));
    }

    [AvaloniaFact]
    public void Demo_location_cannot_fetch_gps_or_contact_map_provider()
    {
        var gpsCalls = 0; var mapCalls = 0;
        var window = new LocationWindow("Synthetic public landmark", _ => { gpsCalls++; throw new InvalidOperationException(); }, true,
            () => { mapCalls++; return new Mapsui.Layers.MemoryLayer(); });
        window.Show();
        Assert.NotNull(window.DisplayedLocation); Assert.False(window.StreetMapLoaded);
        var load = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content?.ToString() == "Load OpenStreetMap");
        Assert.False(load.IsEnabled); load.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, gpsCalls); Assert.Equal(0, mapCalls);
        var path = Environment.GetEnvironmentVariable("BYD_MAP_TEST_RENDER_PATH");
        if (!string.IsNullOrEmpty(path)) window.CaptureRenderedFrame()!.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Close(); Assert.Null(window.DisplayedLocation);
    }
    [AvaloniaFact]
    public void Location_map_requires_explicit_opt_in_and_old_fix_remains_labelled()
    {
        var calls = 0;
        var window = new LocationWindow("Synthetic vehicle", _ => Task.FromResult(new Byd.Client.VehicleLocation(-33.8568, 151.2153, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(-2))),
            streetLayerFactory: () => { calls++; return new Mapsui.Layers.MemoryLayer(); });
        window.Show();
        Assert.Equal(0, calls); Assert.False(window.StreetMapLoaded);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("STALE OR UNVERIFIED") == true);
        var load = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content?.ToString() == "Load OpenStreetMap");
        load.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, calls); Assert.True(window.StreetMapLoaded); Assert.False(load.IsEnabled);
        load.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, calls);
        window.Close(); Assert.Null(window.DisplayedLocation);
    }
    [AvaloniaFact]
    public async Task Startup_checkbox_persists_and_empty_wallet_is_checked_only_once_without_enabling_polling_or_controls()
    {
        var preferences = new MemoryPreferences(); var secrets = new EmptySecrets();
        var window = new MainWindow(false, preferences, secrets); window.Show();
        ShowAdvanced(window);
        var startup = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(), box => box.Content?.ToString()?.StartsWith("Sign in automatically") == true);
        startup.IsChecked = true; Assert.True(preferences.Value.AutoSignInOnStartup);
        window.Stop(); window.Close();
        window = new MainWindow(false, preferences, secrets); window.Show();
        ShowAdvanced(window);
        await window.StartAsync(); await window.StartAsync();
        Assert.Equal(1, secrets.Reads);
        Assert.All(window.GetVisualDescendants().OfType<CheckBox>().Where(box => box.Content?.ToString()?.StartsWith("Sign in automatically") != true), box => Assert.NotEqual(true, box.IsChecked));
        Assert.Contains("Sign in", window.TrayStatus);
        window.Stop(); window.Close();
    }
    [AvaloniaFact]
    public async Task Startup_off_and_demo_never_read_the_wallet()
    {
        var secrets = new EmptySecrets(); var window = new MainWindow(false, new MemoryPreferences(), secrets);
        await window.StartAsync(); Assert.Equal(0, secrets.Reads); window.Stop();
        window = new MainWindow(true, new MemoryPreferences(true), secrets);
        await window.StartAsync(); Assert.Equal(0, secrets.Reads); window.Stop();
    }
    [AvaloniaFact]
    public void Failed_preference_save_restores_checkbox_instead_of_claiming_it_will_take_effect()
    {
        var preferences = new MemoryPreferences { FailWrites = true };
        var window = new MainWindow(false, preferences, new EmptySecrets()); window.Show();
        ShowAdvanced(window);
        var startup = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(), box => box.Content?.ToString()?.StartsWith("Sign in automatically") == true);
        startup.IsChecked = true;
        Assert.False(startup.IsChecked); Assert.False(preferences.Value.AutoSignInOnStartup);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("could not be saved") == true);
        window.Stop(); window.Close();
    }
}
