using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ClusterProjection;

public class CP_TransportersArrivalAction_ClusterVisitSite : TransportersArrivalAction
{
    private Site site;
    private PawnsArrivalModeDef arrivalMode;

    public override bool GeneratesMap => true;

    public CP_TransportersArrivalAction_ClusterVisitSite()
    {
    }

    public CP_TransportersArrivalAction_ClusterVisitSite(Site site, PawnsArrivalModeDef arrivalMode)
    {
        this.site = site;
        this.arrivalMode = arrivalMode;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref site, "site");
        Scribe_Defs.Look(ref arrivalMode, "arrivalMode");
    }

    public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
    {
        var report = base.StillValid(pods, destinationTile);
        if (!report)
            return report;

        if (site != null && site.Tile != destinationTile)
            return false;

        return TransportersArrivalAction_VisitSite.CanVisit(pods, site);
    }

    public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile)
    {
        return !site.HasMap;
    }

    public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
    {
        var lookTarget = TransportersArrivalActionUtility.GetLookTarget(transporters);
        var generatedHostileMap = !site.HasMap;
        var map = GetOrGenerateMapUtility.GetOrGenerateMap(site.Tile, site.PreferredMapSize, null);
        if (generatedHostileMap)
        {
            Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
            PawnRelationUtility.Notify_PawnsSeenByPlayer_Letter_Send(map.mapPawns.AllPawns, "LetterRelatedPawnsInMapWherePlayerLanded".Translate(Faction.OfPlayer.def.pawnsPlural), LetterDefOf.NeutralEvent, true);
        }

        if (site.Faction != null && site.Faction != Faction.OfPlayer && site.MainSitePartDef.considerEnteringAsAttack)
        {
            Faction.OfPlayer.TryAffectGoodwillWith(site.Faction, Faction.OfPlayer.GoodwillToMakeHostile(site.Faction), true, true, HistoryEventDefOf.AttackedSettlement);
        }

        Messages.Message("MessageTransportPodsArrived".Translate(), lookTarget, MessageTypeDefOf.TaskCompletion);

        var center = CP_TransportersArrivalAction_ClusterDrop.FindLandingCenterForArrivalMode(map, arrivalMode ?? PawnsArrivalModeDefOf.EdgeDrop);
        CP_TransportersArrivalAction_ClusterDrop.LandCluster(transporters, map, center);
    }

    public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Action<PlanetTile, TransportersArrivalAction> launchAction, IEnumerable<IThingHolder> pods, Site site)
    {
        foreach (var option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                     () => TransportersArrivalAction_VisitSite.CanVisit(pods, site),
                     () => new CP_TransportersArrivalAction_ClusterVisitSite(site, PawnsArrivalModeDefOf.EdgeDrop),
                     "DropAtEdge".Translate(),
                     launchAction,
                     site.Tile))
        {
            yield return option;
        }

        foreach (var option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                     () => TransportersArrivalAction_VisitSite.CanVisit(pods, site),
                     () => new CP_TransportersArrivalAction_ClusterVisitSite(site, PawnsArrivalModeDefOf.CenterDrop),
                     "DropInCenter".Translate(),
                     launchAction,
                     site.Tile))
        {
            yield return option;
        }
    }
}