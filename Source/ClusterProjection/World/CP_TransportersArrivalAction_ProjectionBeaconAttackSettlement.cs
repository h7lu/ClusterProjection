using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ClusterProjection;

public class CP_TransportersArrivalAction_ProjectionBeaconAttackSettlement : TransportersArrivalAction
{
    private Settlement settlement;
    private PawnsArrivalModeDef arrivalMode;

    public override bool GeneratesMap => true;

    public CP_TransportersArrivalAction_ProjectionBeaconAttackSettlement()
    {
    }

    public CP_TransportersArrivalAction_ProjectionBeaconAttackSettlement(Settlement settlement, PawnsArrivalModeDef arrivalMode)
    {
        this.settlement = settlement;
        this.arrivalMode = arrivalMode;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref settlement, "settlement");
        Scribe_Defs.Look(ref arrivalMode, "arrivalMode");
    }

    public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
    {
        var report = base.StillValid(pods, destinationTile);
        if (!report)
            return report;

        if (settlement != null && settlement.Tile != destinationTile)
            return false;

        return settlement != null && settlement.Spawned && settlement.Attackable;
    }

    public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile)
    {
        return settlement != null && !settlement.HasMap;
    }

    public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
    {
        var lookTarget = TransportersArrivalActionUtility.GetLookTarget(transporters);
        var generatedHostileMap = !settlement.HasMap;
        var map = GetOrGenerateMapUtility.GetOrGenerateMap(settlement.Tile, null);
        var letterLabel = "LetterLabelCaravanEnteredEnemyBase".Translate();
        var letterText = "LetterTransportPodsLandedInEnemyBase".Translate(settlement.Label).CapitalizeFirst();
        SettlementUtility.AffectRelationsOnAttacked(settlement, ref letterText);
        if (generatedHostileMap)
        {
            Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
            PawnRelationUtility.Notify_PawnsSeenByPlayer_Letter(map.mapPawns.AllPawns, ref letterLabel, ref letterText, "LetterRelatedPawnsInMapWherePlayerLanded".Translate(Faction.OfPlayer.def.pawnsPlural), true);
        }

        Find.LetterStack.ReceiveLetter(letterLabel, letterText, LetterDefOf.NeutralEvent, lookTarget, settlement.Faction);

        var center = CP_TransportersArrivalAction_ClusterDrop.FindLandingCenterForArrivalMode(map, arrivalMode ?? PawnsArrivalModeDefOf.EdgeDrop);
        CP_ProjectionBeaconArrivalUtility.Arrive(transporters, map, center);
    }

    public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Action<PlanetTile, TransportersArrivalAction> launchAction, Settlement settlement)
    {
        foreach (var option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                     () => settlement != null && settlement.Spawned && settlement.Attackable,
                     () => new CP_TransportersArrivalAction_ProjectionBeaconAttackSettlement(settlement, PawnsArrivalModeDefOf.EdgeDrop),
                     "AttackAndDropAtEdge".Translate(settlement.Label),
                     launchAction,
                     settlement.Tile))
        {
            yield return option;
        }

        foreach (var option in TransportersArrivalActionUtility.GetFloatMenuOptions(
                     () => settlement != null && settlement.Spawned && settlement.Attackable,
                     () => new CP_TransportersArrivalAction_ProjectionBeaconAttackSettlement(settlement, PawnsArrivalModeDefOf.CenterDrop),
                     "AttackAndDropInCenter".Translate(settlement.Label),
                     launchAction,
                     settlement.Tile))
        {
            yield return option;
        }
    }
}