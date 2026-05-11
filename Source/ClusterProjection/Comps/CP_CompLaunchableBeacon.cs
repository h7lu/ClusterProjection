using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ClusterProjection;

public class CP_CompLaunchableBeacon : CompLaunchable_TransportPod
{
    public override AcceptanceReport CanLaunch(float? overrideFuelLevel = null)
    {
        if (!Transporter.LoadingInProgressOrReadyToLaunch)
            return "CommandLaunchGroupFailNotLoaded".Translate();

        if (parent.Spawned && parent.Position.Roofed(parent.Map))
            return "CommandLaunchGroupFailUnderRoof".Translate();

        var fuelSource = Refuelable;
        var availableFuel = overrideFuelLevel ?? FuelLevel;
        if (fuelSource == null || availableFuel < Props.minFuelCost || !fuelSource.HasFuel)
            return "CommandLaunchGroupFailNoFuel".Translate();

        var cooldownTicksLeft = Props.cooldownTicks - Find.TickManager.TicksGame + lastLaunchTick;
        if (Props.cooldownTicks > 0 && lastLaunchTick > 0 && cooldownTicksLeft > 0)
            return "CommandLaunchGroupCooldown".Translate() + " (" + cooldownTicksLeft.ToStringTicksToPeriod() + ")";

        return true;
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        var transporter = parent.GetComp<CompTransporter>();
        if (transporter != null && !transporter.Groupable)
        {
            var selectedTransporters = 0;
            foreach (var selectedObject in Find.Selector.SelectedObjects)
            {
                if (selectedObject is ThingWithComps thing && thing.HasComp<CompTransporter>())
                    selectedTransporters++;
            }

            if (selectedTransporters > 1)
                yield break;
        }

        var launch = new Command_Action
        {
            defaultLabel = "CommandLaunchGroup".Translate(),
            defaultDesc = "CommandLaunchGroupDesc".Translate(),
            icon = LaunchCommandTex,
            alsoClickIfOtherInGroupClicked = false,
            action = () => StartChoosingDestination(TryLaunch)
        };

        var acceptance = CanLaunch();
        if (!acceptance.Accepted)
            launch.Disable(acceptance.Reason);

        yield return launch;

        if (DebugSettings.ShowDevGizmos)
        {
            yield return new Command_Action
            {
                defaultLabel = "DEV: End cooldown",
                action = () => lastLaunchTick = Find.TickManager.TicksGame - Props.cooldownTicks
            };
        }
    }

    public override string CompInspectStringExtra()
    {
        if (!Transporter.LoadingInProgressOrReadyToLaunch)
            return null;

        if (Refuelable == null || !Refuelable.HasFuel)
            return "NotReadyForLaunch".Translate() + ": " + "NotAllLaunchablesInGroupHaveAnyFuel".Translate().CapitalizeFirst() + ".";

        var cooldownTicksLeft = Props.cooldownTicks - Find.TickManager.TicksGame + lastLaunchTick;
        if (Props.cooldownTicks > 0 && lastLaunchTick > 0 && cooldownTicksLeft > 0)
            return "NotReadyForLaunch".Translate() + ": " + "CommandLaunchGroupCooldown".Translate() + " (" + cooldownTicksLeft.ToStringTicksToPeriod() + ")";

        return "ReadyForLaunch".Translate();
    }

    public new void TryLaunch(PlanetTile destinationTile, TransportersArrivalAction arrivalAction)
    {
        if (!parent.Spawned)
        {
            Log.Error($"Tried to launch {parent}, but it's unspawned.");
            return;
        }

        if (!CanLaunch())
            return;

        var map = parent.Map;
        var distance = Find.WorldGrid.TraversalDistanceBetween(map.Tile, destinationTile, true, int.MaxValue, true);
        Current.Game.CurrentMap = map;

        if (distance > MaxLaunchDistanceAtFuelLevel(FuelLevel, destinationTile.Layer))
            return;

        Transporter.TryRemoveLord(map);
        var groupId = Transporter.groupID;
        var fuelCost = Mathf.Max(FuelNeededToLaunchAtDist(distance, destinationTile.Layer), 1f);
        lastLaunchTick = Find.TickManager.TicksGame;

        var refuelable = Refuelable;
        refuelable?.ConsumeFuel(fuelCost);

        var beacon = (Building_ProjectionBeacon)ThingMaker.MakeThing(parent.def);
        var activeTransporter = (ActiveTransporter)ThingMaker.MakeThing(Props.activeTransporterDef ?? ThingDefOf.ActiveDropPod);
        activeTransporter.Contents = new ActiveTransporterInfo();
        activeTransporter.Contents.innerContainer.TryAdd(beacon);
        activeTransporter.Contents.sentTransporterDef = parent.def;
        activeTransporter.Rotation = parent.Rotation;

        var skyfaller = (FlyShipLeaving)SkyfallerMaker.MakeSkyfaller(Props.skyfallerLeaving ?? ThingDefOf.DropPodLeaving, activeTransporter);
        skyfaller.groupID = groupId;
        skyfaller.destinationTile = destinationTile;
        skyfaller.arrivalAction = arrivalAction;
        skyfaller.worldObjectDef = Props.worldObjectDef ?? WorldObjectDefOf.TravellingTransporters;

        Transporter.CleanUpLoadingVars(map);
        var launchPos = parent.Position;
        parent.Destroy();
        GenSpawn.Spawn(skyfaller, launchPos, map);

        if (refuelable?.parent is INotifyLaunchableLaunch notifyLaunchableLaunch)
            notifyLaunchableLaunch.Notify_LaunchableLaunched(this);

        CameraJumper.TryHideWorld();
    }
}