using ClusterProjection.DefOfs;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ClusterProjection;

public class Building_ClusterProjectionConsole : Building
{
    private sealed class LinkedTankState
    {
        public int ThingId;
        public ThingDef FuelDef;
        public float CapacityBonus;
        public IntVec3 Position;
    }

    private sealed class PendingPodLaunch
    {
        public Building_ClusterPod Pod;
        public int DelayTicks;
        public IntVec3 LandingOffset;
    }

    private sealed class LaunchPreviewHolder : IThingHolder
    {
        private readonly ThingOwner heldThings;

        public IThingHolder ParentHolder => null;

        public LaunchPreviewHolder(ThingOwner heldThings)
        {
            this.heldThings = heldThings;
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return heldThings;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, heldThings);
        }
    }

    private static readonly Color AreaColor = new Color(0f, 1f, 1f, 0.35f);
    private static readonly Color EdgeColor = new Color(0f, 1f, 1f, 1f);
    private static readonly Color LaunchableBuildingColor = new Color(0.15f, 1f, 0.15f, 0.75f);
    private static readonly Color UnlaunchableBuildingColor = new Color(1f, 0.2f, 0.2f, 0.75f);
    private static readonly Color LandingGhostColor = new Color(0.8f, 0.8f, 0.8f, 0.14f);
    private static readonly Color LandingGhostEdgeColor = new Color(0.8f, 0.8f, 0.8f, 0.5f);

    private bool assemblyActive;
    private bool assemblyReturning;
    private int currentAssemblyIndex;
    private int assemblyStayTicksLeft;
    private int lastLaunchMoteSecondShown;
    private int launchCountdownInitialTicks;
    private Vector3 assemblyHeadPosition;
    private float assemblyRailHorizontalZ; // world-space z of the E/W rail
    private float assemblyRailVerticalX;   // world-space x of the N/S rail
    private int launchCountdownTicksLeft;
    private int launchGroupID = -1;
    private PlanetTile launchDestinationTile;
    private TransportersArrivalAction launchArrivalAction;
    private List<Building> assemblyTargets = new();
    private List<PendingPodLaunch> pendingPodLaunches = new();
    private Dictionary<int, LinkedTankState> linkedTankStates = new();
    private Effecter assemblyEffecter;
    private Material assemblyHeadMaterial;

    private CP_AssemblyProperties AssemblyProps => def.GetModExtension<CP_AssemblyProperties>() ?? new CP_AssemblyProperties();

    private CP_CompRefuelable FuelComp => GetRefuelableCompFor(ThingDefOf.Chemfuel);

    private CP_CompRefuelable SteelComp => GetRefuelableCompFor(ThingDefOf.Steel);

    public CP_CompRefuelable SteelStorageComp => SteelComp;

    private CP_CompRefuelable GetRefuelableCompFor(ThingDef fuelDef)
    {
        return GetComps<CP_CompRefuelable>().FirstOrDefault(c => c?.Props?.fuelFilter != null && c.Props.fuelFilter.Allows(fuelDef));
    }

    private Material AssemblyHeadMaterial => assemblyHeadMaterial ??= MaterialPool.MatFrom("assembly_head", ShaderDatabase.Transparent);

    private Material RodHorizontalMaterial => MaterialPool.MatFrom("Rail/rod_horizontal", ShaderDatabase.Transparent);
    private Material RodVerticalMaterial => MaterialPool.MatFrom("Rail/rod_vertical", ShaderDatabase.Transparent);
    private Material AttacherHorizontalMaterial => MaterialPool.MatFrom("Rail/rail_attacher_vertical", ShaderDatabase.Transparent);
    private Material AttacherVerticalMaterial => MaterialPool.MatFrom("Rail/rail_attacher_horizontal", ShaderDatabase.Transparent);

    private bool LaunchActive => launchCountdownTicksLeft > 0 || pendingPodLaunches.Count > 0;

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        UpdateLinkedTankCapacities();
    }

    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var gizmo in base.GetGizmos())
            yield return gizmo;

        if (Prefs.DevMode)
        {
            var loadSteel = new Command_Action
            {
                defaultLabel = "CP_LoadSteel".Translate(),
                defaultDesc = "CP_LoadSteelDesc".Translate(),
                icon = ThingDefOf.Steel.uiIcon,
                action = LoadSteelFromMap
            };
            yield return loadSteel;
        }

        var ejectSteel = new Command_Action
        {
            defaultLabel = "CP_EjectSteel".Translate(),
            defaultDesc = "CP_EjectSteelDesc".Translate(),
            icon = ThingDefOf.Steel.uiIcon,
            action = EjectSteel
        };
        if (SteelComp == null || SteelComp.Fuel <= 0f)
            ejectSteel.Disable("CommandDisabledNoStoredSteel".TranslateSimple());
        yield return ejectSteel;

        var ejectFuel = new Command_Action
        {
            defaultLabel = "CP_EjectFuel".Translate(),
            defaultDesc = "CP_EjectFuelDesc".Translate(),
            icon = ThingDefOf.Chemfuel.uiIcon,
            action = EjectFuel
        };
        if (FuelComp == null || FuelComp.Fuel <= 0f)
            ejectFuel.Disable("CommandDisabledNoFuel".TranslateSimple());
        yield return ejectFuel;

        var assemble = new Command_Action
        {
            defaultLabel = "CP_AssemblyAndLaunch".Translate(),
            defaultDesc = "CP_AssemblyAndLaunchDesc".Translate(),
            icon = CP_ThingDefOf.CP_ClusterPod.uiIcon,
            action = TryStartAssemblyAndLaunch
        };
        if (assemblyActive || LaunchActive)
            assemble.Disable("Busy".Translate());
        yield return assemble;

        var launch = new Command_Action
        {
            defaultLabel = "CP_LaunchCluster".Translate(),
            defaultDesc = "CP_LaunchClusterDesc".Translate(),
            icon = CompLaunchable.LaunchCommandTex,
            action = StartChoosingLaunchDestination
        };

        var launchDisableReason = GetLaunchDisabledReason();
        if (!launchDisableReason.NullOrEmpty())
            launch.Disable(launchDisableReason);
        yield return launch;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            assemblyActive = false;
            assemblyReturning = false;
            launchCountdownTicksLeft = 0;
            launchGroupID = -1;
            launchDestinationTile = PlanetTile.Invalid;
            launchArrivalAction = null;
            assemblyTargets = new List<Building>();
            pendingPodLaunches = new List<PendingPodLaunch>();
            linkedTankStates = new Dictionary<int, LinkedTankState>();
            assemblyEffecter = null;
        }
    }

    protected override void Tick()
    {
        base.Tick();
        UpdateLinkedTankCapacities();
        if (assemblyActive)
            TickAssembly();
        if (LaunchActive)
            TickLaunch();
    }

    private void UpdateLinkedTankCapacities()
    {
        var currentStates = GetCurrentLinkedTankStates();
        UpdateLinkedTankCapacity(ThingDefOf.Chemfuel, FuelComp, currentStates);
        UpdateLinkedTankCapacity(ThingDefOf.Steel, SteelComp, currentStates);
        linkedTankStates = currentStates;
    }

    private void UpdateLinkedTankCapacity(ThingDef fuelDef, CP_CompRefuelable comp, Dictionary<int, LinkedTankState> currentStates)
    {
        if (comp == null)
            return;

        var newCapacity = comp.BaseFuelCapacity;
        foreach (var state in currentStates.Values)
        {
            if (state.FuelDef == fuelDef)
                newCapacity += state.CapacityBonus;
        }

        comp.SetCapacity(newCapacity);

        var overflow = comp.ClampFuelToCapacity();
        if (overflow <= 0f)
            return;

        var removedStates = linkedTankStates.Values
            .Where(state => state.FuelDef == fuelDef && !currentStates.ContainsKey(state.ThingId))
            .OrderBy(state => state.ThingId)
            .ToList();

        DropOverflowAtRemovedTanks(fuelDef, overflow, removedStates);
    }

    private void DropOverflowAtRemovedTanks(ThingDef fuelDef, float overflow, List<LinkedTankState> removedStates)
    {
        if (!Spawned || Map == null || overflow <= 0f || fuelDef == null)
            return;

        var remainingOverflow = Mathf.FloorToInt(overflow);
        if (remainingOverflow <= 0)
            return;

        if (removedStates.Count == 0)
        {
            DropResourceNear(Position, fuelDef, remainingOverflow);
            return;
        }

        var remainingBonus = removedStates.Sum(state => state.CapacityBonus);
        for (var i = 0; i < removedStates.Count && remainingOverflow > 0; i++)
        {
            var removedState = removedStates[i];
            int amountToDrop;
            if (i == removedStates.Count - 1 || remainingBonus <= 0f)
            {
                amountToDrop = remainingOverflow;
            }
            else
            {
                amountToDrop = Mathf.Min(remainingOverflow, Mathf.RoundToInt(overflow * (removedState.CapacityBonus / remainingBonus)));
                if (amountToDrop <= 0)
                    amountToDrop = Mathf.Min(remainingOverflow, 1);
            }

            DropResourceNear(removedState.Position, fuelDef, amountToDrop);
            remainingOverflow -= amountToDrop;
            remainingBonus -= removedState.CapacityBonus;
        }
    }

    private void DropResourceNear(IntVec3 position, ThingDef fuelDef, int amount)
    {
        while (amount > 0)
        {
            var thing = ThingMaker.MakeThing(fuelDef);
            thing.stackCount = Mathf.Min(amount, fuelDef.stackLimit);
            amount -= thing.stackCount;
            GenPlace.TryPlaceThing(thing, position, Map, ThingPlaceMode.Near);
            thing.SetForbidden(true, false);
        }
    }

    private Dictionary<int, LinkedTankState> GetCurrentLinkedTankStates()
    {
        var states = new Dictionary<int, LinkedTankState>();
        if (!Spawned || Map == null)
            return states;

        foreach (var thing in GenRadial.RadialDistinctThingsAround(Position, Map, 4.9f, true))
        {
            if (thing is not Building_ClusterExpansionTank tank || tank.GetLinkedConsole() != this)
                continue;

            states[tank.thingIDNumber] = new LinkedTankState
            {
                ThingId = tank.thingIDNumber,
                FuelDef = tank.FuelDef,
                CapacityBonus = tank.CapacityBonus,
                Position = tank.Position
            };
        }

        return states;
    }

    private float GetEffectiveCapacity(ThingDef fuelDef)
    {
        var comp = GetRefuelableCompFor(fuelDef);
        if (comp == null)
            return 0f;

        var additionalCapacity = 0f;
        if (Spawned)
        {
            foreach (var tank in GetLinkedExpansionTanks())
            {
                if (tank.FuelDef == fuelDef)
                    additionalCapacity += tank.CapacityBonus;
            }
        }

        return comp.BaseFuelCapacity + additionalCapacity;
    }

    private IEnumerable<Building_ClusterExpansionTank> GetLinkedExpansionTanks()
    {
        if (!Spawned || Map == null)
            yield break;

        foreach (var thing in GenRadial.RadialDistinctThingsAround(Position, Map, 4.9f, true))
        {
            if (thing is Building_ClusterExpansionTank tank && tank.GetLinkedConsole() == this)
                yield return tank;
        }
    }

    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        base.DrawAt(drawLoc, flip);
        if (!assemblyActive)
            return;

        var rodAltitude  = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
        var headAltitude = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead) + 0.01f; // head above rods

        var headPos = assemblyHeadPosition;
        headPos.y = headAltitude;

        // attacher_vertical: sits on the E/W (horizontal) rail, tracks head's x position
        var attacherVerticalPos = new Vector3(headPos.x, rodAltitude, assemblyRailHorizontalZ);
        // attacher_horizontal: sits on the N/S (vertical) rail, tracks head's z position
        var attacherHorizontalPos = new Vector3(assemblyRailVerticalX, rodAltitude, headPos.z);

        // rod_vertical: stretches along z between attacherVertical and head (no texture rotation)
        DrawRodVertical(attacherVerticalPos, headPos, RodVerticalMaterial);

        // rod_horizontal: stretches along x between attacherHorizontal and head (no texture rotation)
        DrawRodHorizontal(attacherHorizontalPos, headPos, RodHorizontalMaterial);

        // Draw attachers
        var attacherScale = new Vector3(1.5f, 1f, 1.5f);
        Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(attacherVerticalPos,   Quaternion.identity, attacherScale), AttacherVerticalMaterial,   0);
        Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(attacherHorizontalPos, Quaternion.identity, attacherScale), AttacherHorizontalMaterial, 0);

        // Draw assembly head on top
        Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(headPos, Quaternion.identity, new Vector3(2f, 1f, 2f)), AssemblyHeadMaterial, 0);
    }

    // Stretches a horizontal (E/W) texture along the x-axis without rotation.
    private static void DrawRodHorizontal(Vector3 a, Vector3 b, Material mat)
    {
        var length = Mathf.Abs(b.x - a.x);
        if (length < 0.01f)
            return;
        var midpoint = new Vector3((a.x + b.x) * 0.5f, a.y, a.z);
        Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(midpoint, Quaternion.identity, new Vector3(length, 1f, 1f)), mat, 0);
    }

    // Stretches a vertical (N/S) texture along the z-axis without rotation.
    private static void DrawRodVertical(Vector3 a, Vector3 b, Material mat)
    {
        var length = Mathf.Abs(b.z - a.z);
        if (length < 0.01f)
            return;
        var midpoint = new Vector3(a.x, a.y, (a.z + b.z) * 0.5f);
        Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(midpoint, Quaternion.identity, new Vector3(1f, 1f, length)), mat, 0);
    }

    public override void DrawExtraSelectionOverlays()
    {
        base.DrawExtraSelectionOverlays();

        var logKey = $"ConsoleOverlay {Position}";

        if (!Spawned)
        {
            CP_Debug.Message(logKey, "selection overlay skipped because console is not spawned.", 120, onlyOnChange: true);
            return;
        }

        if (!LaunchAreaSolver.TryComputeLargestArea(Map, Position, def.Size, out var area))
        {
            CP_Debug.Message(logKey, "selected but solver returned no valid launch area.", 120, onlyOnChange: true);
            return;
        }

        CP_Debug.Message(logKey,
            $"drawing overlay width={area.Width} height={area.Height} interior={area.allInteriorCells.Count} launchable={area.LaunchableCount}",
            120,
            onlyOnChange: true);

        // Only draw the tiles that have launcher floor (launchable cells)
        if (area.launchableCells.Count > 0)
        {
            GenDraw.DrawDiagonalStripes(area.launchableCells, AreaColor);
            GenDraw.DrawFieldEdges(area.launchableCells, EdgeColor);

            var launchableBuildingCells = new List<IntVec3>();
            var unlaunchableBuildingCells = new List<IntVec3>();
            var launchableSet = new HashSet<IntVec3>(area.launchableCells);
            var seenBuildings = new HashSet<Building>();

            for (var i = 0; i < area.launchableCells.Count; i++)
            {
                var cell = area.launchableCells[i];
                var things = cell.GetThingList(Map);
                for (var j = 0; j < things.Count; j++)
                {
                    if (things[j] is not Building building)
                        continue;
                    if (!seenBuildings.Add(building))
                        continue;

                    var isUnlaunchable = IsUnlaunchable(building.def);
                    foreach (var occupiedCell in GenAdj.OccupiedRect(building.Position, building.Rotation, building.def.size))
                    {
                        if (!launchableSet.Contains(occupiedCell))
                            continue;

                        if (isUnlaunchable)
                            unlaunchableBuildingCells.Add(occupiedCell);
                        else
                            launchableBuildingCells.Add(occupiedCell);
                    }
                }
            }

            if (launchableBuildingCells.Count > 0)
                GenDraw.DrawDiagonalStripes(launchableBuildingCells, LaunchableBuildingColor, 0.018f);
            if (unlaunchableBuildingCells.Count > 0)
                GenDraw.DrawDiagonalStripes(unlaunchableBuildingCells, UnlaunchableBuildingColor, 0.018f);
        }
    }

    private bool IsUnlaunchable(ThingDef thingDef)
    {
        if (thingDef.terrainAffordanceNeeded == null || thingDef.terrainAffordanceNeeded.defName != "Heavy")
            return false;

        return thingDef.size.x * thingDef.size.z >= AssemblyProps.maxHeavyBuildingArea;
    }

    private void TryStartAssemblyAndLaunch()
    {
        if (assemblyActive || LaunchActive)
            return;

        if (!LaunchAreaSolver.TryComputeLargestArea(Map, Position, def.Size, out var area))
        {
            Messages.Message("CP_NoValidLaunchArea".Translate(), MessageTypeDefOf.RejectInput, false);
            return;
        }

        if (area.allInteriorCells.Any(cell => cell.Roofed(Map)))
        {
            Messages.Message("CP_LaunchAreaRoofed".Translate(), MessageTypeDefOf.RejectInput, false);
            return;
        }

        var buildings = GetLaunchableBuildings(area);
        if (buildings.Count == 0)
        {
            Messages.Message("CP_NoLaunchableBuildings".Translate(), MessageTypeDefOf.RejectInput, false);
            return;
        }

        var props = AssemblyProps;
        var steelComp = SteelComp;
        var steelNeeded = Mathf.CeilToInt(props.baseSteelConsumption * buildings.Count);
        var storedSteel = steelComp?.Fuel ?? 0f;
        if (steelComp == null || storedSteel < steelNeeded)
        {
            Messages.Message("CP_NotEnoughSteel".Translate(steelNeeded, storedSteel.ToStringDecimalIfSmall()), MessageTypeDefOf.RejectInput, false);
            return;
        }

        var fuelNeededForOneTile = props.baseFuelConsumption * buildings.Count;
        if (FuelComp == null || FuelComp.Fuel < fuelNeededForOneTile)
        {
            var storedFuel = FuelComp == null ? "0" : FuelComp.Fuel.ToStringDecimalIfSmall();
            Messages.Message("CP_NotEnoughFuel".Translate(fuelNeededForOneTile.ToStringDecimalIfSmall(), storedFuel), MessageTypeDefOf.RejectInput, false);
            return;
        }

        steelComp.ConsumeFuel(steelNeeded);
        assemblyTargets = OrderAssemblyTargets(buildings);
        currentAssemblyIndex = 0;
        assemblyStayTicksLeft = 0;
        assemblyReturning = false;
        assemblyHeadPosition = this.TrueCenter();
        assemblyRailHorizontalZ = area.RailHorizontalZ;
        assemblyRailVerticalX   = area.RailVerticalX;
        assemblyActive = true;
        Messages.Message("CP_AssemblyStarted".Translate(assemblyTargets.Count), MessageTypeDefOf.TaskCompletion, false);
    }

    private List<Building> GetLaunchableBuildings(LaunchAreaData area)
    {
        var result = new List<Building>();
        var seenBuildings = new HashSet<Building>();
        var launchableSet = new HashSet<IntVec3>(area.launchableCells);

        for (var i = 0; i < area.launchableCells.Count; i++)
        {
            var things = area.launchableCells[i].GetThingList(Map);
            for (var j = 0; j < things.Count; j++)
            {
                if (things[j] is not Building building)
                    continue;
                if (!seenBuildings.Add(building))
                    continue;
                if (building == this || building.def == CP_ThingDefOf.CP_AutoAssemblyRail || building.def == CP_ThingDefOf.CP_ClusterPod)
                    continue;
                if (AssemblyProps.packWires && IsPackableWire(building))
                    continue;
                if (IsUnlaunchable(building.def))
                    continue;
                if (!IsFullyInsideLaunchableArea(building, launchableSet))
                    continue;

                result.Add(building);
            }
        }

        return result;
    }

    private static bool IsPackableWire(Building building)
    {
        return building?.def.building?.isPowerConduit == true;
    }

    private List<Building_ClusterPod> GetClusterPodsInLaunchArea(LaunchAreaData area)
    {
        var result = new List<Building_ClusterPod>();
        var seenPods = new HashSet<Building_ClusterPod>();
        var launchableSet = new HashSet<IntVec3>(area.launchableCells);

        for (var i = 0; i < area.launchableCells.Count; i++)
        {
            var things = area.launchableCells[i].GetThingList(Map);
            for (var j = 0; j < things.Count; j++)
            {
                if (things[j] is not Building_ClusterPod pod)
                    continue;
                if (!seenPods.Add(pod))
                    continue;
                if (!IsFullyInsideLaunchableArea(pod, launchableSet))
                    continue;

                result.Add(pod);
            }
        }

        return result;
    }

    private static bool IsFullyInsideLaunchableArea(Building building, HashSet<IntVec3> launchableSet)
    {
        foreach (var occupiedCell in GenAdj.OccupiedRect(building.Position, building.Rotation, building.def.size))
        {
            if (!launchableSet.Contains(occupiedCell))
                return false;
        }
        return true;
    }

    private List<Building> OrderAssemblyTargets(List<Building> buildings)
    {
        var remaining = new List<Building>(buildings);
        var ordered = new List<Building>();
        var current = this.TrueCenter();

        while (remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < remaining.Count; i++)
            {
                var distance = (remaining[i].TrueCenter() - current).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            var next = remaining[bestIndex];
            remaining.RemoveAt(bestIndex);
            ordered.Add(next);
            current = next.TrueCenter();
        }

        return ordered;
    }

    private void TickAssembly()
    {
        TrySpawnAssemblyMote();
        RefreshAssemblyTargetsAndPackedWires();
        var speed = Mathf.Max(AssemblyProps.assemblyHeadSpeedCellsPerTick, 0.01f);

        if (assemblyReturning)
        {
            var home = this.TrueCenter();
            var homeOffset = home - assemblyHeadPosition;
            var homeDistance = homeOffset.magnitude;
            if (homeDistance <= speed)
            {
                assemblyHeadPosition = home;
                FinishAssembly();
            }
            else
            {
                assemblyHeadPosition += homeOffset / homeDistance * speed;
            }
            return;
        }

        if (currentAssemblyIndex >= assemblyTargets.Count)
        {
            CleanupAssemblyEffecter();
            assemblyReturning = true;
            return;
        }

        var target = assemblyTargets[currentAssemblyIndex];
        if (target == null || target.Destroyed || !target.Spawned)
        {
            currentAssemblyIndex++;
            assemblyStayTicksLeft = 0;
            CleanupAssemblyEffecter();
            return;
        }

        if (assemblyStayTicksLeft > 0)
        {
            var targetInfo = new TargetInfo(target.Position, Map);
            assemblyEffecter ??= EffecterDefOf.ConstructMetal.Spawn(target.Position, Map);
            assemblyEffecter.EffectTick(targetInfo, TargetInfo.Invalid);
            assemblyStayTicksLeft--;

            if (assemblyStayTicksLeft <= 0)
            {
                CleanupAssemblyEffecter();
                ReplaceBuildingWithPod(target);
                currentAssemblyIndex++;
            }
            return;
        }

        var targetCenter = target.TrueCenter();
        var offset = targetCenter - assemblyHeadPosition;
        var distance = offset.magnitude;
        if (distance <= speed)
        {
            assemblyHeadPosition = targetCenter;
            assemblyStayTicksLeft = Mathf.Max(1, Mathf.RoundToInt(AssemblyProps.baseAssemblyTicks * target.def.size.x * target.def.size.z));
        }
        else
        {
            assemblyHeadPosition += offset / distance * speed;
        }
    }

    private void ReplaceBuildingWithPod(Building building)
    {
        var map = building.Map;
        var position = building.Position;

        var pod = (Building_ClusterPod)ThingMaker.MakeThing(CP_ThingDefOf.CP_ClusterPod);
        pod.SetFaction(Faction.OfPlayer);
        pod.Store(building);
        GenSpawn.Spawn(pod, position, map, Rot4.North);

        if (AssemblyProps.packWires && LaunchAreaSolver.TryComputeLargestArea(Map, Position, def.Size, out var area))
            TryCapturePackedWires(area);
    }

    private void RefreshAssemblyTargetsAndPackedWires()
    {
        if (!assemblyActive || Map == null || !Spawned)
            return;

        if (!LaunchAreaSolver.TryComputeLargestArea(Map, Position, def.Size, out var area))
            return;

        var knownTargets = new HashSet<Building>(assemblyTargets);
        var newTargets = GetLaunchableBuildings(area)
            .Where(building => !knownTargets.Contains(building))
            .OrderBy(building => (building.TrueCenter() - assemblyHeadPosition).sqrMagnitude)
            .ToList();

        for (var i = 0; i < newTargets.Count; i++)
            assemblyTargets.Add(newTargets[i]);

        if (AssemblyProps.packWires)
            TryCapturePackedWires(area);
    }

    private void TryCapturePackedWires(LaunchAreaData area)
    {
        var carrierPod = GetWireCarrierPod(area);
        if (carrierPod == null)
            return;

        var seenPositions = new HashSet<IntVec3>();
        for (var i = 0; i < area.launchableCells.Count; i++)
        {
            var cell = area.launchableCells[i];
            var things = new List<Thing>(cell.GetThingList(Map));
            for (var j = 0; j < things.Count; j++)
            {
                if (things[j] is not Building wire || !IsPackableWire(wire))
                    continue;
                if (!seenPositions.Add(wire.Position))
                    continue;
                if (carrierPod.HasPackedWireAtSourcePosition(wire.Position))
                    continue;

                carrierPod.AddPackedWire(wire);
                if (wire.Spawned)
                    wire.DeSpawn(DestroyMode.Vanish);
            }
        }
    }

    private Building_ClusterPod GetWireCarrierPod(LaunchAreaData area)
    {
        var pods = GetClusterPodsInLaunchArea(area);
        return pods.FirstOrDefault(pod => pod.HasPackedWires)
            ?? pods.OrderBy(pod => pod.thingIDNumber).FirstOrDefault();
    }

    private void FinishAssembly()
    {
        CleanupAssemblyEffecter();
        assemblyTargets.Clear();
        assemblyReturning = false;
        assemblyActive = false;
        Messages.Message("CP_AssemblyComplete".Translate(), MessageTypeDefOf.TaskCompletion, false);
    }

    private void StartChoosingLaunchDestination()
    {
        if (!Spawned)
            return;

        var launchDisableReason = GetLaunchDisabledReason();
        if (!launchDisableReason.NullOrEmpty())
        {
            Messages.Message(launchDisableReason, MessageTypeDefOf.RejectInput, false);
            return;
        }

        var originTile = Map.Tile;
        CameraJumper.TryJump(CameraJumper.GetWorldTarget(new GlobalTargetInfo(originTile)));
        Find.WorldSelector.ClearSelection();
        Find.WorldTargeter.BeginTargeting(
            target => ChooseLaunchDestination(target, originTile),
            true,
            CompLaunchable.TargeterMouseAttachment,
            true,
            () => DrawLaunchRangeRing(originTile),
            target => GetLaunchTargetLabel(target, originTile),
            null,
            originTile,
            true);
    }

    private bool ChooseLaunchDestination(GlobalTargetInfo target, PlanetTile originTile)
    {
        if (!target.IsValid || !target.Tile.Valid)
            return false;

        var launchDisableReason = GetLaunchDisabledReason();
        if (!launchDisableReason.NullOrEmpty())
        {
            Messages.Message(launchDisableReason, this, MessageTypeDefOf.RejectInput, false);
            return true;
        }

        var pods = GetCurrentLaunchPods();
        if (pods.Count == 0)
            return true;

        if (TryGetDestinationMapForCellTargeting(target.Tile, out var destinationMap))
        {
            StartChoosingMapLandingCell(target.Tile, destinationMap, pods);
            return true;
        }

        if (pods.Any(pod => pod.HasTransportablePawns()) && !TryGetBeaconMaintainedDestinationMap(target.Tile, out _))
            return ChooseClusterWorldTarget(target, originTile, pods);

        return TryBeginLaunchSequence(target.Tile, new CP_TransportersArrivalAction_ClusterDrop());
    }

    private bool ChooseClusterWorldTarget(GlobalTargetInfo target, PlanetTile originTile, List<Building_ClusterPod> pods)
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

        var distance = GetWorldDistance(originTile, target.Tile);
        var maxLaunchDistance = GetMaxLaunchDistance(pods.Count);
        if (maxLaunchDistance >= 0 && distance > maxLaunchDistance)
        {
            Messages.Message("TransportPodDestinationBeyondMaximumRange".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        var options = GetClusterFloatMenuOptionsAt(target.Tile, pods).ToList();
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

    private IEnumerable<FloatMenuOption> GetClusterFloatMenuOptionsAt(PlanetTile destinationTile, List<Building_ClusterPod> pods)
    {
        var previewPods = GetLaunchPreviewPods(pods);
        var anything = false;

        if (TransportersArrivalAction_FormCaravan.CanFormCaravanAt(previewPods, destinationTile)
            && !Find.WorldObjects.AnySettlementBaseAt(destinationTile)
            && !Find.WorldObjects.AnySiteAt(destinationTile))
        {
            anything = true;
            yield return new FloatMenuOption(
                "FormCaravanHere".Translate(),
                () => BeginLaunchSequenceFromMenu(destinationTile, new CP_TransportersArrivalAction_ClusterCamp()));
        }

        var worldObjects = Find.WorldObjects.AllWorldObjects;
        for (var i = 0; i < worldObjects.Count; i++)
        {
            var worldObject = worldObjects[i];
            if (worldObject.Tile != destinationTile)
                continue;

            if (worldObject is Settlement settlement && settlement.Attackable)
            {
                anything = true;
                foreach (var option in CP_TransportersArrivalAction_ClusterAttackSettlement.GetFloatMenuOptions(BeginLaunchSequenceFromMenu, previewPods, settlement))
                    yield return option;
                continue;
            }

            if (worldObject is Site site)
            {
                anything = true;
                foreach (var option in CP_TransportersArrivalAction_ClusterVisitSite.GetFloatMenuOptions(BeginLaunchSequenceFromMenu, previewPods, site))
                    yield return option;
                continue;
            }

            foreach (var option in worldObject.GetTransportersFloatMenuOptions(previewPods, BeginLaunchSequenceFromMenu))
            {
                anything = true;
                yield return option;
            }
        }

        if (!anything && !Find.World.Impassable(destinationTile))
        {
            yield return new FloatMenuOption(
                "FormCaravanHere".Translate(),
                () => BeginLaunchSequenceFromMenu(destinationTile, new CP_TransportersArrivalAction_ClusterCamp()));
        }
    }

    private void BeginLaunchSequenceFromMenu(PlanetTile destinationTile, TransportersArrivalAction arrivalAction)
    {
        TryBeginLaunchSequence(destinationTile, arrivalAction);
    }

    private bool TryGetDestinationMapForCellTargeting(PlanetTile destinationTile, out Map map)
    {
        map = null;
        var mapParent = Find.WorldObjects.MapParentAt(destinationTile);
        if (mapParent == null || !mapParent.HasMap)
            return false;

        map = mapParent.Map;
        return map != null;
    }

    private static bool TryGetBeaconMaintainedDestinationMap(PlanetTile destinationTile, out Map map)
    {
        map = null;
        var mapParent = Find.WorldObjects.MapParentAt(destinationTile);
        if (mapParent == null || !mapParent.HasMap || mapParent.Map == null)
            return false;

        if (!CP_ProjectionBeaconUtility.HasActiveProjectionBeacon(mapParent.Map))
            return false;

        map = mapParent.Map;
        return true;
    }

    private void StartChoosingMapLandingCell(PlanetTile destinationTile, Map destinationMap, List<Building_ClusterPod> launchPods)
    {
        var footprintOffsets = BuildClusterFootprintOffsets(launchPods);

        CameraJumper.TryHideWorld();
        CameraJumper.TryJump(destinationMap.Center, destinationMap);

        Action<LocalTargetInfo> onSelected = target =>
        {
            if (!target.IsValid || !target.Cell.IsValid)
                return;

            TryBeginLaunchSequence(destinationTile, new CP_TransportersArrivalAction_ClusterDrop(target.Cell));
        };

        Action<LocalTargetInfo> onHighlight = target => DrawLandingGhost(target, destinationMap, footprintOffsets);
        Func<LocalTargetInfo, bool> validator = target => IsLandingGhostPlacementValid(target, destinationMap, footprintOffsets);

        Find.Targeter.BeginTargeting(
            TargetingParameters.ForDropPodsDestination(),
            onSelected,
            onHighlight,
            validator,
            null,
            () => CameraJumper.TryHideWorld(),
            CompLaunchable.TargeterMouseAttachment);
    }

    private static List<IntVec3> BuildClusterFootprintOffsets(List<Building_ClusterPod> launchPods)
    {
        var offsets = new HashSet<IntVec3>();
        if (launchPods == null || launchPods.Count == 0)
            return offsets.ToList();

        var anchor = GetClusterLandingAnchor(launchPods);
        for (var i = 0; i < launchPods.Count; i++)
        {
            var pod = launchPods[i];
            if (pod == null || pod.Destroyed)
                continue;

            var containedThing = pod.ContainedThing;
            if (containedThing is Building containedBuilding)
            {
                foreach (var occupiedCell in GenAdj.OccupiedRect(pod.Position, containedBuilding.Rotation, containedBuilding.def.size))
                    offsets.Add(occupiedCell - anchor);
                continue;
            }

            offsets.Add(pod.Position - anchor);
        }

        return offsets.ToList();
    }

    private static void DrawLandingGhost(LocalTargetInfo target, Map map, List<IntVec3> footprintOffsets)
    {
        if (!target.IsValid || !target.Cell.IsValid || map == null || footprintOffsets == null || footprintOffsets.Count == 0)
            return;

        var ghostCells = footprintOffsets
            .Select(offset => target.Cell + offset)
            .Where(cell => cell.InBounds(map))
            .Distinct()
            .ToList();

        if (ghostCells.Count == 0)
            return;

        GenDraw.DrawDiagonalStripes(ghostCells, LandingGhostColor, 0.021f);
        GenDraw.DrawFieldEdges(ghostCells, LandingGhostEdgeColor);
    }

    private static bool IsLandingGhostPlacementValid(LocalTargetInfo target, Map map, List<IntVec3> footprintOffsets)
    {
        if (!target.IsValid || !target.Cell.IsValid || map == null || footprintOffsets == null || footprintOffsets.Count == 0)
            return false;

        for (var i = 0; i < footprintOffsets.Count; i++)
        {
            var cell = target.Cell + footprintOffsets[i];
            if (!cell.InBounds(map))
                return false;
        }

        return true;
    }

    private bool TryBeginLaunchSequence(PlanetTile destinationTile, TransportersArrivalAction arrivalAction)
    {
        var launchDisableReason = GetLaunchDisabledReason();
        if (!launchDisableReason.NullOrEmpty())
        {
            Messages.Message(launchDisableReason, this, MessageTypeDefOf.RejectInput, false);
            return true;
        }

        var pods = GetCurrentLaunchPods();
        if (pods.Count == 0)
        {
            Messages.Message("CP_NoClusterPodsReady".Translate(), this, MessageTypeDefOf.RejectInput, false);
            return true;
        }

        if (arrivalAction == null && pods.Any(pod => pod.HasTransportablePawns()))
        {
            Messages.Message("MessageTransportPodsDestinationIsInvalid".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        var perTileFuelCost = GetLaunchFuelPerTile(pods.Count);
        var distance = GetWorldDistance(Map.Tile, destinationTile);
        var requiredFuel = perTileFuelCost * distance;

        if (requiredFuel > 0f && (FuelComp == null || FuelComp.Fuel < requiredFuel))
        {
            var storedFuel = FuelComp == null ? "0" : FuelComp.Fuel.ToStringDecimalIfSmall();
            Messages.Message("CP_NotEnoughFuelForLaunch".Translate(requiredFuel.ToStringDecimalIfSmall(), storedFuel), this, MessageTypeDefOf.RejectInput, false);
            return true;
        }

        if (FuelComp != null && requiredFuel > 0f)
            FuelComp.ConsumeFuel(requiredFuel);

        launchDestinationTile = destinationTile;
        launchArrivalAction = arrivalAction ?? new CP_TransportersArrivalAction_ClusterDrop();
        launchCountdownTicksLeft = Mathf.Max(1, AssemblyProps.launchCountdownTicks);
        launchCountdownInitialTicks = launchCountdownTicksLeft;
        lastLaunchMoteSecondShown = 0;
        launchGroupID = Find.UniqueIDsManager.GetNextTransporterGroupID();
        var landingAnchor = GetClusterLandingAnchor(pods);
        if (AssemblyProps.packWires)
        {
            for (var i = 0; i < pods.Count; i++)
                pods[i].PreparePackedWiresForLaunch(landingAnchor);
        }

        pendingPodLaunches = pods
            .Select(pod => new PendingPodLaunch
            {
                Pod = pod,
                DelayTicks = Rand.RangeInclusive(0, Mathf.Max(0, AssemblyProps.maxLaunchRandomDelayTicks)),
                LandingOffset = pod.Position - landingAnchor
            })
            .ToList();

        Messages.Message("CP_LaunchCountdownStarted".Translate((launchCountdownTicksLeft / 60f).ToString("0.#"), pods.Count), this, MessageTypeDefOf.TaskCompletion, false);
        TrySpawnLaunchMote();
        return true;
    }

    private void TickLaunch()
    {
        TrySpawnLaunchMote();
        if (launchCountdownTicksLeft > 0)
        {
            launchCountdownTicksLeft--;
            if (launchCountdownTicksLeft > 0)
                return;
        }

        for (var i = pendingPodLaunches.Count - 1; i >= 0; i--)
        {
            var pending = pendingPodLaunches[i];
            if (pending.Pod == null || pending.Pod.Destroyed || !pending.Pod.Spawned)
            {
                pendingPodLaunches.RemoveAt(i);
                continue;
            }

            if (pending.DelayTicks > 0)
            {
                pending.DelayTicks--;
                continue;
            }

            LaunchPod(pending);
            pendingPodLaunches.RemoveAt(i);
        }

        if (pendingPodLaunches.Count == 0)
        {
            launchDestinationTile = PlanetTile.Invalid;
            launchGroupID = -1;
            launchArrivalAction = null;
            launchCountdownInitialTicks = 0;
            lastLaunchMoteSecondShown = 0;
            Messages.Message("CP_LaunchStarted".Translate(), this, MessageTypeDefOf.TaskCompletion, false);
        }
    }

    private void TrySpawnAssemblyMote()
    {
        if (!Spawned || Map == null)
            return;

        if (!this.IsHashIntervalTick(55))
            return;

        MoteMaker.MakeStaticMote(DrawPos, Map, CP_ThingDefOf.CP_MoteAssembly, 4f);
    }

    private void TrySpawnLaunchMote()
    {
        if (!Spawned || Map == null || launchCountdownInitialTicks <= 0)
            return;

        var elapsedSecond = 1 + Mathf.FloorToInt((launchCountdownInitialTicks - launchCountdownTicksLeft) / 60f);
        if (elapsedSecond <= 0 || elapsedSecond == lastLaunchMoteSecondShown)
            return;

        var moteDef = GetLaunchMoteForElapsedSecond(elapsedSecond);
        if (moteDef == null)
            return;

        lastLaunchMoteSecondShown = elapsedSecond;
        MoteMaker.MakeStaticMote(DrawPos, Map, moteDef, 4f);
    }

    private static ThingDef GetLaunchMoteForElapsedSecond(int elapsedSecond)
    {
        return elapsedSecond switch
        {
            1 => CP_ThingDefOf.CP_MoteCount3f,
            2 => CP_ThingDefOf.CP_MoteCount2f,
            3 => CP_ThingDefOf.CP_MoteCount1f,
            >= 4 => CP_ThingDefOf.CP_MoteLaunch,
            _ => null
        };
    }

    private void LaunchPod(PendingPodLaunch pending)
    {
        var pod = pending.Pod;
        if (pod == null || pod.Destroyed)
            return;

        var map = pod.Map;
        var position = pod.Position;
        pod.SetLandingOffset(pending.LandingOffset);

        var releaseInfantryPawnsSeparately = pod.HasTransportablePawns();

        var activeTransporter = (ActiveTransporter)ThingMaker.MakeThing(ThingDefOf.ActiveDropPod);
        activeTransporter.Contents = pod.ExtractActiveTransporterInfo(CP_ThingDefOf.CP_ClusterPod, releaseInfantryPawnsSeparately);
        activeTransporter.Rotation = Rot4.North;

        var skyfaller = (FlyShipLeaving)SkyfallerMaker.MakeSkyfaller(ThingDefOf.DropPodLeaving, activeTransporter);
        skyfaller.groupID = launchGroupID;
        skyfaller.destinationTile = launchDestinationTile;
        skyfaller.arrivalAction = launchArrivalAction ?? new CP_TransportersArrivalAction_ClusterDrop();
        skyfaller.worldObjectDef = WorldObjectDefOf.TravellingTransporters;

        GenSpawn.Spawn(skyfaller, position, map);
    }

    private static IntVec3 GetClusterLandingAnchor(List<Building_ClusterPod> pods)
    {
        if (pods == null || pods.Count == 0)
            return IntVec3.Zero;

        var sumX = 0f;
        var sumZ = 0f;
        for (var i = 0; i < pods.Count; i++)
        {
            sumX += pods[i].Position.x;
            sumZ += pods[i].Position.z;
        }

        return new IntVec3(Mathf.RoundToInt(sumX / pods.Count), 0, Mathf.RoundToInt(sumZ / pods.Count));
    }

    private TaggedString GetLaunchDisabledReason()
    {
        if (assemblyActive || LaunchActive)
            return "Busy".Translate();
        if (!Spawned)
            return "CP_NoValidLaunchArea".Translate();
        if (!LaunchAreaSolver.TryComputeLargestArea(Map, Position, def.Size, out var area))
            return "CP_NoValidLaunchArea".Translate();
        if (area.allInteriorCells.Any(cell => cell.Roofed(Map)))
            return "CP_LaunchAreaRoofed".Translate();
        if (GetClusterPodsInLaunchArea(area).Count == 0)
            return "CP_NoClusterPodsReady".Translate();

        var perTileFuelCost = GetLaunchFuelPerTile(GetClusterPodsInLaunchArea(area).Count);
        if (FuelComp == null || FuelComp.Fuel < perTileFuelCost)
        {
            var storedFuel = FuelComp == null ? "0" : FuelComp.Fuel.ToStringDecimalIfSmall();
            return "CP_NotEnoughFuel".Translate(perTileFuelCost.ToStringDecimalIfSmall(), storedFuel);
        }

        return null;
    }

    private List<Building_ClusterPod> GetCurrentLaunchPods()
    {
        if (!LaunchAreaSolver.TryComputeLargestArea(Map, Position, def.Size, out var area))
            return new List<Building_ClusterPod>();
        return GetClusterPodsInLaunchArea(area);
    }

    private float GetLaunchFuelPerTile(int podCount)
    {
        return podCount * AssemblyProps.baseFuelConsumption;
    }

    private int GetMaxLaunchDistance(int podCount)
    {
        var fuelPerTile = GetLaunchFuelPerTile(podCount);
        if (fuelPerTile <= 0f || FuelComp == null)
            return 0;
        return Mathf.FloorToInt(FuelComp.Fuel / fuelPerTile);
    }

    private int GetWorldDistance(PlanetTile originTile, PlanetTile destinationTile)
    {
        return Find.WorldGrid.TraversalDistanceBetween(originTile, destinationTile, passImpassable: true, int.MaxValue, canTraverseLayers: true);
    }

    private List<IThingHolder> GetLaunchPreviewPods(List<Building_ClusterPod> pods)
    {
        return pods
            .Select(pod => (IThingHolder)new LaunchPreviewHolder(pod.GetLaunchPreviewContents()))
            .ToList();
    }

    private void DrawLaunchRangeRing(PlanetTile originTile)
    {
        var podCount = GetCurrentLaunchPods().Count;
        if (podCount <= 0)
            return;

        var maxDistance = GetMaxLaunchDistance(podCount);
        if (maxDistance <= 0)
            return;

        var originOnSelectedLayer = Find.WorldSelector.SelectedLayer?.GetClosestTile_NewTemp(originTile) ?? originTile;
        GenDraw.DrawWorldRadiusRing(originOnSelectedLayer, maxDistance, CompPilotConsole.GetFuelRadiusMat(originOnSelectedLayer));
    }

    private string GetLaunchTargetLabel(GlobalTargetInfo target, PlanetTile originTile)
    {
        if (!target.IsValid || !target.Tile.Valid)
            return "";

        var podCount = GetCurrentLaunchPods().Count;
        var distance = GetWorldDistance(originTile, target.Tile);
        var maxDistance = GetMaxLaunchDistance(podCount);
        var requiredFuel = GetLaunchFuelPerTile(podCount) * distance;
        return "CP_LaunchTargetLabel".Translate(distance, maxDistance, requiredFuel.ToStringDecimalIfSmall());
    }

    private void CleanupAssemblyEffecter()
    {
        assemblyEffecter?.Cleanup();
        assemblyEffecter = null;
    }

    private void LoadSteelFromMap()
    {
        var steelComp = SteelComp;
        if (steelComp == null)
            return;

        var needed = steelComp.GetFuelCountToFullyRefuel();
        if (needed <= 0)
            return;

        var loaded = 0;
        var steelStacks = Map.listerThings.ThingsOfDef(ThingDefOf.Steel);
        for (var i = steelStacks.Count - 1; i >= 0 && needed > 0; i--)
        {
            var stack = steelStacks[i];
            if (!stack.Spawned || stack.Destroyed)
                continue;

            var take = Mathf.Min(needed, stack.stackCount);
            if (take <= 0)
                continue;

            steelComp.Refuel(take);
            stack.SplitOff(take).Destroy(DestroyMode.Vanish);
            loaded += take;
            needed -= take;
        }

        if (loaded <= 0)
            Messages.Message("CP_NoSteelFound".Translate(), MessageTypeDefOf.RejectInput, false);
        else
            Messages.Message("CP_LoadedSteel".Translate(loaded), MessageTypeDefOf.TaskCompletion, false);
    }

    private void EjectSteel()
    {
        var steelComp = SteelComp;
        if (steelComp == null)
            return;
        if (!steelComp.CanEjectFuel().Accepted)
            return;
        steelComp.EjectFuel();
    }

    private void EjectFuel()
    {
        if (FuelComp == null)
            return;
        if (!FuelComp.CanEjectFuel().Accepted)
            return;
        FuelComp.EjectFuel();
    }

    public override string GetInspectString()
    {
        var baseText = base.GetInspectString();
        var status = "Cluster projection console online.";
        if (Spawned && LaunchAreaSolver.TryComputeLargestArea(Map, Position, def.Size, out var area))
        {
            status += "\nLaunch area: " + area.Width + "x" + area.Height + " (launchable: " + area.LaunchableCount + ")";
        }
        else
        {
            status += "\n" + "CP_NoValidLaunchArea".Translate();
        }

        var steelComp = SteelComp;
        if (steelComp != null)
            status += "\nStored steel: " + steelComp.Fuel.ToStringDecimalIfSmall() + " / " + steelComp.Props.fuelCapacity.ToStringDecimalIfSmall();

        if (FuelComp != null)
            status += "\nStored chemfuel: " + FuelComp.Fuel.ToStringDecimalIfSmall() + " / " + FuelComp.Props.fuelCapacity.ToStringDecimalIfSmall();

        var readyPods = Spawned ? GetCurrentLaunchPods().Count : 0;
        if (readyPods > 0)
        {
            var perTileFuel = GetLaunchFuelPerTile(readyPods);
            status += "\nReady cluster pods: " + readyPods;
            status += "\nFuel per world tile: " + perTileFuel.ToStringDecimalIfSmall();
            status += "\nMax launch distance: " + GetMaxLaunchDistance(readyPods);
        }

        if (launchCountdownTicksLeft > 0)
            status += "\n" + "CP_LaunchCountdownStatus".Translate(launchCountdownTicksLeft.ToStringTicksToPeriod());

        return baseText.NullOrEmpty() ? status : baseText + "\n" + status;
    }
}

