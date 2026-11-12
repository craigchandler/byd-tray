using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using Byd.Client;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.UI.Avalonia;
using MapBrush = Mapsui.Styles.Brush;
using MapColor = Mapsui.Styles.Color;

namespace Byd.Tray;

internal sealed class LocationWindow : Window
{
    private readonly Func<CancellationToken, Task<VehicleLocation>>? fetch;
    private readonly bool demo;
    private readonly Func<ILayer> streetLayerFactory;
    private readonly MapControl map = new();
    private readonly MemoryLayer marker = new() { Name = "Vehicle", Style = new SymbolStyle { Fill = new MapBrush(MapColor.FromString("#E24C45")), Outline = new Mapsui.Styles.Pen(MapColor.White, 3), SymbolScale = 0.8 } };
    private readonly TextBlock info = new() { Text = "No vehicle location yet", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button refresh = new() { Content = "Refresh location" };
    private readonly Button streets = new() { Content = "Load OpenStreetMap", IsEnabled = false };
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer age = new() { Interval = TimeSpan.FromMinutes(1) };
    private VehicleLocation? location;
    private DateTimeOffset lastAttempt = DateTimeOffset.MinValue;
    private bool loading, closed, streetsLoaded;
    internal bool StreetMapLoaded => streetsLoaded;
    internal VehicleLocation? DisplayedLocation => location;

    internal LocationWindow(string vehicleName, Func<CancellationToken, Task<VehicleLocation>>? fetch, bool demo = false, Func<ILayer>? streetLayerFactory = null)
    {
        this.fetch = fetch; this.demo = demo;
        this.streetLayerFactory = streetLayerFactory ?? CreateStreetLayer;
        Title = demo ? "Vehicle location — DEMO" : "Vehicle location";
        Width = 900; Height = 740; MinWidth = 620; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        map.Map.BackColor = MapColor.FromString("#E8EEF3"); map.Map.Layers.Add(marker);
        map.Map.DataChanged += (_, args) =>
        {
            if (args.Error != null) Dispatcher.UIThread.Post(() => { if (!closed) message.Text = "Street-map data could not load. Check the internet connection; the marker still shows the reported GPS fix."; });
        };
        var zoomIn = new Button { Content = "+" }; zoomIn.Click += (_, _) => map.Map.Navigator.ZoomIn();
        var zoomOut = new Button { Content = "−" }; zoomOut.Click += (_, _) => map.Map.Navigator.ZoomOut();
        var center = new Button { Content = "Centre on vehicle" }; center.Click += (_, _) => Centre();
        foreach (var button in new[] { refresh, streets, center, zoomIn, zoomOut }) button.Margin = new Thickness(0, 0, 8, 0);
        refresh.Click += async (_, _) => await RefreshLocation();
        streets.Click += (_, _) => LoadStreets();
        var layout = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        var header = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 12),
            Children = { new TextBlock { Text = vehicleName, FontSize = 23 },
                new TextBlock { Text = demo ? "DEMO • Synthetic public landmark • No network" : "Last reported vehicle location • Refresh can wake the car", TextWrapping = TextWrapping.Wrap },
                info,
                new WrapPanel { Orientation = Orientation.Horizontal, Children = { refresh, streets, center, zoomIn, zoomOut } },
                new TextBlock { Text = "Loading OpenStreetMap shares the viewed area with its tile service. Location and map tiles stay in memory and are not written to files.", FontSize = 12, TextWrapping = TextWrapping.Wrap }, message }
        };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var attribution = new TextBlock { Text = "© OpenStreetMap contributors  ·  openstreetmap.org/copyright", Margin = new Thickness(0, 10, 0, 0), FontSize = 12 };
        DockPanel.SetDock(attribution, Dock.Bottom); layout.Children.Add(attribution);
        layout.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = map }); Content = layout;
        age.Tick += (_, _) => UpdateInfo(); age.Start();
        Opened += async (_, _) =>
        {
            if (demo) { SetLocation(new(-33.8568, 151.2153, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)); refresh.IsEnabled = false; streets.IsEnabled = false; message.Text = "Synthetic preview. Online map loading is disabled."; }
            else await RefreshLocation();
        };
        Closed += (_, _) =>
        {
            closed = true; lifetime.Cancel(); age.Stop(); location = null; marker.Features = []; map.Dispose();
        };
    }

    private async Task RefreshLocation()
    {
        if (closed || loading || demo || fetch == null) return;
        if (DateTimeOffset.UtcNow - lastAttempt < TimeSpan.FromMinutes(1)) { message.Text = "Please wait at least one minute between location requests."; return; }
        loading = true; refresh.IsEnabled = false; lastAttempt = DateTimeOffset.UtcNow;
        message.Text = "Requesting the vehicle's reported location…";
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var fix = await fetch(lifetime.Token);
            if (closed) return;
            SetLocation(fix); message.Text = $"Location received in {elapsed.Elapsed.TotalSeconds:0.0} seconds. Street map is {(streetsLoaded ? "loaded" : "not loaded")}.";
            Audit.Write(AuditEvent.LocationSucceeded);
        }
        catch (OperationCanceledException) when (closed) { }
        catch (Exception error)
        {
            if (closed) return;
            message.Text = (error is BydException byd ? byd.Message : "Could not read the vehicle location. Sensitive details were omitted.")
                + (location != null ? " The marker still shows the previous reported fix." : "");
            Audit.Write(AuditEvent.LocationFailed);
        }
        finally { loading = false; if (!closed) refresh.IsEnabled = true; }
    }
    private void SetLocation(VehicleLocation fix)
    {
        location = fix;
        var (x, y) = SphericalMercator.FromLonLat(fix.Longitude, Math.Clamp(fix.Latitude, -85.05112878, 85.05112878));
        marker.Features = [new PointFeature(new MPoint(x, y))]; marker.DataHasChanged();
        streets.IsEnabled = !demo && !streetsLoaded; UpdateInfo(); Centre();
    }
    private void UpdateInfo()
    {
        if (location == null) return;
        info.Text = $"GPS fix: {(location.FixTime is { } time ? time.ToLocalTime().ToString("g") : "timestamp not supplied")}  ·  Received: {location.RetrievedAt.ToLocalTime():g}\n"
            + (location.IsStale(DateTimeOffset.UtcNow) ? "STALE OR UNVERIFIED — this marker may not be the car's current position." : "Recent reported GPS fix — this is not continuous tracking.");
    }
    private void Centre()
    {
        if (location == null) return;
        var (x, y) = SphericalMercator.FromLonLat(location.Longitude, Math.Clamp(location.Latitude, -85.05112878, 85.05112878));
        map.Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), 2.4); map.Map.RefreshGraphics();
    }
    private void LoadStreets()
    {
        if (demo || location == null || streetsLoaded) return;
        map.Map.Layers.Insert(0, streetLayerFactory());
        streetsLoaded = true; streets.IsEnabled = false; message.Text = "Street map enabled. Drag to pan, scroll or use +/− to zoom."; map.RefreshData();
    }
    private static ILayer CreateStreetLayer()
    {
        var source = new HttpTileSource(new GlobalSphericalMercator(), "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            name: "OpenStreetMap", attribution: new Attribution("© OpenStreetMap contributors", "https://www.openstreetmap.org/copyright"),
            persistentCache: SessionTileCache.Instance,
            configureHttpRequestMessage: request => request.Headers.TryAddWithoutValidation("User-Agent", "BydTray/0.1.0 (native vehicle-location viewer)"));
        return new TileLayer(source) { Name = "OpenStreetMap" };
    }
}

// BruTile's cache interface is named IPersistentCache; this implementation never touches disk.
// Seven-day retention while the app runs, bounded to 2048 tiles. No offline/prefetch feature.
internal sealed class SessionTileCache : IPersistentCache<byte[]>
{
    internal static readonly SessionTileCache Instance = new();
    private readonly Dictionary<TileIndex, (byte[] Data, DateTimeOffset Expires)> tiles = [];
    public void Add(TileIndex index, byte[] tile)
    {
        lock (tiles)
        {
            if (tiles.Count >= 2048 && !tiles.ContainsKey(index)) tiles.Remove(tiles.MinBy(pair => pair.Value.Expires).Key);
            tiles[index] = (tile, DateTimeOffset.UtcNow.AddDays(7));
        }
    }
    public void Remove(TileIndex index) { lock (tiles) tiles.Remove(index); }
    public byte[]? Find(TileIndex index)
    {
        lock (tiles)
        {
            if (!tiles.TryGetValue(index, out var tile)) return null;
            if (tile.Expires > DateTimeOffset.UtcNow) return tile.Data;
            tiles.Remove(index); return null;
        }
    }
}
