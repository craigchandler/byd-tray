using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Byd.Tray;

public sealed class App : Application
{
    private TrayIcon? tray;
    private LinuxTrayTooltip? linuxTooltip;
    public override void Initialize()
    {
        Audit.Disabled = Program.Demo;
        // Map diagnostics can contain tile URLs (viewed areas); never forward them to logs.
        Mapsui.Logging.Logger.LogDelegate = (_, _, _) => { };
        Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow(Program.Demo);
            if (!Program.Options.StartInTray) desktop.MainWindow = window;
            var icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Byd.Tray/Assets/tray.png")));
            window.Icon = icon;
            var menu = new NativeMenu();
            void OpenWindow() { desktop.MainWindow = window; window.ShowStatusView(); window.Show(); window.Activate(); }
            var open = new NativeMenuItem("Open BYD Tray"); open.Click += (_, _) => OpenWindow();
            var refresh = new NativeMenuItem("Refresh status"); refresh.Click += async (_, _) => await window.Refresh();
            var quit = new NativeMenuItem("Quit"); quit.Click += (_, _) => { window.Stop(); linuxTooltip?.Dispose(); tray?.Dispose(); desktop.Shutdown(); };
            menu.Items.Add(open); menu.Items.Add(refresh); menu.Items.Add(new NativeMenuItemSeparator()); menu.Items.Add(quit);
            tray = new TrayIcon { Icon = icon, ToolTipText = window.TrayStatus, Menu = menu, IsVisible = true };
            TrayIcon.SetIcons(this, new TrayIcons { tray });
            linuxTooltip = LinuxTrayTooltip.Attach(tray, () => window.TrayStatus);
            tray.Clicked += (_, _) => OpenWindow();
            window.StatusChanged += text => tray.ToolTipText = text;
            window.Closing += (_, e) => { e.Cancel = true; window.Hide(); };
            desktop.Exit += (_, _) => { window.Stop(); linuxTooltip?.Dispose(); tray?.Dispose(); };
            if (!Program.Options.StartInTray) window.Show();
            Dispatcher.UIThread.Post(async () => await window.StartAsync());
            Audit.Write(AuditEvent.Started);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
