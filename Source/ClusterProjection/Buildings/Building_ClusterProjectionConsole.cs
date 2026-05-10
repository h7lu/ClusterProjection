using ClusterProjection.DefOfs;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ClusterProjection;

public class Building_ClusterProjectionConsole : Building
{
    private static readonly Color AreaColor = new Color(0f, 1f, 1f, 0.35f);
    private static readonly Color EdgeColor = new Color(0f, 1f, 1f, 1f);
    private static readonly Color LaunchableBuildingColor = new Color(0.15f, 1f, 0.15f, 0.75f);
    private static readonly Color UnlaunchableBuildingColor = new Color(1f, 0.2f, 0.2f, 0.75f);

    private bool assemblyActive;
    private bool assemblyReturning;
    private int currentAssemblyIndex;
    private int assemblyStayTicksLeft;
    private Vector3 assemblyHeadPosition;
    private List<Building> assemblyTargets = new();
    private Effecter assemblyEffecter;
    private Material assemblyHeadMaterial;

    private CP_AssemblyProperties AssemblyProps => def.GetModExtension<CP_AssemblyProperties>() ?? new CP_AssemblyProperties();

    private CompRefuelable FuelComp => GetRefuelableCompFor(ThingDefOf.Chemfuel);

    private CompRefuelable SteelComp => GetRefuelableCompFor(ThingDefOf.Steel);

    private CompRefuelable GetRefuelableCompFor(ThingDef fuelDef)
    {
        return GetComps<CompRefuelable>().FirstOrDefault(c => c?.Props?.fuelFilter != null && c.Props.fuelFilter.Allows(fuelDef));
    }

    private Material AssemblyHeadMaterial => assemblyHeadMaterial ??= MaterialPool.MatFrom("assembly_head", ShaderDatabase.Transparent);

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
        if (assemblyActive)
            assemble.Disable("Busy".Translate());
        yield return assemble;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            assemblyActive = false;
            assemblyReturning = false;
            assemblyTargets = new List<Building>();
            assemblyEffecter = null;
        }
    }

    protected override void Tick()
    {
        base.Tick();
        if (assemblyActive)
            TickAssembly();
    }

    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        base.DrawAt(drawLoc, flip);
        if (!assemblyActive)
            return;

        var drawPosition = assemblyHeadPosition;
        drawPosition.y = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
        var matrix = Matrix4x4.TRS(drawPosition, Quaternion.identity, new Vector3(2f, 1f, 2f));
        Graphics.DrawMesh(MeshPool.plane10, matrix, AssemblyHeadMaterial, 0);
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

    private static bool IsUnlaunchable(ThingDef def)
    {
        if (def.terrainAffordanceNeeded == null || def.terrainAffordanceNeeded.defName != "Heavy")
            return false;
        return def.size.x * def.size.z >= 9;
    }

    private void TryStartAssemblyAndLaunch()
    {
        if (assemblyActive)
            return;

        if (!LaunchAreaSolver.TryComputeLargestArea(Map, Position, def.Size, out var area))
        {
            Messages.Message("CP_NoValidLaunchArea".Translate(), MessageTypeDefOf.RejectInput, false);
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
                if (IsUnlaunchable(building.def))
                    continue;
                if (!IsFullyInsideLaunchableArea(building, launchableSet))
                    continue;

                result.Add(building);
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
        var rotation = building.Rotation;

        building.DeSpawn(DestroyMode.Vanish);
        var pod = (Building_ClusterPod)ThingMaker.MakeThing(CP_ThingDefOf.CP_ClusterPod);
        pod.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(pod, position, map, rotation);
        pod.Store(building);
    }

    private void FinishAssembly()
    {
        CleanupAssemblyEffecter();
        assemblyTargets.Clear();
        assemblyReturning = false;
        assemblyActive = false;
        Messages.Message("CP_AssemblyComplete".Translate(), MessageTypeDefOf.TaskCompletion, false);
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

        return baseText.NullOrEmpty() ? status : baseText + "\n" + status;
    }
}
