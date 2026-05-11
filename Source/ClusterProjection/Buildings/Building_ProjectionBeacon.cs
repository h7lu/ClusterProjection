using System.Text;
using RimWorld;
using Verse;

namespace ClusterProjection;

public class Building_ProjectionBeacon : Building
{
    private int activeTicksLeft = -1;

    private CP_ProjectionBeaconProperties Properties => def.GetModExtension<CP_ProjectionBeaconProperties>();

    public bool IsProjectionActive => activeTicksLeft > 0;

    public void ActivateProjection()
    {
        activeTicksLeft = Properties?.mapHoldTicks ?? 0;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref activeTicksLeft, "activeTicksLeft", -1);
    }

    protected override void Tick()
    {
        base.Tick();
        if (activeTicksLeft <= 0)
            return;

        activeTicksLeft--;
        if (activeTicksLeft <= 0 && !Destroyed)
            Destroy(DestroyMode.KillFinalize);
    }

    public override string GetInspectString()
    {
        var builder = new StringBuilder();
        var baseText = base.GetInspectString();
        if (!baseText.NullOrEmpty())
            builder.Append(baseText);

        if (IsProjectionActive)
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append("CP_ProjectionBeaconRemaining".Translate(activeTicksLeft.ToStringTicksToPeriod()));
        }

        return builder.ToString();
    }
}