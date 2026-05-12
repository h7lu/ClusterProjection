using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ClusterProjection;

public class Dialog_LoadInfantryCapsule : Window
{
    private readonly Map map;
    private readonly Building_InfantryCapsule capsule;
    private readonly CP_CompTransporter transporter;

    private List<TransferableOneWay> transferables = new();
    private TransferableOneWayWidget pawnsTransfer;
    private bool massUsageDirty = true;
    private float cachedMassUsage;

    public override Vector2 InitialSize => new(1024f, UI.screenHeight);

    protected override float Margin => 0f;

    private float MassCapacity => transporter.MassCapacity;

    private float MassUsage
    {
        get
        {
            if (massUsageDirty)
            {
                massUsageDirty = false;
                cachedMassUsage = CollectionsMassCalculator.MassUsageTransferables(
                    transferables,
                    IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload,
                    includePawnsMass: true);
            }

            return cachedMassUsage;
        }
    }

    public Dialog_LoadInfantryCapsule(Map map, Building_InfantryCapsule capsule)
    {
        this.map = map;
        this.capsule = capsule;
        transporter = capsule.GetComp<CP_CompTransporter>();
        forcePause = true;
        absorbInputAroundWindow = true;
    }

    public override void PostOpen()
    {
        base.PostOpen();
        CalculateAndRecacheTransferables();
        if (transporter.LoadingInProgressOrReadyToLaunch)
            SetLoadedPawnsToLoad();
    }

    public override void DoWindowContents(Rect inRect)
    {
        var titleRect = new Rect(0f, 0f, inRect.width, 35f);
        Text.Font = GameFont.Medium;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(titleRect, "LoadTransporters".Translate(capsule.LabelNoCount));
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;

        var massRect = new Rect(12f, 35f, inRect.width - 24f, 40f);
        Widgets.Label(massRect, $"Mass: {MassUsage.ToStringMass()} / {MassCapacity.ToStringMass()}");

        inRect.yMin += 79f;
        Widgets.DrawMenuSection(inRect);
        var contentRect = inRect.ContractedBy(17f);
        contentRect.height += 17f;

        Widgets.BeginGroup(contentRect);
        var innerRect = contentRect.AtZero();

        DoBottomButtons(innerRect);

        var listRect = innerRect;
        listRect.yMax -= 59f;
        var anythingChanged = false;
        pawnsTransfer.OnGUI(listRect, out anythingChanged);
        if (anythingChanged)
            CountToTransferChanged();

        Widgets.EndGroup();
    }

    public override bool CausesMessageBackground()
    {
        return true;
    }

    private void AddToTransferables(Thing thing)
    {
        var transferable = TransferableUtility.TransferableMatching(thing, transferables, TransferAsOneMode.PodsOrCaravanPacking);
        if (transferable == null)
        {
            transferable = new TransferableOneWay();
            transferables.Add(transferable);
        }

        if (!transferable.things.Contains(thing))
            transferable.things.Add(thing);
    }

    private void DoBottomButtons(Rect rect)
    {
        var centerRect = new Rect(rect.width / 2f - 80f, rect.height - 55f, 160f, 40f);
        if (Widgets.ButtonText(centerRect, "AcceptButton".Translate()))
        {
            if (TryAccept())
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Close(doCloseSound: false);
            }
        }

        var resetRect = new Rect(centerRect.x - 170f, centerRect.y, 160f, 40f);
        if (Widgets.ButtonText(resetRect, "ResetButton".Translate()))
        {
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            CalculateAndRecacheTransferables();
            if (transporter.LoadingInProgressOrReadyToLaunch)
                SetLoadedPawnsToLoad();
        }

        var cancelRect = new Rect(centerRect.xMax + 10f, centerRect.y, 160f, 40f);
        if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
            Close();
    }

    private void CalculateAndRecacheTransferables()
    {
        transferables = new List<TransferableOneWay>();
        var transporters = new List<CompTransporter> { transporter };

        foreach (var pawn in TransporterUtility.AllSendablePawns(transporters, map))
            AddToTransferables(pawn);

        if (transporter.LoadingInProgressOrReadyToLaunch)
        {
            for (var i = 0; i < transporter.innerContainer.Count; i++)
                AddToTransferables(transporter.innerContainer[i]);
        }

        pawnsTransfer = new TransferableOneWayWidget(
            null,
            null,
            null,
            "FormCaravanColonyThingCountTip".Translate(),
            drawMass: true,
            IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload,
            includePawnsMassInMassUsage: true,
            () => MassCapacity - MassUsage,
            0f,
            ignoreSpawnedCorpseGearAndInventoryMass: false,
            map.Tile,
            drawMarketValue: true,
            drawEquippedWeapon: true,
            drawNutritionEatenPerDay: true,
            drawItemNutrition: false,
            drawForagedFoodPerDay: true);
        CaravanUIUtility.AddPawnsSections(pawnsTransfer, transferables);
        CountToTransferChanged();
    }

    private bool TryAccept()
    {
        var selectedPawns = GetSelectedPawns();
        if (!CheckForErrors(selectedPawns))
            return false;

        var transporters = new List<CompTransporter> { transporter };
        if (transporter.LoadingInProgressOrReadyToLaunch)
        {
            AssignTransferablesToTransporter();
            TransporterUtility.MakeLordsAsAppropriate(selectedPawns, transporters, map);
            var allPawns = map.mapPawns.AllPawnsSpawned;
            for (var i = 0; i < allPawns.Count; i++)
            {
                if (allPawns[i].CurJobDef == JobDefOf.HaulToTransporter && ((JobDriver_HaulToTransporter)allPawns[i].jobs.curDriver).Transporter == transporter)
                    allPawns[i].jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }
        else
        {
            TransporterUtility.InitiateLoading(transporters);
            AssignTransferablesToTransporter();
            TransporterUtility.MakeLordsAsAppropriate(selectedPawns, transporters, map);
            Messages.Message("MessageTransporterSingleLoadingProcessStarted".Translate(), capsule, MessageTypeDefOf.TaskCompletion, false);
        }

        return true;
    }

    private void SetLoadedPawnsToLoad()
    {
        for (var i = 0; i < transporter.innerContainer.Count; i++)
        {
            var transferable = transferables.Find(x => x.things.Contains(transporter.innerContainer[i]));
            if (transferable != null && transferable.CanAdjustBy(transporter.innerContainer[i].stackCount).Accepted)
                transferable.AdjustBy(transporter.innerContainer[i].stackCount);
        }

        if (transporter.leftToLoad == null)
            return;

        for (var i = 0; i < transporter.leftToLoad.Count; i++)
        {
            var queued = transporter.leftToLoad[i];
            if (queued.CountToTransfer == 0 || !queued.HasAnyThing)
                continue;

            var transferable = TransferableUtility.TransferableMatchingDesperate(queued.AnyThing, transferables, TransferAsOneMode.PodsOrCaravanPacking);
            if (transferable != null && transferable.CanAdjustBy(queued.CountToTransferToDestination).Accepted)
                transferable.AdjustBy(queued.CountToTransferToDestination);
        }
    }

    private void AssignTransferablesToTransporter()
    {
        var heldThings = transporter.GetDirectlyHeldThings();
        transporter.leftToLoad?.Clear();

        for (var i = 0; i < transferables.Count; i++)
        {
            var transferable = transferables[i];
            var desiredCount = transferable.CountToTransfer;
            var loadedMatches = heldThings.Where(thing => TransferableUtility.TransferAsOne(transferable.AnyThing, thing, TransferAsOneMode.PodsOrCaravanPacking)).ToList();
            var keepCount = Mathf.Min(desiredCount, loadedMatches.Count);

            for (var j = keepCount; j < loadedMatches.Count; j++)
                heldThings.TryDrop(loadedMatches[j], ThingPlaceMode.Near, out var _);

            var remainingToLoad = desiredCount - keepCount;
            if (remainingToLoad > 0)
                transporter.AddToTheToLoadList(transferable, remainingToLoad);
        }
    }

    private bool CheckForErrors(List<Pawn> selectedPawns)
    {
        if (!selectedPawns.Any() && transporter.innerContainer.Count == 0)
        {
            Messages.Message("CantSendEmptyTransporterSingle".Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        if (MassUsage > MassCapacity)
        {
            Messages.Message("Mass capacity exceeded.", MessageTypeDefOf.RejectInput, false);
            return false;
        }

        return true;
    }

    private List<Pawn> GetSelectedPawns()
    {
        var result = new List<Pawn>();
        for (var i = 0; i < transferables.Count; i++)
        {
            var remaining = transferables[i].CountToTransfer;
            if (remaining <= 0)
                continue;

            for (var j = 0; j < transferables[i].things.Count && remaining > 0; j++)
            {
                if (transferables[i].things[j] is not Pawn pawn)
                    continue;

                result.Add(pawn);
                remaining--;
            }
        }

        return result;
    }

    private void CountToTransferChanged()
    {
        massUsageDirty = true;
    }
}