using Byd.Client;

namespace Byd.Tray;

internal sealed partial class MainWindow
{
    internal static string IanaTimeZone(string id) => id.Contains('/', StringComparison.Ordinal) || id == "UTC" ? id : TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) ? iana : "UTC";
    private void ChangeCountry()
    {
        if (restoringCountry || demo || accountCountry.SelectedItem is not BydMarket market || market.CountryCode == preferences.CountryCode) return;
        if (busy || stopped) { RestoreCountry(); return; }
        var updated = preferences with { CountryCode = market.CountryCode };
        try { preferencesStore.Save(updated); }
        catch { RestoreCountry(); message.Text = "The country selection could not be saved; the previous selection is restored."; Audit.Write(AuditEvent.PreferencesFailed); return; }
        preferences = updated;
        // Region changes never reuse a session, account input, polling setting or control approval.
        client?.Dispose(); client = null; controls.IsChecked = false; automatic.IsChecked = false; polling.Stop();
        email.Text = ""; password.Text = ""; remember.IsChecked = false;
        vehicles.ItemsSource = null; status = null; statusRefreshFailed = false; ResetAdvanced();
        secretStore = createSecretStore(market);
        message.Text = "Account country changed. Load that country's saved account or enter its credentials, then sign in.";
        UpdateStatus(); UpdateEnabled();
    }
    private void RestoreCountry()
    {
        restoringCountry = true;
        accountCountry.SelectedItem = BydMarkets.ForCountry(preferences.CountryCode);
        restoringCountry = false;
    }
}
