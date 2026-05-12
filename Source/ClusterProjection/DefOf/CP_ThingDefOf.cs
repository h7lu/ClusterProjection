using RimWorld;
using Verse;

namespace ClusterProjection.DefOfs;

[DefOf]
public static class CP_ThingDefOf
{
    public static ThingDef CP_ClusterProjectionConsole;
    public static ThingDef CP_ClusterProjectionConsoleAdvanced;
    public static ThingDef CP_AutoAssemblyRail;
    public static ThingDef CP_InfantryCapsule;
    public static ThingDef CP_ProjectionBeacon;
    public static ThingDef CP_ClusterPod;
    public static ThingDef CP_OilExpansionTank;
    public static ThingDef CP_SteelExpansionTank;
    public static ThingDef CP_MoteAssembly;
    public static ThingDef CP_MoteCount1f;
    public static ThingDef CP_MoteCount2f;
    public static ThingDef CP_MoteCount3f;
    public static ThingDef CP_MoteLaunch;

    static CP_ThingDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(CP_ThingDefOf));
    }
}
