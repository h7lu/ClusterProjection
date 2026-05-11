using RimWorld;
using Verse;
using Verse.AI;

namespace ClusterProjection;

public static class CP_SteelRefuelUtility
{
    public static bool CanRefuel(Pawn pawn, Building_ClusterProjectionConsole console, bool forced = false)
    {
        var comp = console?.SteelStorageComp;
        if (comp == null || comp.parent.Fogged() || comp.IsFull || !forced && !comp.allowAutoRefuel)
            return false;

        if (comp.FuelPercentOfMax > 0f && !comp.Props.allowRefuelIfNotEmpty)
            return false;

        if (!forced && !comp.ShouldAutoRefuelNow)
            return false;

        if (!pawn.CanReserve(console, 1, -1, null, forced))
            return false;

        if (console.Faction != pawn.Faction)
            return false;

        var bestFuel = FindBestFuel(pawn, console);
        if (bestFuel == null)
        {
            JobFailReason.Is("NoFuelToRefuel".Translate(comp.Props.fuelFilter.Summary));
            return false;
        }

        return true;
    }

    public static Job RefuelJob(Pawn pawn, Building_ClusterProjectionConsole console)
    {
        var thing = FindBestFuel(pawn, console);
        return thing == null ? null : JobMaker.MakeJob(DefOfs.CP_JobDefOf.CP_RefuelSteelConsole, console, thing);
    }

    public static Thing FindBestFuel(Pawn pawn, Building_ClusterProjectionConsole console)
    {
        var comp = console?.SteelStorageComp;
        if (comp == null)
            return null;

        var filter = comp.Props.fuelFilter;
        return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, filter.BestThingRequest, PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, Validator);

        bool Validator(Thing thing)
        {
            if (thing.IsForbidden(pawn) || !pawn.CanReserve(thing))
                return false;

            return filter.Allows(thing);
        }
    }
}