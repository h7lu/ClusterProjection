using UnityEngine;
using Verse;

namespace ClusterProjection;

public class Building_ClusterExpansionTank : Building
{
    private CP_ClusterCapacityExtension CapacityProps => def.GetModExtension<CP_ClusterCapacityExtension>();

    public ThingDef FuelDef => CapacityProps?.fuelDef;

    public float CapacityBonus => CapacityProps?.capacityBonus ?? 0f;

    public float ConnectionRadius => CapacityProps?.connectionRadius ?? 4.9f;

    public Building_ClusterProjectionConsole GetLinkedConsole()
    {
        if (!Spawned || Map == null)
            return null;

        Building_ClusterProjectionConsole bestConsole = null;
        var bestDistanceSquared = float.MaxValue;
        var center = Position.ToVector3Shifted();
        var maxDistanceSquared = ConnectionRadius * ConnectionRadius;

        foreach (var thing in GenRadial.RadialDistinctThingsAround(Position, Map, ConnectionRadius, true))
        {
            if (thing is not Building_ClusterProjectionConsole console)
                continue;

            var otherCenter = console.Position.ToVector3Shifted();
            var dx = otherCenter.x - center.x;
            var dz = otherCenter.z - center.z;
            var distanceSquared = dx * dx + dz * dz;
            if (distanceSquared > maxDistanceSquared + 0.0001f)
                continue;

            if (bestConsole == null || distanceSquared < bestDistanceSquared - 0.0001f || Mathf.Abs(distanceSquared - bestDistanceSquared) <= 0.0001f && console.thingIDNumber < bestConsole.thingIDNumber)
            {
                bestConsole = console;
                bestDistanceSquared = distanceSquared;
            }
        }

        return bestConsole;
    }

    public override string GetInspectString()
    {
        var baseText = base.GetInspectString();
        var linkedConsole = GetLinkedConsole();
        var status = "CP_CapacityBonus".Translate(CapacityBonus.ToStringDecimalIfSmall(), FuelDef?.label ?? "resource");
        status += "\n" + (linkedConsole == null
            ? "CP_ExpansionTankUnlinked".Translate()
            : "CP_ExpansionTankLinked".Translate(linkedConsole.LabelShortCap));

        return baseText.NullOrEmpty() ? status : baseText + "\n" + status;
    }
}