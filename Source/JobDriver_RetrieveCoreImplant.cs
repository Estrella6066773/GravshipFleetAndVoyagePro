using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ShipCorePersona
{
    public class JobDriver_RetrieveCoreImplant : JobDriver
    {
        private Thing Core => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() =>
            {
                CompShipCorePersona comp = Core?.TryGetComp<CompShipCorePersona>();
                ThingDef item = comp?.RetrievalAt(job.count);
                return comp?.Persona == null || item == null || !ImplantAccess.CanRetrieve(comp.Persona, item);
            });

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(90).WithProgressBarToilDelay(TargetIndex.A);
            yield return new Toil
            {
                initAction = () =>
                {
                    CompShipCorePersona comp = Core?.TryGetComp<CompShipCorePersona>();
                    ThingDef item = comp?.RetrievalAt(job.count);
                    if (comp != null && item != null)
                        ImplantAccess.Retrieve(comp, item, pawn);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
