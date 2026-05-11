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
                continue;

            // The direction of the connection must align with the new rail's axis.
            // E/W neighbours = the new rail extends horizontally; N/S neighbours = vertically.
            bool connectionIsHorizontal = (dir.x != 0);
            if (connectionIsHorizontal != axisIsHorizontal)
            {
                // A neighbour exists in a perpendicular direction - check if it's a rail or console
                // before rejecting, so empty cells don't trigger the rule.
                var thingInDir = c.GetFirstThing(map, CP_ThingDefOf.CP_AutoAssemblyRail)
                              ?? (Thing)c.GetFirstThing(map, CP_ThingDefOf.CP_ClusterProjectionConsole);
                if (thingInDir != null)
                    return "CP_RailMustStayStraight".Translate();
                continue;
            }

            var rail = c.GetFirstThing(map, CP_ThingDefOf.CP_AutoAssemblyRail) as Building_AutoAssemblyRail;
            if (rail != null)
            {
                // The existing rail's own axis must also match (prevents connecting end-on into a perpendicular rail).
                if (IsHorizontalAxis(rail.Rotation) != axisIsHorizontal)
                    return "CP_RailMustStayStraight".Translate();

                hasValidConnection = true;
                continue;
            }

            var console = c.GetFirstThing(map, CP_ThingDefOf.CP_ClusterProjectionConsole);
            if (console != null)
            {
                hasValidConnection = true;
            }
        }

        if (!hasValidConnection)
            return "CP_NotConnectedToConsole".Translate();

        return true;
    }

    private static bool IsHorizontalAxis(Rot4 rot)
    {
        return rot == Rot4.East || rot == Rot4.West;
    }
}
