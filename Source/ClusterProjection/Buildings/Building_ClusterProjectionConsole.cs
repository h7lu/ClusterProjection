using RimWorld;
using Verse;

namespace ClusterProjection;

public class Building_ClusterProjectionConsole : Building
{
    public override string GetInspectString()
    {
        var baseText = base.GetInspectString();
        var status = "Cluster projection console online.";
        if (Spawned && LaunchAreaSolver.TryComputeLargestArea(Map, Position, out var area))
        {
            status += "\nLaunch area: " + area.Width + "x" + area.Height + " (launchable: " + area.LaunchableCount + ")";
        }
        else
        {
            status += "\n" + "CP_NoValidLaunchArea".Translate();
        }

        return baseText.NullOrEmpty() ? status : baseText + "\n" + status;
    }
}
