using System.Globalization;

namespace Icono.App.State;

/// <summary>The unit the page is measured in (CANVAS SIZE card): the page size and the main grid are shown and typed in it. Values stay stored in px.</summary>
public enum PageUnit
{
    Px,
    Cm,
    Mm,
    In,
}

public static class PageUnits
{
    private const double PxPerInch = 96.0;
    private const double MmPerInch = 25.4;

    public static double FromPx(double px, PageUnit unit) => unit switch
    {
        PageUnit.Cm => px / PxPerInch * (MmPerInch / 10),
        PageUnit.Mm => px / PxPerInch * MmPerInch,
        PageUnit.In => px / PxPerInch,
        _ => px,
    };

    public static double ToPx(double value, PageUnit unit) => unit switch
    {
        PageUnit.Cm => value / (MmPerInch / 10) * PxPerInch,
        PageUnit.Mm => value / MmPerInch * PxPerInch,
        PageUnit.In => value * PxPerInch,
        _ => value,
    };

    public static string Name(PageUnit unit) => unit switch
    {
        PageUnit.Cm => "cm",
        PageUnit.Mm => "mm",
        PageUnit.In => "inch",
        _ => "px",
    };

    public static string Format(double px, PageUnit unit) =>
        FromPx(px, unit).ToString(unit == PageUnit.In ? "0.###" : "0.##", CultureInfo.InvariantCulture);
}
