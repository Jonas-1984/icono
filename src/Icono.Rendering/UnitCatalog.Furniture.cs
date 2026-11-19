using System.Globalization;
using System.Text;

namespace Icono.Rendering;

/// <summary>Furniture and bathroom fittings for floor plans, drawn in plan view (seen from above) at their real size in cm. Every object is made of a few simple
/// outlines (rectangles with rounded corners, circles, ovals, lines) that are written as SVG path data in the 24 x 24 box of a combined path, like the other catalogues.
/// The top of the drawing is the head, the back or the wall side.</summary>
public static partial class UnitCatalog
{
    public const string FurnitureGroup = "Furniture";

    public const string BathroomGroup = "Bathroom";

    private static readonly UnitObject[] GeneratedFurniture = BuildFurniture();

    /// <summary>The outlines of one object in cm; <see cref="Data"/> gives them in the 24 x 24 box.</summary>
    private sealed class PlanDrawing(double widthCm, double heightCm)
    {
        private readonly StringBuilder _data = new();

        public string Data => _data.ToString().Trim();

        private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private string X(double cm) => N(cm * 24 / widthCm);

        private string Y(double cm) => N(cm * 24 / heightCm);

        private string Point(double x, double y) => X(x) + " " + Y(y);

        /// <summary>A rectangle with the corners rounded by <paramref name="radius"/> cm.</summary>
        public PlanDrawing Rect(double x, double y, double w, double h, double radius = 0)
        {
            var r = Math.Min(radius, Math.Min(w, h) / 2);
            if (r <= 0.01)
            {
                _data.Append($"M{Point(x, y)}L{Point(x + w, y)}L{Point(x + w, y + h)}L{Point(x, y + h)}Z ");
                return this;
            }

            var arc = $"A{X(r)} {Y(r)} 0 0 1 ";
            _data.Append($"M{Point(x + r, y)}L{Point(x + w - r, y)}{arc}{Point(x + w, y + r)}L{Point(x + w, y + h - r)}{arc}{Point(x + w - r, y + h)}");
            _data.Append($"L{Point(x + r, y + h)}{arc}{Point(x, y + h - r)}L{Point(x, y + r)}{arc}{Point(x + r, y)}Z ");
            return this;
        }

        public PlanDrawing Oval(double cx, double cy, double rx, double ry)
        {
            var arc = $"A{X(rx)} {Y(ry)} 0 0 1 ";
            _data.Append($"M{Point(cx - rx, cy)}{arc}{Point(cx + rx, cy)}{arc}{Point(cx - rx, cy)}Z ");
            return this;
        }

        public PlanDrawing Circle(double cx, double cy, double r) => Oval(cx, cy, r, r);

        public PlanDrawing Line(double x1, double y1, double x2, double y2)
        {
            _data.Append($"M{Point(x1, y1)}L{Point(x2, y2)} ");
            return this;
        }
    }

    private static UnitObject MakeObject(string id, string name, double width, double height, string group, Action<PlanDrawing> draw)
    {
        var drawing = new PlanDrawing(width, height);
        draw(drawing);
        return new UnitObject(id, name, width, height, drawing.Data, group);
    }

    private static UnitObject Furniture(string id, string name, double width, double height, Action<PlanDrawing> draw) =>
        MakeObject(id, name, width, height, FurnitureGroup, draw);

    private static UnitObject Bathroom(string id, string name, double width, double height, Action<PlanDrawing> draw) =>
        MakeObject(id, name, width, height, BathroomGroup, draw);

    private static UnitObject Bed(string id, double width)
    {
        var name = $"Bed {width:0} x 200 cm (plan)";
        return Furniture(id, name, width, 200, d =>
        {
            d.Rect(0, 0, width, 200, 4);
            if (width >= 140)
            {
                var pillow = (width - 30) / 2; // two pillows at the head
                d.Rect(10, 6, pillow, 32, 8).Rect(20 + pillow, 6, pillow, 32, 8);
            }
            else
            {
                d.Rect(width * 0.15, 6, width * 0.7, 32, 8);
            }

            d.Rect(4, 62, width - 8, 134, 6); // the duvet
        });
    }

    private static UnitObject Sofa(string id, string name, double width, int seats, double depth = 90, double arm = 18)
    {
        return Furniture(id, name, width, depth, d =>
        {
            d.Rect(0, 0, width, depth, 8);
            d.Rect(0, 0, width, 22, 8); // the back
            d.Rect(0, 22, arm, depth - 22, 6).Rect(width - arm, 22, arm, depth - 22, 6); // the arms
            var seat = (width - 2 * arm) / seats;
            for (var i = 1; i < seats; i++)
            {
                d.Line(arm + i * seat, 24, arm + i * seat, depth - 3);
            }
        });
    }

    private static UnitObject Wardrobe(string id, double width)
    {
        var doors = Math.Max(2, (int)Math.Round(width / 50));
        return Furniture(id, $"Wardrobe {width:0} x 60 cm (plan)", width, 60, d =>
        {
            d.Rect(0, 0, width, 60, 1);
            d.Rect(2, 2, width - 4, 56, 1);
            for (var i = 1; i < doors; i++)
            {
                d.Line(i * width / doors, 38, i * width / doors, 60); // the doors on the front side
            }
        });
    }

    private static UnitObject[] BuildFurniture() =>
    [
        Bed("furn01", 90),
        Bed("furn02", 140),
        Bed("furn03", 160),
        Bed("furn04", 180),
        Furniture("furn05", "Bedside table 40 x 40 cm (plan)", 40, 40, d => d.Rect(0, 0, 40, 40, 2).Rect(5, 5, 30, 30, 1)),
        Sofa("furn06", "Sofa 2 seats 160 x 90 cm (plan)", 160, 2),
        Sofa("furn07", "Sofa 3 seats 210 x 90 cm (plan)", 210, 3),
        Sofa("furn08", "Armchair 85 x 85 cm (plan)", 85, 1, depth: 85, arm: 16),
        Furniture("furn09", "Dining table 160 x 90 cm (plan)", 160, 90, d => d.Rect(0, 0, 160, 90, 3)),
        Furniture("furn10", "Round table 100 cm (plan)", 100, 100, d => d.Circle(50, 50, 50)),
        Furniture("furn11", "Table 80 x 80 cm (plan)", 80, 80, d => d.Rect(0, 0, 80, 80, 3)),
        Furniture("furn12", "Coffee table 100 x 55 cm (plan)", 100, 55, d => d.Rect(0, 0, 100, 55, 4).Rect(5, 5, 90, 45, 2)),
        Furniture("furn13", "Chair 45 x 45 cm (plan)", 45, 45, d => d.Rect(0, 0, 45, 9, 3).Rect(2, 11, 41, 34, 5)),
        Wardrobe("furn14", 100),
        Wardrobe("furn15", 200),
        Furniture("furn16", "Chest of drawers 80 x 45 cm (plan)", 80, 45, d => d.Rect(0, 0, 80, 45, 2).Rect(3, 3, 74, 39, 1)),
        Furniture("furn17", "Bookshelf 100 x 30 cm (plan)", 100, 30, d => d.Rect(0, 0, 100, 30, 1).Line(25, 0, 25, 30).Line(50, 0, 50, 30).Line(75, 0, 75, 30)),
        Furniture("furn18", "Desk 140 x 70 cm (plan)", 140, 70, d => d.Rect(0, 0, 140, 70, 2).Rect(100, 0, 40, 70, 0).Line(100, 23, 140, 23).Line(100, 47, 140, 47)),
        Furniture("furn19", "TV unit 160 x 40 cm (plan)", 160, 40, d => d.Rect(0, 0, 160, 40, 2).Line(53, 0, 53, 40).Line(107, 0, 107, 40)),

        Bathroom("bath01", "Bathtub 170 x 75 cm (plan)", 170, 75, d => d.Rect(0, 0, 170, 75, 10).Rect(6, 6, 158, 63, 26).Circle(20, 37.5, 3)),
        Bathroom("bath02", "Shower tray 90 x 90 cm (plan)", 90, 90, d => d.Rect(0, 0, 90, 90, 4).Rect(5, 5, 80, 80, 2).Circle(45, 45, 4)),
        Bathroom("bath03", "Shower tray 120 x 80 cm (plan)", 120, 80, d => d.Rect(0, 0, 120, 80, 4).Rect(5, 5, 110, 70, 2).Circle(60, 40, 4)),
        Bathroom("bath04", "Toilet 38 x 69 cm (plan)", 38, 69, d => d.Rect(0, 0, 38, 18, 3).Oval(19, 44, 19, 25).Oval(19, 46, 12, 17)),
        Bathroom("bath05", "Washbasin 60 x 45 cm (plan)", 60, 45, d => d.Rect(0, 0, 60, 45, 4).Oval(30, 26, 21, 15).Circle(30, 26, 1.8).Circle(30, 6, 2.5)),
        Bathroom("bath06", "Double washbasin 120 x 50 cm (plan)", 120, 50, d => d.Rect(0, 0, 120, 50, 4)
            .Oval(30, 29, 21, 16).Circle(30, 29, 1.8).Circle(30, 6, 2.5)
            .Oval(90, 29, 21, 16).Circle(90, 29, 1.8).Circle(90, 6, 2.5)),
        Bathroom("bath07", "Washing machine 60 x 60 cm (plan)", 60, 60, d => d.Rect(0, 0, 60, 60, 3).Line(0, 10, 60, 10).Circle(30, 35, 19).Circle(30, 35, 13)),
    ];
}
