using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ClusterProjection;

public class Building_ClusterPod : Building, IThingHolder
{
    private sealed class PackedWireRecord : IExposable
    {
        public ThingDef def;
        public ThingDef stuffDef;
        public Rot4 rotation = Rot4.North;
        public IntVec3 sourcePosition = IntVec3.Invalid;
        public IntVec3 deploymentOffset = IntVec3.Invalid;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Values.Look(ref rotation, "rotation", Rot4.North);
            Scribe_Values.Look(ref sourcePosition, "sourcePosition", IntVec3.Invalid);
            Scribe_Values.Look(ref deploymentOffset, "deploymentOffset", IntVec3.Invalid);
        }
    }

    private ThingOwner<Thing> innerContainer;
    private ThingOwner<Thing> emptyContainer; // Used by GetDirectlyHeldThings() to prevent auto-ticking
    private ThingOwner<Thing> cargoContainer; // Stores items that were on the building
    private List<PackedWireRecord> packedWires;
    private Rot4 storedRotation;
    private IntVec3 landingOffset;
    private bool autoDeployOnSpawn;
    private int autoDeployTicksLeft;

    public Thing ContainedThing => innerContainer.Count > 0 ? innerContainer[0] : null;
    public bool HasPackedWires => !packedWires.NullOrEmpty();
    public IntVec3 LandingOffset => landingOffset;

    public Building_ClusterPod()
    {
        innerContainer = new ThingOwner<Thing>(this, oneStackOnly: true);
        emptyContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
        cargoContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
        packedWires = new List<PackedWireRecord>();
        storedRotation = Rot4.North;
        landingOffset = IntVec3.Zero;
        autoDeployOnSpawn = false;
        autoDeployTicksLeft = -1;
    }

    public bool HasPackedWireAtSourcePosition(IntVec3 position)
    {
        return packedWires.Any(record => record.sourcePosition == position);
    }

    public void AddPackedWire(Building wire)
    {
        if (wire == null || wire.def.building?.isPowerConduit != true || HasPackedWireAtSourcePosition(wire.Position))
            return;

        packedWires.Add(new PackedWireRecord
        {
            def = wire.def,
            stuffDef = wire.Stuff,
            rotation = wire.Rotation,
            sourcePosition = wire.Position,
            deploymentOffset = IntVec3.Invalid
        });
    }

    public void PreparePackedWiresForLaunch(IntVec3 clusterAnchor)
    {
        if (packedWires.NullOrEmpty())
            return;

        for (var i = 0; i < packedWires.Count; i++)
            packedWires[i].deploymentOffset = packedWires[i].sourcePosition - clusterAnchor;
    }

    public void SetLandingOffset(IntVec3 offset)
    {
        landingOffset = offset;
    }

    public void ArmAutoDeployOnSpawn(int delayTicks = 60)
    {
        autoDeployOnSpawn = true;
        autoDeployTicksLeft = delayTicks;
    }

    public void Store(Thing thing)
    {
        var previousMap = thing.Map;
        var previousCells = thing.Spawned
            ? GenAdj.OccupiedRect(thing.Position, thing.Rotation, thing.def.size).Cells.ToList()
            : new List<IntVec3>();

        if (previousMap != null && previousCells.Count > 0)
        {
            foreach (var cell in previousCells)
            {
                var thingsOnCell = new List<Thing>(previousMap.thingGrid.ThingsListAtFast(cell));
                for (var i = 0; i < thingsOnCell.Count; i++)
                {
                    var item = thingsOnCell[i];
                    if (item.def.category == ThingCategory.Item && item != thing)
                    {
                        if (item.Spawned)
                            item.DeSpawn(DestroyMode.Vanish);
                        cargoContainer.TryAdd(item);
                    }
                }
            }
        }

        if (thing is IThingHolder holder)
        {
            var ownedThings = new List<Thing>();
            foreach (var heldThing in holder.GetDirectlyHeldThings())
                ownedThings.Add(heldThing);

            var preserveHeldEntities = thing is Building_HoldingPlatform;

            for (var i = 0; i < ownedThings.Count; i++)
            {
                var item = ownedThings[i];
                if (item.def.category == ThingCategory.Item || (preserveHeldEntities && item is Pawn))
                {
                    holder.GetDirectlyHeldThings().Remove(item);
                    cargoContainer.TryAdd(item);
                }
            }
        }

        if (thing is Building_InfantryCapsule infantryCapsule)
            infantryCapsule.PrepareForEncapsulation();

        if (thing.Spawned)
            thing.DeSpawn(DestroyMode.Vanish);

        if (thing is Building building)
            storedRotation = building.Rotation;

        innerContainer.TryAdd(thing);
    }

    public bool HasTransportablePawns()
    {
        return ContainedThing is Building_InfantryCapsule capsule && capsule.HasTransportablePawns;
    }

    public ThingOwner GetLaunchPreviewContents()
    {
        if (ContainedThing is Building_InfantryCapsule capsule && capsule.HasTransportablePawns)
            return capsule.GetLaunchPreviewContents();

        return innerContainer;
    }

    public ActiveTransporterInfo ExtractActiveTransporterInfo(ThingDef sentTransporterDef, bool releaseInfantryPawnsSeparately)
    {
        var info = new ActiveTransporterInfo
        {
            sentTransporterDef = sentTransporterDef
        };

        if (releaseInfantryPawnsSeparately && ContainedThing is Building_InfantryCapsule capsule)
        {
            capsule.ExtractContentsForLaunch(info.innerContainer);
        }

        ArmAutoDeployOnSpawn();
        if (Spawned)
            DeSpawn(DestroyMode.Vanish);

        info.innerContainer.TryAdd(this);
        return info;
    }

    public bool TryDeployStoredThingAt(IntVec3 position, Map map)
    {
        if (innerContainer.Count == 0)
            return false;

        var thing = innerContainer[0];
        innerContainer.Remove(thing);

        if (thing is Building building)
            building.Rotation = storedRotation;

        GenSpawn.Spawn(thing, position, map, storedRotation, WipeMode.VanishOrMoveAside);

        if (thing is Building_InfantryCapsule infantryCapsule)
            infantryCapsule.NotifyReturnedToMap();

        if (cargoContainer.Count > 0 && thing is Building deployedBuilding)
        {
            var buildingCells = GenAdj.OccupiedRect(position, storedRotation, deployedBuilding.def.size).Cells.ToList();
            var holder = deployedBuilding as IThingHolder;
            var heldThings = holder?.GetDirectlyHeldThings();
            var cargoItems = cargoContainer.ToList();
            foreach (var item in cargoItems)
            {
                cargoContainer.Remove(item);

                if (heldThings != null && heldThings.TryAdd(item))
                    continue;

                var cargoCell = position;
                foreach (var cell in buildingCells)
                {
                    if (cell.Standable(map))
                    {
                        cargoCell = cell;
                        break;
                    }
                }

                GenSpawn.Spawn(item, cargoCell, map, WipeMode.VanishOrMoveAside);
            }
        }

        DeployPackedWires(position, map);
        packedWires.Clear();

        return true;
    }

    private void DeployPackedWires(IntVec3 position, Map map)
    {
        if (packedWires.NullOrEmpty() || map == null)
            return;

        var clusterAnchor = position - landingOffset;
        for (var i = 0; i < packedWires.Count; i++)
        {
            var record = packedWires[i];
            if (record.def?.building?.isPowerConduit != true)
                continue;

            var spawnCell = record.deploymentOffset.IsValid ? clusterAnchor + record.deploymentOffset : record.sourcePosition;
            if (!spawnCell.IsValid || !spawnCell.InBounds(map))
                continue;

            var wire = ThingMaker.MakeThing(record.def, record.stuffDef);
            wire.SetFaction(Faction);
            GenSpawn.Spawn(wire, spawnCell, map, record.rotation, WipeMode.VanishOrMoveAside);
        }
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        if (!respawningAfterLoad && autoDeployOnSpawn && autoDeployTicksLeft < 0)
            autoDeployTicksLeft = 1;
    }

    public ThingOwner GetDirectlyHeldThings()
    {
        return emptyContainer;
    }

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, innerContainer);
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, cargoContainer);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        Scribe_Deep.Look(ref cargoContainer, "cargoContainer", this);
        Scribe_Collections.Look(ref packedWires, "packedWires", LookMode.Deep);
        Scribe_Values.Look(ref storedRotation, "storedRotation", Rot4.North);
        Scribe_Values.Look(ref landingOffset, "landingOffset", IntVec3.Zero);
        Scribe_Values.Look(ref autoDeployOnSpawn, "autoDeployOnSpawn", false);
        Scribe_Values.Look(ref autoDeployTicksLeft, "autoDeployTicksLeft", -1);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            emptyContainer ??= new ThingOwner<Thing>(this, oneStackOnly: false);
            cargoContainer ??= new ThingOwner<Thing>(this, oneStackOnly: false);
            packedWires ??= new List<PackedWireRecord>();
        }
    }

    protected override void Tick()
    {
        base.Tick();
        if (!autoDeployOnSpawn || !Spawned)
            return;

        if (autoDeployTicksLeft > 0)
        {
            autoDeployTicksLeft--;
            return;
        }

        autoDeployOnSpawn = false;
        autoDeployTicksLeft = -1;
        if (TryDeployStoredThingAt(Position, Map) && !Destroyed)
            Destroy(DestroyMode.Vanish);
    }

    public override string GetInspectString()
    {
        var baseText = base.GetInspectString();
        var contained = ContainedThing == null ? "CP_ClusterPodEmpty".Translate() : "CP_ClusterPodContains".Translate(ContainedThing.LabelCap);
        return baseText.NullOrEmpty() ? contained : baseText + "\n" + contained;
    }

    public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
    {
        if (Destroyed)
            return;

        var previousMap = Map;
        var previousPosition = Position;
        if (previousMap != null && innerContainer.Count > 0)
        {
            for (var i = innerContainer.Count - 1; i >= 0; i--)
            {
                var thing = innerContainer[i];
                innerContainer.Remove(thing);

                if (thing is Building building)
                    building.Rotation = storedRotation;

                GenSpawn.Spawn(thing, previousPosition, previousMap, storedRotation);
            }
        }

        if (previousMap != null && packedWires.Count > 0)
        {
            DeployPackedWires(previousPosition, previousMap);
            packedWires.Clear();
        }

        base.Destroy(mode);
    }
}