using ClusterProjection.DefOfs;
using Verse;

namespace ClusterProjection;

public class PlaceWorker_AutoAssemblyRail : PlaceWorker
{
    public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
    {
        var axisIsHorizontal = IsHorizontalAxis(rot);
        var hasValidConnection = false;

        foreach (var dir in GenAdj.CardinalDirections)
        {
            var c = loc + dir;
            if (!c.InBounds(map))
            {
                continue;
            }

            var rail = c.GetFirstThing(map, CP_ThingDefOf.CP_AutoAssemblyRail) as Building_AutoAssemblyRail;
            if (rail != null)
            {
                var neighborAxisIsHorizontal = IsHorizontalAxis(rail.Rotation);
                if (neighborAxisIsHorizontal != axisIsHorizontal)
                {
                    return "CP_RailMustStayStraight".Translate();
                }

                hasValidConnection = true;
                continue;
            }

            var console = c.GetFirstThing(map, CP_ThingDefOf.CP_ClusterProjectionConsole) as Building_ClusterProjectionConsole;
            if (console != null)
            {
                hasValidConnection = true;
            }
        }

        if (!hasValidConnection)
        {
            return "CP_NotConnectedToConsole".Translate();
        }

        return true;
    }

    private static bool IsHorizontalAxis(Rot4 rot)
    {
        return rot == Rot4.East || rot == Rot4.West;
    }
}
