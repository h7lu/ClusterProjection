using HarmonyLib;
using Verse;

namespace ClusterProjection;

[StaticConstructorOnStartup]
public static class CP_HarmonyBootstrap
{
    static CP_HarmonyBootstrap()
    {
        new Harmony("local.clusterprojection").PatchAll();
    }
}