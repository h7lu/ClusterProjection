using RimWorld;
using Verse;
using Verse.AI;

namespace ClusterProjection;

public class WorkGiver_RefuelSteelConsole : WorkGiver_Scanner
{
    public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

    public override PathEndMode PathEndMode => PathEndMode.Touch;

    public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
    {
        return thing is Building_ClusterProjectionConsole console && CP_SteelRefuelUtility.CanRefuel(pawn, console, forced);
    }

    public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
    {
        return thing is Building_ClusterProjectionConsole console ? CP_SteelRefuelUtility.RefuelJob(pawn, console) : null;
    }
}