using RimWorld;
using Verse;

namespace ClusterProjection;

public class CP_CompTransporter : CompTransporter
{
    private bool suppressContentsDropOnDespawn;

    public bool SuppressContentsDropOnDespawn
    {
        get => suppressContentsDropOnDespawn;
        set => suppressContentsDropOnDespawn = value;
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref suppressContentsDropOnDespawn, "suppressContentsDropOnDespawn", false);
    }

    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        if (suppressContentsDropOnDespawn)
            return;

        base.PostDeSpawn(map, mode);
    }

    public void NotifyReturnedToMap()
    {
        suppressContentsDropOnDespawn = false;
    }
}
