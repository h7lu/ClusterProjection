using RimWorld;
using Verse;

namespace ClusterProjection.DefOfs;

[DefOf]
public static class CP_ResearchProjectDefOf
{
    public static ResearchProjectDef ClusterProjection;

    static CP_ResearchProjectDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(CP_ResearchProjectDefOf));
    }
}
