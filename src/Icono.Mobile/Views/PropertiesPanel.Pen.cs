using Avalonia;
using Icono.Editor;

namespace Icono.Mobile.Views;

/// <summary>The settings of the Pen tool (handwriting), shown while it is the tool: colour, width on the paper and smoothing of the tremor.</summary>
public sealed partial class PropertiesPanel
{
    /// <summary>The Lucide eraser.</summary>
    private const string EraserIcon = "M21 21H8a2 2 0 0 1-1.42-.587l-3.994-3.999a2 2 0 0 1 0-2.828l10-10a2 2 0 0 1 2.829 0l5.999 6a2 2 0 0 1 0 2.828L12.834 21 M5.082 11.09 l8.828 8.828";

    /// <summary>A switch that is an icon key: lit blue when on. The note beside it says what it does in the state it is in.</summary>
    private void IconToggle(string iconData, string tip, bool on, Action<bool> set, string note)
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse(iconData), StrokeThickness = 1.2, StrokeLineCap = Avalonia.Media.PenLineCap.Round,
            StrokeJoin = Avalonia.Media.PenLineJoin.Round, Width = 19, Height = 19, Stretch = Avalonia.Media.Stretch.Uniform,
        };
        var key = new Avalonia.Controls.Button
        {
            Content = icon, Width = 44, Height = 40, CornerRadius = new Avalonia.CornerRadius(11), Padding = new Avalonia.Thickness(0), Classes = { "soft" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        key.MinHeight = 40;
        key.Classes.Set("on", on);
        icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, key.GetObservable(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty));
        Avalonia.Controls.ToolTip.SetTip(key, tip);
        key.Click += (_, _) => set(!on);

        var text = new Avalonia.Controls.TextBlock
        {
            Text = note, FontSize = 11.5, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Brush("MutedTextBrush"), Margin = new Avalonia.Thickness(12, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var row = new Avalonia.Controls.Grid { ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions("Auto,*") };
        row.Children.Add(key);
        Avalonia.Controls.Grid.SetColumn(text, 1);
        row.Children.Add(text);
        _into.Children.Add(row);
    }

    private void BuildPen(EditorContext context)
    {
        Title("Pen");
        Note("Write or draw with the pen or a finger. Every stroke is one object you can move, recolour, delete or undo. " +
             "Two fingers move and zoom the page.");

        Heading("PRESSURE");
        Toggle("Pen pressure", context.PenPressure, on => context.PenPressure = on);
        if (context.PenPressure)
        {
            // 0 = always one width, 7 (the start) = 0.3 to 1.7 times the pen width, 10 = from a hairline to twice the width
            NumberRow("Sensitivity", Math.Round(context.PenPressureStrength * 10), "", 1, 0, 10, "0", v => context.PenPressureStrength = v / 10);
        }

        Note("With a pen that senses pressure the line gets thinner when you press lightly and thicker when you press hard. The Width below is the line at half pressure. Sensitivity: 0 = always the same width, 10 = from a hairline to twice the width. A finger writes one width.");

        Heading("SHAPE CORRECTION");
        Toggle("Hold to make shapes clean", context.PenSnapShapes, on => context.PenSnapShapes = on);
        Note("Draw a circle, line, rectangle or triangle and keep the pen or finger still for a moment: it becomes the clean shape. Lift to keep it, or move on to go on drawing freehand.");

        Heading("ERASER");
        IconToggle(EraserIcon, "Erase strokes: drag over them", context.PenEraser, on => context.PenEraser = on,
            context.PenEraser
                ? "Eraser on: touching a stroke deletes it. Only handwriting is erased - never walls, furniture or text. Undo brings a stroke back."
                : "Eraser off: the pen writes. A pen with an eraser end erases with that end.");

        Heading("COLOUR");
        ColorRow(context.PenColor, color => context.PenColor = color);

        Heading("WIDTH");
        NumberRow("Width", context.PenWidth / PxPerCm * 10, "mm", 0.1, 0.2, 15, "0.0", mm => context.PenWidth = mm / 10 * PxPerCm);

        Heading("SMOOTHING");
        NumberRow("Smoothing", context.PenSmoothing, "", 1, 0, 10, "0", value => context.PenSmoothing = value);
        Note("0 = exactly as written; higher values take the tremor out of the stroke.");
    }
}
