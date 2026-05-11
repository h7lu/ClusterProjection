using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ClusterProjection;

public class CP_TransportersArrivalAction_ProjectionBeaconCamp : TransportersArrivalAction
{
    public override bool GeneratesMap => true;

    public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile)
    {
        return Current.Game.FindMap(tile) == null;
    }

    public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
    {
        var report = base.StillValid(pods, destinationTile);
        if (!report)
            return report;

        return !Find.World.Impassable(destinationTile);
    }

    public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
    {
        var map = GetOrGenerateMapUtility.GetOrGenerateMap(
            tile,
            WorldObjectDefOf.Camp.overrideMapSize ?? Find.World.info.initialMapSize,
            WorldObjectDefOf.Camp);
        map.Parent.SetFaction(Faction.OfPlayer);
        map.Parent.GetComponent<TimedDetectionRaids>()?.StartDetectionCountdown(240000, 60000);

        var center = CP_TransportersArrivalAction_ClusterDrop.FindLandingCenterForArrivalMode(map, PawnsArrivalModeDefOf.CenterDrop);
        CP_ProjectionBeaconArrivalUtility.Arrive(transporters, map, center);
        Messages.Message("MessageTransportPodsArrived".Translate(), TransportersArrivalActionUtility.GetLookTarget(transporters), MessageTypeDefOf.TaskCompletion);
    }
}