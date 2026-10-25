namespace Icono.Core;

public enum ShapeKind
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    Triangle,
    Diamond,
    Pentagon,
    Hexagon,
    Star,
    Parallelogram,
    Trapezoid,
    Cross,
    HalfCircle,
    Ring,
    SpeechBubble,
    Line,
    Arrow,
    DoubleArrow,
    Polyline,
    Curve,
    Text,

    // More basic shapes.
    Octagon,
    Heart,
    Cloud,
    Shield,
    Bookmark,
    Gear,
    Club,
    Box3D,
    CombinedPath,
    RasterImage,

    // Icon glyph shapes (parsed straight from user-supplied Lucide SVGs).
    StarCheck,
    StarPlus,
    StarMinus,
    SquareArrowOutUpRight,
    Blocks,
    SwatchBook,
    LensConcave,
    Cylinder,
    Cone,
    Pyramid,
    TriangleAlert,
    Spade,
    SquareDashedBottom,
    Share,
    ShieldCog,
    Umbrella,
    Cloudy,
    TrafficCone,

    // Arrow shapes palette.
    ArrowRight,
    ArrowLeft,
    ArrowUp,
    ArrowDown,
    ChevronRight,
    ChevronLeft,
    ChevronUp,
    ChevronDown,
    ArrowLeftRight,
    ArrowUpDown,
    BentArrowRight,
    BentArrowUp,
    LoopArrowClockwise,
    LoopArrowCounterClockwise,
    CompassArrows,
    NotchedArrowRight,
    PlayTriangleRight,
    StripedArrowRight,

    // Office furniture, top-view floor-plan symbols.
    Worktable,
    RoundTable,
    OfficeChair,
    Monitor,
    Sofa,
    Bookshelf,
    Door,
    Plant,
    Workstation,
    FlowerCluster,
    StarFlower,
    DaisyFlower,
    Workplace,
    FilingCabinet,
    Printer,
    TrashBin,
    Whiteboard,
    Window,
    Stairs,
    PottedTree,
    Rug,
    Bed,
    Lamp,

    /// <summary>A checkered "design grid" (default 2 mm squares) whose individual squares can be coloured with the
    /// bucket tool — see <see cref="Shape.GridCellSize"/> and <see cref="Shape.GridCells"/>.</summary>
    DesignGrid,

    /// <summary>An architectural wall: a thick band along a polyline (<see cref="Shape.Points"/>), optionally closed into a ring,
    /// filled with a hatch pattern and carrying its own dimensions.</summary>
    Wall,

    /// <summary>A measurement drawn next to other shapes (extension lines, dimension line, arrows and the value) - see <see cref="Shape.DimKind"/>.</summary>
    Dimension,

    // Architectural plan symbols (see ArchitecturalSymbols): they sit in a wall and cut it open.

    /// <summary>A single door with its swing arc.</summary>
    DoorSingle,

    /// <summary>A double door: two leaves that meet in the middle.</summary>
    DoorDouble,

    /// <summary>A window: the opening with the glass lines.</summary>
    WindowSingle,

    /// <summary>A double window: two sashes with a post in the middle.</summary>
    WindowDouble,

    /// <summary>An opening in a wall without a door or window.</summary>
    WallOpening,

    // Symbols of an electrical and kitchen plan (see PlanSymbols): placed at a point, drawn at their real size.

    /// <summary>A socket: a half circle on a line with a bar to its top.</summary>
    Socket,

    /// <summary>A double socket: the socket with a 2.</summary>
    DoubleSocket,

    /// <summary>An electrical appliance: a square with an E.</summary>
    ElectricAppliance,

    /// <summary>An electric cooker: a square with its three hotplates.</summary>
    Cooker,

    /// <summary>An oven: a square with the door line and a handle dot.</summary>
    Oven,

    /// <summary>A microwave: a rounded square with three waves.</summary>
    Microwave,

    /// <summary>The connections for water and waste water: a big circle between two small ones.</summary>
    WaterAndWastewater,

    /// <summary>A cooker hood: a circle with a cross.</summary>
    Hood,
}
