using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using Byd.Tray.DBus;
using Tmds.DBus.Protocol;

namespace Byd.Tray;

/// <summary>
/// Compatibility adapter for the pinned Avalonia 12.1.6 Linux SNI backend, whose ToolTip is empty.
/// Only Properties requests are decorated; activation, menu methods, icons and connections stay owned by Avalonia.
/// Recheck this adapter when upgrading Avalonia. It never logs the status text.
/// </summary>
internal sealed class LinuxTrayTooltip : IDisposable
{
    private readonly object backend;
    private readonly DBusConnection connection;
    private readonly Func<string> text;
    private readonly DispatcherTimer retry = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly CancellationTokenSource lifetime = new();
    private bool installed, disposed;
    private int attempts;

    private LinuxTrayTooltip(object backend, DBusConnection connection, Func<string> text)
    {
        this.backend = backend; this.connection = connection; this.text = text;
        retry.Tick += (_, _) => TryInstall(); retry.Start();
        _ = WatchTrayHost();
    }
    internal static LinuxTrayTooltip? Attach(TrayIcon icon, Func<string> text)
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            var backend = typeof(TrayIcon).GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(icon);
            if (backend?.GetType().FullName != "Avalonia.FreeDesktop.DBusTrayIconImpl") throw new InvalidOperationException();
            var connection = Field(backend, "_connection") as DBusConnection ?? throw new InvalidOperationException();
            return new(backend, connection, text);
        }
        catch { Audit.Write(AuditEvent.TrayTooltipFailed); return null; }
    }
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(value);

    private void TryInstall()
    {
        if (disposed || installed) { retry.Stop(); return; }
        if (++attempts > 120) { retry.Stop(); Audit.Write(AuditEvent.TrayTooltipFailed); return; }
        if (Field(backend, "_itemExported") is not true) return;
        try
        {
            var original = Field(backend, "_statusNotifierItemDbusObj") as IPathMethodHandler ?? throw new InvalidOperationException();
            var properties = original.GetType().GetInterface("Avalonia.FreeDesktop.DBus.IStatusNotifierItemProperties") ?? throw new InvalidOperationException();
            object? Read(string name) => properties.GetProperty(name)?.GetValue(original);
            var handler = new TooltipPropertyHandler(connection, original.Path, Read, text);
            connection.RemoveMethodHandler(original.Path);
            try { connection.AddMethodHandler(new Decorator(original, handler)); }
            catch { connection.AddMethodHandler(original); throw; }
            installed = true; retry.Stop(); connection.EmitNewToolTip(original.Path);
            Audit.Write(AuditEvent.TrayTooltipReady);
        }
        catch { retry.Stop(); Audit.Write(AuditEvent.TrayTooltipFailed); }
    }
    private async Task WatchTrayHost()
    {
        try
        {
            using var watcher = await connection.WatchNameOwnerAsync("org.kde.StatusNotifierWatcher");
            var owner = watcher.GetCurrentOwner();
            while (!lifetime.IsCancellationRequested)
            {
                if (owner != null)
                {
                    Dispatcher.UIThread.Post(() => { if (!disposed) { installed = false; attempts = 0; retry.Start(); } });
                    using var changed = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, watcher.GetOwnerChangedCancellationToken(owner));
                    try { await Task.Delay(Timeout.Infinite, changed.Token); }
                    catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { }
                }
                else await watcher.WaitForOwnerAsync(lifetime.Token);
                owner = watcher.GetCurrentOwner();
            }
        }
        catch (OperationCanceledException) { }
        catch { if (!disposed) Audit.Write(AuditEvent.TrayTooltipFailed); }
    }
    public void Dispose() { disposed = true; retry.Stop(); lifetime.Cancel(); }

    private sealed class Decorator(IPathMethodHandler original, IPathMethodHandler properties) : IPathMethodHandler
    {
        public string Path => original.Path;
        public bool HandlesChildPaths => original.HandlesChildPaths;
        public ValueTask HandleMethodAsync(MethodContext context) => context.Request.InterfaceAsString == "org.freedesktop.DBus.Properties"
            ? properties.HandleMethodAsync(context) : original.HandleMethodAsync(context);
    }
}

internal sealed class TooltipPropertyHandler(DBusConnection connection, string path, Func<string, object?> read, Func<string> text)
    : DBusHandler(connection, path, handlesChildPaths: false), IStatusNotifierItemHandler, IStatusNotifierItemProperties
{
    private T Native<T>(string name) => read(name) is T value ? value : throw new InvalidOperationException("Unsupported tray property.");
    public string Category => Native<string>(nameof(Category));
    public string Id => "BydTray"; // Stable desktop identity; never persist battery/status text as the item ID.
    public string Title => Native<string>(nameof(Title));
    public string Status => Native<string>(nameof(Status));
    public int WindowId => Native<int>(nameof(WindowId));
    public string IconThemePath => Native<string>(nameof(IconThemePath));
    public ObjectPath Menu => Native<ObjectPath>(nameof(Menu));
    public bool ItemIsMenu => Native<bool>(nameof(ItemIsMenu));
    public string IconName => Native<string>(nameof(IconName));
    public (int, int, byte[])[] IconPixmap => Native<(int, int, byte[])[]>(nameof(IconPixmap));
    public string OverlayIconName => Native<string>(nameof(OverlayIconName));
    public (int, int, byte[])[] OverlayIconPixmap => Native<(int, int, byte[])[]>(nameof(OverlayIconPixmap));
    public string AttentionIconName => Native<string>(nameof(AttentionIconName));
    public (int, int, byte[])[] AttentionIconPixmap => Native<(int, int, byte[])[]>(nameof(AttentionIconPixmap));
    public string AttentionMovieName => Native<string>(nameof(AttentionMovieName));
    public (string, (int, int, byte[])[], string, string) ToolTip => ("", [], text(), "");
    public ValueTask HandleGetPropertyAsync(IStatusNotifierItemHandler.GetPropertyContext context) => context.Handle(this);
    public ValueTask HandleGetAllPropertiesAsync(IStatusNotifierItemHandler.GetAllPropertiesContext context) => context.Handle(this);
}
