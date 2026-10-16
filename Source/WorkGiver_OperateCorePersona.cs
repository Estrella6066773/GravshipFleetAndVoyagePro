using RimWorld;
using Verse;
using Verse.AI;

namespace ShipCorePersona
{
    /// <summary>
    /// 医生给飞船主脑做手术。材料先放进医生背包，材料齐了才在飞船电脑核心旁边做手术。
    /// </summary>
    public class WorkGiver_OperateCorePersona : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest =>
            ThingRequest.ForDef(DefDatabase<ThingDef>.GetNamed("Ship_ComputerCore"));

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return EligibleBill(pawn, t) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompShipCorePersona comp = t.TryGetComp<CompShipCorePersona>();
            Pawn persona = comp?.Persona;
            if (persona?.health?.surgeryBills == null)
                return null;
            if (t.IsForbidden(pawn) || !pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly))
                return null;

            foreach (Bill bill in persona.health.surgeryBills)
            {
                if (!IsOurInstallBill(bill, persona, pawn))
                    continue;

                if (TryFindMissingIngredient(pawn, bill, out Thing ingredient, out int count))
                {
                    Job haul = JobMaker.MakeJob(ShipCorePersonaDefOf.OperateShipCorePersona, t, ingredient);
                    haul.count = count;
                    haul.bill = bill;
                    return haul;
                }

                if (!HasAllIngredients(pawn, bill))
                {
                    if (forced)
                        JobFailReason.Is("ShipCorePersona_MissingIngredient".Translate());
                    continue;
                }

                Job operate = JobMaker.MakeJob(ShipCorePersonaDefOf.OperateShipCorePersona, t);
                operate.bill = bill;
                return operate;
            }

            return null;
        }

        private static Bill EligibleBill(Pawn pawn, Thing t)
        {
            CompShipCorePersona comp = t.TryGetComp<CompShipCorePersona>();
            Pawn persona = comp?.Persona;
            if (persona?.health?.surgeryBills == null)
                return null;
            if (t.IsForbidden(pawn) || !pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Deadly))
                return null;

            foreach (Bill bill in persona.health.surgeryBills)
            {
                if (IsOurInstallBill(bill, persona, pawn))
                    return bill;
            }

            return null;
        }

        private static bool IsOurInstallBill(Bill bill, Pawn persona, Pawn doctor)
        {
            if (bill.suspended || bill.recipe?.Worker is not Recipe_InstallCorePersonaImplant)
                return false;
            return bill.recipe.AvailableOnNow(persona) && bill.recipe.PawnSatisfiesSkillRequirements(doctor);
        }

        public static bool HasAllIngredients(Pawn doctor, Bill bill)
        {
            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                int need = UnityEngine.Mathf.RoundToInt(ingredient.GetBaseCount());
                if (CountInInventory(doctor, ingredient) < need)
                    return false;
            }

            return true;
        }

        public static bool TryFindMissingIngredient(Pawn doctor, Bill bill, out Thing found, out int count)
        {
            found = null;
            count = 0;
            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                int need = UnityEngine.Mathf.RoundToInt(ingredient.GetBaseCount());
                int have = CountInInventory(doctor, ingredient);
                if (have >= need)
                    continue;

                Thing thing = GenClosest.ClosestThingReachable(
                    doctor.Position,
                    doctor.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(doctor),
                    999f,
                    candidate => !candidate.IsForbidden(doctor)
                        && ingredient.filter.Allows(candidate)
                        && doctor.CanReserve(candidate));
                if (thing == null)
                    return false;

                found = thing;
                count = UnityEngine.Mathf.Min(thing.stackCount, need - have);
                return true;
            }

            return false;
        }

        private static int CountInInventory(Pawn doctor, IngredientCount ingredient)
        {
            if (doctor.inventory?.innerContainer == null)
                return 0;

            int count = 0;
            foreach (Thing thing in doctor.inventory.innerContainer)
            {
                if (ingredient.filter.Allows(thing))
                    count += thing.stackCount;
            }

            return count;
        }
    }
}
