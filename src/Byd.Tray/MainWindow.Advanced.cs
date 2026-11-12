using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Byd.Client;

namespace Byd.Tray;

internal sealed partial class MainWindow
{
    private VehicleCapabilities? capabilities;
    private VehicleFeatureCatalog? featureCatalog;
    private ChargingSnapshot? chargingSnapshot;
    private ClimateSnapshot? climateSnapshot;
    private EnergySnapshot? energySnapshot;
    private readonly TextBlock vehicleReadings = Label("Refresh status for temperature, odometer and tyre readings.");
    private readonly TextBlock featureList = Label("Load supported features to check availability for this vehicle.");
    private readonly TextBlock chargingReadings = Label("Charging details have not been read.");
    private readonly TextBlock climateReadings = Label("Climate details have not been read.");
    private readonly TextBlock energyReadings = Label("Energy statistics have not been read.");
    private readonly StackPanel energyBars = new() { Spacing = 7 };
    private readonly List<Button> advancedReadButtons = [];
    private readonly List<(Button Button, VehicleCommand Command, Func<CommandSettings> Settings)> extraCommands = [];
    private readonly Dictionary<string, DateTimeOffset> lastAdvancedRead = [];
    private readonly TextBox chargeStart = new() { Text = "00:00", PlaceholderText = "HH:mm", Width = 90 };
    private readonly TextBox chargeEnd = new() { Text = "06:00", PlaceholderText = "HH:mm", Width = 90 };
    private readonly CheckBox untilFull = new() { Content = "Until full" };
    private readonly CheckBox chargeEnabled = new() { Content = "Enable this charging schedule" };
    private readonly ComboBox chargeRepeat = new() { ItemsSource = Enum.GetValues<ChargeRepeat>(), SelectedIndex = 1, MinWidth = 140 };
    private readonly ComboBox bookingOperation = new() { ItemsSource = Enum.GetValues<BookingOperation>(), SelectedIndex = 0, MinWidth = 140 };
    private readonly ComboBox bookingChoice = new() { PlaceholderText = "Read and select a reported booking", MinWidth = 250 };
    private readonly DatePicker departureDate = new() { SelectedDate = DateTimeOffset.Now.Date.AddDays(1) };
    private readonly TextBox departureTime = new() { Text = "07:00", PlaceholderText = "HH:mm", Width = 90 };
    private readonly NumericUpDown bookingTemperature = new() { Minimum = 15, Maximum = 31, Value = 21, Increment = 1, Width = 100 };
    private readonly ComboBox bookingDuration = new() { ItemsSource = new[] { 10, 15, 20, 25, 30 }, SelectedIndex = 0, Width = 100 };
    private readonly ComboBox seatChoice = new() { ItemsSource = Enum.GetValues<SeatFeature>().Select(s => new SeatChoice(s)).ToArray(), SelectedIndex = 0, MinWidth = 200 };
    private readonly ComboBox comfortChoice = new() { ItemsSource = Enum.GetValues<ComfortLevel>(), SelectedIndex = 0, MinWidth = 100 };

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static WrapPanel Row(params Control[] children)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children) { child.Margin = new Thickness(0, 0, 10, 10); row.Children.Add(child); }
        return row;
    }
    private static Expander Section(string title, params Control[] children)
    {
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(4, 12) };
        foreach (var child in children) content.Children.Add(child);
        return new() { Header = title, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = content };
    }
    private Control BuildAdvanced()
    {
        Button Read(string label, string key, Func<string, Task> action)
        {
            var b = Button(label, () => ReadAdvanced(key, action)); advancedReadButtons.Add(b); return b;
        }
        Button Extra(VehicleCommand command, Func<CommandSettings>? settings = null)
        {
            settings ??= () => new();
            var factory = settings;
            var button = Button("Review " + CommandPresentation.Name(command).ToLowerInvariant(), async () =>
            {
                if (demo || busy || capabilities == null) return;
                CommandSettings selected;
                try { selected = factory(); }
                catch { message.Text = "Check the schedule times, selected booking and comfort settings before reviewing."; return; }
                if (!capabilities.Supports(command, selected)) { message.Text = "This feature is not reported as available for this vehicle."; return; }
                await SendCommand(command, selected);
            });
            extraCommands.Add((button, command, factory)); return button;
        }
        var features = Read("Load supported features", "features", async vin => { featureCatalog = await client!.GetVehicleFeaturesAsync(vin, lifetime.Token); capabilities = featureCatalog.Capabilities; UpdateFeatureList(); });
        var readCharging = Read("Read charging details", "charging", async vin => { chargingSnapshot = await client!.GetChargingAsync(vin, lifetime.Token); UpdateChargingReadings(); });
        var readClimate = Read("Read climate & bookings", "climate", async vin =>
        {
            climateSnapshot = await client!.GetClimateAsync(vin, lifetime.Token); UpdateClimateReadings();
            var bookings = await client.GetClimateBookingsAsync(vin, lifetime.Token);
            bookingChoice.ItemsSource = bookings.Items.Select(b => new BookingChoice(b)).ToArray();
            if (bookings.Items.Count == 0) message.Text = "No climate bookings were reported. BYD can omit existing bookings; this does not prove none are set.";
        });
        var readEnergy = Read("Read energy statistics", "energy", async vin => { energySnapshot = await client!.GetEnergyAsync(vin, lifetime.Token); UpdateEnergyReadings(); });
        seatChoice.SelectionChanged += (_, _) =>
        {
            comfortChoice.ItemsSource = (seatChoice.SelectedItem as SeatChoice)?.Value == SeatFeature.SteeringWheelHeating ? new[] { ComfortLevel.Off, ComfortLevel.High } : Enum.GetValues<ComfortLevel>();
            comfortChoice.SelectedIndex = 0; UpdateAdvancedEnabled();
        };
        comfortChoice.SelectionChanged += (_, _) => UpdateAdvancedEnabled();
        foreach (var editor in new Control[] { chargeStart, chargeEnd, untilFull, chargeEnabled, chargeRepeat, bookingOperation, departureDate, departureTime, bookingTemperature, bookingDuration })
            editor.PropertyChanged += (_, change) => { if (change.Property.Name is "Text" or "IsChecked" or "SelectedItem" or "SelectedDate" or "Value") UpdateAdvancedEnabled(); };
        bookingChoice.SelectionChanged += (_, _) =>
        {
            if (bookingChoice.SelectedItem is not BookingChoice choice) return;
            if (choice.Value.Departure is { } time) { departureDate.SelectedDate = time.ToLocalTime(); departureTime.Text = time.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture); }
            if (choice.Value.TemperatureC is >= 15 and <= 31) bookingTemperature.Value = (decimal)choice.Value.TemperatureC.Value;
            if (choice.Value.DurationMinutes != null) bookingDuration.SelectedItem = choice.Value.DurationMinutes.Value;
        };
        var result = new StackPanel { Spacing = 14, Children =
        {
            Heading("Advanced features"), features, Section("Reported feature availability", featureList),
            Label("Extra controls require reported support, session opt-in and a status refresh. Each review opens a separate confirmation and PIN dialog."),
            Section("Charging & schedules", readCharging, chargingReadings,
            Row(Extra(VehicleCommand.StartCharging), Extra(VehicleCommand.StopCharging)),
            Label("Charging window · vehicle local time · overnight windows are allowed"), Row(Label("From"), chargeStart, Label("To"), chargeEnd, untilFull, chargeRepeat), chargeEnabled,
            Extra(VehicleCommand.SaveChargingSchedule, () => new() { ChargeStart = ParseTime(chargeStart), ChargeEnd = untilFull.IsChecked == true ? default : ParseTime(chargeEnd), ChargeUntilFull = untilFull.IsChecked == true, Enabled = chargeEnabled.IsChecked == true, Repeat = (ChargeRepeat)(chargeRepeat.SelectedItem ?? ChargeRepeat.Daily) })),
            Section("Climate & departure", readClimate, climateReadings,
            Row(bookingOperation, bookingChoice), Label("Departure · this computer's local time"), Row(departureDate, departureTime), Row(Label("Temperature °C"), bookingTemperature, Label("Minutes"), bookingDuration),
            Extra(VehicleCommand.ScheduleClimate, BuildBookingSettings)),
            Section("Windows & boot", Row(Extra(VehicleCommand.CloseWindows), Extra(VehicleCommand.VentWindows), Extra(VehicleCommand.OpenBoot), Extra(VehicleCommand.CloseBoot)),
            Label("Window venting is enabled only when the vehicle reports both window support and a venting flag. Powered boot controls also require reported support.")),
            Section("Seat & battery comfort", Label("Seat updates preserve other reported comfort settings. If another supported setting is unknown, read climate details before trying again."),
            Row(seatChoice, comfortChoice), Extra(VehicleCommand.ChangeSeatClimate, BuildSeatSettings),
            Row(Extra(VehicleCommand.BatteryHeating, () => new() { Enabled = true }), Extra(VehicleCommand.BatteryHeating, () => new() { Enabled = false }))),
            Section("Energy use", readEnergy, energyReadings, energyBars),
            Label("Closing this window keeps the tray running. Use the tray menu to quit. PINs are never saved.")
        } };
        extraCommands.Where(c => c.Command == VehicleCommand.BatteryHeating).First().Button.Content = "Review battery heating on";
        extraCommands.Where(c => c.Command == VehicleCommand.BatteryHeating).Last().Button.Content = "Review battery heating off";
        return result;
    }
    private static TimeOnly ParseTime(TextBox text) => TimeOnly.TryParseExact(text.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : throw new ArgumentException("Enter HH:mm.");
    private CommandSettings BuildBookingSettings()
    {
        var operation = (BookingOperation)(bookingOperation.SelectedItem ?? BookingOperation.Create);
        var selected = (bookingChoice.SelectedItem as BookingChoice)?.Value;
        if (operation != BookingOperation.Create && selected == null) throw new ArgumentException("Select a booking.");
        DateTimeOffset? departure = null;
        if (operation != BookingOperation.Remove)
        {
            var date = departureDate.SelectedDate ?? throw new ArgumentException("Choose a date.");
            var local = DateTime.SpecifyKind(date.Date + ParseTime(departureTime).ToTimeSpan(), DateTimeKind.Unspecified);
            if (TimeZoneInfo.Local.IsInvalidTime(local)) throw new ArgumentException("Invalid local time.");
            departure = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            if (departure < DateTimeOffset.Now.AddMinutes(2) || departure > DateTimeOffset.Now.AddDays(30)) throw new ArgumentException("Choose a future departure.");
        }
        return new() { BookingOperation = operation, BookingId = operation == BookingOperation.Create ? null : selected!.Id, Departure = departure ?? selected?.Departure, TemperatureC = (int)(bookingTemperature.Value ?? 21), DurationMinutes = (int)(bookingDuration.SelectedItem ?? 10) };
    }
    private CommandSettings BuildSeatSettings() => new() { Seat = (seatChoice.SelectedItem as SeatChoice)?.Value ?? SeatFeature.DriverHeating, Level = (ComfortLevel)(comfortChoice.SelectedItem ?? ComfortLevel.Off) };
    private async Task ReadAdvanced(string key, Func<string, Task> action)
    {
        if (busy || demo || client == null || vehicles.SelectedItem is not Vehicle vehicle) return;
        if (lastAdvancedRead.TryGetValue(key, out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromMinutes(1)) { message.Text = "Please wait one minute between reads of the same advanced section."; return; }
        busy = true; lastAdvancedRead[key] = DateTimeOffset.UtcNow; UpdateEnabled(); message.Text = "Reading the selected vehicle's advanced information…";
        try { await action(vehicle.Vin); if (key != "climate" || bookingChoice.ItemCount > 0) message.Text = "Advanced information received. Readings may be unavailable or older than the vehicle's current state."; Audit.Write(AuditEvent.AdvancedReadSucceeded); }
        catch (Exception ex) { message.Text = SafeMessage(ex); Audit.Write(AuditEvent.AdvancedReadFailed); }
        finally { busy = false; UpdateEnabled(); }
    }
    private void UpdateAdvancedEnabled()
    {
        foreach (var editor in new Control[] { chargeStart, chargeEnd, untilFull, chargeEnabled, chargeRepeat, bookingOperation, bookingChoice, departureDate, departureTime, bookingTemperature, bookingDuration, seatChoice, comfortChoice }) editor.IsEnabled = !busy && !demo;
        chargeEnd.IsEnabled = !busy && !demo && untilFull.IsChecked != true;
        var readReady = !busy && !demo && client != null && vehicles.SelectedItem is Vehicle;
        foreach (var button in advancedReadButtons) button.IsEnabled = readReady;
        foreach (var (button, command, factory) in extraCommands)
        {
            var supported = false;
            try { supported = capabilities?.Supports(command, factory()) == true; } catch { }
            button.IsEnabled = readReady && client?.RemoteControlsEnabled == true && controls.IsChecked == true && status != null && supported;
            ToolTip.SetTip(button, capabilities == null ? "Load supported features first." : !supported ? "Not reported as supported, or review settings are incomplete." : status == null ? "Refresh status first." : "Requires session control opt-in, confirmation and PIN.");
        }
    }
    private void ResetAdvanced()
    {
        capabilities = null; featureCatalog = null; chargingSnapshot = null; climateSnapshot = null; energySnapshot = null; bookingChoice.ItemsSource = Array.Empty<BookingChoice>();
        UpdateFeatureList(); UpdateChargingReadings(); UpdateClimateReadings(); UpdateEnergyReadings();
    }
    private void UpdateFeatureList()
    {
        if (capabilities == null && !demo) { featureList.Text = "Load supported features to check this vehicle. No extra control is enabled until support is reported."; return; }
        if (featureCatalog != null)
        {
            featureList.Text = string.Join("\n", featureCatalog.Features.Select(f => $"{(f.Seat is { } seat ? CommandPresentation.SeatName(seat) : f.Command is { } command ? CommandPresentation.Name(command) : f.Name)}: {f.Availability switch { BydFeatureAvailability.ReportedAvailable => "Reported available", BydFeatureAvailability.NotReported => "Not reported", _ => "Unknown; read to check" }}"));
            return;
        }
        featureList.Text = string.Join("\n", Enum.GetValues<VehicleCommand>().Where(c => c != VehicleCommand.ChangeSeatClimate).Select(c => $"{CommandPresentation.Name(c)}: {(demo ? "Available (synthetic)" : capabilities!.Supports(c) ? "Reported available" : "Not reported")}"))
            + "\n" + string.Join("\n", Enum.GetValues<SeatFeature>().Select(s => $"{CommandPresentation.SeatName(s)}: {(demo ? "Available (synthetic)" : capabilities!.SupportsSeat(s) ? "Reported available" : "Not reported")}"));
    }
    private static string Number(double? value, string suffix, string format = "0.#") => value is { } v ? v.ToString(format, CultureInfo.CurrentCulture) + suffix : "Unknown";
    private static string Flag(bool? value) => value switch { true => "Yes", false => "No", _ => "Unknown" };
    private void UpdateDetailReadings()
    {
        var d = status?.Details;
        vehicleReadings.Text = $"Cabin: {Number(climateSnapshot?.CabinTemperatureC ?? d?.CabinTemperatureC, " °C")} · Outside: {Number(climateSnapshot?.OutsideTemperatureC ?? d?.OutsideTemperatureC, " °C")}\nOdometer: {Number(d?.OdometerKm, " km")}\nTyres (kPa) · Front left: {Number(d?.FrontLeftTyreKpa, "")} · Front right: {Number(d?.FrontRightTyreKpa, "")}\nRear left: {Number(d?.RearLeftTyreKpa, "")} · Rear right: {Number(d?.RearRightTyreKpa, "")}";
    }
    private void UpdateChargingReadings()
    {
        if (chargingSnapshot is not { } c) { chargingReadings.Text = "Charging details have not been read."; return; }
        chargingReadings.Text = $"Battery: {Number(c.BatteryPercent, "%")} · Charging: {Flag(c.Charging)} · Connection reported: {Flag(c.Connected)}\nEstimated time to full: {(c.MinutesToFull is { } m ? $"{m / 60} h {m % 60} min" : "Unknown")}\nFetched: {c.RetrievedAt.ToLocalTime():g} · Vehicle timestamp: {(c.VehicleTime?.ToLocalTime().ToString("g") ?? "Unknown")}\n"
            + (c.Schedule is { } s ? $"Charging window: {s.Start?.ToString("HH:mm") ?? "Unknown"}–{(s.UntilFull ? "full" : s.End?.ToString("HH:mm") ?? "Unknown")} · {s.Repeat} · Enabled: {Flag(s.Enabled)}" : "No charging window reported")
            + (c.Journey is { } j ? $"\nDeparture schedule (read only): {j.ReadyAt?.ToString("HH:mm") ?? "Unknown"} · Off-peak {j.OffPeakStart?.ToString("HH:mm") ?? "Unknown"}–{j.OffPeakEnd?.ToString("HH:mm") ?? "Unknown"} · {j.Repeat} · Enabled: {Flag(j.Enabled)}" : "");
    }
    private void UpdateClimateReadings()
    {
        UpdateDetailReadings();
        climateReadings.Text = climateSnapshot is not { } c ? "Climate details have not been read." : $"Running: {Flag(c.Running)} · Cabin: {Number(c.CabinTemperatureC, " °C")} · Outside: {Number(c.OutsideTemperatureC, " °C")}\nFetched: {c.RetrievedAt.ToLocalTime():g}\n" + string.Join(" · ", Enum.GetValues<SeatFeature>().Select(s => CommandPresentation.SeatName(s) + ": " + (c.GetSeat(s)?.ToString() ?? "Unknown")));
    }
    private void UpdateEnergyReadings()
    {
        energyBars.Children.Clear();
        if (energySnapshot is not { } e) { energyReadings.Text = "Energy statistics have not been read."; return; }
        energyReadings.Text = $"Lifetime average: {Number(e.LifetimeKwhPer100Km, " kWh/100 km")} · Recent average: {Number(e.RecentKwhPer100Km, " kWh/100 km")}\nDistance: {Number(e.OdometerKm, " km")}\nReported use · Driving: {Number(e.DrivingShare, "%")} · Climate: {Number(e.ClimateShare, "%")} · Other: {Number(e.OtherShare, "%")}\nFetched: {e.RetrievedAt.ToLocalTime():g} · Vehicle timestamp: {e.VehicleTime?.ToLocalTime().ToString("g") ?? "Unknown"}\nSeven-day consumption · {e.Unit}";
        var max = Math.Max(1, e.SevenDayConsumption.Where(n => n.HasValue).Select(n => n!.Value).DefaultIfEmpty(1).Max());
        foreach (var pair in e.SevenDayConsumption.Select((value, index) => (value, index)))
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,80") };
            row.Children.Add(Label("Day " + (pair.index + 1)));
            var bar = new ProgressBar { Minimum = 0, Maximum = max, Value = pair.value ?? 0, Height = 14, Margin = new Thickness(0, 4, 12, 0) }; Grid.SetColumn(bar, 1); row.Children.Add(bar);
            var value = Label(Number(pair.value, "")); Grid.SetColumn(value, 2); row.Children.Add(value); energyBars.Children.Add(row);
        }
    }
    private void SetAdvancedDemo()
    {
        status = status! with { Details = new() { CabinTemperatureC = 27, OutsideTemperatureC = 23, OdometerKm = 12500, FrontLeftTyreKpa = 245, FrontRightTyreKpa = 247, RearLeftTyreKpa = 246, RearRightTyreKpa = 245 } };
        chargingSnapshot = new(78, false, true, 95, new(new(0, 0), new(6, 0), false, "Daily", true), null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Australia/Adelaide");
        climateSnapshot = new(27, 23, false, DateTimeOffset.UtcNow);
        energySnapshot = new(12500, 15.2, 14.7, Array.AsReadOnly<double?>([15.1, 14.2, 16.3, 14.7, 13.9, 15.5, 14.7]), "kWh/100 km", 76, 18, 6, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        UpdateFeatureList(); UpdateChargingReadings(); UpdateClimateReadings(); UpdateEnergyReadings();
    }
    private sealed record SeatChoice(SeatFeature Value) { public override string ToString() => CommandPresentation.SeatName(Value); }
    private sealed record BookingChoice(ClimateBooking Value) { public override string ToString() => $"{Value.Departure?.ToLocalTime().ToString("g") ?? "Time unknown"} · {Number(Value.TemperatureC, " °C")}"; }
}

internal static class CommandPresentation
{
    internal static string Name(VehicleCommand c) => c switch
    {
        VehicleCommand.Lock => "Lock", VehicleCommand.Unlock => "Unlock", VehicleCommand.StartClimate => "Start climate", VehicleCommand.StopClimate => "Stop climate", VehicleCommand.CloseWindows => "Close windows", VehicleCommand.VentWindows => "Vent windows", VehicleCommand.OpenBoot => "Open boot", VehicleCommand.CloseBoot => "Close boot", VehicleCommand.ScheduleClimate => "Climate booking", VehicleCommand.ChangeSeatClimate => "Seat comfort", VehicleCommand.BatteryHeating => "Battery heating", VehicleCommand.StartCharging => "Start charging", VehicleCommand.StopCharging => "Stop charging", VehicleCommand.SaveChargingSchedule => "Charging schedule", _ => "Vehicle command"
    };
    internal static string SeatName(SeatFeature s) => s switch { SeatFeature.DriverHeating => "Driver seat heating", SeatFeature.DriverVentilation => "Driver seat ventilation", SeatFeature.PassengerHeating => "Passenger seat heating", SeatFeature.PassengerVentilation => "Passenger seat ventilation", SeatFeature.SteeringWheelHeating => "Steering-wheel heating", _ => "Seat comfort" };
    internal static string Details(VehicleCommand c, CommandSettings s) => c switch
    {
        VehicleCommand.ScheduleClimate => $"{s.BookingOperation} selected climate booking\nDeparture: {s.Departure?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz") ?? "Not supplied"}\n" + (s.BookingOperation == BookingOperation.Remove ? "Remove this reported booking." : $"{s.TemperatureC} °C · {s.DurationMinutes} minutes\nThis booking can run after BYD Tray is closed."),
        VehicleCommand.SaveChargingSchedule => $"Replace charging window: {s.ChargeStart:HH:mm}–{(s.ChargeUntilFull ? "until full" : s.ChargeEnd.ToString("HH:mm"))}\nRepeat: {s.Repeat} · Enabled: {s.Enabled}\nTimes use the vehicle's local clock; an overnight window crosses midnight. The schedule remains configured after BYD Tray is closed.",
        VehicleCommand.ChangeSeatClimate => $"{SeatName(s.Seat)}: {s.Level}\nOther reported comfort settings will be preserved. Unknown supported settings will block this change.",
        VehicleCommand.BatteryHeating => "Battery heating: " + (s.Enabled ? "On" : "Off"),
        VehicleCommand.VentWindows => "Open the windows slightly for ventilation. The amount depends on the vehicle.",
        VehicleCommand.CloseWindows => "Close all windows.", VehicleCommand.OpenBoot => "Open the boot.", VehicleCommand.CloseBoot => "Close the boot.",
        VehicleCommand.StartCharging => "Start charging the selected vehicle.", VehicleCommand.StopCharging => "Stop charging the selected vehicle.", _ => Name(c)
    };
}
