using RimWorld;
using Verse;

namespace ClusterProjection.DefOfs;

[DefOf]
public static class CP_JobDefOf
{
    public static JobDef CP_RefuelSteelConsole;

    static CP_JobDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(CP_JobDefOf));
    }
}