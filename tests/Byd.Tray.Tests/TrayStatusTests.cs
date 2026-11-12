using Byd.Client;
using Xunit;

namespace Byd.Tray.Tests;

public class TrayStatusTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T02:00:00Z");
    private static VehicleStatus Closed() => new(78, 325, "Not charging", "Locked", Now, Now, true)
    { Openings = new(0, 0, true, true, false, false, false) };
    [Fact]
    public void Hover_has_battery_charging_and_confirmed_closed_state_on_one_line()
    {
        var text = TrayStatusText.Format(Closed(), Now);
        Assert.Contains("78%", text); Assert.Contains("Not charging", text); Assert.Contains("Locked", text); Assert.Contains("doors/windows closed", text);
        Assert.DoesNotContain("⚠", text); Assert.DoesNotContain('\n', text);
    }
    [Fact]
    public void Hover_warns_for_unlocked_vehicle_open_door_boot_window_or_roof()
    {
        var status = Closed() with { Locks = "Unlocked", Openings = new(1, 1, true, true, true, true, false) };
        var text = TrayStatusText.Format(status, Now);
        Assert.Contains("⚠", text); Assert.Contains("unlocked", text); Assert.Contains("door/boot open", text); Assert.Contains("window/roof open", text);
        Assert.DoesNotContain("closed", text);
    }
    [Fact]
    public void Unknown_stale_and_failed_refresh_never_present_as_a_current_safe_state()
    {
        var status = Closed() with { BatteryPercent = null, Locks = "Unknown", VehicleTime = Now.AddHours(-1), Openings = VehicleOpenings.Unknown };
        var text = TrayStatusText.Format(status, Now, refreshFailed: true);
        Assert.Contains("battery ?", text); Assert.Contains("states unknown", text); Assert.Contains("stale", text); Assert.Contains("refresh failed", text);
        Assert.DoesNotContain("Locked", text); Assert.DoesNotContain("closed", text); Assert.DoesNotContain('\n', text);
    }
    [Fact]
    public void Boot_and_roof_alone_are_enough_for_opening_warnings()
    {
        var text = TrayStatusText.Format(Closed() with { Openings = new(0, 0, true, true, true, true, false) }, Now);
        Assert.Contains("door/boot open", text); Assert.Contains("window/roof open", text);
    }
    [Fact]
    public void Most_detailed_hover_stays_within_windows_tooltip_length_without_losing_warnings()
    {
        var text = TrayStatusText.Format(Closed() with { BatteryPercent = null, Locks = "Unlocked", VehicleTime = Now.AddHours(-1), Openings = new(1, 1, false, false, null, true, true) }, Now, refreshFailed: true);
        Assert.InRange(text.Length, 1, 127); Assert.Contains("refresh failed", text); Assert.Contains("unlocked", text);
    }
    [Fact]
    public void Disconnected_and_demo_states_are_explicit()
    {
        Assert.Contains("Sign in", TrayStatusText.Format(null, Now));
        Assert.Contains("No status", TrayStatusText.Format(null, Now, signedIn: true));
        Assert.Contains("DEMO", TrayStatusText.Format(Closed(), Now, demo: true));
    }
    [Fact]
    public void Missing_roof_state_does_not_claim_all_windows_are_confirmed_closed()
    {
        var status = Closed() with { Openings = new(0, 0, true, true, false, null, false) };
        var text = TrayStatusText.Format(status, Now);
        Assert.Contains("states unknown", text); Assert.DoesNotContain("windows closed", text);
    }
    [Fact]
    public void Unrecognized_compatibility_labels_use_typed_unknown_states_instead_of_being_shown_or_claiming_safety()
    {
        var text = TrayStatusText.Format(Closed() with { Charging = "Synthetic unrecognized charge", Locks = "Synthetic unrecognized lock" }, Now);
        Assert.Contains("charging ?", text); Assert.Contains("states unknown", text);
        Assert.DoesNotContain("Synthetic", text); Assert.DoesNotContain("closed", text);
        Assert.Equal("Charging status unavailable", TrayStatusText.ChargingLabel((VehicleChargingState)99));
        Assert.Equal("Unknown", TrayStatusText.LockLabel((VehicleLockState)99));
    }
}
