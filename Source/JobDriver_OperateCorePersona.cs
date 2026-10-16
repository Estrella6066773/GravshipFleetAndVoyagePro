using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ShipCorePersona
{
    /// <summary>
    /// 目标 A 是飞船电脑核心，目标 B 是还缺的材料。材料齐了才做手术。
    /// </summary>
    public class JobDriver_OperateCorePersona : JobDriver
    {
        private Thing Core => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
                return false;
            if (job.targetB.HasThing)
                return pawn.Reserve(job.targetB, job, 1, job.count, null, errorOnFailed);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() =>
            {
                CompShipCorePersona comp = Core?.TryGetComp<CompShipCorePersona>();
                Pawn persona = comp?.Persona;
                return persona == null || job.bill == null || !persona.health.surgeryBills.Bills.Contains(job.bill);
            });

            if (job.targetB.HasThing)
            {
                this.FailOnDestroyedOrNull(TargetIndex.B);
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);
                yield return Toils_Haul.StartCarryThing(TargetIndex.B);
                yield return new Toil
                {
                    initAction = () =>
                    {
                        Thing carried = pawn.carryTracker?.CarriedThing;
                        if (carried != null)
                            pawn.inventory.innerContainer.TryAddOrTransfer(carried);
                    },
                    defaultCompleteMode = ToilCompleteMode.Instant
                };
                yield break;
            }

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            int duration = SurgeryDuration();
            Toil wait = Toils_General.Wait(duration);
            wait.WithProgressBarToilDelay(TargetIndex.A, duration);
            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("Recipe_Surgery");
            if (sound != null)
                wait.PlaySustainerOrSound(sound);
            yield return wait;

            yield return new Toil
            {
                initAction = ApplySurgery,
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        private int SurgeryDuration()
        {
            float speed = Mathf.Max(0.2f, pawn.GetStatValue(StatDefOf.GeneralLaborSpeed));
            return Mathf.Max(180, Mathf.RoundToInt(job.bill.recipe.workAmount / speed));
        }

        private void ApplySurgery()
        {
            CompShipCorePersona comp = Core?.TryGetComp<CompShipCorePersona>();
            Pawn persona = comp?.Persona;
            Bill bill = job.bill;
            if (persona == null || bill?.recipe == null)
                return;
            if (!WorkGiver_OperateCorePersona.HasAllIngredients(pawn, bill))
                return;

            var consumed = new List<Thing>();
            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                int need = Mathf.RoundToInt(ingredient.GetBaseCount());
                foreach (Thing thing in pawn.inventory.innerContainer.ToList())
                {
                    if (need <= 0)
                        break;
                    if (!ingredient.filter.Allows(thing))
                        continue;

                    int take = Mathf.Min(need, thing.stackCount);
                    Thing split = thing.SplitOff(take);
                    consumed.Add(split);
                    need -= take;
                }
            }

            BodyPartRecord brain = CorePersonaUtility.BrainOf(persona);
            bill.recipe.Worker.ApplyOnPawn(persona, brain, pawn, consumed, bill);
            bill.Notify_IterationCompleted(pawn, consumed);
            for (int i = 0; i < consumed.Count; i++)
            {
                if (consumed[i] != null && !consumed[i].Destroyed)
                    consumed[i].Destroy(DestroyMode.Vanish);
            }
        }
    }
}
