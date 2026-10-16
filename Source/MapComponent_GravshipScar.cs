using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 逆重起飞留痕的执行器与暂存处。每张地图一个实例，由 Map.FillComponents 自动创建，
    /// 不需要手动注册；新增这个组件类型对旧存档也安全，因为地图组件是每次读档时按 def 重建的。
    ///
    /// 它管两件事：
    ///
    /// 一、起飞记录。GenerateGravship 把引擎搬走之前，把它的有效基架抄在这里；紧接着的
    ///     AbandonMap 再来取。抄的时候引擎还在、AbandonMap 的时候引擎已经不在了，所以必须留一手。
    ///     记录随存档走：Gravship Fleet 的起飞是分步带延迟的流程，中途存档再读回来，
    ///     纯内存的记录会丢，这里不会。
    ///
    /// 二、延迟塌方。飞走的那块基架上先立起厚岩顶，隔一小段时间才塌落。这个延迟不是装饰：
    ///     原版只在「屋顶支撑物增删」的时候才重新判定屋顶支撑（RoofCollapseCellsFinder），
    ///     光铺一片厚岩顶它不会自己塌，所以塌方必须由我们推一把。
    ///
    /// 到点后逐个格子调用 RoofCollapseBuffer.MarkToCollapse，剩下的交给原版流程收尾：
    /// 下一 tick 的 Map.MapPreTick → RoofCollapseBufferResolver → RoofCollapserImmediate.DropRoofInCells，
    /// 掉下 CollapsedRocks、播 Roof_Collapse 音效、砸到东西时再发原版的「屋顶塌方」信。
    /// CollapsedRocks 是 RockBase 的子类，本身托举屋顶，所以塌过一次之后这片区域就稳定了。
    /// </summary>
    public class MapComponent_GravshipScar : MapComponent
    {
        /// <summary>起飞流程走完（含过场动画）通常远不到 600 tick，留一倍余量。</summary>
        private const int ExpireTicks = 1200;

        private List<IntVec3> takeoffFootprint;
        private int takeoffTick = -1;
        private int takeoffEngineId = -1;

        private List<IntVec3> pendingCells = new List<IntVec3>();
        private int collapseAtTick = -1;

        public MapComponent_GravshipScar(Map map)
            : base(map)
        {
        }

        public bool HasPending => collapseAtTick >= 0;

        /// <summary>
        /// 引擎被搬走之前把它的有效基架抄下来。同样的地图连着起飞两艘船时，后一次的记录覆盖前一次——
        /// 前一次的记录此刻已经被 AbandonMap 取走了，不会撞上。
        /// </summary>
        public void StashTakeoff(Building_GravEngine engine)
        {
            if (engine == null || engine.Map != map || engine.ValidSubstructure == null)
            {
                return;
            }
            takeoffFootprint = engine.ValidSubstructure.ToList();
            takeoffTick = Find.TickManager.TicksGame;
            takeoffEngineId = engine.thingIDNumber;
        }

        /// <summary>
        /// 取走这次起飞记录，取走即清空，免得一份过期记录误伤后来的弃图。
        /// 记录里的引擎编号用来在判断「还有别的引擎吗」时把本次起飞的引擎排除掉。
        /// </summary>
        public bool TryConsumeTakeoff(out List<IntVec3> footprint, out int engineId)
        {
            footprint = takeoffFootprint;
            engineId = takeoffEngineId;
            int tick = takeoffTick;

            takeoffFootprint = null;
            takeoffTick = -1;
            takeoffEngineId = -1;

            if (footprint.NullOrEmpty() || tick < 0)
            {
                return false;
            }
            return Find.TickManager.TicksGame - tick <= ExpireTicks;
        }

        /// <summary>
        /// 安排一次延迟塌方。
        ///
        /// 重复调用会累加到同一批上，而不是覆盖：编队一起起飞时，Gravship Fleet 对每一艘船
        /// 各调用一次 AbandonMap，于是同一张地图上会连着排好几次留痕。覆盖掉前一批会让先前
        /// 那块的厚岩顶永远立着不塌（没有新变化，原版不会重新判定屋顶支撑）。
        /// 到点时间取最早的那个，所有排进来的格子在同一波里塌掉。
        /// </summary>
        public void Schedule(List<IntVec3> cells, int delayTicks)
        {
            if (cells == null || cells.Count == 0)
            {
                return;
            }
            pendingCells.AddRange(cells);
            int at = Find.TickManager.TicksGame + UnityEngine.Mathf.Max(0, delayTicks);
            if (collapseAtTick < 0 || at < collapseAtTick)
            {
                collapseAtTick = at;
            }
        }

        public override void MapComponentTick()
        {
            if (collapseAtTick < 0 || Find.TickManager.TicksGame < collapseAtTick)
            {
                return;
            }
            List<IntVec3> cells = pendingCells;
            pendingCells = new List<IntVec3>();
            collapseAtTick = -1;

            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 c = cells[i];
                // 只标记当前确实有屋顶的格子，避免把已经塌掉的格子重复送进流程。
                if (c.InBounds(map) && c.Roofed(map))
                {
                    map.roofCollapseBuffer.MarkToCollapse(c);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref takeoffFootprint, "takeoffFootprint", LookMode.Value);
            Scribe_Values.Look(ref takeoffTick, "takeoffTick", -1);
            Scribe_Values.Look(ref takeoffEngineId, "takeoffEngineId", -1);

            Scribe_Collections.Look(ref pendingCells, "pendingCells", LookMode.Value);
            Scribe_Values.Look(ref collapseAtTick, "collapseAtTick", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && pendingCells == null)
            {
                pendingCells = new List<IntVec3>();
            }
        }
    }
}
