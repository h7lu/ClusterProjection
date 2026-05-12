using Verse;

namespace ClusterProjection;

public class CP_AssemblyProperties : DefModExtension
{
    public int steelCapacity = 500;
    public int maxHeavyBuildingArea = 9;
    public bool packWires;
    public float baseSteelConsumption = 30f;
    public float baseFuelConsumption = 1f;
    public float assemblyHeadSpeedCellsPerTick = 0.08f;
    public int baseAssemblyTicks = 60;
    public int launchCountdownTicks = 180;
    public int maxLaunchRandomDelayTicks = 30;
}