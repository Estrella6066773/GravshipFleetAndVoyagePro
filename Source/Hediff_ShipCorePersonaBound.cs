using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 人格芯片离开后，原版会拆掉机械师组件，名下机械族的分组就没了。
    /// 分组先记在这个状态上。芯片回到头上，再按原来的分组装回去。
    /// </summary>
    public class Hediff_ShipCorePersonaBound : Hediff
    {
        private List<SavedMechGroup> groups;
        private bool pendingRestore;

        private static readonly MethodInfo GroupCountChanged =
            AccessTools.Method(typeof(Pawn_MechanitorTracker), "Notify_ControlGroupAmountMayChanged");

        private static readonly FieldInfo TagsField =
            AccessTools.Field(typeof(MechanitorControlGroup), "tags");

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref groups, "mechGroups", LookMode.Deep);
            Scribe_Values.Look(ref pendingRestore, "mechGroupsPending", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && groups == null)
                groups = new List<SavedMechGroup>();
        }

        public static void Remember(Pawn persona)
        {
            RememberFrom(persona, persona);
        }

        public static void RememberFrom(Pawn persona, Pawn source)
        {
            List<SavedMechGroup> copied = Copy(source);
            if (!WorthKeeping(copied))
                return;

            Hediff_ShipCorePersonaBound bound = Of(persona);
            if (bound == null)
                return;

            bound.groups = copied;
            bound.pendingRestore = true;
        }

        public static void Restore(Pawn persona)
        {
            Hediff_ShipCorePersonaBound bound = Of(persona);
            if (bound == null || !bound.pendingRestore || bound.groups == null || bound.groups.Count == 0)
                return;
            if (persona.mechanitor == null)
                PawnComponentsUtility.AddAndRemoveDynamicComponents(persona, false);
            if (persona.mechanitor == null || persona.mechanitor.TotalAvailableControlGroups <= 0)
                return;

            Pawn_MechanitorTracker tracker = persona.mechanitor;
            GroupCountChanged?.Invoke(tracker, null);
            if (tracker.controlGroups.Count == 0)
                return;

            int count = bound.groups.Count < tracker.controlGroups.Count ? bound.groups.Count : tracker.controlGroups.Count;
            for (int i = 0; i < count; i++)
                ApplyGroup(persona, tracker, tracker.controlGroups[i], bound.groups[i]);

            tracker.Notify_BandwidthChanged();
            // 机控中枢还在时，卸掉它会把多出来的分组并进第一组。等它卸掉再算装完。
            if (persona.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.MechlinkImplant) == null)
                bound.pendingRestore = false;
        }

        private static Hediff_ShipCorePersonaBound Of(Pawn pawn)
        {
            return pawn?.health?.hediffSet?.GetFirstHediffOfDef(ShipCorePersonaDefOf.ShipCorePersonaBound) as Hediff_ShipCorePersonaBound;
        }

        private static void ApplyGroup(Pawn persona, Pawn_MechanitorTracker tracker, MechanitorControlGroup live, SavedMechGroup saved)
        {
            if (live == null || saved == null)
                return;

            TagsField?.SetValue(live, null);
            if (saved.workMode != null)
                live.SetWorkMode(saved.workMode, saved.target);
            if (saved.hasRecharge)
                live.mechRechargeThresholds = saved.recharge;

            if (saved.mechs == null)
                return;

            for (int i = 0; i < saved.mechs.Count; i++)
            {
                Pawn mech = saved.mechs[i];
                if (mech == null || mech.Dead || mech.Destroyed)
                    continue;

                Pawn boss = mech.GetOverseer();
                if (boss != null && boss != persona)
                {
                    tracker.UnassignPawnFromAnyControlGroup(mech);
                    continue;
                }

                if (boss == null && persona.relations != null)
                    persona.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);

                live.Assign(mech);
            }

            if (saved.taggedMechs == null || saved.tagValues == null)
                return;

            int tags = saved.taggedMechs.Count < saved.tagValues.Count ? saved.taggedMechs.Count : saved.tagValues.Count;
            for (int i = 0; i < tags; i++)
            {
                Pawn mech = saved.taggedMechs[i];
                if (mech == null || mech.Dead || saved.tagValues[i] == null)
                    continue;
                if (mech.GetOverseer() != persona)
                    continue;
                live.SetTag(mech, saved.tagValues[i]);
            }
        }

        private static List<SavedMechGroup> Copy(Pawn source)
        {
            List<SavedMechGroup> copied = new List<SavedMechGroup>();
            List<MechanitorControlGroup> live = source?.mechanitor?.controlGroups;
            if (live == null)
                return copied;

            for (int i = 0; i < live.Count; i++)
            {
                MechanitorControlGroup group = live[i];
                SavedMechGroup saved = new SavedMechGroup();
                if (group == null)
                {
                    copied.Add(saved);
                    continue;
                }

                saved.workMode = group.WorkMode;
                saved.target = group.Target;
                saved.hasRecharge = true;
                saved.recharge = group.mechRechargeThresholds;
                List<AssignedMech> assigned = group.AssignedMechs;
                for (int j = 0; assigned != null && j < assigned.Count; j++)
                {
                    Pawn mech = assigned[j]?.pawn;
                    if (mech != null && !mech.Destroyed)
                        saved.mechs.Add(mech);
                }

                if (TagsField?.GetValue(group) is Dictionary<Pawn, string> tags)
                {
                    foreach (KeyValuePair<Pawn, string> tag in tags)
                    {
                        if (tag.Key == null || tag.Key.Destroyed || tag.Value == null)
                            continue;
                        saved.taggedMechs.Add(tag.Key);
                        saved.tagValues.Add(tag.Value);
                    }
                }

                copied.Add(saved);
            }

            return copied;
        }

        private static bool WorthKeeping(List<SavedMechGroup> copied)
        {
            if (copied == null)
                return false;

            for (int i = 0; i < copied.Count; i++)
            {
                SavedMechGroup group = copied[i];
                if (group == null)
                    continue;
                if (group.mechs != null && group.mechs.Count > 0)
                    return true;
                if (group.workMode != null && group.workMode != MechWorkModeDefOf.Work)
                    return true;
                if (group.target.IsValid)
                    return true;
                if (group.taggedMechs != null && group.taggedMechs.Count > 0)
                    return true;
                if (group.hasRecharge && group.recharge != MechanitorControlGroup.DefaultMechRechargeThresholds)
                    return true;
            }

            return false;
        }
    }

    public class SavedMechGroup : IExposable
    {
        public MechWorkModeDef workMode;
        public GlobalTargetInfo target = GlobalTargetInfo.Invalid;
        public bool hasRecharge;
        public FloatRange recharge;
        public List<Pawn> mechs = new List<Pawn>();
        public List<Pawn> taggedMechs = new List<Pawn>();
        public List<string> tagValues = new List<string>();

        public void ExposeData()
        {
            Scribe_Defs.Look(ref workMode, "workMode");
            Scribe_TargetInfo.Look(ref target, "target");
            Scribe_Values.Look(ref hasRecharge, "hasRecharge", false);
            Scribe_Values.Look(ref recharge, "recharge");
            Scribe_Collections.Look(ref mechs, "mechs", LookMode.Reference);
            Scribe_Collections.Look(ref taggedMechs, "taggedMechs", LookMode.Reference);
            Scribe_Collections.Look(ref tagValues, "tagValues", LookMode.Value);
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            if (mechs == null)
                mechs = new List<Pawn>();
            mechs.RemoveAll(mech => mech == null);
            if (taggedMechs == null)
                taggedMechs = new List<Pawn>();
            if (tagValues == null)
                tagValues = new List<string>();
            while (taggedMechs.Count > tagValues.Count)
                taggedMechs.RemoveAt(taggedMechs.Count - 1);
            while (tagValues.Count > taggedMechs.Count)
                tagValues.RemoveAt(tagValues.Count - 1);
            for (int i = taggedMechs.Count - 1; i >= 0; i--)
            {
                if (taggedMechs[i] != null)
                    continue;
                taggedMechs.RemoveAt(i);
                tagValues.RemoveAt(i);
            }
        }
    }
}
