using RimWorld;
using Verse;

namespace ClusterProjection.DefOfs;

[DefOf]
public static class CP_ThingDefOf
{
    public static ThingDef CP_ClusterProjectionConsole;
    public static ThingDef CP_AutoAssemblyRail;
    public static ThingDef CP_InfantryCapsule;

    static CP_ThingDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(CP_ThingDefOf));
    }
}
