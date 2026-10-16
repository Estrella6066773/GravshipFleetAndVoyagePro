using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ShipCorePersona
{
    public class JobDriver_StoreCoreImplant : JobDriver
    {
        private Thing Core => job.GetTarget(TargetIndex.A).Thing;
        private Thing Item => job.GetTarget(TargetIndex.B).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOn(() =>
            {
                CompShipCorePersona comp = Core?.TryGetComp<CompShipCorePersona>();
                return comp?.Persona == null || Item == null || !ImplantAccess.CanStore(comp.Persona, Item.def);
            });

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(90).WithProgressBarToilDelay(TargetIndex.A);
            yield return new Toil
            {
                initAction = () =>
                {
                    CompShipCorePersona comp = Core?.TryGetComp<CompShipCorePersona>();
                    if (comp != null && Item != null)
                        ImplantAccess.Store(comp, Item);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
