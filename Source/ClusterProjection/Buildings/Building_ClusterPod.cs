using System.Collections.Generic;
using Verse;

namespace ClusterProjection;

public class Building_ClusterPod : Building, IThingHolder
{
    private ThingOwner<Thing> innerContainer;

    public Thing ContainedThing => innerContainer.Count > 0 ? innerContainer[0] : null;

    public Building_ClusterPod()
    {
        innerContainer = new ThingOwner<Thing>(this, oneStackOnly: true);
    }

    public void Store(Thing thing)
    {
        if (thing.Spawned)
            thing.DeSpawn(DestroyMode.Vanish);
        innerContainer.TryAdd(thing);
    }

    public ThingOwner GetDirectlyHeldThings()
    {
        return innerContainer;
    }

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
    }

    public override string GetInspectString()
    {
        var baseText = base.GetInspectString();
        var contained = ContainedThing == null ? "CP_ClusterPodEmpty".Translate() : "CP_ClusterPodContains".Translate(ContainedThing.LabelCap);
        return baseText.NullOrEmpty() ? contained : baseText + "\n" + contained;
    }

    public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
    {
        var previousMap = Map;
        var previousPosition = Position;
        if (mode != DestroyMode.Vanish && previousMap != null)
            innerContainer.TryDropAll(previousPosition, previousMap, ThingPlaceMode.Near);
        base.Destroy(mode);
    }
}