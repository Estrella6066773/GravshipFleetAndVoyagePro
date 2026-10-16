using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 认出飞船主脑。机械师身份和 20 点机械带宽由 Gravship Voyage 的人格芯片提供。
    /// 旧档里自动加上的机控中枢要卸掉。卸的时候人格芯片还在，机械族不能断开。
    /// </summary>
    public static class CorePersonaUtility
    {
        public const string VoyageChipHediff = "AsuNib_Voyage_PersonaChipCarrier";

        public static bool IsPersona(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.HasHediff(ShipCorePersonaDefOf.ShipCorePersonaBound) == true;
        }

        public static Thing CoreOf(Pawn pawn)
        {
            if (pawn?.ParentHolder is ThingOwner owner && owner.Owner is CompShipCorePersona comp)
                return comp.parent;
            if (pawn == null || !pawn.Spawned)
                return null;

            ThingDef coreDef = DefDatabase<ThingDef>.GetNamedSilentFail("Ship_ComputerCore");
            if (coreDef == null)
                return null;

            List<Thing> cores = pawn.Map.listerThings.ThingsOfDef(coreDef);
            for (int i = 0; i < cores.Count; i++)
            {
                CompShipCorePersona onCore = cores[i].TryGetComp<CompShipCorePersona>();
                if (onCore?.Persona == pawn)
                    return cores[i];
            }

            return null;
        }

        public static bool HasVoyageChip(Pawn pawn)
        {
            HediffDef chip = DefDatabase<HediffDef>.GetNamedSilentFail(VoyageChipHediff);
            return chip != null && pawn?.health?.hediffSet?.HasHediff(chip) == true;
        }

        public static void RetireAutoMechlink(Pawn persona)
        {
            if (persona?.health == null || !HasVoyageChip(persona))
                return;

            Hediff mechlink = persona.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.MechlinkImplant);
            if (mechlink != null)
            {
                persona.health.RemoveHediff(mechlink);
                PawnComponentsUtility.AddAndRemoveDynamicComponents(persona, false);
                persona.mechanitor?.Notify_BandwidthChanged();
            }

            // 卸掉机控中枢时原版会重算分组。分组要在这之后装回去。
            Hediff_ShipCorePersonaBound.Restore(persona);
        }

        public static void TransferMechs(Pawn from, Pawn to)
        {
            if (from == null || to == null || from == to)
                return;

            // 原版交接会把机械族全部放进第一组。分组先记在飞船主脑身上，芯片接上后再装回去。
            Hediff_ShipCorePersonaBound.RememberFrom(to, from);
            if (from.mechanitor == null)
                return;

            List<Pawn> mechs = from.mechanitor.ControlledPawns.ToList();
            List<MechanitorControlGroup> groups = from.mechanitor.controlGroups;
            for (int i = 0; groups != null && i < groups.Count; i++)
            {
                List<AssignedMech> assigned = groups[i]?.AssignedMechs;
                for (int j = 0; assigned != null && j < assigned.Count; j++)
                {
                    Pawn mech = assigned[j]?.pawn;
                    if (mech != null && !mechs.Contains(mech))
                        mechs.Add(mech);
                }
            }

            for (int i = 0; i < mechs.Count; i++)
            {
                Pawn mech = mechs[i];
                if (mech == null || mech.Dead)
                    continue;

                from.mechanitor.UnassignPawnFromAnyControlGroup(mech);
                from.relations?.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
                mech.OverseerSubject?.Notify_DisconnectedFromOverseer();
                if (to.relations != null && mech.GetOverseer() != to)
                    to.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            }

            from.mechanitor.Notify_BandwidthChanged();
            to.mechanitor?.Notify_BandwidthChanged();
        }

        public static BodyPartRecord BrainOf(Pawn pawn)
        {
            BodyPartDef brain = DefDatabase<BodyPartDef>.GetNamedSilentFail("Brain");
            if (brain == null)
                return null;
            return pawn.RaceProps?.body?.GetPartsWithDef(brain).FirstOrDefault();
        }
    }
}
