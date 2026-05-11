using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using ClusterProjection.DefOfs;

namespace ClusterProjection;

[HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.StartChoosingDestination))]
public static class CP_CompLaunchable_StartChoosingDestination_Patch
{
    private static Texture2D BeaconTargeterMouseAttachment => ContentFinder<Texture2D>.Get("beacon_worldtarget");

    public static bool Prefix(CompLaunchable __instance, System.Action<PlanetTile, TransportersArrivalAction> launchAction, float? overrideFuelLevel)
    {
        var transporters = AccessTools.Property(typeof(CompLaunchable), "TransportersInGroup")?.GetValue(__instance) as IEnumerable<IThingHolder>;
        if (!CP_ProjectionBeaconUtility.ContainsProjectionBeacon(transporters))
            return true;

        var transporter = AccessTools.Property(typeof(CompLaunchable), "Transporter")?.GetValue(__instance) as CompTransporter;

        var originTile = __instance.parent.Tile;
        CameraJumper.TryJump(CameraJumper.GetWorldTarget(new GlobalTargetInfo(originTile)));
        Find.WorldSelector.ClearSelection();
        Find.WorldTargeter.BeginTargeting(
            target => CP_ProjectionBeaconLaunchUtility.ChoseWorldTarget(target, originTile, transporters, __instance.MaxLaunchDistanceEver(target.Tile.Layer), launchAction, __instance, overrideFuelLevel),
            true,
            BeaconTargeterMouseAttachment,
            transporter == null || !CaravanShuttleUtility.IsCaravanShuttle(transporter),
            () =>
            {
                var selectedLayer = Find.WorldSelector.SelectedLayer;
                var closestTile = selectedLayer.GetClosestTile_NewTemp(originTile);
                var maxDistance = __instance.MaxLaunchDistanceEver(closestTile.Layer);
                GenDraw.DrawWorldRadiusRing(closestTile, maxDistance, CompPilotConsole.GetThrusterRadiusMat(closestTile));
                if (__instance.Refuelable != null)
                {
                    var currentFuelDistance = __instance.MaxLaunchDistanceAtFuelLevel(__instance.FuelLevel, PlanetLayer.Selected);
                    if (currentFuelDistance < maxDistance)
                        GenDraw.DrawWorldRadiusRing(closestTile, currentFuelDistance, CompPilotConsole.GetFuelRadiusMat(closestTile));
                }
            },
            target => CompLaunchable.TargetingLabelGetter(target, originTile, __instance.MaxLaunchDistanceEver((target.IsValid ? target.Tile : originTile).Layer), transporters, launchAction, __instance, overrideFuelLevel),
            null,
            originTile,
            true);
        return false;
    }
}

[HarmonyPatch(typeof(Building_PodLauncher), nameof(Building_PodLauncher.GetGizmos))]
public static class CP_Building_PodLauncher_GetGizmos_Patch
{
    public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building_PodLauncher __instance)
    {
        foreach (var gizmo in __result)
            yield return gizmo;

        var designator = BuildCopyCommandUtility.FindAllowedDesignator(CP_ThingDefOf.CP_ProjectionBeacon);
        if (designator == null)
            yield break;

        var beaconDef = CP_ThingDefOf.CP_ProjectionBeacon;
        var buildCell = FuelingPortUtility.GetFuelingPortCell(__instance);
        var acceptance = GenConstruct.CanPlaceBlueprintAt(beaconDef, buildCell, beaconDef.defaultPlacingRot, __instance.Map);

        var command = new Command_Action
        {
            defaultLabel = "BuildThing".Translate(beaconDef.label),
            defaultDesc = designator.Desc,
            icon = designator.icon,
            action = () =>
            {
                GenConstruct.PlaceBlueprintForBuild(beaconDef, buildCell, __instance.Map, beaconDef.defaultPlacingRot, Faction.OfPlayer, null);
            }
        };

        if (!acceptance.Accepted)
            command.Disable(acceptance.Reason);

        yield return command;
    }
}

[HarmonyPatch(
    typeof(CompLaunchable),
    nameof(CompLaunchable.ChoseWorldTarget),
    new[]
    {
        typeof(GlobalTargetInfo),
        typeof(PlanetTile),
        typeof(IEnumerable<IThingHolder>),
        typeof(int),
        typeof(System.Action<PlanetTile, TransportersArrivalAction>),
        typeof(CompLaunchable),
        typeof(float?)
    })]
public static class CP_CompLaunchable_ChoseWorldTarget_Patch
{
    public static bool Prefix(GlobalTargetInfo target, PlanetTile tile, IEnumerable<IThingHolder> pods, int maxLaunchDistance, System.Action<PlanetTile, TransportersArrivalAction> launchAction, CompLaunchable launchable, float? overrideFuelLevel, ref bool __result)
    {
        if (!CP_ProjectionBeaconUtility.ContainsProjectionBeacon(pods))
            return true;

        __result = CP_ProjectionBeaconLaunchUtility.ChoseWorldTarget(target, tile, pods, maxLaunchDistance, launchAction, launchable, overrideFuelLevel);
        return false;
    }
}

[HarmonyPatch]
public static class CP_MapParent_ShouldRemoveMapNow_Patch
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Camp), nameof(Camp.ShouldRemoveMapNow));
        yield return AccessTools.Method(typeof(Site), nameof(Site.ShouldRemoveMapNow));
        yield return AccessTools.Method(typeof(Settlement), nameof(Settlement.ShouldRemoveMapNow));
    }

    public static void Postfix(MapParent __instance, ref bool alsoRemoveWorldObject, ref bool __result)
    {
        if (!__result || __instance.Map == null)
            return;

        if (!CP_ProjectionBeaconUtility.HasActiveProjectionBeacon(__instance.Map))
            return;

        alsoRemoveWorldObject = false;
        __result = false;
    }
}