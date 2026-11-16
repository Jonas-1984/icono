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
    private sealed record ToolDefinition(string Name, ActiveTool Tool, ShapeKind Kind, string Icon, string Hint);

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
    ];

    private readonly List<(ToolDefinition Tool, Button Button)> _toolButtons = [];

    private void BuildTools()
    {
        foreach (var tool in Tools)
        {
            var icon = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(tool.Icon),
                StrokeThickness = 1.6,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Width = 19,
                Height = 19,
                Stretch = Stretch.Uniform,
            };
            var label = new TextBlock { Text = tool.Name, FontSize = 9.5, HorizontalAlignment = HorizontalAlignment.Center };
            var button = new Button
            {
                Classes = { "palette" },
                Content = new StackPanel { Spacing = 2, Children = { icon, label } },
            };

            // the icon takes the text colour of its button: muted, and bright for the active tool
            icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, button.GetObservable(TemplatedControl.ForegroundProperty));
            button.Click += (_, _) => ChooseTool(tool);
            ToolPanel.Children.Add(button);
            _toolButtons.Add((tool, button));
        }

        _context.PropertyChanged += OnContextPropertyChanged;
        Canvas.PathDrawingChanged += (_, _) => UpdatePathBar();
        UpdateToolButtons();
    }

    private void ChooseTool(ToolDefinition tool)
    {
        // a wall or line under way is kept when another tool is chosen
        if (Canvas.IsDrawingPath)
        {
            Canvas.FinishPath();
        }

        if (tool.Tool != ActiveTool.Select)
        {
            _context.SelectedShapeKind = tool.Kind;
            _context.CurrentDimensionKind = DimensionKind.Aligned;
            _context.SelectedShapes.Clear();
            _context.RequestRedraw();
        }

        _context.ActiveTool = tool.Tool;
        UpdateToolButtons();
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorContext.ActiveTool) or nameof(EditorContext.SelectedShapeKind))
        {
            UpdateToolButtons();
        }
    }

    /// <summary>The current tool, as in the tool bar.</summary>
    private ToolDefinition CurrentTool =>
        Tools.FirstOrDefault(t => t.Tool == _context.ActiveTool && (t.Tool == ActiveTool.Select || t.Kind == _context.SelectedShapeKind)) ?? Tools[0];

    private void UpdateToolButtons()
    {
        var current = CurrentTool;
        foreach (var (tool, button) in _toolButtons)
        {
            button.Classes.Set("active", ReferenceEquals(tool, current));
        }

        UpdateCommands();
    }

    private void UpdatePathBar()
    {
        PathBar.IsVisible = Canvas.IsDrawingPath;
        if (!Canvas.IsDrawingPath)
        {
            return;
        }

        var points = Canvas.PathPointCount;
        PathBarText.Text = $"{CurrentTool.Name}: {points} point{(points == 1 ? "" : "s")}";
        PathUndoButton.IsEnabled = points > 0;
        PathDoneButton.IsEnabled = points >= 2;
    }

    private void OnPathUndoPoint(object? sender, RoutedEventArgs e) => Canvas.RemoveLastPathPoint();

    private void OnPathCancel(object? sender, RoutedEventArgs e) => Canvas.CancelPath();

    private void OnPathDone(object? sender, RoutedEventArgs e) => Canvas.FinishPath();
}
