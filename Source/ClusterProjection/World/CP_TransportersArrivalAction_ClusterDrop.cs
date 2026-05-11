using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ClusterProjection;

public class CP_TransportersArrivalAction_ClusterDrop : TransportersArrivalAction
{
    private IntVec3 preferredLandingCenter = IntVec3.Invalid;

    public override bool GeneratesMap => false;

    public CP_TransportersArrivalAction_ClusterDrop()
    {
    }

    public CP_TransportersArrivalAction_ClusterDrop(IntVec3 preferredLandingCenter)
    {
        this.preferredLandingCenter = preferredLandingCenter;
    }

    public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
    {
        var map = GetOrGenerateMapUtility.GetOrGenerateMap(tile, WorldObjectDefOf.Site);
        if (map == null)
        {
            Log.Error($"Cluster drop failed to generate map at tile {tile}.");
            return;
        }

        LandCluster(transporters, map, preferredLandingCenter);
        Messages.Message("CP_ClusterArrived".Translate(), TransportersArrivalActionUtility.GetLookTarget(transporters), MessageTypeDefOf.TaskCompletion);
    }

    public static void LandCluster(List<ActiveTransporterInfo> transporters, Map map, IntVec3 preferredLandingCenter = default, int podOpenDelay = 60, int clusterDeployDelay = 60)
    {
        TransportersArrivalActionUtility.RemovePawnsFromWorldPawns(transporters);

        var landingEntries = BuildLandingEntries(transporters);
        var center = ResolveLandingCenter(map, landingEntries.Select(e => e.Offset), preferredLandingCenter);

        for (var i = 0; i < landingEntries.Count; i++)
        {
            var entry = landingEntries[i];
            var desiredCell = center + entry.Offset;
            var landingCell = ResolveLandingCell(desiredCell, map);
            PrepareIncomingPod(entry, podOpenDelay, clusterDeployDelay);
            DropPodUtility.MakeDropPodAt(landingCell, map, entry.Transporter);
        }
    }

    public static IntVec3 FindLandingCenterForArrivalMode(Map map, PawnsArrivalModeDef arrivalMode)
    {
        if (arrivalMode == PawnsArrivalModeDefOf.EdgeDrop)
            return DropCellFinder.FindRaidDropCenterDistant(map, allowRoofed: false);

        if (!DropCellFinder.TryFindRaidDropCenterClose(out var spot, map))
            spot = DropCellFinder.FindRaidDropCenterDistant(map);

        return spot;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref preferredLandingCenter, "preferredLandingCenter", IntVec3.Invalid);
    }

    private static List<LandingEntry> BuildLandingEntries(List<ActiveTransporterInfo> transporters)
    {
        var entries = new List<LandingEntry>(transporters.Count);
        for (var i = 0; i < transporters.Count; i++)
        {
            var transporter = transporters[i];
            Building_ClusterPod clusterPod = null;
            IntVec3 offset = IntVec3.Zero;

            for (var j = 0; j < transporter.innerContainer.Count; j++)
            {
                if (transporter.innerContainer[j] is not Building_ClusterPod pod)
                    continue;

                clusterPod = pod;
                offset = pod.LandingOffset;
                break;
            }

            entries.Add(new LandingEntry
            {
                Transporter = transporter,
                ClusterPod = clusterPod,
                Offset = offset
            });
        }

        return entries;
    }

    private static IntVec3 ResolveLandingCenter(Map map, IEnumerable<IntVec3> offsets, IntVec3 preferredLandingCenter)
    {
        var requiredOffsets = offsets?.ToList() ?? new List<IntVec3>();
        if (preferredLandingCenter.IsValid && preferredLandingCenter.InBounds(map))
        {
            if (CanFitOffsets(map, preferredLandingCenter, requiredOffsets))
                return preferredLandingCenter;

            if (TryFindClosestFittingCenterNear(map, preferredLandingCenter, requiredOffsets, 12, out var adjustedCenter))
                return adjustedCenter;

            return preferredLandingCenter;
        }

        return FindLandingCenter(map, requiredOffsets);
    }

    private static IntVec3 FindLandingCenter(Map map, List<IntVec3> requiredOffsets)
    {
        for (var i = 0; i < 80; i++)
        {
            var candidate = DropCellFinder.RandomDropSpot(map);
            if (candidate.IsValid && CanFitOffsets(map, candidate, requiredOffsets))
                return candidate;
        }

        var fallback = CellFinderLoose.RandomCellWith(cell => cell.Standable(map), map);
        if (fallback.IsValid)
            return fallback;

        var center = map.Center;
        return center.IsValid ? center : IntVec3.Zero;
    }

    private static bool TryFindClosestFittingCenterNear(Map map, IntVec3 around, List<IntVec3> offsets, int radius, out IntVec3 center)
    {
        if (CanFitOffsets(map, around, offsets))
        {
            center = around;
            return true;
        }

        for (var r = 1; r <= radius; r++)
        {
            foreach (var cell in GenRadial.RadialCellsAround(around, r, useCenter: false))
            {
                if (!cell.InBounds(map))
                    continue;
                if (!CanFitOffsets(map, cell, offsets))
                    continue;

                center = cell;
                return true;
            }
        }

        center = IntVec3.Invalid;
        return false;
    }

    private static bool CanFitOffsets(Map map, IntVec3 center, List<IntVec3> offsets)
    {
        for (var i = 0; i < offsets.Count; i++)
        {
            var cell = center + offsets[i];
            if (!cell.IsValid || !cell.InBounds(map))
                return false;
        }

        return true;
    }

    private static IntVec3 ResolveLandingCell(IntVec3 desiredCell, Map map)
    {
        if (desiredCell.IsValid && desiredCell.InBounds(map))
            return desiredCell;

        if (DropCellFinder.TryFindDropSpotNear(map.Center, map, out var fallback, allowFogged: false, canRoofPunch: true))
            return fallback;

        var standable = CellFinderLoose.RandomCellWith(cell => cell.Standable(map), map);
        return standable.IsValid ? standable : map.Center;
    }

    private static void PrepareIncomingPod(LandingEntry entry, int podOpenDelay, int clusterDeployDelay)
    {
        entry.Transporter.openDelay = podOpenDelay;
        entry.Transporter.leaveSlag = false;

        if (entry.ClusterPod == null)
            return;

        entry.ClusterPod.ArmAutoDeployOnSpawn(clusterDeployDelay);
        entry.Transporter.despawnPodBeforeSpawningThing = true;
        entry.Transporter.spawnWipeMode = WipeMode.VanishOrMoveAside;
        entry.Transporter.moveItemsAsideBeforeSpawning = true;
        entry.Transporter.setRotation = Rot4.North;
    }

    private sealed class LandingEntry
    {
        public ActiveTransporterInfo Transporter;
        public Building_ClusterPod ClusterPod;
        public IntVec3 Offset;
    }
}