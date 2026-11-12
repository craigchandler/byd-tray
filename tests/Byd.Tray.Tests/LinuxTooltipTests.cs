using Byd.Tray.DBus;
using Tmds.DBus.Protocol;
using Xunit;

namespace Byd.Tray.Tests;

public class LinuxTooltipTests
{
    [Fact]
    public void Native_tooltip_is_populated_updates_without_replacing_handler_and_has_stable_identity()
    {
        // No session bus or socket is opened by this test.
        using var connection = new DBusConnection("unix:path=/synthetic-not-connected");
        var text = "BYD · 78% · Not charging · DEMO";
        IStatusNotifierItemProperties properties = new TooltipPropertyHandler(connection, "/StatusNotifierItem", _ => "legacy changing identity", () => text);
        Assert.Equal(text, properties.ToolTip.Item3); Assert.Empty(properties.ToolTip.Item4);
        Assert.Equal("BydTray", properties.Id);
        text = "BYD · 79% · Charging · ⚠ unlocked · DEMO";
        Assert.Equal(text, properties.ToolTip.Item3); Assert.Equal("BydTray", properties.Id);
    }
    [Fact]
    public void Native_icon_and_menu_properties_are_preserved()
    {
        using var connection = new DBusConnection("unix:path=/synthetic-not-connected");
        var pixmap = new[] { (1, 1, new byte[] { 255, 0, 0, 0 }) };
        var menu = new ObjectPath("/SyntheticMenu");
        IStatusNotifierItemProperties properties = new TooltipPropertyHandler(connection, "/StatusNotifierItem", name => name switch
        {
            "IconPixmap" => pixmap,
            "Menu" => menu,
            "Status" => "Active",
            "ItemIsMenu" => false,
            _ => throw new InvalidOperationException("Unexpected synthetic property")
        }, () => "Synthetic status");
        Assert.Same(pixmap, properties.IconPixmap); Assert.Equal(menu, properties.Menu);
        Assert.Equal("Active", properties.Status); Assert.False(properties.ItemIsMenu);
    }
}
