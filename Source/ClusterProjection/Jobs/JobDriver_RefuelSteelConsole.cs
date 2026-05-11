using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ClusterProjection;

public class JobDriver_RefuelSteelConsole : JobDriver
{
    private Building_ClusterProjectionConsole Console => job.GetTarget(TargetIndex.A).Thing as Building_ClusterProjectionConsole;

    private CP_CompRefuelable SteelComp => Console?.SteelStorageComp;

    private Thing Fuel => job.GetTarget(TargetIndex.B).Thing;

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return pawn.Reserve(Console, job, 1, -1, null, errorOnFailed) && pawn.Reserve(Fuel, job, 1, -1, null, errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
        AddEndCondition(() => SteelComp == null || SteelComp.IsFull ? JobCondition.Succeeded : JobCondition.Ongoing);
        AddFailCondition(() => SteelComp == null || !job.playerForced && !SteelComp.ShouldAutoRefuelNowIgnoringFuelPct);
        AddFailCondition(() => SteelComp != null && !SteelComp.allowAutoRefuel && !job.playerForced);

        yield return Toils_General.DoAtomic(delegate
        {
            job.count = SteelComp?.GetFuelCountToFullyRefuel() ?? 0;
        });

        var reserveFuel = Toils_Reserve.Reserve(TargetIndex.B);
        yield return reserveFuel;
        yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(TargetIndex.B).FailOnSomeonePhysicallyInteracting(TargetIndex.B);
        yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, true).FailOnDestroyedNullOrForbidden(TargetIndex.B);
        yield return Toils_Haul.CheckForGetOpportunityDuplicate(reserveFuel, TargetIndex.B, TargetIndex.None, true);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        yield return Toils_General.Wait(240).FailOnDestroyedNullOrForbidden(TargetIndex.B).FailOnDestroyedNullOrForbidden(TargetIndex.A).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch).WithProgressBarToilDelay(TargetIndex.A);
        yield return FinalizeSteelRefueling();
    }

    private Toil FinalizeSteelRefueling()
    {
        var toil = ToilMaker.MakeToil("FinalizeSteelRefueling");
        toil.initAction = delegate
        {
            var currentJob = toil.actor.CurJob;
            var steelComp = Console?.SteelStorageComp;
            if (steelComp == null)
                return;

            if (currentJob.placedThings.NullOrEmpty())
            {
                steelComp.Refuel(new List<Thing> { currentJob.GetTarget(TargetIndex.B).Thing });
            }
            else
            {
                steelComp.Refuel(currentJob.placedThings.ConvertAll(thingCount => thingCount.thing));
            }
        };
        toil.defaultCompleteMode = ToilCompleteMode.Instant;
        return toil;
    }
}