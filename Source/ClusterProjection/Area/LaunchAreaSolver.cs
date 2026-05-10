using ClusterProjection.DefOfs;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ClusterProjection;

public static class LaunchAreaSolver
{
    private sealed class ArmSegment
    {
        public IntVec3 Root;
        public int Length;
    }

    public static bool TryComputeLargestArea(Map map, IntVec3 consolePos, IntVec2 consoleSize, out LaunchAreaData data)
    {
        data = null;

        var prefix = $"LaunchAreaSolver {consolePos}";

        CP_Debug.Message(prefix, $"consoleSize={consoleSize.x}x{consoleSize.z}", 120, onlyOnChange: true);

        var east = GetRailSegments(map, consolePos, IntVec3.East, consoleSize);
        var west = GetRailSegments(map, consolePos, IntVec3.West, consoleSize);
        var north = GetRailSegments(map, consolePos, IntVec3.North, consoleSize);
        var south = GetRailSegments(map, consolePos, IntVec3.South, consoleSize);

        CP_Debug.Message(prefix,
            $"rail lengths east={MaxLen(east)} west={MaxLen(west)} north={MaxLen(north)} south={MaxLen(south)}",
            120,
            onlyOnChange: true);

        var candidates = new List<LaunchAreaData>();
        TryAddCandidatesForPair(map, IntVec3.East, east, IntVec3.North, north, candidates, prefix + " E+N");
        TryAddCandidatesForPair(map, IntVec3.East, east, IntVec3.South, south, candidates, prefix + " E+S");
        TryAddCandidatesForPair(map, IntVec3.West, west, IntVec3.North, north, candidates, prefix + " W+N");
        TryAddCandidatesForPair(map, IntVec3.West, west, IntVec3.South, south, candidates, prefix + " W+S");

        if (candidates.Count == 0)
        {
            CP_Debug.Message(prefix, "no candidates were generated.", 120, onlyOnChange: true);
            return false;
        }

        LaunchAreaData best = null;
        foreach (var c in candidates)
        {
            if (best == null
                || c.LaunchableCount > best.LaunchableCount
                || (c.LaunchableCount == best.LaunchableCount && c.allInteriorCells.Count > best.allInteriorCells.Count))
            {
                best = c;
            }
        }

        data = best;
        var success = data != null && data.allInteriorCells.Count > 0;
        CP_Debug.Message(prefix,
            success
                ? $"selected best candidate width={data.Width} height={data.Height} interior={data.allInteriorCells.Count} launchable={data.LaunchableCount}"
                : "best candidate was null or had no interior cells.",
            120,
            onlyOnChange: true);
        return success;
    }

    private static int MaxLen(List<ArmSegment> segments)
    {
        var max = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Length > max)
                max = segments[i].Length;
        }
        return max;
    }

    private static void TryAddCandidatesForPair(
        Map map,
        IntVec3 xDir,
        List<ArmSegment> xArms,
        IntVec3 zDir,
        List<ArmSegment> zArms,
        List<LaunchAreaData> outCandidates,
        string logKey)
    {
        if (xArms.Count == 0 || zArms.Count == 0)
        {
            CP_Debug.Message(logKey, $"skipped because xArms={xArms.Count} zArms={zArms.Count}.", 120, onlyOnChange: true);
            return;
        }

        var added = 0;
        for (var i = 0; i < xArms.Count; i++)
        {
            for (var j = 0; j < zArms.Count; j++)
            {
                if (TryBuildCandidateFromArms(map, xDir, xArms[i], zDir, zArms[j], out var candidate))
                {
                    outCandidates.Add(candidate);
                    added++;
                }
            }
        }

        CP_Debug.Message(logKey, $"evaluated pairs={xArms.Count * zArms.Count}, added={added}.", 120, onlyOnChange: true);
    }

    private static bool TryBuildCandidateFromArms(Map map, IntVec3 xDir, ArmSegment xArm, IntVec3 zDir, ArmSegment zArm, out LaunchAreaData candidate)
    {
        candidate = null;

        int xStart;
        int xEnd;
        int zStart;
        int zEnd;

        if (xDir == IntVec3.East)
        {
            xStart = zArm.Root.x + 1;
            xEnd = xArm.Root.x + xArm.Length - 1;
        }
        else
        {
            xStart = xArm.Root.x - xArm.Length + 1;
            xEnd = zArm.Root.x - 1;
        }

        if (zDir == IntVec3.North)
        {
            zStart = xArm.Root.z + 1;
            zEnd = zArm.Root.z + zArm.Length - 1;
        }
        else
        {
            zStart = zArm.Root.z - zArm.Length + 1;
            zEnd = xArm.Root.z - 1;
        }

        if (xStart > xEnd || zStart > zEnd)
        {
            return false;
        }

        candidate = new LaunchAreaData
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

        if (candidate.allInteriorCells.Count == 0)
            return false;

        return true;
    }

    private static List<ArmSegment> GetRailSegments(Map map, IntVec3 consolePos, IntVec3 dir, IntVec2 size)
    {
        var segments = new List<ArmSegment>();

        // For each direction, scan at ±2 perpendicular distance along the full scan zone.
        if (dir == IntVec3.East)
        {
            // Scan at x = consolePos.x + size.x + 2 (±2 beyond building face)
            // Along z from consolePos.z - 2 to consolePos.z + size.z + 1 (covering ±2 zones)
            for (var z = consolePos.z - 2; z <= consolePos.z + size.z + 1; z++)
            {
                var scanCell = new IntVec3(consolePos.x + size.x + 2, 0, z);
                TryAddArmSegment(map, dir, scanCell, segments);
            }
        }
        else if (dir == IntVec3.West)
        {
            // Scan at x = consolePos.x - 2 (±2 beyond building face)
            // Along z from consolePos.z - 2 to consolePos.z + size.z + 1
            for (var z = consolePos.z - 2; z <= consolePos.z + size.z + 1; z++)
            {
                var scanCell = new IntVec3(consolePos.x - 2, 0, z);
                TryAddArmSegment(map, dir, scanCell, segments);
            }
        }
        else if (dir == IntVec3.North)
        {
            // Scan at z = consolePos.z + size.z + 2 (±2 beyond building face)
            // Along x from consolePos.x - 2 to consolePos.x + size.x + 1
            for (var x = consolePos.x - 2; x <= consolePos.x + size.x + 1; x++)
            {
                var scanCell = new IntVec3(x, 0, consolePos.z + size.z + 2);
                TryAddArmSegment(map, dir, scanCell, segments);
            }
        }
        else // South
        {
            // Scan at z = consolePos.z - 2 (±2 beyond building face)
            // Along x from consolePos.x - 2 to consolePos.x + size.x + 1
            for (var x = consolePos.x - 2; x <= consolePos.x + size.x + 1; x++)
            {
                var scanCell = new IntVec3(x, 0, consolePos.z - 2);
                TryAddArmSegment(map, dir, scanCell, segments);
            }
        }

        return segments;
    }

    private static void TryAddArmSegment(Map map, IntVec3 dir, IntVec3 scanCell, List<ArmSegment> segments)
    {
        var len = 0;
        var cur = scanCell;
        while (cur.InBounds(map) && cur.GetFirstThing(map, CP_ThingDefOf.CP_AutoAssemblyRail) is Building_AutoAssemblyRail)
        {
            len++;
            cur += dir;
        }

        if (len <= 0)
            return;

        segments.Add(new ArmSegment
        {
            Root = scanCell,
            Length = len
        });
    }
}
