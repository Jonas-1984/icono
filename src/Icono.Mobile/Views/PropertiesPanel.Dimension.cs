using System.Globalization;
using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Mobile.Views;

/// <summary>The cards DIMENSION, DOOR / WINDOW / WALL OPENING and WALL of the Windows program: the look of the measurements (unit, decimals, marks,
/// sizes, where the number sits), what a door or window shows, and everything about a wall. A wall has one dimension per segment; the card edits
/// the one that is chosen (or the one whose number was tapped on the page) and keeps the setting in the wall (<see cref="WallDimensionOverride"/>).</summary>
public sealed partial class PropertiesPanel
{
    private static readonly (string Name, DimensionArrowStyle Value)[] ArrowStyles =
    [
        ("Arrow (filled)", DimensionArrowStyle.Arrow), ("Arrow (open)", DimensionArrowStyle.OpenArrow), ("Slash  /", DimensionArrowStyle.Slash),
        ("Backslash  \\", DimensionArrowStyle.Backslash), ("Double slash  //", DimensionArrowStyle.DoubleSlash), ("Dot", DimensionArrowStyle.Dot),
        ("Ring", DimensionArrowStyle.OpenDot), ("Square", DimensionArrowStyle.Box), ("None", DimensionArrowStyle.None),
    ];

    private static readonly (string Name, WallMeasure Value)[] WallMeasures =
    [
        ("Inside (clear size of the room)", WallMeasure.Inside), ("Centre line of the wall", WallMeasure.Centre), ("Outside of the wall", WallMeasure.Outside),
    ];

    /// <summary>The wall dimension (segment) the card edits, and the wall it was chosen for.</summary>
    private int _dimensionSegment;

    private Shape? _dimensionSegmentShape;

    private static bool HasDimensions(Shape shape) =>
        shape.Kind == ShapeKind.Dimension || (shape.Kind == ShapeKind.Wall && shape.WallShowDimensions)
        || (ArchitecturalSymbols.IsSymbol(shape.Kind) && (shape.SymbolShowDimension || shape.SymbolShowGaps));

    // ---------------------------------------------------------------- DIMENSION

    private void BuildDimension(EditorContext context, IReadOnlyList<Shape> shapes)
    {
        var targets = shapes.Where(HasDimensions).ToList();
        if (targets.Count == 0)
        {
            context.WallSegmentHighlight = null;
            return;
        }

        var shape = targets[0];
        var isWall = shape.Kind == ShapeKind.Wall;
        var isDimension = shape.Kind == ShapeKind.Dimension;
        var isSymbol = ArchitecturalSymbols.IsSymbol(shape.Kind);
        Heading("DIMENSION");

        if (isDimension)
        {
            var linked = shape.DimTarget is not null && shape.DimRole != DimensionRole.None;
            Note(linked
                ? "Attached to its shape: it moves and changes with it."
                : shape.DimAnchors is { Count: > 0 } && shape.DimTarget is null && shape.DimAnchors.Any(a => a is not null)
                    ? "Stuck to its shape by its points: it follows when the shape moves or is resized."
                    : "Free dimension (its points are its own).");
            if (linked)
            {
                ButtonRow(("Detach from the shape", () => context.SetOnSelection(s =>
                {
                    s.DimTarget = null;
                    s.DimRole = DimensionRole.None;
                    s.DimAnchors = null;
                }, s => s.Kind == ShapeKind.Dimension)));
            }
        }

        // a wall has one dimension per segment: the one that is chosen (or whose number was tapped) is edited
        var segment = -1;
        WallDimensionOverride? custom = null;
        if (isWall)
        {
            if (!ReferenceEquals(_dimensionSegmentShape, shape))
            {
                _dimensionSegmentShape = shape;
                _dimensionSegment = 0;
            }

            if (context.PickedDimensionSegment is { } picked)
            {
                _dimensionSegment = picked;
                context.PickedDimensionSegment = null;
            }

            var lengths = WallGeometry.Segments(shape);
            if (_dimensionSegment < 0 || _dimensionSegment >= lengths.Count)
            {
                _dimensionSegment = 0;
            }

            segment = _dimensionSegment;
            var options = new List<(string Name, int Value)>();
            for (var i = 0; i < lengths.Count; i++)
            {
                var hidden = WallRenderer.FindOverride(shape, i) is { Hidden: true };
                var measured = DimensionRenderer.Format(WallGeometry.MeasuredLength(shape, i), WallRenderer.SegmentUnit(shape, i), WallRenderer.SegmentPrecision(shape, i));
                options.Add(($"{i + 1}:  {measured}{(hidden ? "  (hidden)" : string.Empty)}", i));
            }

            Choice("Which", options, segment, i =>
            {
                _dimensionSegment = i;
                OnChanged();
            });
            custom = WallRenderer.FindOverride(shape, segment);
            context.WallSegmentHighlight = segment;
            Toggle("Show this one", custom is not { Hidden: true }, on => EditWallDimension(context, segment, c => c with { Hidden = !on }));
        }
        else
        {
            context.WallSegmentHighlight = null;
        }

        // sets a look of the chosen wall dimension, or of every selected dimension / door / window
        void Set(Func<WallDimensionOverride, WallDimensionOverride> perSegment, Action<Shape> perShape)
        {
            if (isWall && segment >= 0)
            {
                EditWallDimension(context, segment, perSegment);
            }
            else
            {
                context.SetOnSelection(perShape, HasDimensions);
            }
        }

        var unit = isWall ? custom?.Unit ?? shape.WallDimensionUnit : shape.DimUnit;
        Choice("Unit", Units.Select(u => (u.Name, u.Unit)).ToList(), unit,
            u => Set(c => c with { Unit = u }, s =>
            {
                if (s.Kind == ShapeKind.Dimension || ArchitecturalSymbols.IsSymbol(s.Kind))
                {
                    s.DimUnit = u;
                }
            }));
        Choice("Decimals", Enumerable.Range(0, 7).Select(i => (i.ToString(CultureInfo.InvariantCulture), i)).ToList(), custom?.Precision ?? shape.DimPrecision,
            places => Set(c => c with { Precision = places }, s => s.DimPrecision = places));
        Choice("Marks", ArrowStyles, custom?.ArrowStyle ?? shape.DimArrowStyle, a => Set(c => c with { ArrowStyle = a }, s => s.DimArrowStyle = a));

        var style = DimensionStyle.For(shape, custom);
        var fonts = FontFamilies().ToList();
        if (!fonts.Any(f => string.Equals(f.Value, style.FontFamily, StringComparison.OrdinalIgnoreCase)))
        {
            fonts.Insert(0, (style.FontFamily, style.FontFamily));
        }

        Choice("Font", fonts, fonts.First(f => string.Equals(f.Value, style.FontFamily, StringComparison.OrdinalIgnoreCase)).Value,
            name => Set(c => c with { FontFamily = name }, s => s.TextFontFamily = name));
        NumberRow("Number size", style.FontSize, "px", 1, 4, 200, "0.#", v => Set(c => c with { FontSize = v }, s => s.DimFontSize = v));
        NumberRow("Line width", style.LineWidth, "px", 0.5, 0.1, 30, "0.#", v => Set(c => c with { LineWidth = v }, s => s.DimLineWidth = v));
        NumberRow("Mark size", style.ArrowSize, "px", 1, 1, 200, "0.#", v => Set(c => c with { ArrowSize = v }, s => s.DimArrowSize = v));
        NumberRow("Gap", style.ExtGap, "px", 1, 0, 200, "0.#", v => Set(c => c with { ExtGap = v }, s => s.DimExtGap = v));
        NumberRow("Overshoot", style.ExtOvershoot, "px", 1, 0, 200, "0.#", v => Set(c => c with { ExtOvershoot = v }, s => s.DimExtOvershoot = v));
        NumberRow("Number at", (custom?.TextPosition ?? shape.DimTextPosition ?? 0.5) * 100, "%", 5, 0, 100, "0.#",
            v => Set(c => c with { TextPosition = v / 100 }, s => s.DimTextPosition = v / 100));
        NumberRow("Number off", custom?.TextShift ?? shape.DimTextShift, "px", 2, -300, 300, "0.#", v => Set(c => c with { TextShift = v }, s => s.DimTextShift = v));
        ButtonRow(("Reset the look", () => Set(c => c.WithoutStyle(), s =>
        {
            s.DimFontSize = null;
            s.DimLineWidth = null;
            s.DimArrowSize = null;
            s.DimExtGap = null;
            s.DimExtOvershoot = null;
            s.TextFontFamily = null;
        })));
        if (isSymbol)
        {
            Note("The same look is used for all dimensions of doors and windows you have selected.");
        }
    }

    /// <summary>Changes the setting of one dimension of every selected wall (one undo step); a setting that is the default again is dropped.</summary>
    private static void EditWallDimension(EditorContext context, int segment, Func<WallDimensionOverride, WallDimensionOverride> change) =>
        context.SetOnSelection(s =>
        {
            var list = (s.WallDimensionOverrides ?? []).ToList();
            var current = list.FirstOrDefault(o => o.Segment == segment) ?? new WallDimensionOverride(segment);
            var updated = change(current);
            list.RemoveAll(o => o.Segment == segment);
            if (!updated.IsDefault)
            {
                list.Add(updated);
            }

            s.WallDimensionOverrides = list.Count == 0 ? null : list.OrderBy(o => o.Segment).ToList();
        }, s => s.Kind == ShapeKind.Wall);

    // ---------------------------------------------------------------- DOOR / WINDOW / WALL OPENING

    private void BuildSymbol(EditorContext context, Shape symbol)
    {
        var isDoor = ArchitecturalSymbols.IsDoor(symbol.Kind);
        Heading(isDoor ? "DOOR" : ArchitecturalSymbols.IsWindow(symbol.Kind) ? "WINDOW" : "WALL OPENING");
        Note(isDoor
            ? "Lay the door over a wall: it cuts the wall open. Its width is the opening, the strip at the bottom is the wall."
            : "Lay it over a wall: it cuts the wall open. Its height is the wall thickness.");
        NumberRow("Width", ToRealCm(symbol.Width), "cm", 5, 10, 1000, "0.#", cm => context.SetSymbolWidth(symbol, FromRealCm(cm)));
        Toggle("Show the width", symbol.SymbolShowDimension,
            on => context.SetOnSelection(s => s.SymbolShowDimension = on, s => ArchitecturalSymbols.IsSymbol(s.Kind)));
        Toggle("Show the gaps to the wall", symbol.SymbolShowGaps,
            on => context.SetOnSelection(s => s.SymbolShowGaps = on, s => ArchitecturalSymbols.IsSymbol(s.Kind)));
        if (isDoor)
        {
            NumberRow("Wall thickness", ToRealCm(symbol.WallThickness), "cm", 1, 1, 500, "0.#",
                cm => context.SetOnSelection(s => s.WallThickness = Math.Max(1, FromRealCm(cm)), s => ArchitecturalSymbols.IsDoor(s.Kind)));
            NumberRow("Opens by", symbol.DoorOpenAngle, "°", 15, 0, 120, "0", angle => context.SetOnSelection(s => s.DoorOpenAngle = angle, s => ArchitecturalSymbols.IsDoor(s.Kind)));
            ButtonRow(
                ("Closed 0°", () => context.SetOnSelection(s => s.DoorOpenAngle = 0, s => ArchitecturalSymbols.IsDoor(s.Kind))),
                ("45°", () => context.SetOnSelection(s => s.DoorOpenAngle = 45, s => ArchitecturalSymbols.IsDoor(s.Kind))),
                ("90°", () => context.SetOnSelection(s => s.DoorOpenAngle = 90, s => ArchitecturalSymbols.IsDoor(s.Kind))));
        }
    }

    // ---------------------------------------------------------------- WALL

    private void BuildWall(EditorContext context, Shape wall)
    {
        bool IsWall(Shape s) => s.Kind == ShapeKind.Wall;
        Heading("WALL");
        NumberRow("Thickness", ToRealCm(wall.WallThickness), "cm", 1, 1, 500, "0.#", cm => context.SetOnSelection(s => s.WallThickness = Math.Max(0.5, FromRealCm(cm)), IsWall));
        if (wall.Points is { Count: >= 3 })
        {
            Toggle("Closed (a room)", wall.WallClosed, on => context.SetOnSelection(s =>
            {
                s.WallClosed = on && s.Points is { Count: >= 3 };
                WallEditor.PruneOverrides(s); // an opened ring has one segment less
            }, IsWall));
        }

        Choice("Hatch", Enum.GetValues<HatchKind>().Select(h => (Words(h.ToString()), h)).ToList(), wall.Hatch, h => context.SetOnSelection(s => s.Hatch = h, IsWall));
        if (wall.Hatch != HatchKind.Solid)
        {
            NumberRow("Hatch spacing", wall.HatchSpacing, "px", 1, 3, 40, "0.#", v => context.SetOnSelection(s => s.HatchSpacing = v, IsWall));
            ColorRow(wall.HatchColor, c => context.SetOnSelection(s => s.HatchColor = c, IsWall));
        }

        Toggle("Dimensions", wall.WallShowDimensions, on => context.SetOnSelection(s => s.WallShowDimensions = on, IsWall));
        Choice("Dimension unit", Units.Select(u => (u.Name, u.Unit)).ToList(), wall.WallDimensionUnit, u => context.SetOnSelection(s => s.WallDimensionUnit = u, IsWall));
        Choice("Measures", WallMeasures, wall.WallDimensionMeasure, m => context.SetOnSelection(s => s.WallDimensionMeasure = m, IsWall));
        Note("Join, close, merge or split walls: ARRANGE below.");
    }
}
