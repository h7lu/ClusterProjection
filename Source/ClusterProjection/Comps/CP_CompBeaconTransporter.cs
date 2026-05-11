using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ClusterProjection;

public class CP_CompBeaconTransporter : CompTransporter
{
    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);

        // A beacon launches as itself, so it should enter a ready-to-launch group
        // immediately instead of waiting for a load assignment.
        if (groupID < 0)
            groupID = Find.UniqueIDsManager.GetNextTransporterGroupID();

        if (leftToLoad != null)
            leftToLoad.Clear();
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        if (leftToLoad != null)
            leftToLoad.Clear();
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        yield break;
    }

    public override string CompInspectStringExtra()
    {
        return null;
    }
}