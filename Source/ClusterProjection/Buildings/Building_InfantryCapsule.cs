using RimWorld;
using Verse;

namespace ClusterProjection;

public class Building_InfantryCapsule : Building_Casket
{
    private const float MaxBodySize = 5f;

    private float TotalBodySize
    {
        get
        {
            float sum = 0f;
            foreach (var thing in innerContainer)
            {
                if (thing is Pawn pawn)
                {
                    sum += pawn.BodySize;
                }
            }

            return sum;
        }
    }

    public override bool Accepts(Thing thing)
    {
        if (!base.Accepts(thing))
        {
            return false;
        }

        if (thing is not Pawn pawn)
        {
            return false;
        }

        return TotalBodySize + pawn.BodySize <= MaxBodySize;
    }

    public override string GetInspectString()
    {
        var baseText = base.GetInspectString();
        var extra = "Capacity used: " + TotalBodySize.ToString("0.0") + " / " + MaxBodySize.ToString("0.0") + " body size";
        return baseText.NullOrEmpty() ? extra : baseText + "\n" + extra;
    }
}
