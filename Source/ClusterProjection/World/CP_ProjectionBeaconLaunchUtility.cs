using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ClusterProjection;

public static class CP_ProjectionBeaconLaunchUtility
{
    public static bool ChoseWorldTarget(GlobalTargetInfo target, PlanetTile originTile, IEnumerable<IThingHolder> pods, int maxLaunchDistance, Action<PlanetTile, TransportersArrivalAction> launchAction, CompLaunchable launchable, float? overrideFuelLevel)
    {
        if (!target.IsValid)
        {
            Messages.Message("MessageTransportPodsDestinationIsInvalid".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        if (target.HasWorldObject && !target.WorldObject.def.validLaunchTarget)
        {
            Messages.Message("MessageWorldObjectIsInvalid".Translate(target.WorldObject.Named("OBJECT")), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        if (ModsConfig.OdysseyActive && target.HasWorldObject && target.WorldObject.RequiresSignalJammerToReach)
        {
            Messages.Message("TransportPodDestinationRequiresSignalJammer".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        var fuelLevel = overrideFuelLevel ?? launchable?.FuelLevel ?? -1f;
        var distance = Find.WorldGrid.TraversalDistanceBetween(originTile, target.Tile, true, int.MaxValue, true);
        var maxDistanceAtFuel = launchable?.MaxLaunchDistanceAtFuelLevel(fuelLevel, target.Tile.Layer) ?? -1f;
        if (maxLaunchDistance >= 0 && distance > maxLaunchDistance)
        {
            Messages.Message("TransportPodDestinationBeyondMaximumRange".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        if (maxDistanceAtFuel >= 0f && distance > maxDistanceAtFuel)
        {
            Messages.Message("TransportPodNotEnoughFuel".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        var options = GetFloatMenuOptionsAt(target.Tile, pods, launchAction).ToList();
        if (!options.Any())
        {
            Messages.Message("MessageTransportPodsDestinationIsInvalid".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        if (options.Count == 1)
        {
            if (!options[0].Disabled)
            {
                options[0].action();
                return true;
            }

            return false;
        }

        Find.WindowStack.Add(new FloatMenu(options));
        return false;
    }

    private static IEnumerable<FloatMenuOption> GetFloatMenuOptionsAt(PlanetTile destinationTile, IEnumerable<IThingHolder> pods, Action<PlanetTile, TransportersArrivalAction> launchAction)
    {
        var anything = false;
        var worldObjects = Find.WorldObjects.AllWorldObjects;
        for (var i = 0; i < worldObjects.Count; i++)
        {
            var worldObject = worldObjects[i];
            if (worldObject.Tile != destinationTile)
                continue;

            if (worldObject is Settlement settlement && settlement.Attackable)
            {
                anything = true;
                foreach (var option in CP_TransportersArrivalAction_ProjectionBeaconAttackSettlement.GetFloatMenuOptions(launchAction, settlement))
                    yield return option;
                continue;
            }

            if (worldObject is Site site)
            {
                anything = true;
                foreach (var option in CP_TransportersArrivalAction_ProjectionBeaconVisitSite.GetFloatMenuOptions(launchAction, site))
                    yield return option;
                continue;
            }

            foreach (var option in worldObject.GetTransportersFloatMenuOptions(pods, launchAction))
            {
                anything = true;
                yield return option;
            }
        }

        if (!anything && !Find.World.Impassable(destinationTile))
        {
            yield return new FloatMenuOption(
                "CP_DeployProjectionCamp".Translate(),
                () => launchAction(destinationTile, new CP_TransportersArrivalAction_ProjectionBeaconCamp()));
        }
    }
}