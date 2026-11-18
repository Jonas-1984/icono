using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>One object of the library: a room (inserted at once), or a shape / plan symbol / catalogue unit that is then placed with a tap or a
/// drag.</summary>
public sealed record LibraryItem(string Name, string Group, ShapeKind Kind, UnitObject? Unit = null, RoomPreset? Room = null, MyIcons.Icon? Icon = null);

/// <summary>The objects of the Windows program's libraries in groups (rooms, doors and windows, plan symbols, the catalogues of cabinets,
/// kitchen elements and computers, shapes, arrows, IT, icons) with a search box. The groups fold open with a tap on their name.</summary>
public sealed class ObjectLibrary : UserControl
{
    private static readonly (string Group, string[] Kinds)[] ShapeGroups =
    [
        ("Doors & windows", ["DoorSingle", "DoorDouble", "WindowSingle", "WindowDouble", "WallOpening"]),
        ("Plan symbols", ["Socket", "DoubleSocket", "ElectricAppliance", "Cooker", "Oven", "Microwave", "WaterAndWastewater", "Hood"]),
        ("Shapes", ["Rectangle", "RoundedRectangle", "Ellipse", "Triangle", "Diamond", "Pentagon", "Hexagon", "Octagon", "Star", "Parallelogram",
            "Trapezoid", "Cross", "DesignGrid", "HalfCircle", "Ring", "SpeechBubble", "Heart", "Cloud", "Shield", "Bookmark", "Gear", "Club", "Box3D", "Cylinder",
            "Cone", "Pyramid"]),
        ("Arrows", ["ArrowRight", "ArrowLeft", "ArrowUp", "ArrowDown", "ChevronRight", "ChevronLeft", "ChevronUp", "ChevronDown", "ArrowLeftRight",
            "ArrowUpDown", "BentArrowRight", "BentArrowUp", "LoopArrowClockwise", "LoopArrowCounterClockwise", "CompassArrows", "NotchedArrowRight",
            "PlayTriangleRight", "StripedArrowRight", "Undo2", "SquareArrowRightExit", "SquareChevronRight", "ArrowBigRightDash"]),
        ("IT", ["MonitorPc", "PcCase", "Server", "ServerNas", "Router", "UserRound", "UsersRound", "UserStar", "EthernetPort", "DataTransfer", "Power",
            "GlobeCode", "GlobeOff", "GlobeCheck", "Globe", "LaptopMinimal", "TvMinimal", "Wifi", "BluetoothConnected"]),
        ("Icons", ["StarCheck", "StarPlus", "StarMinus", "SquareArrowOutUpRight", "Blocks", "SwatchBook", "LensConcave", "TriangleAlert", "Spade",
            "SquareDashedBottom", "Share", "ShieldCog", "Umbrella", "Cloudy", "TrafficCone"]),
    ];

    private static readonly (RoomPreset Preset, string Name)[] Rooms =
    [
        (RoomPreset.Closed, "Room"), (RoomPreset.OneCorner, "L-shaped room"), (RoomPreset.OneSlantedWall, "One slanted wall"),
        (RoomPreset.TwoSlantedWalls, "Two slanted walls"), (RoomPreset.CornerColumn, "Corner column"), (RoomPreset.WallColumn, "Wall column"),
    ];

    /// <summary>The groups that start open.</summary>
    private static readonly HashSet<string> OpenAtStart = ["Rooms", "Doors & windows"];

    private readonly StackPanel _groups = new() { Spacing = 2 };
    private readonly TextBox _search;
    private readonly List<(string Group, Button Header, WrapPanel Tiles, List<(LibraryItem Item, Control Tile)> Items)> _built = [];
    private readonly HashSet<string> _open = [.. OpenAtStart];

    public ObjectLibrary()
    {
        _search = new TextBox { PlaceholderText = "Search objects", FontSize = 13, MinHeight = 34, Margin = new Thickness(10, 10, 10, 6) };
        _search.TextChanged += (_, _) => ApplyFilter();

        var layout = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        layout.Children.Add(_search);
        layout.Children.Add(new ScrollViewer
        {
            Content = _groups, Padding = new Thickness(6, 0, 6, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        Content = layout;

        Build();
    }

    /// <summary>Raised when an object is tapped.</summary>
    public event EventHandler<LibraryItem>? ItemChosen;

    /// <summary>Every object of the library, group by group.</summary>
    public static IEnumerable<LibraryItem> Items()
    {
        foreach (var (preset, name) in Rooms)
        {
            yield return new LibraryItem(name, "Rooms", ShapeKind.Wall, Room: preset);
        }

        foreach (var (group, kinds) in ShapeGroups.Take(2))
        {
            foreach (var kind in Kinds(kinds))
            {
                yield return new LibraryItem(Words(kind.ToString()), group, kind);
            }
        }

        // the catalogues of the Windows program (cabinets, kitchen elements, computers ...)
        foreach (var unit in UnitCatalog.All)
        {
            yield return new LibraryItem(unit.Name, unit.Group, ShapeKind.CombinedPath, unit);
        }

        foreach (var (group, kinds) in ShapeGroups.Skip(2))
        {
            foreach (var kind in Kinds(kinds))
            {
                yield return new LibraryItem(Words(kind.ToString()), group, kind);
            }
        }
    }

    private static IEnumerable<ShapeKind> Kinds(IEnumerable<string> names) =>
        names.Select(n => Enum.TryParse<ShapeKind>(n, out var kind) ? kind : (ShapeKind?)null).OfType<ShapeKind>();

    private static string Words(string name) => System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");

    /// <summary>A fresh shape that shows the item (for its picture).</summary>
    private static Shape Sample(LibraryItem item)
    {
        if (item.Room is { } room)
        {
            return WallRooms.Build(room, 100, 80, 160, 120, 40);
        }

        var shape = ShapeTemplates.NewBox(item.Kind, 0, 0, ArgbColor.FromRgb(0x9E, 0xD9, 0xD1), item.Unit);
        if (item.Unit is { } unit)
        {
            (shape.Width, shape.Height) = (unit.WidthCm, unit.HeightCm);
        }
        else
        {
            // doors, windows and symbols get their real proportions from a tap at 1:50; other shapes are square
            var probe = new Document { Width = 800, Height = 800, ScaleDenominator = 50 };
            ShapeTemplates.ApplyClickSize(shape, probe);
            if (shape.Width < 2 || shape.Height < 2)
            {
                (shape.Width, shape.Height) = (80, 80);
            }
        }

        return shape;
    }

    private const string MyIconsGroup = "My icons";

    private void Build()
    {
        foreach (var group in Items().GroupBy(i => i.Group))
        {
            AddGroup(group.Key, group, -1);
        }

        BuildMyIcons();
        ApplyFilter();
    }

    /// <summary>Reads the icons again (one was saved or removed since): only the group of the icons is made new.</summary>
    public void Refresh()
    {
        BuildMyIcons();
        ApplyFilter();
    }

    /// <summary>The icons that come with icono and the ones the user saved, as the first group (open while it has icons of the user).</summary>
    private void BuildMyIcons()
    {
        var old = _built.FindIndex(b => b.Group == MyIconsGroup);
        if (old >= 0)
        {
            _groups.Children.Remove(_built[old].Header);
            _groups.Children.Remove(_built[old].Tiles);
            _built.RemoveAt(old);
        }

        var icons = MyIcons.Load();
        if (icons.Count == 0)
        {
            return;
        }

        var items = icons.Select(i => new LibraryItem(i.Entry.Name, MyIconsGroup, ShapeKind.CombinedPath, Icon: i)).ToList();
        AddGroup(MyIconsGroup, items, 0);
        if (icons.Any(i => !i.IsBuiltIn))
        {
            _open.Add(MyIconsGroup);
        }
    }

    private void AddGroup(string name, IEnumerable<LibraryItem> group, int insertAt)
    {
        var tiles = new WrapPanel { Margin = new Thickness(2, 2, 2, 8) };
        var items = new List<(LibraryItem, Control)>();
        foreach (var item in group)
        {
            var tile = Tile(item);
            tiles.Children.Add(tile);
            items.Add((item, tile));
        }

        var header = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 6),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("MutedTextBrush"),
        };
        header.Click += (_, _) =>
        {
            if (!_open.Remove(name))
            {
                _open.Add(name);
            }

            ApplyFilter();
        };

        if (insertAt >= 0)
        {
            _groups.Children.Insert(insertAt, tiles);
            _groups.Children.Insert(insertAt, header);
            _built.Insert(insertAt, (name, header, tiles, items));
        }
        else
        {
            _groups.Children.Add(header);
            _groups.Children.Add(tiles);
            _built.Add((name, header, tiles, items));
        }
    }

    private Control Tile(LibraryItem item)
    {
        // an icon is shown from copies of its shapes (the thumbnail moves them); everything else from a fresh sample shape
        var picture = item.Icon is { } icon
            ? new ObjectThumbnail(icon.Entry.Shapes.Select(IconLibrary.Clone).ToList()) { Width = 58, Height = 50 }
            : new ObjectThumbnail(Sample(item)) { Width = 58, Height = 50 };
        var name = new TextBlock
        {
            Text = item.Name, FontSize = 9, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis, Width = 62, Foreground = Brush("ForegroundTextBrush"),
        };
        var button = new Button
        {
            Content = new StackPanel { Spacing = 3, Children = { picture, name } },
            Padding = new Thickness(3),
            Margin = new Thickness(2),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(8),
        };

        var held = false;
        if (item.Icon is { IsBuiltIn: false } own)
        {
            // an icon of the user: a long press offers to remove it (the tap that ends the long press must not insert it)
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                held = true;
                var remove = new MenuItem { Header = "Remove \"" + own.Entry.Name + "\" from My icons" };
                remove.Click += (_, _) =>
                {
                    MyIcons.Store.Delete(own.FilePath);
                    Refresh();
                };
                new MenuFlyout { Items = { remove } }.ShowAt(button);
            };
            button.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                held = false;
                timer.Start();
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => timer.Stop(), RoutingStrategies.Tunnel, handledEventsToo: true);
            button.AddHandler(InputElement.PointerExitedEvent, (_, _) => timer.Stop(), RoutingStrategies.Tunnel, handledEventsToo: true);
            // the list scrolls under the finger: that is no long press
            button.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => timer.Stop(), RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        button.Click += (_, _) =>
        {
            if (held)
            {
                held = false;
                return;
            }

            ItemChosen?.Invoke(this, item);
        };
        return button;
    }

    /// <summary>Shows the open groups; while searching, every group with a match is shown open with only its matches (every word must match).</summary>
    private void ApplyFilter()
    {
        var words = (_search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var searching = words.Length > 0;
        foreach (var (group, header, tiles, items) in _built)
        {
            var hits = 0;
            foreach (var (item, tile) in items)
            {
                var text = item.Name + " " + group;
                var match = words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
                tile.IsVisible = match;
                hits += match ? 1 : 0;
            }

            var open = searching ? hits > 0 : _open.Contains(group);
            header.IsVisible = !searching || hits > 0;
            header.Content = $"{(open ? "▾" : "▸")}  {group.ToUpperInvariant()}  ({(searching ? hits : items.Count)})";
            tiles.IsVisible = open;
        }
    }

    private static IBrush Brush(string key) =>
        Application.Current?.TryGetResource(key, null, out var value) == true && value is IBrush brush ? brush : Brushes.Gray;
}
