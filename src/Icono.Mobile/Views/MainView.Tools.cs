using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Icono.Core;
using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>The tool bar on the left and the bar shown while a wall, polyline or dimension is drawn.</summary>
public partial class MainView
{
    /// <summary>One tool of the bar. The icons are the ones of the Windows tool bar (24 x 24, drawn as lines).</summary>
    private sealed record ToolDefinition(string Name, ActiveTool Tool, ShapeKind Kind, string Icon, string Hint, bool More = false);

    private static readonly ToolDefinition[] Tools =
    [
        new("Select", ActiveTool.Select, ShapeKind.Rectangle, "M4,3 L4,19 L9,15 L13,21 L16,19 L12,13 L18,13 Z", ""),
        new("Rectangle", ActiveTool.Shape, ShapeKind.Rectangle, "M3,5 L21,5 L21,19 L3,19 Z",
            "Rectangle: drag it open with one finger - a tap gives a 2.5 cm square."),
        new("Ellipse", ActiveTool.Shape, ShapeKind.Ellipse, "M3,12 A9,7 0 1 0 21,12 A9,7 0 1 0 3,12 Z",
            "Ellipse: drag it open with one finger - a tap gives a 2.5 cm circle."),
        new("Line", ActiveTool.Shape, ShapeKind.Line, "M4,20 L20,4",
            "Line: drag from the start to the end - the ends snap to the points of other objects."),
        new("Arrow", ActiveTool.Shape, ShapeKind.Arrow, "M3,12 L18,12 M13,6 L19,12 L13,18",
            "Arrow: drag from the start to the tip - the ends snap to the points of other objects."),
        new("Text", ActiveTool.Shape, ShapeKind.Text, "M12 4v16 M4 7V5a1 1 0 0 1 1-1h14a1 1 0 0 1 1 1v2 M9 20h6",
            "Text: tap where the text should go and type it. Double tap a text or a shape later to change its text."),
        new("Pen", ActiveTool.Freehand, ShapeKind.Polyline,
            "M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7 M18.375 2.625a1 1 0 0 1 3 3l-9.013 9.014a2 2 0 0 1-.853.505l-2.873.84a.5.5 0 0 1-.62-.62l.84-2.873a2 2 0 0 1 .506-.852z",
            "Pen: write or draw with the pen or a finger - every stroke is one object. Colour and width: Properties. Two fingers move and zoom."),
        new("Curve", ActiveTool.CornerCurve, ShapeKind.Rectangle, "M4,20 V12 A8,8 0 0 1 12,4 H20",
            "Curve: tap one side of a corner, then the side next to it, and type the size - only that corner is rounded (0 makes it sharp)."),
        new("Polyline", ActiveTool.PathDraw, ShapeKind.Polyline, "M3,19 L8,9 L14,15 L21,5",
            "Polyline: finger down, slide to the point, lift to place it. Done ends."),
        new("Wall", ActiveTool.PathDraw, ShapeKind.Wall, "M3,21 L3,3 L21,3 M8,21 L8,8 L21,8 M3,21 L8,21",
            "Wall: finger down, slide to the corner, lift to place it. Tap the first corner again to close the room, Done ends."),
        new("Dimension", ActiveTool.PathDraw, ShapeKind.Dimension,
            "M4,20 L9,8 M20,12 L15,24 M7.5,13 L17.5,17 M7.5,13 L11,13.4 M7.5,13 L9,16.4 M17.5,17 L14,16.6 M17.5,17 L16,13.6",
            "Dimension: place the two points to measure, then where the dimension line runs."),
        new("Door", ActiveTool.Shape, ShapeKind.DoorSingle,
            "M1,19 H5 V22 H1 Z M19,19 H23 V22 H19 Z M5,19 V3 M5,3 A16,16 0 0 1 19,19 M5,3 H7.2 V19",
            "Door: tap for a 90 cm door at the drawing scale, or drag it open."),
        new("Window", ActiveTool.Shape, ShapeKind.WindowSingle,
            "M1,8 H4 V16 H1 Z M20,8 H23 V16 H20 Z M4,8 H20 V16 H4 Z M4,11 H20 M4,13 H20",
            "Window: tap for a 100 cm window at the drawing scale, or drag it open."),

        // the other tools of the Windows program: behind "More"
        new("Double arrow", ActiveTool.Shape, ShapeKind.DoubleArrow, "M3,12 L21,12 M7,8 L3,12 L7,16 M17,8 L21,12 L17,16",
            "Double arrow: drag from one tip to the other - the ends snap to the points of other objects.", More: true),
        new("Match", ActiveTool.Match, ShapeKind.Rectangle, "M14.622 17.897 l-10.68-2.913 M18.376 2.622a1 1 0 1 1 3.002 3.002L17.36 9.643a.5 .5 0 0 0 0 .707l.944 .944a2.41 2.41 0 0 1 0 3.408l-.944 .944a.5 .5 0 0 1-.707 0L8.354 7.348a.5 .5 0 0 1 0-.707l.944-.944a2.41 2.41 0 0 1 3.408 0l.944 .944a.5 .5 0 0 0 .707 0z M9 8c-1.804 2.71-3.97 3.46-6.583 3.948a.507 .507 0 0 0-.302 .819l7.32 8.883a1 1 0 0 0 1.185 .204C12.735 20.405 16 16.792 16 15",
            "Match: tap the object whose look you want to copy, then tap the objects that should get it. Tap beside everything to end.", More: true),
        new("Colour picker", ActiveTool.Eyedropper, ShapeKind.Rectangle, "M12 9 l-8.414 8.414A2 2 0 0 0 3 18.828v1.344a2 2 0 0 1-.586 1.414A2 2 0 0 1 3.828 21h1.344a2 2 0 0 0 1.414-.586L15 12 M18 9 l.4 .4a1 1 0 1 1-3 3l-3.8-3.8a1 1 0 1 1 3-3l.4 .4 3.4-3.4a1 1 0 1 1 3 3z M2 22 l.414-.414",
            "Colour picker: tap a spot of the drawing - the next shapes get its colour.", More: true),
        new("Region colour", ActiveTool.RegionColor, ShapeKind.Rectangle, "M11 7 6 2 M18.992 12H2.041 M21.145 18.38A3.34 3.34 0 0 1 20 16.5a3.3 3.3 0 0 1-1.145 1.88c-.575 .46-.855 1.02-.855 1.595A2 2 0 0 0 20 22a2 2 0 0 0 2-2.025c0-.58-.285-1.13-.855-1.595 M8.5 4.5 l2.148-2.148a1.205 1.205 0 0 1 1.704 0l7.296 7.296a1.205 1.205 0 0 1 0 1.704l-7.592 7.592a3.615 3.615 0 0 1-5.112 0l-3.888-3.888a3.615 3.615 0 0 1 0-5.112L5.67 7.33",
            "Region colour: tap a shape to colour it with the current colour; on a design grid slide over the squares to paint them.", More: true),
        new("Add point", ActiveTool.AddPoint, ShapeKind.Rectangle, "M4,20 L12,8 L20,16 M17,3 V9 M14,6 H20",
            "Add point: select a line, wall or shape of points, then tap on one of its sides.", More: true),
        new("Delete point", ActiveTool.DeletePoint, ShapeKind.Rectangle, "M4,20 L12,8 L20,16 M14,6 H20",
            "Delete point: select a line, wall or shape of points, then tap on the point to take out.", More: true),
        new("Cut wall", ActiveTool.WallCut, ShapeKind.Rectangle, "M5.42 9.42 8 12 M6,8 A2,2 0 1 1 2,8 A2,2 0 1 1 6,8 M14 6 l-8.58 8.58 M6,16 A2,2 0 1 1 2,16 A2,2 0 1 1 6,16 M10.8 14.8 14 18 M16 12h-2 M22 12h-2",
            "Cut wall: tap on a wall where it is to be cut in two.", More: true),
        new("Ruler", ActiveTool.Measure, ShapeKind.Rectangle, "M3,16 L16,3 L21,8 L8,21 Z M7,12 L9,14 M10,9 L12,11 M13,6 L15,8",
            "Ruler: drag from one point to another to measure (nothing is added). It stays until the next measurement.", More: true),
        new("Distance", ActiveTool.MeasureGap, ShapeKind.Rectangle, "M3,6 V18 M21,6 V18 M3,12 H21 M7,8 L3,12 L7,16 M17,8 L21,12 L17,16",
            "Distance: tap one object, then another - a dimension of the gap between them is added and follows both.", More: true),
    ];

    private const string AreaIcon = "M5,3 H8 M11,3 H14 M17,3 H19 A2,2 0 0 1 21,5 V8 M21,11 V14 M21,17 V19 A2,2 0 0 1 19,21 H17 M14,21 H11 M8,21 H5 A2,2 0 0 1 3,19 V17 M3,14 V11 M3,8 V5 A2,2 0 0 1 5,3";

    private const string ObjectsIcon = "M3,3 H10 V10 H3 Z M14,3 H21 V10 H14 Z M3,14 H10 V21 H3 Z M14,14 H21 V21 H14 Z";

    private readonly List<(ToolDefinition Tool, Button Button)> _toolButtons = [];

    /// <summary>The library object being placed (chosen in Objects), or null.</summary>
    private LibraryItem? _libraryItem;

    private Button? _objectsButton;
    private Button? _areaButton;
    private Button? _moreButton;

    private void BuildTools()
    {
        foreach (var tool in Tools)
        {
            var button = PaletteButton(tool.Name, tool.Icon);
            button.Click += (_, _) => ChooseTool(tool);
            _toolButtons.Add((tool, button));
            if (tool.More)
            {
                continue; // in the flyout of the More button
            }

            if (tool.Name is "Rectangle" or "Polyline")
            {
                ToolPanel.Children.Add(Separator());
            }

            ToolPanel.Children.Add(button);

            if (tool.Tool == ActiveTool.Select)
            {
                // choosing several objects: a dashed frame (the Area mode, DocumentCanvas.Area.cs)
                _areaButton = PaletteButton("Area", AreaIcon);
                _areaButton.Click += (_, _) => ToggleAreaMode();
                ToolPanel.Children.Add(_areaButton);
            }
        }

        ToolPanel.Children.Add(Separator());
        // the libraries of the Windows program: rooms, doors, symbols, cabinets, shapes ...
        _objectsButton = PaletteButton("Objects", ObjectsIcon);
        _objectsButton.Click += (_, _) => ShowLibrary(!LibraryPane.IsVisible);
        ToolPanel.Children.Add(_objectsButton);
        Library.ItemChosen += (_, item) => ChooseLibraryItem(item);

        // the rest of the tools of the Windows program: a tap on More opens them
        _moreButton = PaletteButton("More", "M6,12 A1,1 0 1 1 4,12 A1,1 0 1 1 6,12 Z M13,12 A1,1 0 1 1 11,12 A1,1 0 1 1 13,12 Z M20,12 A1,1 0 1 1 18,12 A1,1 0 1 1 20,12 Z");
        var more = new UniformGrid { Columns = 3 };
        var moreFlyout = new Flyout { Content = more, Placement = PlacementMode.RightEdgeAlignedTop };
        foreach (var (tool, button) in _toolButtons.Where(t => t.Tool.More))
        {
            button.Width = 78;
            button.Click += (_, _) => moreFlyout.Hide();
            more.Children.Add(button);
        }

        _moreButton.Click += (_, _) => moreFlyout.ShowAt(_moreButton);
        ToolPanel.Children.Add(_moreButton);

        _context.PropertyChanged += OnContextPropertyChanged;
        Canvas.PathDrawingChanged += (_, _) => UpdatePathBar();
        Canvas.AreaModeChanged += (_, _) => UpdateToolButtons();
        UpdateToolButtons();
    }

    /// <summary>A fine line between the groups of tools.</summary>
    private static Border Separator() => new() { Classes = { "paletteLine" } };

    private static Button PaletteButton(string name, string iconData)
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(iconData),
            StrokeThickness = 1.2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 19,
            Height = 19,
            Stretch = Stretch.Uniform,
        };
        var label = new TextBlock { Text = name, FontSize = 8.5, FontWeight = FontWeight.Medium, HorizontalAlignment = HorizontalAlignment.Center };
        var button = new Button
        {
            Classes = { "palette" },
            Content = new StackPanel { Spacing = 2, Children = { icon, label } },
        };

        // the icon takes the text colour of its button: muted, and bright for the active tool
        icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, button.GetObservable(TemplatedControl.ForegroundProperty));
        return button;
    }

    private void ChooseTool(ToolDefinition tool)
    {
        // a wall or line under way is kept when another tool is chosen
        if (Canvas.IsDrawingPath)
        {
            Canvas.FinishPath();
        }

        _libraryItem = null;
        Canvas.AreaMode = false;
        Canvas.ResetToolState();
        ShowLibrary(false);
        if (tool.Tool == ActiveTool.Match)
        {
            // the selected object (if there is one) is the source at once
            _context.BeginMatch();
            UpdateToolButtons();
            return;
        }

        if (tool.Tool != ActiveTool.Select)
        {
            _context.SelectedShapeKind = tool.Kind;
            _context.PendingUnit = null;
            _context.CurrentDimensionKind = DimensionKind.Aligned;

            // the tools that work on the selected object keep it
            if (tool.Tool is not (ActiveTool.AddPoint or ActiveTool.DeletePoint))
            {
                _context.SelectedShapes.Clear();
            }

            _context.RequestRedraw();
        }

        _context.ActiveTool = tool.Tool;
        UpdateToolButtons();
    }

    /// <summary>The Area button: switches the mode for choosing several objects on or off.</summary>
    private void ToggleAreaMode()
    {
        if (Canvas.IsDrawingPath)
        {
            Canvas.FinishPath();
        }

        var on = !Canvas.AreaMode;
        _libraryItem = null;
        ShowLibrary(false);
        if (on)
        {
            _context.ActiveTool = ActiveTool.Select;
        }

        Canvas.AreaMode = on;
        UpdateToolButtons();
    }

    private void ShowLibrary(bool show)
    {
        if (show)
        {
            Library.Refresh(); // an icon may have been saved
        }

        LibraryPane.IsVisible = show;
        UpdateToolButtons();
    }

    /// <summary>An object of the library was tapped: a room goes into the drawing at once (as on Windows), anything else becomes the tool -
    /// a tap on the drawing places it at its real size, a drag opens it to any size.</summary>
    private void ChooseLibraryItem(LibraryItem item)
    {
        if (Canvas.IsDrawingPath)
        {
            Canvas.FinishPath();
        }

        Canvas.AreaMode = false;
        ShowLibrary(false);
        if (item.Icon is { } icon)
        {
            // an icon is put into the drawing at once: in the middle of the page when it comes with icono, else where it was saved
            _libraryItem = null;
            _context.ActiveTool = ActiveTool.Select;
            var copies = IconLibrary.Insert(_context, icon.Entry, icon.IsBuiltIn);
            StatusText.Text = copies.Count > 0 ? icon.Entry.Name + " added - drag it to move it, its corners to change the size." : "There is no layer to put the icon on.";
            UpdateToolButtons();
            return;
        }

        if (item.Room is { } room)
        {
            _libraryItem = null;
            _context.InsertRoom(room);
            StatusText.Text = item.Name + " added - drag its corners to change it.";
            return;
        }

        _libraryItem = item;
        _context.SelectedShapeKind = item.Kind;
        _context.PendingUnit = item.Unit; // after the kind: choosing a kind other than a catalogue unit clears it
        _context.SelectedShapes.Clear();
        _context.ActiveTool = ActiveTool.Shape;
        _context.RequestRedraw();
        UpdateToolButtons();
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateStatusPills();
        if (e.PropertyName is nameof(EditorContext.ActiveTool) or nameof(EditorContext.SelectedShapeKind))
        {
            UpdateToolButtons();
        }
    }

    /// <summary>The current tool of the tool bar; null while an object of the library is being placed.</summary>
    private ToolDefinition? CurrentTool
    {
        get
        {
            if (_context.ActiveTool == ActiveTool.Select)
            {
                return Tools[0];
            }

            return _libraryItem is not null && _context.SelectedShapeKind == _libraryItem.Kind
                ? null
                : Tools.FirstOrDefault(t => t.Tool == _context.ActiveTool && (t.Tool == ActiveTool.Freehand || t.Kind == _context.SelectedShapeKind)) ?? Tools[0];
        }
    }

    /// <summary>One line of how to use the current tool.</summary>
    private string ToolHint => Canvas.AreaMode
        ? "Area: drag a frame - to the right: what is completely inside; to the left (green): everything it touches. A tap adds or removes an object."
        : CurrentTool?.Hint
        ?? $"{_libraryItem?.Name}: tap to place it at its real size, or drag it open to any size.";

    private void UpdateToolButtons()
    {
        var current = CurrentTool;
        foreach (var (tool, button) in _toolButtons)
        {
            button.Classes.Set("active", ReferenceEquals(tool, current) && !Canvas.AreaMode);
        }

        _areaButton?.Classes.Set("active", Canvas.AreaMode);

        _moreButton?.Classes.Set("active", _toolButtons.Any(t => t.Tool.More && ReferenceEquals(t.Tool, current) && !Canvas.AreaMode));
        _objectsButton?.Classes.Set("active", LibraryPane.IsVisible || (current is null && _context.ActiveTool != ActiveTool.Select));
        ToolNameText.Text = Canvas.AreaMode ? "Area" : current?.Name ?? _libraryItem?.Name ?? "";
        ToolIcon.Data = Geometry.Parse(Canvas.AreaMode ? AreaIcon : current?.Icon ?? ObjectsIcon);
        UpdateCommands();
    }

    private void UpdatePathBar()
    {
        PathBar.IsVisible = Canvas.IsDrawingPath;
        PathLengthButton.IsVisible = Canvas.CanTypeLength;
        PathCloseButton.IsVisible = Canvas.CanClosePath;
        PathArcButton.IsVisible = Canvas.CanUseArcs;
        PathArcButton.Classes.Set("on", Canvas.ArcMode);
        PathCloseButton.Content = CurrentTool?.Tool == ActiveTool.PathDraw && _context.SelectedShapeKind == ShapeKind.Wall ? "Close room" : "Close shape";
        if (!Canvas.IsDrawingPath)
        {
            LengthLayer.IsVisible = false; // the wall is finished (e.g. a typed room was closed)
            return;
        }

        var points = Canvas.PathPointCount;
        var arcHint = !Canvas.ArcMode ? "" : Canvas.ArcWaitingForEnd ? " - arc: tap the end point" : " - arc: tap a point on the arc";
        PathBarText.Text = $"{CurrentTool?.Name}: {points} point{(points == 1 ? "" : "s")}{arcHint}";
        PathUndoButton.IsEnabled = points > 0 || Canvas.ArcWaitingForEnd;
        PathDoneButton.IsEnabled = points >= 2;
    }

    private void OnPathUndoPoint(object? sender, RoutedEventArgs e) => Canvas.RemoveLastPathPoint();

    private void OnPathArc(object? sender, RoutedEventArgs e) => Canvas.ToggleArcMode();

    private void OnPathCancel(object? sender, RoutedEventArgs e) => Canvas.CancelPath();

    private void OnPathDone(object? sender, RoutedEventArgs e) => Canvas.FinishPath();

    private void OnPathClose(object? sender, RoutedEventArgs e) => Canvas.ClosePath();
}
