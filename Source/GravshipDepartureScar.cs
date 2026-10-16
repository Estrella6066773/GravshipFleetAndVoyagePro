using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.BaseGen;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 多艘逆重飞船「各飞各的」：起飞不再必然摧毁地图。
    ///
    /// 原版唯一的判据是 WorldComponent_GravshipController.mapHasGravAnchor，
    /// 它在 InitiateTakeoff 时由 map.listerThings.AnyThingWithDef(ThingDefOf.GravAnchor) 定下：
    ///
    ///     if (!mapHasGravAnchor) GravshipUtility.AbandonMap(map);
    ///     else GravshipUtility.UpdateBillDestinations(map);
    ///     GravshipUtility.TravelTo(gravship, takeoffTile, landingTile);
    ///
    /// 也就是说一张地图上停着几艘船完全不影响这个决定，单独起飞一艘，地图照样被弃。
    /// Gravship Fleet 也没有改这里：它的 launch.anchored 就是同一个锚判定，
    /// 然后在自己的 Depart 里照抄一遍同样的分支（anchored 走 UpdateBillDestinations，否则 AbandonMap）。
    /// 所以弃图的收口只有一个方法：GravshipUtility.AbandonMap。补它一处，两条流程都覆盖。
    ///
    /// 本模组在这里加上「第二台逆重引擎也能把地面钉住」这一档，优先级如下：
    ///
    /// 1. 地图上有逆重锚 —— 原版行为，地图保留且不留任何破坏（最高优先级，轮不到我们插手）。
    /// 2. 没有锚，但地图上还有另一台己方逆重引擎在运转 —— 地图保留；
    ///    起飞点被连根撕开：原本的基架上隆起厚岩顶，随后塌落成岩石。
    /// 3. 两者都没有（例如整队最后一艘离开）—— 按原版弃置地图。
    ///
    /// 判定用的是「GenerateGravship 已经把这台引擎移走之后，地图上是否还有别的己方逆重引擎」，
    /// 不需要额外状态，而且天然覆盖整队依次起飞：Fleet 是一艘一艘走的，最后一艘离开时地图上不再有引擎，
    /// 于是自动落回原版弃图。
    ///
    /// 注意保留地图意味着 AbandonMap 里那段处理也不再执行：
    /// 地图上没上船的殖民者不会被 Notify_LeftBehind + DestroyOrPassToWorld 抹掉，原图那片殖民地继续存在。
    /// 这正是「各飞各的」该有的样子。
    /// </summary>
    [RequiresFleet]
    [HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.GenerateGravship))]
    public static class Patch_GenerateGravship_StashFootprint
    {
        /// <summary>
        /// 引擎被移走之前把它的有效基架抄进地图组件。等到 AbandonMap 时引擎已经不在了，抄不到。
        /// </summary>
        public static void Prefix(Building_GravEngine engine)
        {
            engine?.Map?.GetComponent<MapComponent_GravshipScar>()?.StashTakeoff(engine);
        }
    }

    [RequiresFleet]
    [HarmonyPatch(typeof(GravshipUtility), nameof(GravshipUtility.AbandonMap))]
    public static class Patch_AbandonMap_KeepMapWhileAnotherEngineRemains
    {
        /// <summary>
        /// 排在前面：先由我们判断要不要保留地图。我们放行时（return true）后面的补丁照常跑，
        /// 包括 Gravship Voyage 在远航期间拦下弃图的那一个，它的行为不受影响。
        /// </summary>
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Map map)
        {
            MapComponent_GravshipScar scar = map?.GetComponent<MapComponent_GravshipScar>();
            if (scar == null || !scar.TryConsumeTakeoff(out List<IntVec3> footprint, out int engineId))
            {
                // 不是一次逆重起飞（或者记录已过期），交给原版。
                return true;
            }
            if (!GravshipFleetProMod.RetainMapWhileAnotherEngineRemains)
            {
                return true;
            }
            if (!AnotherEngineRemains(map, engineId))
            {
                // 没有第二台引擎：原版弃图，本模组不干预。
                return true;
            }

            // 有另一台引擎把地面钉住。原版在有锚时走的就是 UpdateBillDestinations，这里照做。
            GravshipUtility.UpdateBillDestinations(map);
            RaiseScarOverFootprint(map, footprint, scar);
            Messages.Message("GravshipFleetPro_MapRetained".Translate(), MessageTypeDefOf.PositiveEvent, historical: false);

            // 拦掉弃图。注意此处返回 false 只跳过 AbandonMap 自己的方法体，
            // 调用方 TakeoffEnded / Fleet 的 Depart 仍会继续执行 TravelTo，飞船照常飞走。
            return false;
        }

        /// <summary>
        /// 除这次起飞的那台之外，地图上是否还有己方逆重引擎。
        /// 引擎在 GenerateGravship 里已经被移走，所以正常情况下列表里本来就不含它；
        /// 仍然按编号排除一次，免得日后调用顺序变化时把本次起飞的引擎也算进来。
        /// </summary>
        private static bool AnotherEngineRemains(Map map, int departedEngineId)
        {
            if (map?.listerBuildings == null)
            {
                return false;
            }
            foreach (Building_GravEngine engine in map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>())
            {
                if (engine.thingIDNumber != departedEngineId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 在飞走的基架上留下塌陷：地表换成这一带的天然岩层，铺厚岩顶，然后安排延迟塌方。
        ///
        /// 只铺屋顶、不填岩墙是有意为之：屋顶不受支撑，延迟到点后走原版塌方流程掉下 CollapsedRocks，
        /// 也就是「山体自己塌出岩石墙」的破坏感。CollapsedRocks 是 RockBase 的子类，本身托举屋顶，
        /// 所以塌过一次之后这片区域会稳定下来，不会反复塌。
        /// </summary>
        private static void RaiseScarOverFootprint(Map map, List<IntVec3> footprint, MapComponent_GravshipScar scar)
        {
            // 岩层按地块的区域岩石取，和地图本身的岩层一致。
            TerrainDef rock = BaseGenUtility.RegionalRockTerrainDef(map.Tile, beautiful: false) ?? TerrainDefOf.Gravel;

            for (int i = 0; i < footprint.Count; i++)
            {
                IntVec3 c = footprint[i];
                if (!c.InBounds(map))
                {
                    continue;
                }
                map.terrainGrid.SetTerrain(c, rock);
                map.roofGrid.SetRoof(c, RoofDefOf.RoofRockThick);

                // RoofGrid.SetRoof 不碰这两个区域标记，照 GenerateGravship 的做法清掉，
                // 否则格子可能还带着「禁止屋顶」的标记。
                int index = map.cellIndices.CellToIndex(c);
                map.areaManager.BuildRoof[index] = false;
                map.areaManager.NoRoof[index] = false;
                map.pathing.RecalculatePerceivedPathCostAt(c);
            }
            map.roofGrid.Drawer.SetDirty();

            scar.Schedule(footprint, GravshipFleetProMod.CollapseDelayTicks);
        }
    }
}
