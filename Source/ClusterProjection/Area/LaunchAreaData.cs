using System.Collections.Generic;
using Verse;

namespace ClusterProjection;

public class LaunchAreaData
{
    public readonly List<IntVec3> allInteriorCells = new();
    public readonly List<IntVec3> launchableCells = new();

    public int Width;
    public int Height;

    public int LaunchableCount => launchableCells.Count;
}
