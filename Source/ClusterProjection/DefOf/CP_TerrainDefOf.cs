using Verse;

namespace ClusterProjection.DefOfs;

[DefOf]
public static class CP_TerrainDefOf
{
    public static TerrainDef CP_LauncherFloor;

    static CP_TerrainDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(CP_TerrainDefOf));
    }
}
