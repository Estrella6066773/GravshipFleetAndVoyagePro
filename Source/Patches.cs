using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace ShipCorePersona
{
    /// <summary>
    /// 飞船主脑不在地图上时，原版不让她指挥机械族。飞船电脑核心还在地图上，就允许她指挥机械族。
    /// 倒地、被俘、精神崩溃时，仍按原版不让她指挥机械族。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), "get_CanControlMechs")]
    public static class Patch_CorePersonaCanControlMechs
    {
        public static void Postfix(ref AcceptanceReport __result, Pawn ___pawn)
        {
            if (__result.Accepted)
                return;

            Pawn pawn = ___pawn;
            if (!CorePersonaUtility.IsPersona(pawn))
                return;
            if (pawn.Downed || pawn.IsPrisoner || pawn.InMentalState)
                return;

            Thing core = CorePersonaUtility.CoreOf(pawn);
            if (core != null && core.Spawned)
                __result = true;
        }
    }

    /// <summary>
    /// 旧档里的机控中枢卸掉时，原版会断开她指挥的机械族。人格芯片还在，机械族保持连接。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.Notify_MechlinkRemoved))]
    public static class Patch_ChipKeepsMechs
    {
        public static bool Prefix(Pawn ___pawn)
        {
            return !(CorePersonaUtility.IsPersona(___pawn) && CorePersonaUtility.HasVoyageChip(___pawn));
        }
    }

    /// <summary>
    /// 机械族的「选择监督者」在监督者还没生成时是灰的。这个按钮要能重新选中飞船主脑。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos))]
    public static class Patch_SelectCorePersonaOverseer
    {
        private static readonly AccessTools.FieldRef<Gizmo, bool> DisabledField =
            AccessTools.FieldRefAccess<Gizmo, bool>("disabled");

        public static void Postfix(Pawn mech, ref IEnumerable<Gizmo> __result)
        {
            Pawn overseer = mech.GetOverseer();
            if (!CorePersonaUtility.IsPersona(overseer))
                return;

            string label = "CommandSelectOverseer".Translate();
            IEnumerable<Gizmo> original = __result;
            __result = Enable(original, label);
        }

        private static IEnumerable<Gizmo> Enable(IEnumerable<Gizmo> gizmos, string selectLabel)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                if (gizmo is Command command && command.defaultLabel == selectLabel && DisabledField(command))
                {
                    DisabledField(command) = false;
                    command.disabledReason = null;
                }

                yield return gizmo;
            }
        }
    }

    /// <summary>
    /// 机械师植入物只通过飞船电脑核心存取。飞船主脑自己的手术单里不出现安装和取出这些植入物的操作。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(RecipeWorker), nameof(RecipeWorker.AvailableOnNow))]
    public static class Patch_SurgeryOnlyImplants
    {
        public static void Postfix(Thing thing, ref bool __result)
        {
            if (!__result)
                return;
            if (thing is Pawn pawn && CorePersonaUtility.IsPersona(pawn))
                __result = false;
        }
    }

    /// <summary>
    /// 选中飞船主脑再点机械师植入物时，原版会给出「给她安装」的菜单。安装和取出只通过飞船电脑核心进行。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(CompUsable), nameof(CompUsable.CompFloatMenuOptions))]
    public static class Patch_PersonaDoesNotUseImplants
    {
        public static void Postfix(CompUsable __instance, Pawn myPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            if (!CorePersonaUtility.IsPersona(myPawn))
                return;
            if (!ImplantAccess.IsMechanitorImplant(__instance.parent?.def))
                return;

            __result = System.Array.Empty<FloatMenuOption>();
        }
    }

    /// <summary>
    /// 拿着机械师植入物点飞船主脑时，原版会把她当成安装目标。飞船主脑这个安装目标不可用。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(CompTargetable), nameof(CompTargetable.ValidateTarget))]
    public static class Patch_PersonaIsNotImplantTarget
    {
        public static void Postfix(CompTargetable __instance, LocalTargetInfo target, ref bool __result)
        {
            if (!__result)
                return;
            if (target.Thing is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return;
            if (!ImplantAccess.IsMechanitorImplant(__instance.parent?.def))
                return;

            __result = false;
        }
    }

    /// <summary>
    /// 飞船主脑不能离开飞船电脑核心。
    /// 释放能力和原地等待这两类工作要允许她做，否则能力放不出来，征召也会报错。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_PersonaCannotAct
    {
        private static readonly HashSet<string> Allowed =
            new HashSet<string> { "RepairMechRemote", "Wait", "Wait_Combat", "Wait_MaintainPosture" };

        public static bool Prefix(Pawn ___pawn, Job newJob)
        {
            if (!CorePersonaUtility.IsPersona(___pawn))
                return true;
            if (newJob?.ability != null)
                return true;

            string defName = newJob?.def?.defName;
            return defName != null && Allowed.Contains(defName);
        }
    }

    /// <summary>
    /// 飞船主脑不在地图上画出来。她仍生成在飞船电脑核心所占的格子上，能力用那个格子计算距离。
    /// 原版的机械族指挥范围画在这一步里，跳过绘制时要把范围单独画出来。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DynamicDrawPhaseAt))]
    public static class Patch_PersonaHidden
    {
        public static bool Prefix(Pawn __instance, DrawPhase phase)
        {
            if (!CorePersonaUtility.IsPersona(__instance))
                return true;

            if (phase == DrawPhase.Draw && !PersonaCommand.DrawShipRange(__instance))
                __instance.mechanitor?.DrawCommandRadius();
            return false;
        }
    }

    [RequiresVoyage]
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
    public static class Patch_PersonaHiddenLabel
    {
        public static bool Prefix(Pawn __instance)
        {
            return !CorePersonaUtility.IsPersona(__instance);
        }
    }

    /// <summary>
    /// 飞船主脑站在飞船电脑核心左下角那格上，而核心占 2×2 格，画出来的中心在右上各半格处。
    /// 所以她与绘制位置绑在一起的东西（选中框、镜头对准、画在她身上的范围圈）都往右上各挪半格，落在核心中心上。
    /// 只挪绘制位置，格子不动：能力距离、指挥范围都还按原来那一格算。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawPos), MethodType.Getter)]
    public static class Patch_PersonaDrawPos
    {
        private const float CoreCenterOffset = 0.5f;

        public static void Postfix(Pawn __instance, ref UnityEngine.Vector3 __result)
        {
            if (!__instance.Spawned || __instance.kindDef != ShipCorePersonaDefOf.ShipCorePersona)
                return;
            if (!CorePersonaUtility.IsPersona(__instance))
                return;

            __result = new UnityEngine.Vector3(__result.x + CoreCenterOffset, __result.y, __result.z + CoreCenterOffset);
        }
    }

    /// <summary>
    /// 飞船主脑放能力时不检查视线，距离仍要在能力范围内。
    /// 她在飞船电脑核心里，飞船电脑核心本身会挡住视线，所以这里直接放过视线检查。
    /// 开始施放时，原版另外调用 TryFindShootLineFromTo，不走 CanHitTargetFrom，所以两处都要放过视线检查。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTargetFrom))]
    public static class Patch_PersonaIgnoresSight
    {
        public static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref bool __result)
        {
            if (__instance.caster is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return true;

            __result = InRange(pawn, __instance, root, targ);
            return false;
        }

        internal static bool InRange(Pawn pawn, Verb verb, IntVec3 root, LocalTargetInfo targ)
        {
            Map map = pawn.MapHeld;
            if (map == null || !targ.IsValid || (targ.HasThing && targ.Thing.Map != map))
                return false;

            float range = verb.EffectiveRange;
            return root.DistanceToSquared(targ.Cell) <= range * range;
        }

        internal static bool NearCore(Pawn pawn, LocalTargetInfo targ)
        {
            Thing core = CorePersonaUtility.CoreOf(pawn);
            Map map = pawn.MapHeld;
            if (core == null || !core.Spawned || map == null || !targ.IsValid || (targ.HasThing && targ.Thing.Map != map))
                return false;

            IntVec3 closest = core.OccupiedRect().ClosestCellTo(targ.Cell);
            return closest.DistanceToSquared(targ.Cell) <= 2;
        }
    }

    [RequiresVoyage]
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryFindShootLineFromTo))]
    public static class Patch_PersonaShootLineIgnoresSight
    {
        public static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, bool ignoreRange, ref bool __result)
        {
            if (__instance.caster is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return true;

            if (!ignoreRange && !CanUse(__instance, pawn, root, targ))
            {
                resultingLine = new ShootLine(root, targ.Cell);
                __result = false;
                return false;
            }

            resultingLine = new ShootLine(root, targ.Cell);
            __result = true;
            return false;
        }

        private static bool CanUse(Verb verb, Pawn pawn, IntVec3 root, LocalTargetInfo targ)
        {
            if (verb.EffectiveRange <= 0f)
                return Patch_PersonaIgnoresSight.NearCore(pawn, targ);
            return Patch_PersonaIgnoresSight.InRange(pawn, verb, root, targ);
        }
    }

    /// <summary>
    /// 近身能力原版要求施放者走到目标旁边。飞船主脑站在飞船电脑核心里走不出去，于是每个目标都选不中。
    /// 挨着飞船电脑核心的目标，算飞船主脑够得着这些目标。有射程的能力仍按距离判断能不能打中，不看视线。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(Verb_CastAbility), nameof(Verb_CastAbility.ValidateTarget))]
    public static class Patch_PersonaAbilityTarget
    {
        public static bool Prefix(Verb_CastAbility __instance, LocalTargetInfo target, bool showMessages, ref bool __result)
        {
            Pawn pawn = __instance.ability?.pawn;
            if (!CorePersonaUtility.IsPersona(pawn) || __instance.EffectiveRange > 0f)
                return true;

            if (!Patch_PersonaIgnoresSight.NearCore(pawn, target) || !__instance.IsApplicableTo(target, showMessages))
            {
                __result = false;
                return false;
            }

            List<CompAbilityEffect> effects = __instance.ability.EffectComps;
            for (int i = 0; i < effects.Count; i++)
            {
                if (!effects[i].Valid(target, showMessages))
                {
                    __result = false;
                    return false;
                }
            }

            __result = true;
            return false;
        }
    }

    /// <summary>
    /// 原版找施法格子时，会挑一个看得见目标的位置，施放者就会走出建筑。
    /// 飞船主脑不走出飞船电脑核心，就在当前格子施放能力。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(CastPositionFinder), nameof(CastPositionFinder.TryFindCastPosition))]
    public static class Patch_PersonaCastsFromCore
    {
        public static bool Prefix(CastPositionRequest newReq, ref IntVec3 dest, ref bool __result)
        {
            if (newReq.caster is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return true;
            if (newReq.verb == null || !newReq.verb.CanHitTargetFrom(pawn.Position, newReq.target))
            {
                __result = false;
                return false;
            }

            dest = pawn.Position;
            __result = true;
            return false;
        }
    }

    /// <summary>
    /// 疾病和社交冲突用的是另一份殖民者名单，不是上方栏那份。
    /// 这里交出去的是去掉飞船主脑的副本，不改上方栏正在用的那份名单。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.FreeHumanlikesSpawnedOfFaction))]
    public static class Patch_PersonaNotInSpawnedColonistList
    {
        public static void Postfix(ref List<Pawn> __result)
        {
            WithoutPersonas(ref __result);
        }

        internal static void WithoutPersonas(ref List<Pawn> pawns)
        {
            if (pawns == null)
                return;

            bool found = false;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (CorePersonaUtility.IsPersona(pawns[i]))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return;

            List<Pawn> copy = new List<Pawn>(pawns.Count - 1);
            for (int i = 0; i < pawns.Count; i++)
            {
                if (!CorePersonaUtility.IsPersona(pawns[i]))
                    copy.Add(pawns[i]);
            }

            pawns = copy;
        }
    }

    [RequiresVoyage]
    [HarmonyPatch(typeof(MapPawns), "get_FreeColonistsAndPrisoners")]
    public static class Patch_PersonaNotInColonistsAndPrisoners
    {
        public static void Postfix(ref List<Pawn> __result)
        {
            Patch_PersonaNotInSpawnedColonistList.WithoutPersonas(ref __result);
        }
    }

    [RequiresVoyage]
    [HarmonyPatch(typeof(MapPawns), "get_FreeColonistsAndPrisonersSpawned")]
    public static class Patch_PersonaNotInColonistsAndPrisonersSpawned
    {
        public static void Postfix(ref List<Pawn> __result)
        {
            Patch_PersonaNotInSpawnedColonistList.WithoutPersonas(ref __result);
        }
    }

    /// <summary>
    /// 离开地图时，原版会把殖民者放进世界人物池，并标成失踪。飞船主脑不进世界人物池。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.PassToWorld))]
    public static class Patch_PersonaSkipsWorldPawnPool
    {
        public static void Postfix(Pawn pawn)
        {
            if (!CorePersonaUtility.IsPersona(pawn))
                return;
            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
        }
    }

    /// <summary>
    /// 人格核心不在地图上时，原版把对不上的人标成失踪。飞船主脑不标成失踪。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(SocialCardUtility), nameof(SocialCardUtility.GetPawnSituationLabel))]
    public static class Patch_PersonaNotMarkedMissing
    {
        public static void Postfix(Pawn pawn, ref string __result)
        {
            if (!CorePersonaUtility.IsPersona(pawn))
                return;
            if (__result == "Missing".Translate())
                __result = "";
        }
    }

    /// <summary>
    /// 她站在电脑核心那一格上。火灾、真空、极端温度、毒气和爆炸都打在这一格。
    /// 这些伤害不落在她身上，她也不会死。人格核心被删除时用 Destroy 移除她，不走 Kill。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_PersonaIgnoresDamage
    {
        public static bool Prefix(Pawn __instance, out bool absorbed)
        {
            if (!CorePersonaUtility.IsPersona(__instance))
            {
                absorbed = false;
                return true;
            }

            absorbed = true;
            return false;
        }
    }

    [RequiresVoyage]
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_PersonaDoesNotDie
    {
        public static bool Prefix(Pawn __instance)
        {
            return !CorePersonaUtility.IsPersona(__instance);
        }
    }

    [RequiresVoyage]
    [HarmonyPatch(typeof(StatExtension), nameof(StatExtension.GetStatValue))]
    public static class Patch_PersonaEnvironmentStats
    {
        private static StatDef vacuumResistance;
        private static bool vacuumChecked;

        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (!EnvironmentStat(stat))
                return;
            if (!(thing is Pawn pawn) || !CorePersonaUtility.IsPersona(pawn))
                return;

            if (stat == StatDefOf.Flammability)
                __result = 0f;
            else if (stat == StatDefOf.ComfyTemperatureMin)
                __result = -10000f;
            else if (stat == StatDefOf.ComfyTemperatureMax)
                __result = 10000f;
            else
                __result = 1f;
        }

        private static bool EnvironmentStat(StatDef stat)
        {
            if (stat == null)
                return false;
            if (stat == StatDefOf.Flammability || stat == StatDefOf.ComfyTemperatureMin || stat == StatDefOf.ComfyTemperatureMax)
                return true;
            if (stat == StatDefOf.ToxicResistance || stat == StatDefOf.ToxicEnvironmentResistance)
                return true;
            return stat == VacuumResistance;
        }

        private static StatDef VacuumResistance
        {
            get
            {
                if (!vacuumChecked)
                {
                    vacuumChecked = true;
                    vacuumResistance = DefDatabase<StatDef>.GetNamedSilentFail("VacuumResistance");
                }

                return vacuumResistance;
            }
        }
    }

    [RequiresVoyage]
    [HarmonyPatch(typeof(MentalBreaker), "get_CanDoRandomMentalBreaks")]
    public static class Patch_PersonaNoMentalBreak
    {
        public static void Postfix(Pawn ___pawn, ref bool __result)
        {
            if (CorePersonaUtility.IsPersona(___pawn))
                __result = false;
        }
    }

    [RequiresVoyage]
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Patch_CleanupPersonaLinksAfterLoad
    {
        public static void Postfix()
        {
            GameComponent_PersonaPool.CleanupUnresolvedLinks();
        }
    }

    /// <summary>
    /// 协调器的主人已经不在存档里时，心情条目仍会去读她。这里改成不读，避免打开需求界面时崩溃。
    /// </summary>
    [HarmonyPatch(typeof(Thought_PsychicHarmonizer), "get_LabelCap")]
    public static class Patch_HarmonizerLabelWithoutPawn
    {
        public static bool Prefix(Thought_PsychicHarmonizer __instance, ref string __result)
        {
            if (__instance.harmonizer?.pawn != null && !__instance.harmonizer.pawn.Destroyed)
                return true;

            __result = __instance.def?.label ?? string.Empty;
            return false;
        }
    }

    [RequiresVoyage]
    [HarmonyPatch(typeof(InspirationHandler), nameof(InspirationHandler.TryStartInspiration))]
    public static class Patch_PersonaNoInspiration
    {
        public static bool Prefix(InspirationHandler __instance)
        {
            return !CorePersonaUtility.IsPersona(__instance.pawn);
        }
    }

    /// <summary>
    /// 机械族已经收进飞船。地图放弃时原版会按「留下」拆掉监管关系，并提示失去控制。
    /// 跟着飞船离开的机械族保持监管，不提示。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_RelationsTracker), "RemoveRelationsOnLeftBehind")]
    public static class Patch_MechsAboardKeepOverseer
    {
        private static readonly System.Reflection.FieldInfo TravelingGravshipField =
            AccessTools.Field(typeof(WorldComponent_GravshipController), "gravship");

        public static bool Prefix(Pawn ___pawn, List<DirectPawnRelation> ___directRelations)
        {
            if (___pawn?.relations == null || ___directRelations == null)
                return true;
            if (!AnyMechAboard(___pawn, ___directRelations))
                return true;

            for (int num = ___directRelations.Count - 1; num >= 0; num--)
            {
                DirectPawnRelation relation = ___directRelations[num];
                if (relation?.def == null || !relation.def.removeOnLeftBehind)
                    continue;
                if (relation.def == PawnRelationDefOf.Overseer && MechAboard(___pawn, relation.otherPawn))
                    continue;

                Pawn otherPawn = relation.otherPawn;
                ___pawn.relations.RemoveDirectRelation(relation);
                relation.def.Worker.Notify_PostRemovedLeftBehind(___pawn, otherPawn);
            }

            return false;
        }

        private static bool AnyMechAboard(Pawn owner, List<DirectPawnRelation> relations)
        {
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation?.def == PawnRelationDefOf.Overseer && relation.def.removeOnLeftBehind && MechAboard(owner, relation.otherPawn))
                    return true;
            }

            return false;
        }

        private static bool MechAboard(Pawn owner, Pawn other)
        {
            Pawn mech = MechanoidOf(owner, other);
            if (mech == null || mech.Dead || mech.Destroyed)
                return false;
            if (mech.BeingTransportedOnGravship)
                return true;

            Gravship ship = Current.Game?.Gravship;
            if (ship != null && ship.ContainsPawn(mech))
                return true;

            object traveling = TravelingGravshipField?.GetValue(Find.World?.GetComponent<WorldComponent_GravshipController>());
            return traveling is Gravship gravship && gravship.ContainsPawn(mech);
        }

        private static Pawn MechanoidOf(Pawn first, Pawn second)
        {
            if (first?.RaceProps?.IsMechanoid == true)
                return first;
            if (second?.RaceProps?.IsMechanoid == true)
                return second;
            return null;
        }
    }

    /// <summary>
    /// 读档时先生成人物，再生成建筑。飞船主脑站在不可通行的电脑核心上，原版会把核心退成材料。
    /// 核心落地前先把她挪到占地外面，核心生成后会把她挪回去。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawnBuildingAsPossible))]
    public static class Patch_CoreNotRefundedOnLoad
    {
        public static void Prefix(Building building, Map map)
        {
            if (map == null || building?.def == null || building.def.passability != Traversability.Impassable)
                return;
            if (building.TryGetComp<CompShipCorePersona>() == null)
                return;

            CellRect rect = building.OccupiedRect();
            IntVec3 outside = CellOutside(rect, map);
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map))
                    continue;

                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    if (things[i] is Pawn pawn && CorePersonaUtility.IsPersona(pawn))
                        pawn.Position = outside;
                }
            }
        }

        private static IntVec3 CellOutside(CellRect rect, Map map)
        {
            IntVec3[] tries =
            {
                new IntVec3(rect.maxX + 1, 0, rect.minZ),
                new IntVec3(rect.minX - 1, 0, rect.minZ),
                new IntVec3(rect.minX, 0, rect.maxZ + 1),
                new IntVec3(rect.minX, 0, rect.minZ - 1)
            };
            for (int i = 0; i < tries.Length; i++)
            {
                if (tries[i].InBounds(map))
                    return tries[i];
            }

            return map.Center;
        }
    }
}
