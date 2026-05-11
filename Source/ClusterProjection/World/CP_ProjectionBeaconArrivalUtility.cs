using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ClusterProjection;

public static class CP_ProjectionBeaconArrivalUtility
{
    public static void Arrive(List<ActiveTransporterInfo> transporters, Map map, IntVec3 center, int podOpenDelay = 0)
    {
        var beacons = CP_ProjectionBeaconUtility.ExtractProjectionBeacons(transporters);
        var remainingTransporters = transporters.Where(transporter => transporter.innerContainer.Count > 0).ToList();

        CP_ProjectionBeaconUtility.DeployProjectionBeacons(beacons, map, center);

        if (remainingTransporters.Count > 0)
            CP_TransportersArrivalAction_ClusterDrop.LandCluster(remainingTransporters, map, center, podOpenDelay, 0);
    }
}