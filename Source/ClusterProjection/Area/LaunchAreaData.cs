using System.Collections.Generic;
using Verse;

namespace ClusterProjection;

public class LaunchAreaData
{
    public readonly List<IntVec3> allInteriorCells = new();
    public readonly List<IntVec3> launchableCells = new();

    public int Width;
    public int Height;

    /// <summary>World-space z-centre of the east/west (horizontal) rail arm.</summary>
    public float RailHorizontalZ;

    /// <summary>World-space x-centre of the north/south (vertical) rail arm.</summary>
    public float RailVerticalX;

    public int LaunchableCount => launchableCells.Count;
}
