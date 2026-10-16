using HarmonyLib;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 逆重设施与逆重引擎的连接判定修复（一张地图上有多台逆重引擎时）。
    ///
    /// 原版 CompGravshipFacility.CanBeActive 遍历地图上所有己方逆重引擎：
    ///
    ///     foreach (Building_GravEngine tmpEngine in tmpEngines)
    ///     {
    ///         if (!parent.Position.InHorDistOf(tmpEngine.Position, Props.maxDistance))
    ///         {
    ///             return false;          // ← 只要遇到一台超出距离的引擎就否决整个判定
    ///         }
    ///         if (Props.onlyRequiresLooseConnection &amp;&amp; tmpEngine.LooselyConnectedToGravEngine(parent)) return true;
    ///         if (tmpEngine.OnValidSubstructure(parent)) return true;
    ///     }
    ///
    /// 一张地图上停着两艘以上逆重飞船时，船之间必然有引擎落在 maxDistance 之外，
    /// 于是每艘船上的燃料箱、推进器、飞船控制台等设施统统被判为「未与逆重引擎连接」，
    /// 燃料不会补充、推进器不提供航程、飞船控制台不能发射。
    ///
    /// 原版意图应当是「与任意一台在范围内的引擎相连即可」：距离不够的引擎应当跳过，
    /// 而不是否决整个判定。因此这里用 postfix 只纠正「原版误判为不可用」的情形：
    ///
    /// - 原版已判为可用 → 原样放行，不重复计算。
    /// - 原版判为不可用 → 只有在我们能确认它确实连着某台在范围内的引擎时才翻回 true。
    /// - 断电时原版会在基类 CompFacility.CanBeActive 里直接返回 false，这里不动，
    ///   保持原版的断电语义（base.CanBeActive 只做断电检查，所以断电必须单独排除）。
    ///
    /// 判定范围与引擎侧的两个方法都沿用原版，只把「跳过超距引擎」这一处改掉。
    /// </summary>
    /// 一张地图上出现第二台逆重引擎才会碰到这个误判，所以它跟着舰队一半走。
    [RequiresFleet]
    [HarmonyPatch(typeof(CompGravshipFacility), nameof(CompGravshipFacility.CanBeActive), MethodType.Getter)]
    public static class Patch_GravshipFacility_CanBeActive
    {
        public static void Postfix(CompGravshipFacility __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }
            if (__instance == null || !ModsConfig.OdysseyActive)
            {
                return;
            }
            Thing parent = __instance.parent;
            Map map = parent?.Map;
            if (map == null)
            {
                return;
            }

            // 断电：原版基类判定为假，不能替它翻案。
            CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
            {
                return;
            }

            __result = ConnectedToAnyEngineInRange(__instance, parent, map);
        }

        /// <summary>
        /// 原版判定去掉「超距即否决」之后剩下的部分：
        /// 只要有一台在范围内的引擎与父物体松散连接，或父物体落在那台引擎的有效基架上，就算连上。
        /// </summary>
        private static bool ConnectedToAnyEngineInRange(CompGravshipFacility facility, Thing parent, Map map)
        {
            CompProperties_GravshipFacility props = facility.Props;
            foreach (Building_GravEngine engine in map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>())
            {
                if (!parent.Position.InHorDistOf(engine.Position, props.maxDistance))
                {
                    continue;
                }
                if (props.onlyRequiresLooseConnection && engine.LooselyConnectedToGravEngine(parent))
                {
                    return true;
                }
                if (engine.OnValidSubstructure(parent))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
