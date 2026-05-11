using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ClusterProjection;

public class Building_ClusterPod : Building, IThingHolder
{
    private ThingOwner<Thing> innerContainer;
    private ThingOwner<Thing> emptyContainer; // Used by GetDirectlyHeldThings() to prevent auto-ticking
    private ThingOwner<Thing> cargoContainer; // Stores items that were on the building
    private Rot4 storedRotation;
    private IntVec3 landingOffset;
    private bool autoDeployOnSpawn;
    private int autoDeployTicksLeft;

    public Thing ContainedThing => innerContainer.Count > 0 ? innerContainer[0] : null;
    public IntVec3 LandingOffset => landingOffset;

    public Building_ClusterPod()
    {
        innerContainer = new ThingOwner<Thing>(this, oneStackOnly: true);
        emptyContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
        cargoContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
        storedRotation = Rot4.North;
        landingOffset = IntVec3.Zero;
        autoDeployOnSpawn = false;
        autoDeployTicksLeft = -1;
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

            for (var i = 0; i < ownedThings.Count; i++)
            {
                var item = ownedThings[i];
                if (item.def.category == ThingCategory.Item)
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

        return true;
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
        Scribe_Values.Look(ref storedRotation, "storedRotation", Rot4.North);
        Scribe_Values.Look(ref landingOffset, "landingOffset", IntVec3.Zero);
        Scribe_Values.Look(ref autoDeployOnSpawn, "autoDeployOnSpawn", false);
        Scribe_Values.Look(ref autoDeployTicksLeft, "autoDeployTicksLeft", -1);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            emptyContainer ??= new ThingOwner<Thing>(this, oneStackOnly: false);
            cargoContainer ??= new ThingOwner<Thing>(this, oneStackOnly: false);
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
        base.Destroy(mode);
    }
}