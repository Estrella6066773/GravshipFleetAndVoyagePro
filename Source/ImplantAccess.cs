using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 飞船电脑核心存取机械师植入物，规则与原版自行安装相同。
    /// 取出队列的下标写在工作的 count 上。不能从中间删掉更早的记录，否则读档后的工作会取错物品。
    /// </summary>
    public static class ImplantAccess
    {
        public static bool IsMechanitorImplant(ThingDef def)
        {
            CompProperties_UseEffectInstallImplant props = InstallProps(def);
            if (props?.hediffDef == null)
                return false;
            if (props.hediffDef == HediffDefOf.MechlinkImplant || GrantsMechanitor(props.hediffDef))
                return true;

            CompProperties_Usable usable = def.GetCompProperties<CompProperties_Usable>();
            HediffDef required = usable?.userMustHaveHediff;
            return required == HediffDefOf.MechlinkImplant || GrantsMechanitor(required);
        }

        public static bool IsMechanitorGear(ThingDef def)
        {
            if (def == null || IsMechanitorImplant(def))
                return false;
            if (def.IsApparel || def.equipmentType != EquipmentType.None)
                return HasMechanitorStats(def.equippedStatOffsets);
            return false;
        }

        public static bool CanStore(Pawn pawn, ThingDef def)
        {
            return CanInstall(pawn, def) || CanEquip(pawn, def);
        }

        public static bool CanInstall(Pawn pawn, ThingDef item)
        {
            CompProperties_UseEffectInstallImplant props = InstallProps(item);
            if (pawn?.health == null || props?.hediffDef == null)
                return false;

            CompProperties_Usable usable = item.GetCompProperties<CompProperties_Usable>();
            if (props.hediffDef == HediffDefOf.MechlinkImplant && CorePersonaUtility.IsPersona(pawn))
                return false;
            if (usable?.userMustHaveHediff != null && !SatisfiesImplantRequirement(pawn, usable.userMustHaveHediff))
                return false;
            if (props.requiresPsychicallySensitive && pawn.psychicEntropy != null && !pawn.psychicEntropy.IsPsychicallySensitive)
                return false;

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(props.hediffDef);
            if (existing == null)
            {
                if (props.requiresExistingHediff)
                    return false;
                return PartFor(pawn, props.bodyPart) != null;
            }
            if (!props.canUpgrade || existing is not Hediff_Level level)
                return false;
            if (level.level >= level.def.maxSeverity || props.maxSeverity <= level.level)
                return false;
            if (props.minSeverity > level.level)
                return false;
            return true;
        }

        public static bool CanEquip(Pawn pawn, ThingDef def)
        {
            if (pawn == null || !IsMechanitorGear(def))
                return false;
            if (def.IsApparel)
            {
                if (pawn.apparel == null)
                    return false;
                foreach (Apparel worn in pawn.apparel.WornApparel)
                {
                    if (worn.def == def)
                        return false;
                }

                return true;
            }

            if (pawn.equipment == null)
                return false;
            foreach (ThingWithComps equipped in pawn.equipment.AllEquipmentListForReading)
            {
                if (equipped.def == def)
                    return false;
            }

            return true;
        }

        public static bool CanRetrieve(Pawn pawn, ThingDef item)
        {
            if (pawn == null || item == null)
                return false;
            if (GearOf(pawn, item) != null)
                return true;

            CompProperties_UseEffectInstallImplant props = InstallProps(item);
            if (pawn.health == null || props?.hediffDef == null)
                return false;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(props.hediffDef);
            return hediff != null && ItemToTakeOut(hediff) == item;
        }

        public static void Store(CompShipCorePersona comp, Thing item)
        {
            if (item == null)
                return;
            if (IsMechanitorImplant(item.def))
                Install(comp, item);
            else
                Equip(comp, item);
        }

        public static void Install(CompShipCorePersona comp, Thing item)
        {
            Pawn pawn = comp?.Persona;
            CompProperties_UseEffectInstallImplant props = InstallProps(item?.def);
            if (pawn?.health == null || props?.hediffDef == null || !CanInstall(pawn, item.def))
                return;

            Thing one = item.stackCount > 1 ? item.SplitOff(1) : item;
            BodyPartRecord part = PartFor(pawn, props.bodyPart);
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(props.hediffDef);
            if (existing == null)
                pawn.health.AddHediff(props.hediffDef, part);
            else if (existing is Hediff_Level level)
                level.ChangeLevel(1);

            if (one != null && !one.Destroyed)
                one.Destroy(DestroyMode.Vanish);

            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, false);
            pawn.mechanitor?.Notify_BandwidthChanged();
        }

        public static void Retrieve(CompShipCorePersona comp, ThingDef item, Pawn carrier)
        {
            Pawn pawn = comp?.Persona;
            if (pawn == null || !CanRetrieve(pawn, item))
                return;
            if (GearOf(pawn, item) != null)
            {
                TakeGear(comp, item, carrier);
                return;
            }

            CompProperties_UseEffectInstallImplant props = InstallProps(item);
            if (pawn.health == null || props?.hediffDef == null)
                return;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(props.hediffDef);

            if (hediff is Hediff_Level level && level.level > 1)
                level.ChangeLevel(-1);
            else
                pawn.health.RemoveHediff(hediff);

            Thing made = ThingMaker.MakeThing(item);
            if (carrier?.inventory?.innerContainer == null || !carrier.inventory.innerContainer.TryAdd(made))
                GenPlace.TryPlaceThing(made, comp.parent.Position, comp.parent.Map, ThingPlaceMode.Near);

            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, false);
            pawn.mechanitor?.Notify_BandwidthChanged();
        }

        public static IEnumerable<Thing> CarriedImplants(Pawn pawn)
        {
            if (pawn?.carryTracker?.CarriedThing != null && CanCarryToCore(pawn.carryTracker.CarriedThing.def))
                yield return pawn.carryTracker.CarriedThing;

            if (pawn?.inventory?.innerContainer == null)
                yield break;

            foreach (Thing thing in pawn.inventory.innerContainer)
            {
                if (CanCarryToCore(thing.def))
                    yield return thing;
            }
        }

        public static bool IsMechanitorImplantHediff(Hediff hediff)
        {
            if (hediff?.def == null || hediff.def == HediffDefOf.MechlinkImplant)
                return false;
            if (hediff.def == ShipCorePersonaDefOf.ShipCorePersonaBound
                || hediff.def.defName == CorePersonaUtility.VoyageChipHediff)
                return false;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                CompProperties_UseEffectInstallImplant props = InstallProps(def);
                if (props?.hediffDef == hediff.def && IsMechanitorImplant(def))
                    return true;
            }

            return false;
        }

        public static IEnumerable<ThingDef> Retrievable(Pawn pawn)
        {
            if (pawn?.health == null)
                yield break;

            HashSet<ThingDef> yielded = new HashSet<ThingDef>();
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                ThingDef item = ItemToTakeOut(hediff);
                if (item != null && yielded.Add(item))
                    yield return item;
            }

            if (pawn.apparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    if (IsMechanitorGear(apparel.def) && yielded.Add(apparel.def))
                        yield return apparel.def;
                }
            }

            if (pawn.equipment == null)
                yield break;

            foreach (ThingWithComps equipped in pawn.equipment.AllEquipmentListForReading)
            {
                if (IsMechanitorGear(equipped.def) && yielded.Add(equipped.def))
                    yield return equipped.def;
            }
        }

        private static ThingDef ItemToTakeOut(Hediff hediff)
        {
            if (hediff?.def == null)
                return null;

            int level = hediff is Hediff_Level leveled ? leveled.level : 1;
            int previous = level - 1;
            ThingDef best = null;
            float bestMin = float.MinValue;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                CompProperties_UseEffectInstallImplant props = InstallProps(def);
                if (!IsMechanitorImplant(def) || props.hediffDef != hediff.def)
                    continue;
                if (!props.canUpgrade)
                    return def;
                if (props.requiresExistingHediff && previous <= 0)
                    continue;
                if (props.minSeverity > previous || props.maxSeverity <= previous)
                    continue;
                if (props.minSeverity >= bestMin)
                {
                    bestMin = props.minSeverity;
                    best = def;
                }
            }

            return best;
        }

        private static CompProperties_UseEffectInstallImplant InstallProps(ThingDef def)
        {
            return def?.GetCompProperties<CompProperties_UseEffectInstallImplant>();
        }

        /// <summary>
        /// 远程修理等植入物要求身上有机控中枢。飞船主脑身上的人格芯片承担这项要求。
        /// </summary>
        private static bool SatisfiesImplantRequirement(Pawn pawn, HediffDef required)
        {
            if (pawn.health.hediffSet.HasHediff(required))
                return true;
            if (!CorePersonaUtility.HasVoyageChip(pawn))
                return false;
            return required == HediffDefOf.MechlinkImplant || required.defName.IndexOf("Mechlink", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool CanCarryToCore(ThingDef def)
        {
            return IsMechanitorImplant(def) || IsMechanitorGear(def);
        }

        private static bool GrantsMechanitor(HediffDef def)
        {
            if (def?.stages == null)
                return false;

            for (int i = 0; i < def.stages.Count; i++)
            {
                if (HasMechanitorStats(def.stages[i]?.statOffsets))
                    return true;
            }

            return false;
        }

        private static bool HasMechanitorStats(List<StatModifier> offsets)
        {
            if (offsets == null)
                return false;

            for (int i = 0; i < offsets.Count; i++)
            {
                StatDef stat = offsets[i]?.stat;
                if (stat == null)
                    continue;
                if (stat == StatDefOf.MechBandwidth || stat == StatDefOf.MechControlGroups)
                    return true;
                if (stat.defName != null && stat.defName.IndexOf("RemoteControlDistance", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static BodyPartRecord PartFor(Pawn pawn, BodyPartDef partDef)
        {
            if (partDef == null)
                return CorePersonaUtility.BrainOf(pawn);

            foreach (BodyPartRecord part in pawn.RaceProps.body.GetPartsWithDef(partDef))
                return part;
            return null;
        }

        private static void Equip(CompShipCorePersona comp, Thing item)
        {
            Pawn pawn = comp?.Persona;
            if (pawn == null || item == null || !CanEquip(pawn, item.def))
                return;

            Thing one = item.stackCount > 1 ? item.SplitOff(1) : item;
            if (one is Apparel apparel && pawn.apparel != null)
            {
                pawn.apparel.Wear(apparel, dropReplacedApparel: true, locked: true);
                return;
            }

            if (one is ThingWithComps gear && pawn.equipment != null && gear.def.equipmentType != EquipmentType.None)
            {
                pawn.equipment.MakeRoomFor(gear);
                pawn.equipment.AddEquipment(gear);
            }
        }

        private static void TakeGear(CompShipCorePersona comp, ThingDef def, Pawn carrier)
        {
            Pawn pawn = comp?.Persona;
            Thing gear = GearOf(pawn, def);
            if (gear == null)
                return;

            if (gear is Apparel apparel && pawn.apparel != null)
            {
                if (pawn.apparel.IsLocked(apparel))
                    pawn.apparel.Unlock(apparel);
                pawn.apparel.Remove(apparel);
            }
            else if (gear is ThingWithComps equipped && pawn.equipment != null)
            {
                pawn.equipment.Remove(equipped);
            }

            if (carrier?.inventory?.innerContainer == null || !carrier.inventory.innerContainer.TryAdd(gear))
                GenPlace.TryPlaceThing(gear, comp.parent.Position, comp.parent.Map, ThingPlaceMode.Near);
        }

        private static Thing GearOf(Pawn pawn, ThingDef def)
        {
            if (pawn == null || def == null || !IsMechanitorGear(def))
                return null;

            if (pawn.apparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    if (apparel.def == def)
                        return apparel;
                }
            }

            if (pawn.equipment == null)
                return null;

            foreach (ThingWithComps equipped in pawn.equipment.AllEquipmentListForReading)
            {
                if (equipped.def == def)
                    return equipped;
            }

            return null;
        }
    }

    public class StoredCoreImplant : IExposable
    {
        public string defName;
        public int level = 1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref level, "level", 1);
        }
    }
}
