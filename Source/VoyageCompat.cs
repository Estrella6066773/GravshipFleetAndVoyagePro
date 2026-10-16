using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 用本模组的飞船主脑替换 Gravship Voyage 随机生成的机械师。
    /// 不引用那个程序集。存档时先暂时清掉它的 host，避免同一个人被深拷贝两次。
    /// </summary>
    public static class VoyageCompat
    {
        private const string CoreTypeName = "GravshipVoyage.CompAICore";

        private static readonly System.Type CoreType = AccessTools.TypeByName(CoreTypeName);
        private static readonly FieldInfo HostField = CoreType == null ? null : AccessTools.Field(CoreType, "host");
        private static readonly FieldInfo ChipField = CoreType == null ? null : AccessTools.Field(CoreType, "chip");
        private static readonly FieldInfo MindField = CoreType == null ? null : AccessTools.Field(CoreType, "mind");
        private static readonly FieldInfo BoundIdField = CoreType == null ? null : AccessTools.Field(CoreType, "boundId");
        private static readonly System.Type ChipCompType = AccessTools.TypeByName("GravshipVoyage.CompPersonaChip");
        private static readonly FieldInfo ChipCompMind = ChipCompType == null ? null : AccessTools.Field(ChipCompType, "mind");
        private static readonly System.Type ChipHediffType = AccessTools.TypeByName("GravshipVoyage.Hediff_PersonaChip");
        private static readonly FieldInfo ChipHediffMind = ChipHediffType == null ? null : AccessTools.Field(ChipHediffType, "mind");
        private static readonly MethodInfo StillInTheWorldMethod = AccessTools.Method("GravshipVoyage.VoyageAI:StillInTheWorld");

        public static bool IsLoaded => CoreType != null;

        public static void TryPatch(Harmony harmony)
        {
            if (CoreType == null)
                return;

            harmony.Patch(
                AccessTools.Method(CoreType, "Wake"),
                prefix: new HarmonyMethod(typeof(VoyageCompat), nameof(PrefixWake)));
            harmony.Patch(
                AccessTools.Method(CoreType, "Attach"),
                postfix: new HarmonyMethod(typeof(VoyageCompat), nameof(PostfixAttach)));
            harmony.Patch(
                AccessTools.Method(CoreType, "PostExposeData"),
                prefix: new HarmonyMethod(typeof(VoyageCompat), nameof(PrefixExpose)),
                postfix: new HarmonyMethod(typeof(VoyageCompat), nameof(PostfixExpose)));
            harmony.Patch(
                AccessTools.Method(CoreType, "GiveTo"),
                postfix: new HarmonyMethod(typeof(VoyageCompat), nameof(PostfixChipLeft)));
            harmony.Patch(
                AccessTools.Method(CoreType, "TryWithdraw"),
                postfix: new HarmonyMethod(typeof(VoyageCompat), nameof(PostfixChipLeft)));
            if (ChipHediffType != null)
            {
                harmony.Patch(
                    AccessTools.Method(ChipHediffType, "PostRemoved"),
                    prefix: new HarmonyMethod(typeof(VoyageCompat), nameof(PrefixRememberMechGroups)));
            }

            System.Type extract = AccessTools.TypeByName("GravshipVoyage.CompChipExtract");
            if (extract != null)
            {
                harmony.Patch(
                    AccessTools.Method(extract, "CompFloatMenuOptions"),
                    postfix: new HarmonyMethod(typeof(VoyageCompat), nameof(PostfixNoExtractFromPersona)));
            }

            Log.Message("[ShipCorePersona] 已用飞船主脑替换 Gravship Voyage 随机生成的机械师。");
        }

        public static bool ShowsMechanitorGizmos(Thing core)
        {
            return IsLoaded && ChipSeated(core);
        }

        /// <summary>
        /// 读取人格核心的名字。
        /// 人格芯片还在飞船电脑核心里、名字还空着时，调用 Gravship Voyage 的 EnsureNamed，只取一次名字。
        /// 人格芯片已经离开飞船电脑核心时，飞船电脑核心上的 mind 是空的，这时只读 AIName，不再另外取名字。
        /// </summary>
        public static string CoreName(Thing core)
        {
            if (core == null || !IsLoaded)
                return null;

            ThingComp voyage = VoyageComp(core);
            if (voyage == null)
                return null;

            object mind = MindField?.GetValue(voyage);
            bool seated = ChipField?.GetValue(voyage) is bool chip && chip;
            if (seated && mind != null)
            {
                string ensured = mind.GetType().GetMethod("EnsureNamed")?.Invoke(mind, null) as string;
                if (!string.IsNullOrEmpty(ensured))
                    return ensured;
            }

            string aiName = voyage.GetType().GetProperty("AIName")?.GetValue(voyage) as string;
            if (string.IsNullOrEmpty(aiName) || aiName == "the persona")
                return null;
            return aiName;
        }

        public static int BoundMindId(Thing core)
        {
            if (core == null || !IsLoaded)
                return 0;

            ThingComp voyage = VoyageComp(core);
            if (voyage == null)
                return 0;

            if (ChipField?.GetValue(voyage) is bool seated && seated)
            {
                int seatedId = ReadMindId(MindField?.GetValue(voyage), assignIfMissing: true);
                if (seatedId != 0)
                    return seatedId;
            }

            return BoundIdField?.GetValue(voyage) is int boundId ? boundId : 0;
        }

        /// <summary>
        /// 人格核心还在世界上就返回 true。飞船电脑核心被收起，或正在随逆重飞船转移时，人格芯片仍留在这栋飞船电脑核心里，也算还在世界上。
        /// </summary>
        public static bool MindStillExists(int id)
        {
            if (id == 0 || !IsLoaded)
                return false;
            if (StillInTheWorldMethod != null && StillInTheWorldMethod.Invoke(null, new object[] { id }) is bool alive && alive)
                return true;
            if (SeatedCoreHasMind(id))
                return true;
            if (GravshipStillCarriesMind(id))
                return true;

            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                foreach (Thing thing in caravan.AllThings)
                {
                    if (ThingCarriesMind(thing, id))
                        return true;
                }
            }

            foreach (WorldObject worldObject in Find.WorldObjects.AllWorldObjects)
            {
                if (worldObject is not TravellingTransporters transporters)
                    continue;
                foreach (Pawn pawn in transporters.Pawns)
                {
                    if (PawnCarriesMind(pawn, id))
                        return true;
                }

                ThingOwner held = transporters.GetDirectlyHeldThings();
                if (held == null)
                    continue;
                for (int i = 0; i < held.Count; i++)
                {
                    if (ThingCarriesMind(held[i], id))
                        return true;
                }
            }

            return false;
        }

        public static bool ChipSeated(Thing core)
        {
            if (ChipField == null || core == null)
                return false;

            ThingComp voyage = VoyageComp(core);
            return voyage != null && ChipField.GetValue(voyage) is bool seated && seated;
        }

        public static void ClaimHost(CompShipCorePersona comp)
        {
            if (HostField == null || comp?.parent == null || comp.Persona == null)
                return;

            ThingComp voyage = VoyageComp(comp.parent);
            if (voyage == null)
                return;

            Pawn previous = HostField.GetValue(voyage) as Pawn;
            if (previous != null && previous != comp.Persona)
            {
                CorePersonaUtility.TransferMechs(previous, comp.Persona);
                if (!previous.Destroyed && !previous.Spawned && !CorePersonaUtility.IsPersona(previous))
                    previous.Destroy(DestroyMode.Vanish);
            }

            HostField.SetValue(voyage, comp.Persona);
        }

        public static void ClearHost(Thing core, Pawn persona)
        {
            if (HostField == null || core == null || persona == null)
                return;

            ThingComp voyage = VoyageComp(core);
            if (voyage == null)
                return;
            if (HostField.GetValue(voyage) == persona)
                HostField.SetValue(voyage, null);
        }

        private static readonly FieldInfo GravshipPawnsField = AccessTools.Field(typeof(Gravship), "pawns");
        private static readonly FieldInfo GravshipThingsField = AccessTools.Field(typeof(Gravship), "things");
        private static readonly FieldInfo TravelingGravshipField = AccessTools.Field(typeof(WorldComponent_GravshipController), "gravship");

        /// <summary>
        /// 起飞时她会被算进飞船的人物名单。专属人物池已经收下她，名单里不能再留一份。
        /// </summary>
        public static void DropFromTravelingGravship(Pawn pawn)
        {
            if (pawn == null)
                return;

            Gravship ship = Current.Game?.Gravship;
            if (ship == null || !ship.ContainsPawn(pawn))
                return;

            if (GravshipPawnsField?.GetValue(ship) is System.Collections.IDictionary pawns)
                pawns.Remove(pawn);
        }

        public static void PostfixChipLeft(ThingComp __instance)
        {
            if (__instance?.parent == null || ChipSeated(__instance.parent))
                return;

            __instance.parent.TryGetComp<CompShipCorePersona>()?.DeactivatePersona();
        }

        /// <summary>
        /// Gravship Voyage 把「取出人格核心」挂在机械师身上。飞船主脑不提供「取出人格核心」。人格芯片只从飞船电脑核心取出。
        /// </summary>
        public static void PostfixNoExtractFromPersona(ThingComp __instance, ref System.Collections.Generic.IEnumerable<FloatMenuOption> __result)
        {
            if (__instance?.parent is Pawn pawn && CorePersonaUtility.IsPersona(pawn))
                __result = System.Array.Empty<FloatMenuOption>();
        }

        public static bool PrefixWake(ThingComp __instance)
        {
            CompShipCorePersona comp = __instance?.parent?.TryGetComp<CompShipCorePersona>();
            if (comp == null)
                return true;

            comp.EnsurePersona();
            ClaimHost(comp);
            return comp.Persona == null;
        }

        public static void PostfixAttach(ThingComp __instance)
        {
            CompShipCorePersona comp = __instance?.parent?.TryGetComp<CompShipCorePersona>();
            if (comp?.Persona == null)
                return;

            CorePersonaUtility.RetireAutoMechlink(comp.Persona);
        }

        public static void PrefixRememberMechGroups(Hediff __instance)
        {
            if (CorePersonaUtility.IsPersona(__instance?.pawn))
                Hediff_ShipCorePersonaBound.Remember(__instance.pawn);
        }

        public static void PrefixExpose(ThingComp __instance, ref Pawn __state)
        {
            if (HostField == null || Scribe.mode != LoadSaveMode.Saving)
                return;

            if (HostField.GetValue(__instance) is Pawn host && CorePersonaUtility.IsPersona(host))
            {
                __state = host;
                HostField.SetValue(__instance, null);
            }
        }

        public static void PostfixExpose(ThingComp __instance, Pawn __state)
        {
            if (HostField == null)
                return;

            if (__state != null)
                HostField.SetValue(__instance, __state);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            CompShipCorePersona comp = __instance?.parent?.TryGetComp<CompShipCorePersona>();
            ClaimHost(comp);
        }

        private static bool SeatedCoreHasMind(int id)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Ship_ComputerCore");
            if (def == null)
                return false;

            foreach (Map map in Find.Maps)
            {
                List<Thing> cores = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < cores.Count; i++)
                {
                    if (ChipSeated(cores[i]) && BoundMindId(cores[i]) == id)
                        return true;
                }

                List<Thing> minified = map.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing);
                for (int i = 0; i < minified.Count; i++)
                {
                    if (minified[i] is not MinifiedThing mini || mini.InnerThing == null || mini.InnerThing.def != def)
                        continue;
                    if (ChipSeated(mini.InnerThing) && BoundMindId(mini.InnerThing) == id)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 逆重飞船转移时，飞船电脑核心在飞船物体里，不在地图上。
        /// </summary>
        private static bool GravshipStillCarriesMind(int id)
        {
            return GravshipHolds(core => SeatedMindId(core) == id);
        }

        public static bool GravshipHoldsBuilding(int thingId)
        {
            if (thingId == 0)
                return false;
            return GravshipHolds(thing => thing != null && !thing.Destroyed && thing.thingIDNumber == thingId);
        }

        private static bool GravshipHolds(System.Func<Thing, bool> match)
        {
            bool found = false;
            VisitGravships(ship =>
            {
                if (found || GravshipThingsField?.GetValue(ship) is not System.Collections.IDictionary things)
                    return;
                foreach (object key in things.Keys)
                {
                    if (key is Thing thing && HoldsMatch(thing, match))
                    {
                        found = true;
                        return;
                    }
                }
            });
            return found;
        }

        private static bool HoldsMatch(Thing thing, System.Func<Thing, bool> match)
        {
            if (thing != null && match(thing))
                return true;
            return thing is MinifiedThing mini && mini.InnerThing != null && match(mini.InnerThing);
        }

        private static void VisitGravships(System.Action<Gravship> visit)
        {
            Gravship current = Current.Game?.Gravship;
            Gravship traveling = TravelingGravshipField?.GetValue(Find.World?.GetComponent<WorldComponent_GravshipController>()) as Gravship;
            if (current != null && !current.Destroyed)
                visit(current);
            if (traveling != null && traveling != current && !traveling.Destroyed)
                visit(traveling);
            if (Find.WorldObjects == null)
                return;

            List<WorldObject> worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i] is not Gravship ship || ship == current || ship == traveling || ship.Destroyed)
                    continue;
                visit(ship);
            }
        }

        private static int SeatedMindId(Thing core)
        {
            if (!ChipSeated(core))
                return 0;

            ThingComp voyage = VoyageComp(core);
            int id = ReadMindId(MindField?.GetValue(voyage), assignIfMissing: false);
            if (id != 0)
                return id;
            return BoundIdField?.GetValue(voyage) is int boundId ? boundId : 0;
        }

        private static bool ThingCarriesMind(Thing thing, int id)
        {
            if (thing == null)
                return false;
            if (thing is Pawn pawn && PawnCarriesMind(pawn, id))
                return true;
            if (thing is MinifiedThing mini && ThingCarriesMind(mini.InnerThing, id))
                return true;
            if (ChipCompType == null || ChipCompMind == null || thing is not ThingWithComps withComps)
                return false;

            for (int i = 0; i < withComps.AllComps.Count; i++)
            {
                ThingComp comp = withComps.AllComps[i];
                if (!ChipCompType.IsInstanceOfType(comp))
                    continue;
                if (ReadMindId(ChipCompMind.GetValue(comp), assignIfMissing: false) == id)
                    return true;
            }

            return false;
        }

        private static bool PawnCarriesMind(Pawn pawn, int id)
        {
            if (pawn?.health?.hediffSet == null)
                return false;
            if (ChipHediffType != null && ChipHediffMind != null)
            {
                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (!ChipHediffType.IsInstanceOfType(hediffs[i]))
                        continue;
                    if (ReadMindId(ChipHediffMind.GetValue(hediffs[i]), assignIfMissing: false) == id)
                        return true;
                }
            }

            if (pawn.inventory?.innerContainer == null)
                return false;
            for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
            {
                if (ThingCarriesMind(pawn.inventory.innerContainer[i], id))
                    return true;
            }

            return pawn.carryTracker != null && ThingCarriesMind(pawn.carryTracker.CarriedThing, id);
        }

        private static int ReadMindId(object mind, bool assignIfMissing)
        {
            if (mind == null)
                return 0;
            if (!assignIfMissing)
                return mind.GetType().GetField("id")?.GetValue(mind) is int raw ? raw : 0;
            return mind.GetType().GetProperty("Id")?.GetValue(mind) is int id ? id : 0;
        }

        private static ThingComp VoyageComp(Thing thing)
        {
            if (CoreType == null || thing is not ThingWithComps withComps)
                return null;

            for (int i = 0; i < withComps.AllComps.Count; i++)
            {
                ThingComp comp = withComps.AllComps[i];
                if (CoreType.IsInstanceOfType(comp))
                    return comp;
            }

            return null;
        }
    }
}
