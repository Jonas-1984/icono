namespace Icono.Core;

/// <summary>What the length of a wall dimension measures.</summary>
public enum WallMeasure
{
    /// <summary>The clear length of the wall face on the dimension's side. For a room that is the inside: the size of the room you can
    /// walk in, not the length of the wall's centre line.</summary>
    Inside,

    /// <summary>The centre line of the wall.</summary>
    Centre,

    /// <summary>The outer face of the wall (a room's outside).</summary>
    Outside,
}

/// <summary>Where the dimension lines of a wall are drawn.</summary>
public enum WallDimensionPlacement
{
    /// <summary>On the side that is measured: inside for inside measures, otherwise outside. Dimensions in tight spots move out of the plan.</summary>
    Automatic,

    /// <summary>Inside the plan: in the room, next to the inner faces of the walls.</summary>
    Inside,

    /// <summary>Outside the plan: next to the outer faces of the walls.</summary>
    Outside,
}
