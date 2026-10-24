using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>One field of the title block with the area it occupies.</summary>
public sealed record SheetCell(string Label, string Value, SKRect Bounds);

/// <summary>Where the frame, the title block and its five fields lie on a page.</summary>
public sealed record SheetLayout(SKRect Frame, SKRect Block, IReadOnlyList<SheetCell> Cells);

/// <summary>Draws the sheet of a page: a frame round the drawing area and a title block with Project, Address, Scale, Date and Position
/// at the chosen edge. It is drawn on top of the page, so prints and exports carry it too.</summary>
public static class SheetRenderer
{
    private const float Padding = 5f;
    private static readonly SKColor Ink = new(0x1B, 0x23, 0x32);
    private static readonly SKColor Muted = new(0x6B, 0x72, 0x80);

    private static readonly string[] Labels = ["PROJECT", "ADDRESS", "SCALE", "DATE", "POSITION"];
    private static readonly float[] LongWeights = [3f, 3f, 1f, 1.3f, 1.2f];
    private static readonly float[] ShortWeights = [2f, 2f, 1f, 1f, 1f];

    /// <summary>The layout of the sheet on a page of the given size, or null when the sheet is off or the page is too small for it.</summary>
    public static SheetLayout? Layout(SheetInfo sheet, double scaleDenominator, double pageWidth, double pageHeight)
    {
        if (!sheet.Enabled)
        {
            return null;
        }

        var margin = (float)Math.Max(0, sheet.MarginMm * SheetInfo.PxPerMm);
        var frame = new SKRect(margin, margin, (float)pageWidth - margin, (float)pageHeight - margin);
        if (frame.Width < 40 || frame.Height < 40)
        {
            return null;
        }

        // the block never takes more than 45% of the frame, so the drawing area stays usable
        var wanted = (float)(sheet.EffectiveBlockSizeMm * SheetInfo.PxPerMm);
        var thickness = Math.Clamp(wanted, 24f, (sheet.IsHorizontal ? frame.Height : frame.Width) * 0.45f);

        var block = sheet.Placement switch
        {
            SheetPlacement.Top => new SKRect(frame.Left, frame.Top, frame.Right, frame.Top + thickness),
            SheetPlacement.Left => new SKRect(frame.Left, frame.Top, frame.Left + thickness, frame.Bottom),
            SheetPlacement.Right => new SKRect(frame.Right - thickness, frame.Top, frame.Right, frame.Bottom),
            _ => new SKRect(frame.Left, frame.Bottom - thickness, frame.Right, frame.Bottom),
        };

        string[] values =
        [
            sheet.Project,
            sheet.Address,
            DrawingScale.Label(scaleDenominator),
            sheet.Date,
            sheet.Position,
        ];

        var weights = sheet.IsHorizontal ? LongWeights : ShortWeights;
        var total = weights.Sum();
        var cells = new List<SheetCell>();
        var offset = 0f;
        for (var i = 0; i < Labels.Length; i++)
        {
            var share = weights[i] / total;
            SKRect bounds;
            if (sheet.IsHorizontal)
            {
                var width = block.Width * share;
                bounds = new SKRect(block.Left + offset, block.Top, block.Left + offset + width, block.Bottom);
                offset += width;
            }
            else
            {
                var height = block.Height * share;
                bounds = new SKRect(block.Left, block.Top + offset, block.Right, block.Top + offset + height);
                offset += height;
            }

            cells.Add(new SheetCell(Labels[i], values[i], bounds));
        }

        return new SheetLayout(frame, block, cells);
    }

    public static void Draw(SKCanvas canvas, Document document)
    {
        if (Layout(document.Sheet, document.ScaleDenominator, document.Width, document.Height) is not { } layout)
        {
            return;
        }

        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, Color = Ink };
        using var thin = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.9f, Color = Ink };
        using var paper = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White };

        canvas.DrawRect(layout.Block, paper);
        canvas.DrawRect(layout.Frame, line);
        canvas.DrawRect(layout.Block, line);

        var horizontal = document.Sheet.IsHorizontal;
        for (var i = 0; i < layout.Cells.Count; i++)
        {
            var cell = layout.Cells[i];
            if (i > 0)
            {
                if (horizontal)
                {
                    canvas.DrawLine(cell.Bounds.Left, cell.Bounds.Top, cell.Bounds.Left, cell.Bounds.Bottom, thin);
                }
                else
                {
                    canvas.DrawLine(cell.Bounds.Left, cell.Bounds.Top, cell.Bounds.Right, cell.Bounds.Top, thin);
                }
            }

            DrawCell(canvas, cell);
        }
    }

    private static void DrawCell(SKCanvas canvas, SheetCell cell)
    {
        var area = new SKRect(cell.Bounds.Left + Padding, cell.Bounds.Top + Padding, cell.Bounds.Right - Padding, cell.Bounds.Bottom - Padding);
        if (area.Width < 8 || area.Height < 8)
        {
            return;
        }

        canvas.Save();
        canvas.ClipRect(cell.Bounds);

        var labelSize = Math.Clamp(area.Height * 0.2f, 6f, 8f);
        using var labelFont = new SKFont(FontCache.Get("Segoe UI"), labelSize);
        using var label = labelFont.GetTextPath(cell.Label, new SKPoint(area.Left, area.Top - labelFont.Metrics.Ascent));
        using var labelPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Muted };
        canvas.DrawPath(label, labelPaint);

        var valueArea = new SKRect(area.Left, area.Top + labelSize * 1.5f, area.Right, area.Bottom);
        if (!string.IsNullOrWhiteSpace(cell.Value) && valueArea.Height > 4)
        {
            using var valuePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Ink };
            DrawFitted(canvas, cell.Value.Trim(), valueArea, valuePaint);
        }

        canvas.Restore();
    }

    /// <summary>Writes the text in the largest bold size (18 px at most, 6 px at least) at which it fits the area, wrapping at spaces;
    /// text that still does not fit is cut off with "...".</summary>
    private static void DrawFitted(SKCanvas canvas, string text, SKRect area, SKPaint paint)
    {
        var typeface = FontCache.Get("Segoe UI", bold: true);
        var size = Math.Min(18f, area.Height);
        List<string> lines = [];
        SKFont? font = null;

        for (; size >= 6f; size -= 0.5f)
        {
            font?.Dispose();
            font = new SKFont(typeface, size);
            lines = Wrap(text, font, area.Width);
            if (lines.Count * font.Spacing <= area.Height && lines.All(l => font.MeasureText(l) <= area.Width + 0.5f))
            {
                break;
            }
        }

        font ??= new SKFont(typeface, 6f);
        using (font)
        {
            var spacing = font.Spacing;
            var fit = Math.Max(1, (int)Math.Floor(area.Height / spacing));
            if (lines.Count > fit)
            {
                lines = lines.Take(fit).ToList();
                lines[^1] = Ellipsize(lines[^1] + "...", font, area.Width);
            }

            var top = area.Top;
            foreach (var line in lines)
            {
                var shown = font.MeasureText(line) > area.Width ? Ellipsize(line, font, area.Width) : line;
                using var path = font.GetTextPath(shown, new SKPoint(area.Left, top - font.Metrics.Ascent));
                canvas.DrawPath(path, paint);
                top += spacing;
            }
        }
    }

    private static List<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var current = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && font.MeasureText(candidate) > maxWidth)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            lines.Add(current);
        }

        return lines;
    }

    /// <summary>The text shortened (ending in "...") until it fits the width.</summary>
    private static string Ellipsize(string text, SKFont font, float maxWidth)
    {
        var body = text.EndsWith("...", StringComparison.Ordinal) ? text[..^3] : text;
        while (body.Length > 0 && font.MeasureText(body + "...") > maxWidth)
        {
            body = body[..^1];
        }

        return body + "...";
    }
}
