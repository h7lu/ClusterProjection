using ClusterProjection.DefOfs;
using System.Collections.Generic;
using Verse;

namespace ClusterProjection;

public static class LaunchAreaSolver
{
    public static bool TryComputeLargestArea(Map map, IntVec3 consolePos, out LaunchAreaData data)
    {
        data = null;

        var east = GetRailLength(map, consolePos, IntVec3.East);
        var west = GetRailLength(map, consolePos, IntVec3.West);
        var north = GetRailLength(map, consolePos, IntVec3.North);
        var south = GetRailLength(map, consolePos, IntVec3.South);

        var candidates = new List<LaunchAreaData>();
        TryAddCandidate(map, consolePos, IntVec3.East, east, IntVec3.North, north, candidates);
        TryAddCandidate(map, consolePos, IntVec3.East, east, IntVec3.South, south, candidates);
        TryAddCandidate(map, consolePos, IntVec3.West, west, IntVec3.North, north, candidates);
        TryAddCandidate(map, consolePos, IntVec3.West, west, IntVec3.South, south, candidates);

        if (candidates.Count == 0)
        {
            return false;
        }

        LaunchAreaData best = null;
        foreach (var c in candidates)
        {
            if (best == null || c.LaunchableCount > best.LaunchableCount)
            {
                best = c;
            }
        }

        data = best;
        return data != null && data.LaunchableCount > 0;
    }

    private static void TryAddCandidate(Map map, IntVec3 consolePos, IntVec3 xDir, int xLen, IntVec3 zDir, int zLen, List<LaunchAreaData> outCandidates)
    {
        if (xLen <= 0 || zLen <= 0)
        {
            return;
        }

        var xStart = xDir == IntVec3.East ? consolePos.x + 1 : consolePos.x - xLen;
        var xEnd = xDir == IntVec3.East ? consolePos.x + xLen : consolePos.x - 1;
        var zStart = zDir == IntVec3.North ? consolePos.z + 1 : consolePos.z - zLen;
        var zEnd = zDir == IntVec3.North ? consolePos.z + zLen : consolePos.z - 1;

        if (xStart > xEnd || zStart > zEnd)
        {
            return;
        }

        var candidate = new LaunchAreaData
        {
            Width = xEnd - xStart + 1,
            Height = zEnd - zStart + 1
        };

        for (var x = xStart; x <= xEnd; x++)
        {
            for (var z = zStart; z <= zEnd; z++)
            {
                var c = new IntVec3(x, 0, z);
                if (!c.InBounds(map))
                {
                    continue;
                }

                candidate.allInteriorCells.Add(c);
                if (c.GetTerrain(map) == CP_TerrainDefOf.CP_LauncherFloor)
                {
                    candidate.launchableCells.Add(c);
                }
            }
        }

        outCandidates.Add(candidate);
    }

    private static int GetRailLength(Map map, IntVec3 consolePos, IntVec3 dir)
    {
        var length = 0;
        var cur = consolePos + dir;

        while (cur.InBounds(map))
        {
            var rail = cur.GetFirstThing(map, CP_ThingDefOf.CP_AutoAssemblyRail) as Building_AutoAssemblyRail;
            if (rail == null)
            {
                break;
            }

            length++;
            cur += dir;
        }

        return length;
    }
}
