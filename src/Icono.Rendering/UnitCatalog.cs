namespace Icono.Rendering;

/// <summary>One cabinet, unit or fitting of the catalogues: its real size in cm and its drawing, fitted into the 24 x 24 box that every
/// combined path of icono uses (so Width / Height of the shape give the proportion).</summary>
public sealed record UnitObject(string Id, string Name, double WidthCm, double HeightCm, string PathData, string Group = UnitCatalog.CabinetsGroup);

/// <summary>The kitchen catalogues: cabinets and units in plan and front view, sinks, kitchen elements and computers.</summary>
public static partial class UnitCatalog
{
    public const string CabinetsGroup = "Cabinets & units";

    private static IReadOnlyList<UnitObject>? _all;

    public static IReadOnlyList<UnitObject> All => _all ??= Collect();

    /// <summary>True when this build can contain the catalogues kept on this PC only (see UnitCatalog.Local.cs): never in Release builds, the
    /// ones handed to other people, and never in a clone of the repository that does not have those files.</summary>
    public const bool LocalCataloguesAllowed =
#if EXCLUDE_UNVERIFIED_CATALOGUES
        false;
#else
        true;
#endif

    static partial void AddLocalCatalogues(List<UnitObject> all);

    private static List<UnitObject> Collect()
    {
        List<UnitObject> all = [.. Generated, .. GeneratedKitchenElements, .. GeneratedComputers];
        if (LocalCataloguesAllowed)
        {
            AddLocalCatalogues(all);
        }

        return all;
    }

    public static UnitObject? Find(string id) => All.FirstOrDefault(u => u.Id == id);

    /// <summary>The size (px) of a unit that is only clicked: its real size in cm at the drawing scale (1 cm of the drawing = 1 cm real at 1:1, 96 dpi).</summary>
    public static (double Width, double Height) DefaultSize(UnitObject unit, double scaleDenominator)
    {
        var factor = 96 / 2.54 / Math.Max(1, scaleDenominator);
        return (Math.Max(6, unit.WidthCm * factor), Math.Max(6, unit.HeightCm * factor));
    }
}
