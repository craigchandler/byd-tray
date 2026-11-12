using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Byd.Client;

namespace Byd.Tray;

internal sealed partial class MainWindow : Window
{
    private readonly bool demo;
    private readonly TextBox email = new() { PlaceholderText = "BYD account email", MaxLength = 254 };
    private readonly TextBox password = new() { PlaceholderText = "BYD password", PasswordChar = '●', MaxLength = 512 };
    private readonly CheckBox remember = new() { Content = "Save account in the system wallet" };
    private readonly CheckBox controls = new() { Content = "Enable remote controls for this session" };
    private readonly CheckBox automatic = new() { Content = "Refresh every 15 minutes (may wake the car)" };
    private readonly CheckBox autoSignIn = new() { Content = "Sign in automatically at startup (system wallet)" };
    private readonly IPreferencesStore preferencesStore;
    private ISecretStore secretStore;
    private readonly Func<BydMarket, ISecretStore> createSecretStore;
    private readonly ComboBox accountCountry = new() { Name = "AccountCountry", ItemsSource = BydDiscovery.GetRegions().SelectMany(r => r.Countries).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 420 };
    private bool restoringCountry;
    private readonly Func<BydClientOptions, BydClient> createClient;
    private AppPreferences preferences;
    private bool restoringPreference;
    private readonly ComboBox vehicles = new()
    {
        Name = "Vehicles", HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a vehicle",
        // Vehicle.ToString() deliberately redacts identity for diagnostics. Show the
        // explicit display label only in this local account UI, never in audit logs.
        ItemTemplate = new FuncDataTemplate<Vehicle>((vehicle, _) => new TextBlock { Text = vehicle?.DisplayName })
    };
    private readonly TextBlock summary = new() { Text = "No vehicle data yet", FontSize = 25, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { Text = "Sign in, choose your vehicle, then refresh.", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock remoteSetupHint = new() { Name = "RemoteSetupHint", TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly TextBlock commandHint = new() { Name = "CommandHint", TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly TextBlock overview = new() { FontSize = 20, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock freshness = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly TabControl views = new() { Name = "MainViews", SelectedIndex = 0 };
    private readonly WrapPanel commandPanel = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel inputs = new() { Spacing = 10 };
    private readonly Button login, refresh, load, forget, showLocation, checkRemoteSetup;
    private LocationWindow? locationWindow;
    private readonly DispatcherTimer polling = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly DispatcherTimer age = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly CancellationTokenSource lifetime = new();
    private BydClient? client;
    private VehicleStatus? status;
    private bool busy, stopped, restoringControls;
    private bool statusRefreshFailed, startupAttempted;
    private DateTimeOffset lastAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset lastLocationAttempt = DateTimeOffset.MinValue;
    public event Action<string>? StatusChanged;
    internal string TrayStatus => TrayStatusText.Format(status, DateTimeOffset.UtcNow, demo, statusRefreshFailed, client != null);
    internal void ShowStatusView() => views.SelectedIndex = 0;

    public MainWindow(bool demo, IPreferencesStore? preferencesStore = null, ISecretStore? secretStore = null, Func<BydClientOptions, BydClient>? createClient = null, Func<BydMarket, ISecretStore>? createSecretStore = null)
    {
        this.preferencesStore = preferencesStore ?? PreferencesStore.Create();
        this.createSecretStore = createSecretStore ?? (secretStore != null ? _ => secretStore : market => SecretStore.Create(market));
        this.createClient = createClient ?? (options => new BydClient(options));
        preferences = demo ? new() : this.preferencesStore.Load();
        accountCountry.SelectedItem = BydMarkets.ForCountry(preferences.CountryCode);
        this.secretStore = this.createSecretStore((BydMarket)accountCountry.SelectedItem!);
        autoSignIn.IsChecked = preferences.AutoSignInOnStartup;
        this.demo = demo; Title = demo ? "BYD Tray — demo (no network)" : "BYD Tray"; Width = 740; Height = 750; MinWidth = 600; MinHeight = 550;
        login = Button("Sign in — read only", Login);
        login.Name = "SignIn";
        refresh = Button("Refresh status", Refresh);
        load = Button("Use saved account", LoadAccount);
        forget = Button("Forget saved account", ForgetAccount);
        showLocation = Button("Vehicle location map", ShowLocation);
        checkRemoteSetup = Button("Check remote setup", CheckRemoteSetup);
        foreach (var item in new[] { ("Lock", VehicleCommand.Lock), ("Unlock", VehicleCommand.Unlock), ("Start climate", VehicleCommand.StartClimate), ("Stop climate", VehicleCommand.StopClimate) })
        {
            var command = item.Item2; var commandButton = Button(item.Item1, () => SendCommand(command)); commandButton.Margin = new Thickness(0, 0, 8, 8); commandPanel.Children.Add(commandButton);
        }
        var accountSettings = new StackPanel { Spacing = 10, Children = { Label("Account country / server region"), accountCountry, Label("Choose the country where this international BYD account was registered. Changing it signs out and clears the account fields."), email, password, remember, autoSignIn,
            Row(login, load, forget), automatic, Label("Sign-in and status reads do not operate locks or climate. A status refresh can wake the vehicle.") } };
        var remoteSettings = new StackPanel { Spacing = 10, Children = { controls, checkRemoteSetup, remoteSetupHint, commandPanel, commandHint } };
        inputs.Children.Add(new Expander { Name = "AccountSettings", Header = "Account & startup", IsExpanded = !preferences.AutoSignInOnStartup, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = accountSettings });
        inputs.Children.Add(new Expander { Name = "RemoteControls", Header = "Remote controls", IsExpanded = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = remoteSettings });
        var dashboard = new StackPanel { Spacing = 18, Margin = new Thickness(0, 20), Children =
        {
            new Border { Padding = new Thickness(26), CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromArgb(30, 80, 150, 180)), Child = new StackPanel { Spacing = 18, Children = { summary, overview, freshness } } },
            Label("Account settings, detailed readings and vehicle controls are in Settings & advanced.")
        } };
        var advanced = new StackPanel { Spacing = 20, Margin = new Thickness(0, 16), Children = { inputs, Section("Detailed vehicle readings", detail, vehicleReadings), BuildAdvanced() } };
        views.ItemsSource = new[] { new TabItem { Header = "Status", Content = new ScrollViewer { Content = dashboard } }, new TabItem { Header = "Settings & advanced", Content = new ScrollViewer { Name = "AdvancedScroll", Content = advanced } } };
        views.SelectedIndex = 0;
        var layout = new DockPanel { Margin = new Thickness(24), LastChildFill = true };
        var header = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 16), Children = { new TextBlock { Text = "BYD Tray", FontSize = 30, FontWeight = FontWeight.SemiBold }, Label(demo ? "DEMO • Synthetic values • No BYD connection" : "International BYD accounts • Unofficial client"), vehicles } };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var footer = new StackPanel { Spacing = 12, Margin = new Thickness(0, 14, 0, 0), Children = { new WrapPanel { Orientation = Orientation.Horizontal, Children = { refresh, showLocation } }, message } };
        refresh.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(views); Content = layout;
        vehicles.SelectionChanged += (_, _) => { status = null; statusRefreshFailed = false; ResetAdvanced(); UpdateStatus(); UpdateEnabled(); };
        automatic.IsCheckedChanged += (_, _) => { if (automatic.IsChecked == true && !demo) polling.Start(); else polling.Stop(); };
        autoSignIn.IsCheckedChanged += (_, _) => SaveStartupPreference();
        accountCountry.SelectionChanged += (_, _) => ChangeCountry();
        controls.IsCheckedChanged += async (_, _) => await ChangeRemoteControls();
        polling.Tick += async (_, _) => await Refresh();
        age.Tick += (_, _) => UpdateStatus(); age.Start();
        if (demo)
        {
            vehicles.ItemsSource = new[] { new Vehicle("SYNTHETIC000000001", "Atto 3 (demo)", 0) }; vehicles.SelectedIndex = 0;
            status = new(78, 325, "Not charging", "Locked", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, true)
            { Openings = new(0, 0, true, true, false, false, false) };
            UpdateStatus(); message.Text = "Preview only. Sign-in and controls are disabled.";
            SetAdvancedDemo();
        }
        UpdateStatus(); UpdateEnabled();
    }
    internal async Task StartAsync()
    {
        if (startupAttempted || stopped) return;
        startupAttempted = true;
        if (demo || autoSignIn.IsChecked != true) return;
        // The startup checkbox authorizes one read-only login/status attempt, never controls or GPS.
        controls.IsChecked = false; automatic.IsChecked = false; remember.IsChecked = false;
        if (!await TryLoadAccount()) { UpdateStatus(); return; }
        if (stopped || string.IsNullOrEmpty(email.Text) || string.IsNullOrEmpty(password.Text)) { UpdateStatus(); return; }
        await Login();
        if (stopped || client == null || vehicles.SelectedItem is not Vehicle) return;
        await Refresh();
    }
    private void SaveStartupPreference()
    {
        if (demo || restoringPreference) return;
        var updated = preferences with { AutoSignInOnStartup = autoSignIn.IsChecked == true };
        try { preferencesStore.Save(updated); preferences = updated; }
        catch
        {
            restoringPreference = true; autoSignIn.IsChecked = preferences.AutoSignInOnStartup; restoringPreference = false;
            message.Text = "The startup preference could not be saved. The checkbox has been restored."; Audit.Write(AuditEvent.PreferencesFailed);
        }
    }
    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text };
        button.Click += async (_, _) => { try { await action(); } catch { Audit.Write(AuditEvent.UnexpectedFailure); } };
        return button;
    }
    private void UpdateEnabled()
    {
        login.Content = controls.IsChecked == true ? "Sign in — with controls" : "Sign in — read only";
        login.IsEnabled = load.IsEnabled = forget.IsEnabled = !busy && !demo;
        email.IsEnabled = password.IsEnabled = remember.IsEnabled = controls.IsEnabled = !busy && !demo;
        autoSignIn.IsEnabled = !busy && !demo;
        accountCountry.IsEnabled = !busy && !demo;
        vehicles.IsEnabled = !busy; refresh.IsEnabled = !busy && !demo && client != null && vehicles.SelectedItem is Vehicle;
        showLocation.IsEnabled = !busy && vehicles.SelectedItem is Vehicle && (demo || client != null);
        commandPanel.IsEnabled = !busy && !demo && client?.RemoteControlsEnabled == true && controls.IsChecked == true && status != null && vehicles.SelectedItem is Vehicle;
        checkRemoteSetup.IsEnabled = !busy && !demo && client?.RemoteControlsEnabled == true && controls.IsChecked == true && vehicles.SelectedItem is Vehicle;
        remoteSetupHint.Text = demo ? "Setup checks are disabled in the demo."
            : busy ? "Wait for the current operation to finish."
            : client == null ? "Sign in to check remote setup."
            : vehicles.SelectedItem is not Vehicle ? "Choose your vehicle above to check remote setup."
            : controls.IsChecked != true ? "Enable remote controls for this session to check remote setup. No new sign-in is needed."
            : !client.RemoteControlsEnabled ? "Enable remote controls for this session to check remote setup."
            : "Ready: Check remote setup tests lock support and PIN access without operating the car.";
        commandHint.Text = !checkRemoteSetup.IsEnabled ? "Vehicle commands need the session and vehicle steps above."
            : status == null ? "Refresh status to enable Lock, Unlock and climate commands. Signing in again clears previous readings."
            : "Vehicle commands are available. Each needs its own confirmation and PIN.";
        UpdateAdvancedEnabled();
    }
    private async Task ChangeRemoteControls()
    {
        if (restoringControls || stopped || demo) return;
        if (client == null) { UpdateEnabled(); return; }
        if (busy) { RestoreControlPermission(); return; }
        busy = true; UpdateEnabled();
        try
        {
            await client.SetRemoteControlsEnabledAsync(controls.IsChecked == true, lifetime.Token);
            message.Text = client.RemoteControlsEnabled
                ? "Remote controls enabled for this session. Each command needs its own confirmation and PIN. No new sign-in or vehicle request was sent."
                : "Remote controls disabled. Previous command confirmations are no longer valid.";
        }
        catch (Exception ex) { RestoreControlPermission(); message.Text = SafeMessage(ex); }
        finally { busy = false; UpdateEnabled(); }
    }
    private void RestoreControlPermission()
    {
        restoringControls = true;
        try { controls.IsChecked = client?.RemoteControlsEnabled == true; }
        finally { restoringControls = false; }
    }
    private async Task Login()
    {
        if (busy || demo) return;
        var account = new Account(email.Text?.Trim() ?? "", password.Text ?? "");
        if (string.IsNullOrEmpty(account.Username) || string.IsNullOrEmpty(account.Password)) { message.Text = "Enter your BYD account email and password locally in this window."; return; }
        var previousVin = (vehicles.SelectedItem as Vehicle)?.Vin;
        busy = true; UpdateEnabled(); password.Text = ""; status = null; statusRefreshFailed = false; automatic.IsChecked = false; vehicles.ItemsSource = null; client?.Dispose();
        client = createClient(new BydClientOptions { Market = (BydMarket)accountCountry.SelectedItem!, TimeZone = IanaTimeZone(TimeZoneInfo.Local.Id), EnableRemoteControls = controls.IsChecked == true });
        message.Text = "Signing in and reading the vehicle list…";
        try
        {
            await client.LoginAsync(account.Username, account.Password, lifetime.Token);
            var list = await client.GetVehiclesAsync(lifetime.Token);
            vehicles.ItemsSource = list;
            vehicles.SelectedItem = previousVin != null ? list.FirstOrDefault(vehicle => vehicle.Vin == previousVin) : list.Count == 1 ? list[0] : null;
            message.Text = list.Count == 0 ? "Sign-in succeeded, but this account has no vehicles." : "Sign-in succeeded. Choose the vehicle, then refresh status.";
            Audit.Write(AuditEvent.LoginSucceeded);
            if (remember.IsChecked == true)
            {
                try { await secretStore.Save(account); Audit.Write(AuditEvent.WalletSaved); }
                catch { message.Text += " The wallet could not save your account; it has not been saved to a file."; Audit.Write(AuditEvent.WalletFailed); }
            }
        }
        catch (Exception ex)
        {
            message.Text = SafeMessage(ex); Audit.Write(AuditEvent.LoginFailed); client.Dispose(); client = null;
        }
        finally { busy = false; account = null!; UpdateEnabled(); UpdateStatus(); }
    }
    public async Task Refresh()
    {
        if (busy || demo || client == null || vehicles.SelectedItem is not Vehicle vehicle) return;
        if (DateTimeOffset.UtcNow - lastAttempt < TimeSpan.FromSeconds(60)) { message.Text = "Please wait at least one minute between status requests."; return; }
        busy = true; UpdateEnabled(); lastAttempt = DateTimeOffset.UtcNow;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        message.Text = "Waiting for vehicle readings…";
        try
        {
            status = await client.GetStatusAsync(vehicle.Vin, lifetime.Token);
            statusRefreshFailed = false;
            message.Text = $"Status retrieved in {elapsed.Elapsed.TotalSeconds:0.0} seconds.";
            if (status.Source == TelemetrySource.LivePush) Audit.Write(AuditEvent.StatusFromLivePush);
            if (status.Source == TelemetrySource.Http) Audit.Write(AuditEvent.StatusFromHttp);
            Audit.Write(AuditEvent.StatusSucceeded);
        }
        catch (Exception ex)
        {
            statusRefreshFailed = true; message.Text = SafeMessage(ex) + " Automatic refresh is paused."; automatic.IsChecked = false; Audit.Write(AuditEvent.StatusFailed);
        }
        finally { busy = false; UpdateStatus(); UpdateEnabled(); }
    }
    private void UpdateStatus()
    {
        if (status == null) { summary.Text = "No vehicle data yet"; detail.Text = "Choose a vehicle and refresh its status."; overview.Text = client == null ? "Sign in from Settings & advanced." : "Choose your vehicle, then refresh status."; freshness.Text = "No readings have been retrieved for this selection."; UpdateDetailReadings(); StatusChanged?.Invoke(TrayStatus); return; }
        var stale = status.IsStale(DateTimeOffset.UtcNow);
        var battery = status.BatteryPercent is { } b ? $"{b:0.#}%" : "Unknown battery";
        var charging = TrayStatusText.ChargingLabel(status.ChargingState);
        var locks = TrayStatusText.LockLabel(status.LockState);
        summary.Text = $"{battery}   ·   {(status.RangeKm is { } km ? $"{km:0} km" : "Unknown range")}";
        overview.Text = $"{charging} · Locks: {locks}\n{TrayStatusText.OpeningDetails(status.Openings)}";
        freshness.Text = $"Retrieved {status.RetrievedAt.ToLocalTime():t} · {(stale ? "Stale or freshness unverified" : "Recent vehicle timestamp")}{(statusRefreshFailed ? " · Last refresh failed" : "")}";
        detail.Text = $"{charging} · Locks: {locks}\n{TrayStatusText.OpeningDetails(status.Openings)}\nFetched: {status.RetrievedAt.ToLocalTime():g}\nVehicle timestamp: {(status.VehicleTime is { } time ? time.ToLocalTime().ToString("g") : "not supplied")}\n{(stale ? "STALE OR UNVERIFIED — freshness is not confirmed" : "Recent vehicle timestamp")}{(statusRefreshFailed ? "\nLast refresh failed; these are previous readings." : "")}";
        StatusChanged?.Invoke(TrayStatus);
        UpdateDetailReadings();
    }
    private async Task LoadAccount() { await TryLoadAccount(); }
    private async Task<bool> TryLoadAccount()
    {
        if (busy || demo || stopped) return false; busy = true; UpdateEnabled();
        try
        {
            var saved = await secretStore.Read();
            if (saved == null) { message.Text = "No account has been saved for BYD Tray."; return false; }
            email.Text = saved.Username; password.Text = saved.Password;
            message.Text = "Account loaded from the system wallet. Press Sign in when ready.";
            return true;
        }
        catch { message.Text = "Could not read the system wallet. Unlock it or enter the account manually."; Audit.Write(AuditEvent.WalletFailed); return false; }
        finally { busy = false; UpdateEnabled(); }
    }
    private async Task ShowLocation()
    {
        if (busy || vehicles.SelectedItem is not Vehicle vehicle || !demo && client == null) return;
        if (!demo && DateTimeOffset.UtcNow - lastLocationAttempt < TimeSpan.FromMinutes(1)) { message.Text = "Please wait at least one minute between location requests."; return; }
        busy = true; UpdateEnabled();
        try
        {
            locationWindow = new LocationWindow(vehicle.DisplayName, demo ? null : token => { lastLocationAttempt = DateTimeOffset.UtcNow; return client!.GetLocationAsync(vehicle.Vin, token); }, demo);
            await locationWindow.ShowDialog(this);
        }
        finally { locationWindow = null; busy = false; UpdateEnabled(); }
    }
    private async Task ForgetAccount()
    {
        if (busy || demo) return; busy = true; UpdateEnabled();
        try { await secretStore.Delete(); password.Text = ""; remember.IsChecked = false; message.Text = "Saved BYD Tray account removed from the system wallet."; Audit.Write(AuditEvent.WalletDeleted); }
        catch { message.Text = "Could not remove the account from the system wallet."; Audit.Write(AuditEvent.WalletFailed); }
        finally { busy = false; UpdateEnabled(); }
    }
    private async Task CheckRemoteSetup()
    {
        if (busy || demo || client?.RemoteControlsEnabled != true || controls.IsChecked != true || vehicles.SelectedItem is not Vehicle vehicle) return;
        busy = true; UpdateEnabled();
        try
        {
            var pin = await new RemoteSetupDialog(vehicle).ShowDialog<string?>(this);
            if (pin == null) return;
            message.Text = "Checking lock support and PIN access only…";
            try { await client.CheckCommandAccessAsync(vehicle.Vin, VehicleCommand.Lock, pin, lifetime.Token); }
            finally { pin = null; }
            message.Text = "Lock support and PIN access checks passed. No vehicle command was sent. This does not confirm that locking works.";
            Audit.Write(AuditEvent.RemoteSetupPassed);
        }
        catch (Exception ex)
        {
            message.Text = "Setup check only; no vehicle command was sent. " + SafeMessage(ex);
            Audit.Write(ex is BydException { CommandStage: CommandStage.CapabilityCheck } ? AuditEvent.RemoteSetupSupportFailed
                : ex is BydException { CommandStage: CommandStage.PinVerification } ? AuditEvent.RemoteSetupPinFailed : AuditEvent.RemoteSetupFailed);
        }
        finally { busy = false; UpdateEnabled(); }
    }
    private async Task SendCommand(VehicleCommand command, CommandSettings? settings = null)
    {
        if (busy || demo || client?.RemoteControlsEnabled != true || controls.IsChecked != true || status == null || vehicles.SelectedItem is not Vehicle vehicle) return;
        busy = true; UpdateEnabled();
        try
        {
            var dialog = new CommandDialog(vehicle, command, settings);
            var confirmation = await dialog.ShowDialog<CommandInput?>(this);
            if (confirmation == null) return;
            var approval = client.ConfirmCommand(vehicle.Vin, command, confirmation.Settings ?? new CommandSettings { TemperatureC = confirmation.Temperature, DurationMinutes = confirmation.Duration });
            message.Text = "Sending the confirmed command once…"; Audit.Write(AuditEvent.CommandRequested);
            CommandOutcome result;
            try { result = await client.ExecuteAsync(approval, confirmation.Pin, lifetime.Token); }
            finally { confirmation = null; }
            message.Text = result switch { CommandOutcome.Confirmed => command is VehicleCommand.StartCharging or VehicleCommand.StopCharging or VehicleCommand.SaveChargingSchedule
                ? "BYD confirmed the command. Read charging details again to check the reported result."
                : command is VehicleCommand.ScheduleClimate or VehicleCommand.ChangeSeatClimate ? "BYD confirmed the command. Read climate & bookings again to check the reported result."
                : "BYD confirmed the command. Refresh status or check the official app to verify the vehicle.", CommandOutcome.Failed => "BYD reported command failure. No automatic retry was sent.", _ => "Outcome unknown. Check the official app or the vehicle before trying again." };
            Audit.Write(result switch { CommandOutcome.Confirmed => AuditEvent.CommandConfirmed, CommandOutcome.Failed => AuditEvent.CommandFailed, _ => AuditEvent.CommandUnknown });
        }
        catch (Exception ex) { message.Text = CommandFailureMessage(ex); Audit.Write(CommandFailureEvent(ex)); }
        finally { busy = false; UpdateEnabled(); }
    }
    internal static string CommandFailureMessage(Exception ex) => ex is BydException ? SafeMessage(ex)
        : "The command flow failed. Check the official app or vehicle before trying again. Sensitive diagnostic details were omitted.";
    internal static AuditEvent CommandFailureEvent(Exception ex) => ex is BydException { CommandStage: CommandStage.CapabilityCheck or CommandStage.PinVerification }
        ? AuditEvent.CommandNotSent : AuditEvent.CommandUnknown;
    private static string SafeMessage(Exception ex) => ex is BydException byd ? byd.Message + (byd.ApiCode == null ? "" : $" (BYD code {byd.ApiCode})") : ex is OperationCanceledException ? "Operation cancelled." : "The operation failed. Sensitive diagnostic details were omitted.";
    public void Stop() { if (stopped) return; stopped = true; locationWindow?.Close(); polling.Stop(); age.Stop(); lifetime.Cancel(); client?.Dispose(); password.Text = ""; }
}

internal sealed record CommandInput(string Pin, int Temperature, int Duration)
{
    internal CommandSettings? Settings { get; init; }
    public override string ToString() => "Command input (PIN redacted)";
}
internal sealed class RemoteSetupDialog : Window
{
    internal RemoteSetupDialog(Vehicle vehicle)
    {
        Title = "Check remote setup"; Width = 440; Height = 360; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var pin = new TextBox { PlaceholderText = "Six-digit control PIN", PasswordChar = '●', MaxLength = 6 };
        var check = new Button { Content = "Check PIN only", IsEnabled = false };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        pin.TextChanged += (_, _) => check.IsEnabled = pin.Text is { Length: 6 } text && text.All(char.IsAsciiDigit);
        cancel.Click += (_, _) => Close(null);
        check.Click += (_, _) =>
        {
            if (pin.Text is not { Length: 6 } text || !text.All(char.IsAsciiDigit)) return;
            pin.Text = ""; Close(text);
        };
        Closed += (_, _) => pin.Text = "";
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 14, Children =
            {
                new TextBlock { Text = $"Check setup — {vehicle.DisplayName}", FontSize = 20, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Checks lock support and PIN access only. No vehicle command will be sent. Enter the PIN once; a rejected PIN may restrict remote access. The PIN is never saved.", TextWrapping = TextWrapping.Wrap },
                pin, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { cancel, check } }
            }
        };
    }
}
internal sealed class CommandDialog : Window
{
    internal CommandDialog(Vehicle vehicle, VehicleCommand command, CommandSettings? settings = null)
    {
        Title = "Confirm vehicle command"; Width = 430; Height = command == VehicleCommand.StartClimate ? 520 : 370; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var pin = new TextBox { PlaceholderText = "Six-digit control PIN", PasswordChar = '●', MaxLength = 6 };
        var temperature = new NumericUpDown { Minimum = 15, Maximum = 31, Value = 21, Increment = 1 };
        var duration = new ComboBox { ItemsSource = new[] { 10, 15, 20, 25, 30 }, SelectedIndex = 0 };
        var accept = new Button { Content = "Send this command", IsEnabled = false };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        pin.TextChanged += (_, _) => accept.IsEnabled = pin.Text is { Length: 6 } text && text.All(char.IsAsciiDigit);
        cancel.Click += (_, _) => Close(null);
        accept.Click += (_, _) =>
        {
            if (pin.Text is not { Length: 6 } text || !text.All(char.IsAsciiDigit)) return;
            var result = new CommandInput(text, (int)(temperature.Value ?? 21), (int)(duration.SelectedItem ?? 10)) { Settings = settings }; pin.Text = ""; Close(result);
        };
        Closed += (_, _) => pin.Text = "";
        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children ={
            new TextBlock { Text=$"{CommandPresentation.Name(command)} — {vehicle.DisplayName}",FontSize=20,TextWrapping=TextWrapping.Wrap },
            new TextBlock { Text="This will operate the selected vehicle. Confirm that it is safe to do so. The command will be sent once.",TextWrapping=TextWrapping.Wrap }
        }
        };
        if (settings != null) { panel.Children.Add(new TextBlock { Text = CommandPresentation.Details(command, settings), TextWrapping = TextWrapping.Wrap }); Height = 480; }
        if (command == VehicleCommand.StartClimate && settings == null) { panel.Children.Add(new TextBlock { Text = "Temperature °C / duration minutes" }); panel.Children.Add(temperature); panel.Children.Add(duration); }
        panel.Children.Add(pin); panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { cancel, accept } });
        Content = new ScrollViewer { Content = panel };
    }
}
