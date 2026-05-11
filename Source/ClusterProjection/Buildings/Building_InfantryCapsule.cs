using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ClusterProjection;

[StaticConstructorOnStartup]
public class Building_InfantryCapsule : Building
{
    private static readonly Texture2D LoadCommandTex = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter");
    private static readonly Texture2D UnloadCommandTex = ContentFinder<Texture2D>.Get("UI/Commands/PodEject");
    private bool autoUnloadOnReturnToMap;

    private CP_CompTransporter TransporterComp => GetComp<CP_CompTransporter>();

    public bool HasLaunchContents => TransporterComp != null && TransporterComp.innerContainer.Any;

    public bool HasTransportablePawns => TransporterComp != null && TransporterComp.innerContainer.Any(thing => thing is Pawn);

    public ThingOwner GetLaunchPreviewContents()
    {
        return TransporterComp.GetDirectlyHeldThings();
    }

    public void PrepareForEncapsulation()
    {
        if (TransporterComp == null)
            return;

        if (Spawned)
            TransporterComp.TryRemoveLord(Map);

        TransporterComp.groupID = -1;
        TransporterComp.leftToLoad?.Clear();
        TransporterComp.SuppressContentsDropOnDespawn = true;
    }

    public void NotifyReturnedToMap()
    {
        TransporterComp?.NotifyReturnedToMap();

        if (!autoUnloadOnReturnToMap || !Spawned || Map == null)
            return;

        autoUnloadOnReturnToMap = false;
        UnloadAll();
    }

    public void ArmAutoUnloadOnReturnToMap()
    {
        autoUnloadOnReturnToMap = true;
    }

    public void ExtractContentsForLaunch(ThingOwner destination)
    {
        if (TransporterComp == null)
            return;

        var heldThings = TransporterComp.GetDirectlyHeldThings();
        for (var i = heldThings.Count - 1; i >= 0; i--)
        {
            var thing = heldThings[i];
            heldThings.Remove(thing);
            destination.TryAdd(thing);
        }

        TransporterComp.groupID = -1;
        TransporterComp.leftToLoad?.Clear();
    }

    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var gizmo in base.GetGizmos())
        {
            if (!ShouldHideVanillaTransporterGizmo(gizmo))
                yield return gizmo;
        }

        yield return new Command_Action
        {
            defaultLabel = TransporterComp != null && TransporterComp.LoadingInProgressOrReadyToLaunch
                ? "CommandSetToLoadTransporter".Translate()
                : "CommandLoadTransporterSingle".Translate(),
            defaultDesc = TransporterComp != null && TransporterComp.LoadingInProgressOrReadyToLaunch
                ? "CommandSetToLoadTransporterDesc".Translate()
                : "CommandLoadTransporterSingleDesc".Translate(),
            icon = LoadCommandTex,
            action = OpenLoadDialog
        };

        var unloadCommand = new Command_Action
        {
            defaultLabel = "CommandUnload".Translate(),
            defaultDesc = "CommandUnloadDesc".Translate(LabelShort),
            icon = UnloadCommandTex,
            action = UnloadAll
        };
        if (!HasLaunchContents && (TransporterComp == null || !TransporterComp.LoadingInProgressOrReadyToLaunch))
            unloadCommand.Disable("NoContents".Translate());
        yield return unloadCommand;
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        TransporterComp?.NotifyReturnedToMap();
        NotifyReturnedToMap();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref autoUnloadOnReturnToMap, "autoUnloadOnReturnToMap");
    }

    private bool ShouldHideVanillaTransporterGizmo(Gizmo gizmo)
    {
        if (gizmo is Command_LoadToTransporter)
            return true;

        if (gizmo is not Command_Action action)
            return false;

        return action.defaultLabel == "CommandCancelLoad".Translate()
            || action.defaultLabel == "CommandUnload".Translate();
    }

    private void OpenLoadDialog()
    {
        if (Map == null || TransporterComp == null)
            return;

        Find.WindowStack.Add(new Dialog_LoadInfantryCapsule(Map, this));
    }

    private void UnloadAll()
    {
        if (Map == null || TransporterComp == null)
            return;

        TransporterComp.CancelLoad(Map);
        TransporterComp.GetDirectlyHeldThings().TryDropAll(Position, Map, ThingPlaceMode.Near);
    }
}