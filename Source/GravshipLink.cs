using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 把飞船电脑核心登记进逆重引擎可以连接的设施。
    /// 遍历会连接逆重设施的引擎，不把引擎的 defName 写死。定义加载完才能调用这次登记。
    /// </summary>
    public static class GravshipEngineWhitelist
    {
        public static void Register()
        {
            if (!ModsConfig.OdysseyActive)
                return;

            ThingDef core = DefDatabase<ThingDef>.GetNamedSilentFail("Ship_ComputerCore");
            if (core == null)
                return;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.comps == null)
                    continue;

                bool engineClass = def.thingClass != null && typeof(Building_GravEngine).IsAssignableFrom(def.thingClass);
                foreach (CompProperties comp in def.comps)
                {
                    if (comp is CompProperties_AffectedByFacilities affected)
                        TryAddCore(affected, engineClass, core);
                }
            }
        }

        private static void TryAddCore(CompProperties_AffectedByFacilities affected, bool engineClass, ThingDef core)
        {
            bool linksGravFacility = false;
            if (affected.linkableFacilities != null)
            {
                for (int i = 0; i < affected.linkableFacilities.Count; i++)
                {
                    ThingDef facility = affected.linkableFacilities[i];
                    if (facility != null && facility.GetCompProperties<CompProperties_GravshipFacility>() != null)
                    {
                        linksGravFacility = true;
                        break;
                    }
                }
            }

            // 既不是逆重引擎、也不连接任何逆重设施的建筑，不要把飞船电脑核心加进它的可连接列表。
            if (!engineClass && !linksGravFacility)
                return;

            if (affected.linkableFacilities == null)
                affected.linkableFacilities = new List<ThingDef>();
            if (!affected.linkableFacilities.Contains(core))
                affected.linkableFacilities.Add(core);
        }
    }

    /// <summary>
    /// 整座飞船电脑核心都在那艘船的基架上，才算连上这艘逆重飞船。
    /// 原版碰到第一台距离不够的引擎，就会直接判定这台飞船电脑核心不能连上逆重引擎。这里要跳过那台引擎，继续看下一台。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(CompGravshipFacility), "get_CanBeActive")]
    public static class Patch_ShipComputerCoreLinksOnlyOnItsFloor
    {
        public static bool Prefix(CompGravshipFacility __instance, ref bool __result)
        {
            if (__instance?.parent?.def?.defName != "Ship_ComputerCore")
                return true;

            __result = false;
            if (!ModsConfig.OdysseyActive)
                return false;

            Thing parent = __instance.parent;
            CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
                return false;

            CompBreakdownable breakdown = parent.TryGetComp<CompBreakdownable>();
            if (breakdown != null && breakdown.BrokenDown)
                return false;

            Map map = parent.Map;
            if (map == null)
                return false;

            float maxDistance = __instance.Props.maxDistance;
            foreach (Building_GravEngine engine in map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>())
            {
                if (!parent.Position.InHorDistOf(engine.Position, maxDistance))
                    continue;
                if (engine.OnValidSubstructure(parent))
                {
                    __result = true;
                    return false;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 认主：飞船电脑核心只连自己那艘船的逆重引擎，别人的引擎连不上它。
    ///
    /// 设施与逆重引擎怎么连，由引擎的 CompAffectedByFacilities 说了算，两个方向都要先问它一句 CanLinkTo：
    /// 引擎侧 LinkToNearbyFacilities 问一次，核心侧 CompFacility.LinkToNearbyBuildings 也问一次。
    /// 而它原本只看距离、视线和 maxSimultaneous，电脑核心的 maxDistance 又是 500：
    /// 同一张地图上停着两艘船时，两台引擎都会把对方的电脑核心算成自己的设施。
    ///
    /// 代价落在 CompGravshipFacility.engine 上。这个字段由 Notify_LinkAdded 里「谁先连上来」写定，
    /// 却又是 CompPilotConsole 判断「这台驾驶台属于哪艘船」的凭据：原版的起飞与选目的地、
    /// Gravship Voyage 的 VoyageConsole.EngineFor、Gravship Fleet 的「哪些人上哪艘船」都读它。
    /// 认错引擎时，从 A 船的电脑核心点起飞，走的是 B 船。引擎侧的 GravshipComponents 又是它的
    /// AffectedByFacilities.LinkedFacilitiesListForReading，别人的核心留在自己名下，
    /// 缺件判定、信号干扰、燃料统计也会把它一并算进去。
    ///
    /// 所以这里在连接这一步就把不属于自己的核心挡掉，只放行「整座核心都落在自己有效基架上」的那台引擎。
    /// 这条判据与 PersonaCommand、Gravship Voyage 的 VoyageShipParts.EngineUnder 一致；
    /// 万一两艘船的基架连成一片、多台引擎都覆盖同一台核心，取距离最近的那台，保证只有一个答案。
    /// </summary>
    [RequiresVoyage]
    [HarmonyPatch(typeof(CompAffectedByFacilities), nameof(CompAffectedByFacilities.CanLinkTo))]
    public static class Patch_EngineLinksOnlyItsOwnCore
    {
        public static void Postfix(CompAffectedByFacilities __instance, Thing facility, ref bool __result)
        {
            if (!__result || !ShipCoreEngine.IsCore(facility))
                return;
            if (__instance?.parent is not Building_GravEngine engine)
                return;

            __result = ShipCoreEngine.Owns(engine, facility);
        }
    }

    /// <summary>
    /// 认主用的判据：这台飞船电脑核心属于哪台逆重引擎。
    /// 整座核心都落在那台引擎的有效基架上，就算那台引擎的；好几台都满足时取最近的，避免答案随生成顺序变。
    /// </summary>
    internal static class ShipCoreEngine
    {
        public const string CoreDefName = "Ship_ComputerCore";

        public static bool IsCore(Thing thing)
        {
            return thing?.def?.defName == CoreDefName;
        }

        /// <summary>核心整个占地都在这台引擎的有效基架上。</summary>
        public static bool Covers(Building_GravEngine engine, Thing core)
        {
            if (engine == null || core == null || !engine.Spawned || !core.Spawned)
                return false;
            // 这里只做几何判据，不能用 OnValidSubstructure：它读 ValidSubstructure，
            // 会顺带 UpdateSubstructureIfNeeded(regenerateSectionLayers: true)，
            // 在 MapDrawer 里重建 GravshipMask / GravshipHull / SubstructureProps 三层。
            // 读档时 Map.FinalizeInit 跑在 LongEventHandler 的后台线程，CanLinkTo 走到这里，
            // 在非主线程取 Unity 材质会抛「Tried to get a material from a different thread」，
            // 图层重建本身也会失败。认主判定只要知道核心是否落在有效基架上，用不重建图层的集合即可。
            HashSet<IntVec3> substructure = engine.ValidSubstructureNoRegen;
            foreach (IntVec3 cell in core.OccupiedRect())
            {
                if (!substructure.Contains(cell))
                    return false;
            }
            return true;
        }

        /// <summary>这台引擎是这台核心唯一的归属。核心不在地图上、或没有任何引擎覆盖它时都不算。</summary>
        public static bool Owns(Building_GravEngine engine, Thing core)
        {
            if (!Covers(engine, core) || engine.Map == null || engine.Map != core.Map)
                return false;

            float mine = DistanceSquared(engine, core);
            foreach (Building_GravEngine other in engine.Map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>())
            {
                if (other == null || other == engine || !other.Spawned)
                    continue;
                if (!Covers(other, core))
                    continue;
                if (DistanceSquared(other, core) < mine)
                    return false;
            }

            return true;
        }

        private static float DistanceSquared(Building_GravEngine engine, Thing core)
        {
            return (engine.TrueCenter() - core.TrueCenter()).sqrMagnitude;
        }
    }

    /// <summary>
    /// 降落过场的飞船物体已经不在时，原版结束不了这次过场。菜单里的保存和选中后的操作栏会一直不画。
    /// 飞船或引擎数据对不上时，直接结束过场。
    /// </summary>
    [HarmonyPatch(typeof(WorldComponent_GravshipController), "FinalizeInit")]
    public static class Patch_StuckGravshipCutscene
    {
        private static readonly FieldInfo GravshipField = AccessTools.Field(typeof(WorldComponent_GravshipController), "gravship");
        private static readonly FieldInfo CutsceneField = AccessTools.Field(typeof(WorldComponent_GravshipController), "cutsceneInProgress");

        public static void Postfix(WorldComponent_GravshipController __instance, bool fromLoad)
        {
            if (!fromLoad || !WorldComponent_GravshipController.CutsceneInProgress || GravshipReady(__instance))
                return;

            Release(__instance);
        }

        public static bool GravshipReady(WorldComponent_GravshipController controller)
        {
            if (GravshipField?.GetValue(controller) is not Gravship ship)
                return false;
            return ship.Engine != null && ship.Engine.launchInfo != null;
        }

        public static void Release(WorldComponent_GravshipController controller)
        {
            CutsceneField?.SetValue(null, false);
            GravshipField?.SetValue(controller, null);
            if (Find.ScreenshotModeHandler != null)
                Find.ScreenshotModeHandler.Active = false;
            if (Current.Game != null)
                Current.Game.Gravship = null;
        }
    }

    [HarmonyPatch(typeof(WorldComponent_GravshipController), "LandingEnded")]
    public static class Patch_LandingEndedWithoutEngine
    {
        public static bool Prefix(WorldComponent_GravshipController __instance)
        {
            if (Patch_StuckGravshipCutscene.GravshipReady(__instance))
                return true;

            Patch_StuckGravshipCutscene.Release(__instance);
            return false;
        }
    }
}
