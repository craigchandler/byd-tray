using Byd.Client;

namespace Byd.Tray;

internal static class TrayStatusText
{
    internal static string ChargingLabel(VehicleChargingState state) => state switch
    {
        VehicleChargingState.Charging => "Charging",
        VehicleChargingState.NotCharging => "Not charging",
        _ => "Charging status unavailable"
    };
    internal static string LockLabel(VehicleLockState state) => state switch
    {
        VehicleLockState.Locked => "Locked",
        VehicleLockState.Unlocked => "Unlocked",
        _ => "Unknown"
    };
    internal static string Format(VehicleStatus? status, DateTimeOffset now, bool demo = false, bool refreshFailed = false, bool signedIn = false)
    {
        if (status == null) return "BYD · " + (signedIn ? "No status yet" : "Sign in to read status") + (demo ? " · DEMO" : "");
        var battery = status.BatteryPercent is { } charge ? $"{charge:0.#}%" : "battery ?";
        var charging = status.ChargingState == VehicleChargingState.Unknown ? "charging ?" : ChargingLabel(status.ChargingState);
        var warnings = new List<string>();
        var openings = status.Openings;
        if (status.LockState == VehicleLockState.Unlocked) warnings.Add("unlocked");
        if (openings.OpenDoors > 0 || openings.BootOpen == true || openings.FrontCompartmentOpen == true) warnings.Add("door/boot open");
        if (openings.OpenWindows > 0 || openings.SunroofOpen == true) warnings.Add("window/roof open");
        if (status.LockState == VehicleLockState.Unknown || !openings.DoorsKnown || !openings.WindowsKnown || openings.BootOpen == null || openings.SunroofOpen == null) warnings.Add("states unknown");
        if (status.IsStale(now)) warnings.Add("stale");
        if (refreshFailed) warnings.Add("refresh failed");
        var condition = warnings.Count > 0 ? "⚠ " + string.Join(", ", warnings) : "Locked · doors/windows closed";
        return $"BYD · {battery} · {charging} · {condition}{(demo ? " · DEMO" : "")}";
    }

    internal static string OpeningDetails(VehicleOpenings openings)
    {
        static string Group(int count, bool known) => count > 0 ? $"{count} open{(known ? "" : ", others unknown")}" : known ? "closed" : "unknown";
        var text = $"Doors: {Group(openings.OpenDoors, openings.DoorsKnown)} · Windows: {Group(openings.OpenWindows, openings.WindowsKnown)} · Boot: {(openings.BootOpen is { } boot ? boot ? "open" : "closed" : "unknown")}";
        if (openings.SunroofOpen is { } roof) text += " · Sunroof: " + (roof ? "open" : "closed");
        if (openings.FrontCompartmentOpen == true) text += " · Front compartment open";
        return text;
    }
}
