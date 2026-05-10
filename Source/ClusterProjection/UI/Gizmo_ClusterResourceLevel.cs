using RimWorld;
using Verse;

namespace ClusterProjection;

public class Gizmo_ClusterResourceLevel : Gizmo_Slider
{
    private static bool draggingBar;

    private readonly string title;
    private readonly float value;
    private readonly float max;

    protected override float Target
    {
        get => max <= 0f ? 0f : value / max;
        set { }
    }

    protected override float ValuePercent => max <= 0f ? 0f : value / max;

    protected override string Title => title;

    protected override bool IsDraggable => false;

    protected override string BarLabel => value.ToStringDecimalIfSmall() + " / " + max.ToStringDecimalIfSmall();

    protected override bool DraggingBar
    {
        get => draggingBar;
        set => draggingBar = value;
    }

    public Gizmo_ClusterResourceLevel(string title, float value, float max)
    {
        this.title = title;
        this.value = value;
        this.max = max;
    }

    protected override string GetTooltip()
    {
        return string.Empty;
    }
}