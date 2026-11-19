using System.Text.Json;
using Icono.Core;
using Icono.Rendering;

namespace Icono.Editor;

/// <summary>The settings of the editor that a user chooses once and expects to find again: the pen (colour, width, smoothing, pressure and its sensitivity, shape
/// correction), the size of the curve corner, keeping the proportions, the grid, the rulers and everything that is snapped to. Kept in a small file of the app's
/// own data folder; a file that is missing or damaged leaves the usual start values. Shared by every app.</summary>
public sealed class EditorSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string? PenColor { get; set; }

    public double? PenWidth { get; set; }

    public double? PenSmoothing { get; set; }

    public bool? PenPressure { get; set; }

    public double? PenPressureStrength { get; set; }

    public bool? PenSnapShapes { get; set; }

    public double? CurveSizeCm { get; set; }

    public bool? KeepProportions { get; set; }

    public bool? ShowGrid { get; set; }

    public bool? SnapToGrid { get; set; }

    public bool? ShowRulers { get; set; }

    public bool? PointSnapEnabled { get; set; }

    /// <summary>The names of the kinds of point that are switched on (<see cref="PointSnapKind"/>).</summary>
    public List<string>? PointSnapKinds { get; set; }

    public bool? ObjectSnapEnabled { get; set; }

    public bool? ObjectSnapWalls { get; set; }

    public bool? ObjectSnapObjects { get; set; }

    public bool? WallSnapEnabled { get; set; }

    public bool? WallSnapAxis { get; set; }

    public bool? WallSnapParallel { get; set; }

    public bool? WallSnapAlign { get; set; }

    public bool? WallSnapEqualLength { get; set; }

    /// <summary>The settings as they are now.</summary>
    public static EditorSettings From(EditorContext context) => new()
    {
        PenColor = $"#{context.PenColor.A:X2}{context.PenColor.R:X2}{context.PenColor.G:X2}{context.PenColor.B:X2}",
        PenWidth = context.PenWidth,
        PenSmoothing = context.PenSmoothing,
        PenPressure = context.PenPressure,
        PenPressureStrength = context.PenPressureStrength,
        PenSnapShapes = context.PenSnapShapes,
        CurveSizeCm = context.CurveSizeCm,
        KeepProportions = context.KeepProportions,
        ShowGrid = context.ShowGrid,
        SnapToGrid = context.SnapToGrid,
        ShowRulers = context.ShowRulers,
        PointSnapEnabled = context.PointSnap.Enabled,
        PointSnapKinds = Enum.GetValues<PointSnapKind>().Where(context.PointSnap.IsOn).Select(k => k.ToString()).ToList(),
        ObjectSnapEnabled = context.ObjectSnap.Enabled,
        ObjectSnapWalls = context.ObjectSnap.Walls,
        ObjectSnapObjects = context.ObjectSnap.Objects,
        WallSnapEnabled = context.WallSnap.Enabled,
        WallSnapAxis = context.WallSnap.Axis,
        WallSnapParallel = context.WallSnap.Parallel,
        WallSnapAlign = context.WallSnap.Align,
        WallSnapEqualLength = context.WallSnap.EqualLength,
    };

    /// <summary>Puts the settings into the context; every value that is missing or not usable is left as it is.</summary>
    public void ApplyTo(EditorContext context)
    {
        if (PenColor is { } text && TryColour(text, out var colour))
        {
            context.PenColor = colour;
        }

        if (PenWidth is { } width && double.IsFinite(width))
        {
            context.PenWidth = width;
        }

        if (PenSmoothing is { } smoothing && double.IsFinite(smoothing))
        {
            context.PenSmoothing = smoothing;
        }

        if (PenPressureStrength is { } strength && double.IsFinite(strength))
        {
            context.PenPressureStrength = strength;
        }

        if (CurveSizeCm is { } curve && double.IsFinite(curve))
        {
            context.CurveSizeCm = curve;
        }

        context.PenPressure = PenPressure ?? context.PenPressure;
        context.PenSnapShapes = PenSnapShapes ?? context.PenSnapShapes;
        context.KeepProportions = KeepProportions ?? context.KeepProportions;
        context.ShowGrid = ShowGrid ?? context.ShowGrid;
        context.SnapToGrid = SnapToGrid ?? context.SnapToGrid;
        context.ShowRulers = ShowRulers ?? context.ShowRulers;
        context.PointSnap.Enabled = PointSnapEnabled ?? context.PointSnap.Enabled;
        if (PointSnapKinds is { } kinds)
        {
            foreach (var kind in Enum.GetValues<PointSnapKind>())
            {
                context.PointSnap.Set(kind, kinds.Contains(kind.ToString()));
            }
        }

        context.ObjectSnap.Enabled = ObjectSnapEnabled ?? context.ObjectSnap.Enabled;
        context.ObjectSnap.Walls = ObjectSnapWalls ?? context.ObjectSnap.Walls;
        context.ObjectSnap.Objects = ObjectSnapObjects ?? context.ObjectSnap.Objects;
        context.WallSnap.Enabled = WallSnapEnabled ?? context.WallSnap.Enabled;
        context.WallSnap.Axis = WallSnapAxis ?? context.WallSnap.Axis;
        context.WallSnap.Parallel = WallSnapParallel ?? context.WallSnap.Parallel;
        context.WallSnap.Align = WallSnapAlign ?? context.WallSnap.Align;
        context.WallSnap.EqualLength = WallSnapEqualLength ?? context.WallSnap.EqualLength;
    }

    /// <summary>Reads the settings of a file into the context. False when there is no file or it cannot be read (nothing is changed then).</summary>
    public static bool Load(EditorContext context, string path)
    {
        try
        {
            if (!File.Exists(path) || JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(path), Json) is not { } settings)
            {
                return false;
            }

            settings.ApplyTo(context);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Writes the settings of the context into the file (through a temporary file, so a crash cannot leave half a file). False when it could not be written.</summary>
    public static bool Save(EditorContext context, string path)
    {
        try
        {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(From(context), Json));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryColour(string text, out ArgbColor colour)
    {
        colour = default;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length != 8 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        colour = new ArgbColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }
}
