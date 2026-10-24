using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Icono.App.State;
using Icono.Core;
using Icono.Rendering;

namespace Icono.App;

/// <summary>The menu bar and the toolbar: arrange commands, page commands, the tool row and keeping every place that shows the active
/// tool (tool row, tool panel, menus, status bar) in step.</summary>
public partial class MainWindow
{
    private static readonly HashSet<string> ToolTags = new(Enum.GetNames<ShapeKind>())
    {
        "Select", "PanZoom", "AddPoint", "DeletePoint", "RegionColor", "Eyedropper", "WallCut", "Measure", "MeasureGap", "Match",
        "DimAligned", "DimHorizontal", "DimVertical", "DimAngular", "DimRadius", "DimDiameter",
    };

    private bool _syncingToolUi;

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

    /// <summary>A key of the tool row was pressed: switch to that tool.</summary>
    private void ToolbarToolRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingToolUi || sender is not RadioButton { CommandParameter: string tag } || tag == _context.ActiveToolTag)
        {
            return;
        }

        ActivateTool(tag);
    }

    /// <summary>Shows the active tool everywhere: the keys of the tool row, the check marks in the menus and the status bar.</summary>
    private void SyncToolUi()
    {
        var tag = _context.ActiveToolTag;

        _syncingToolUi = true;
        try
        {
            foreach (var radio in ToolRow.Children.OfType<RadioButton>())
            {
                var isActive = tag is not null && radio.CommandParameter is string parameter && parameter == tag;
                if (radio.IsChecked != isActive)
                {
                    radio.IsChecked = isActive;
                }
            }
        }
        finally
        {
            _syncingToolUi = false;
        }

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
