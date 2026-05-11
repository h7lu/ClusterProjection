using System.Collections.Generic;
using System.Linq;
using ClusterProjection.DefOfs;
using RimWorld;
using Verse;

namespace ClusterProjection;

public static class CP_ProjectionBeaconUtility
{
    public static bool ContainsProjectionBeacon(IEnumerable<IThingHolder> holders)
    {
        if (holders == null)
            return false;

        foreach (var holder in holders)
        {
            if (TryGetProjectionBeaconHolder(holder))
                return true;

            if (holder?.GetDirectlyHeldThings() == null)
                continue;

            for (var i = 0; i < holder.GetDirectlyHeldThings().Count; i++)
            {
                if (TryGetProjectionBeacon(holder.GetDirectlyHeldThings()[i], out _))
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetProjectionBeaconHolder(IThingHolder holder)
    {
        switch (holder)
        {
            case Thing thing when TryGetProjectionBeacon(thing, out _):
                return true;
            case ThingComp comp when TryGetProjectionBeacon(comp.parent, out _):
                return true;
            default:
                return false;
        }
    }

    public static bool HasActiveProjectionBeacon(Map map)
    {
        if (map == null)
            return false;

        var beacons = map.listerThings.ThingsOfDef(CP_ThingDefOf.CP_ProjectionBeacon);
        for (var i = 0; i < beacons.Count; i++)
        {
            if (beacons[i] is Building_ProjectionBeacon beacon && beacon.IsProjectionActive)
                return true;
        }

        return false;
    }

    public static List<Building_ProjectionBeacon> ExtractProjectionBeacons(List<ActiveTransporterInfo> transporters)
    {
        var result = new List<Building_ProjectionBeacon>();
        if (transporters == null)
            return result;

        for (var i = 0; i < transporters.Count; i++)
        {
            var container = transporters[i].innerContainer;
            for (var j = container.Count - 1; j >= 0; j--)
            {
                var thing = container[j];
                if (!TryGetProjectionBeacon(thing, out var beacon))
                    continue;

                container.Remove(thing);
                result.Add(beacon);
            }
        }

        return result;
    }

    public static void DeployProjectionBeacons(List<Building_ProjectionBeacon> beacons, Map map, IntVec3 around)
    {
        if (beacons == null || map == null)
            return;

        for (var i = 0; i < beacons.Count; i++)
        {
            var beacon = beacons[i];
            var cell = FindDeploymentCell(map, around, i);
            GenSpawn.Spawn(beacon, cell, map, Rot4.North, WipeMode.VanishOrMoveAside);
            beacon.ActivateProjection();
        }
    }

    private static IntVec3 FindDeploymentCell(Map map, IntVec3 around, int radialIndex)
    {
        if (around.IsValid && around.InBounds(map) && around.Standable(map))
            return around;

        var radialCells = GenRadial.RadialCellsAround(around.IsValid ? around : map.Center, 6f, true).ToList();
        for (var i = radialIndex; i < radialCells.Count; i++)
        {
            var cell = radialCells[i];
            if (cell.InBounds(map) && cell.Standable(map))
                return cell;
        }

        return CellFinderLoose.RandomCellWith(cell => cell.Standable(map), map);
    }

    private static bool TryGetProjectionBeacon(Thing thing, out Building_ProjectionBeacon beacon)
    {
        switch (thing)
        {
            case Building_ProjectionBeacon directBeacon:
                beacon = directBeacon;
                return true;
            default:
                beacon = null;
                return false;
        }
    }
}