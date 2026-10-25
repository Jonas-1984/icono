using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Icono.App.State;
using Icono.Core;
using Icono.Rendering;

namespace Icono.App;

/// <summary>The menu bar: arrange commands, page commands and keeping the places that show the active tool (menus, status bar) in step.</summary>
public partial class MainWindow
{
    private static readonly HashSet<string> ToolTags = new(Enum.GetNames<ShapeKind>())
    {
        "Select", "PanZoom", "AddPoint", "DeletePoint", "RegionColor", "Eyedropper", "WallCut", "Measure", "MeasureGap", "Match",
        "DimAligned", "DimHorizontal", "DimVertical", "DimAngular", "DimRadius", "DimDiameter",
    };

    /// <summary>Opens the drop-down menu that belongs to a toolbar button (align, flip / rotate).</summary>
    private void DropDownButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>Align / Distribute / Flip / Rotate, named by the menu item's tag ("Align:Left", "Distribute:H", "Flip:V", "Rotate:90").</summary>
    private void ArrangeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || tag.Split(':') is not [var command, var argument])
        {
            return;
        }

        switch (command)
        {
            case "Align" when Enum.TryParse<AlignKind>(argument, out var kind):
                _context.AlignSelection(kind);
                break;
            case "Distribute":
                _context.DistributeSelection(argument == "H");
                break;
            case "Flip":
                _context.FlipSelection(argument == "H");
                break;
            case "Rotate" when double.TryParse(argument, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var degrees):
                _context.RotateSelection(degrees);
                break;
        }
    }

    private void WallDimensionPlacementMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<WallDimensionPlacement>(tag, out var placement))
        {
            _context.SetWallDimensionPlacement(placement);
        }
    }

    private void RoomMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<RoomPreset>(tag, out var preset))
        {
            _context.InsertRoom(preset);
        }
    }

    private void PageSizeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag })
        {
            PropertiesPanel.ApplyPagePreset(tag);
        }
    }

    private void PageOrientationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag })
        {
            PropertiesPanel.SetPageOrientation(tag == "Landscape");
        }
    }

    /// <summary>Shows the active tool in the menus (check mark) and the status bar. The tool panel follows by itself.</summary>
    private void SyncToolUi()
    {
        var tag = _context.ActiveToolTag;

        foreach (var item in AllMenuItems(MainMenu.Items))
        {
            if (item.Tag is string itemTag && ToolTags.Contains(itemTag))
            {
                item.IsChecked = itemTag == tag;
            }
        }

        StatusBar.SetTool(_context.ActiveToolName);
    }

    /// <summary>Shows the page size and orientation in the Page menu and the status bar.</summary>
    private void UpdatePageUi()
    {
        var document = _context.Document;
        PortraitMenuItem.IsChecked = document.Width <= document.Height;
        LandscapeMenuItem.IsChecked = document.Width > document.Height;
        StatusBar.SetPage(document.Width, document.Height);
    }

    private static IEnumerable<MenuItem> AllMenuItems(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in AllMenuItems(item.Items))
            {
                yield return child;
            }
        }
    }
}
