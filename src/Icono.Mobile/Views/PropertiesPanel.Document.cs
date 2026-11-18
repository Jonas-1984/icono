using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>The cards of the page as the Windows program has them: CANVAS SIZE (paper presets, orientation, unit, width and height), SHEET (frame and
/// title block) and BACKGROUND (colour, blur backdrop, picture) - shown while nothing is selected - and the DESIGN STYLE of a shape.</summary>
public sealed partial class PropertiesPanel
{
    /// <summary>Raised when the background picture is to be chosen (the window shows its file window and hands the picture back).</summary>
    public event EventHandler? BackgroundImageRequested;

    // ---------------------------------------------------------------- CANVAS SIZE

    private static readonly (string Name, double WidthMm, double HeightMm)[] Papers =
    [
        ("A5", 148, 210), ("A4", 210, 297), ("A3", 297, 420), ("Letter", 215.9, 279.4),
    ];

    private void BuildCanvasSize(EditorContext context)
    {
        var document = context.Document;
        Heading("CANVAS SIZE");

        // a paper size keeps the orientation the page has now
        void Paper(double widthMm, double heightMm)
        {
            var width = (int)Math.Round(PageUnits.ToPx(widthMm, PageUnit.Mm));
            var height = (int)Math.Round(PageUnits.ToPx(heightMm, PageUnit.Mm));
            var landscape = document.Width > document.Height;
            context.ResizePages(landscape ? height : width, landscape ? width : height);
            context.RequestRedraw();
        }

        void Square(int px)
        {
            context.ResizePages(px, px);
            context.RequestRedraw();
        }

        ButtonRow(Papers.Select(p => (p.Name, (Action)(() => Paper(p.WidthMm, p.HeightMm)))).ToArray());
        ButtonRow(("64 px", () => Square(64)), ("128 px", () => Square(128)), ("256 px", () => Square(256)), ("512 px", () => Square(512)));

        Choice("Turn", [("Portrait", false), ("Landscape", true)], document.Width > document.Height, landscape =>
        {
            if (landscape != document.Width > document.Height && document.Width != document.Height)
            {
                context.ResizePages(document.Height, document.Width);
                context.RequestRedraw();
            }
        });
        Choice("Unit", [("Millimetres", PageUnit.Mm), ("Centimetres", PageUnit.Cm), ("Inches", PageUnit.In), ("Pixels", PageUnit.Px)], context.PageUnit,
            unit => context.PageUnit = unit);

        var unit = context.PageUnit;
        var step = unit switch { PageUnit.Px => 8.0, PageUnit.Mm => 10.0, PageUnit.Cm => 1.0, _ => 0.5 };
        var name = PageUnits.Name(unit);
        NumberRow("Width", PageUnits.FromPx(document.Width, unit), name, step, 0.1, 100000, "0.##",
            v => Resize(context, (int)Math.Round(PageUnits.ToPx(v, unit)), document.Height));
        NumberRow("Height", PageUnits.FromPx(document.Height, unit), name, step, 0.1, 100000, "0.##",
            v => Resize(context, document.Width, (int)Math.Round(PageUnits.ToPx(v, unit))));
        Note("Every page of the drawing has this size.");
    }

    private static void Resize(EditorContext context, int width, int height)
    {
        context.ResizePages(Math.Clamp(width, 1, 10000), Math.Clamp(height, 1, 10000));
        context.RequestRedraw();
    }

    // ---------------------------------------------------------------- SHEET

    private void BuildSheet(EditorContext context)
    {
        var sheet = context.Document.Sheet;
        Heading("SHEET");
        Toggle("Frame and title block", sheet.Enabled, on =>
        {
            if (on != context.Document.Sheet.Enabled)
            {
                context.ToggleSheet();
            }
        });
        if (!sheet.Enabled)
        {
            return;
        }

        void Field(string label, string value, Func<SheetInfo, string, SheetInfo> set)
        {
            var shown = string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Length > 28 ? value[..28] + "…" : value;
            ButtonRow(($"{label}: {shown}", () => AskSheetText(context, label, value, text => context.SetSheet(set(context.Document.Sheet, text)))));
        }

        Field("Project", sheet.Project, (s, t) => s with { Project = t });
        Field("Address", sheet.Address, (s, t) => s with { Address = t });
        Field("Position", sheet.Position, (s, t) => s with { Position = t });
        Field("Date", sheet.Date, (s, t) => s with { Date = t });
        ButtonRow(("Date: today", () => context.SetSheet(context.Document.Sheet with { Date = EditorContext.TodayText() })));
        Choice("Title block", [("At the bottom", SheetPlacement.Bottom), ("At the top", SheetPlacement.Top), ("At the left", SheetPlacement.Left),
            ("At the right", SheetPlacement.Right)], sheet.Placement, p => context.SetSheet(context.Document.Sheet with { Placement = p }));
        NumberRow("Margin", sheet.MarginMm, "mm", 1, 0, 100, "0.#", v => context.SetSheet(context.Document.Sheet with { MarginMm = v }));
        NumberRow("Block size", sheet.BlockSizeMm, "mm", 5, 0, 300, "0.#", v => context.SetSheet(context.Document.Sheet with { BlockSizeMm = v }));
        Note($"Block size 0 = automatic. The scale in the title block is 1:{context.Document.ScaleDenominator:0.##} (set it under SCALE).");
    }

    private void AskSheetText(EditorContext context, string label, string current, Action<string> set) =>
        TextBoxRequested?.Invoke(this, new LengthRequest(label, "Written in the title block of the sheet.", current, text =>
        {
            if (text is not null)
            {
                set(text.Trim());
            }
        }));

    // ---------------------------------------------------------------- BACKGROUND

    private void BuildBackground(EditorContext context)
    {
        var document = context.Document;
        void Edit(Action<Document> change) => DocumentBackgroundEditing.Edit(context, change);

        Heading("BACKGROUND");
        Choice("Kind", [("Transparent", BackgroundMode.Transparent), ("Colour", BackgroundMode.SolidColor), ("Blur backdrop", BackgroundMode.Blur),
            ("Picture", BackgroundMode.Image)], document.BackgroundMode, mode => Edit(d => d.BackgroundMode = mode));

        switch (document.BackgroundMode)
        {
            case BackgroundMode.SolidColor:
                ColorRow(document.BackgroundColor, c => Edit(d => d.BackgroundColor = c));
                break;
            case BackgroundMode.Blur:
                ColorRow(document.BackgroundBlurColor, c => Edit(d => d.BackgroundBlurColor = c));
                Toggle("Second colour (gradient)", document.BackgroundBlurGradientEnabled, on => Edit(d => d.BackgroundBlurGradientEnabled = on));
                if (document.BackgroundBlurGradientEnabled)
                {
                    ColorRow(document.BackgroundBlurGradientColor, c => Edit(d => d.BackgroundBlurGradientColor = c));
                }

                NumberRow("Blur", document.BackgroundBlurAmount, "%", 5, 0, 100, "0", v => Edit(d => d.BackgroundBlurAmount = v));
                NumberRow("Fade", document.BackgroundFadeAmount, "%", 5, 0, 100, "0", v => Edit(d => d.BackgroundFadeAmount = v));
                NumberRow("Opacity", document.BackgroundOpacity, "%", 5, 0, 100, "0", v => Edit(d => d.BackgroundOpacity = v));
                break;
            case BackgroundMode.Image:
                var has = document.BackgroundImageBytes is { Length: > 0 };
                Note(has ? document.BackgroundImageName ?? "Imported picture" : "No picture chosen yet.");
                ButtonRow(
                    (has ? "Choose another picture..." : "Choose a picture...", () => BackgroundImageRequested?.Invoke(this, EventArgs.Empty)),
                    ("Remove", () => Edit(d =>
                    {
                        d.BackgroundImageBytes = null;
                        d.BackgroundImageName = null;
                    })));
                Choice("Fit", [("Cover the page", BackgroundImageFit.Cover), ("Whole picture", BackgroundImageFit.Contain), ("Stretch", BackgroundImageFit.Stretch)],
                    document.BackgroundImageFit, fit => Edit(d => d.BackgroundImageFit = fit));
                if (document.BackgroundImageFit == BackgroundImageFit.Contain)
                {
                    Note("The colour behind the picture:");
                    ColorRow(document.BackgroundColor, c => Edit(d => d.BackgroundColor = c));
                }

                break;
        }
    }

    // ---------------------------------------------------------------- DESIGN STYLE

    /// <summary>The ready-made looks (glass, neumorphism, neon ...): a tap sets border, effects and colours that suit the style; they can be changed
    /// afterwards in BORDER and OPACITY, BLUR &amp; SHADOW.</summary>
    private void BuildDesignStyle(EditorContext context, IReadOnlyList<Shape> shapes)
    {
        var supported = shapes.Where(DesignStyleRenderer.Supports).ToList();
        if (supported.Count == 0)
        {
            return;
        }

        var sample = supported[0];
        Heading("DESIGN STYLE");
        var tiles = new UniformGrid { Columns = 3 };
        foreach (var kind in Enum.GetValues<DesignStyleKind>())
        {
            // the sample shows the style in the colours of the selected shape
            var preview = new Shape
            {
                Kind = ShapeKind.RoundedRectangle, X = 0, Y = 0, Width = 60, Height = 60, CornerRadius = 14, FillColor = sample.FillColor,
                BorderColor = sample.BorderColor, BorderThickness = 2, DesignStyle = kind,
            };
            DesignStylePresets.Apply(preview, kind);
            var picture = new ObjectThumbnail(preview) { Width = 66, Height = 56 };
            var caption = new TextBlock
            {
                Text = kind == DesignStyleKind.None ? "None" : Words(kind.ToString()), FontSize = 9, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("ForegroundTextBrush"),
            };
            var current = sample.DesignStyle == kind;
            var button = new Button
            {
                Content = new StackPanel { Spacing = 3, Children = { picture, caption } }, Padding = new Thickness(3), Margin = new Thickness(2),
                Background = Brushes.Transparent, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, BorderBrush = current ? Brush("AccentBrush") : Brushes.Transparent,
                BorderThickness = new Thickness(2),
            };
            var chosen = kind;
            button.Click += (_, _) => context.SetOnSelection(s => DesignStylePresets.Apply(s, chosen), DesignStyleRenderer.Supports);
            tiles.Children.Add(button);
        }

        _into.Children.Add(tiles);
    }
}
