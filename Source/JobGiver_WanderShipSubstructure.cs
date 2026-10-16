using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShipCorePersona
{
    /// <summary>
    /// 保卫模式（工作／充电／保卫里的「保卫」）下，飞船主脑的机械族不再围着主脑那几格打转，
    /// 改在自己所在那艘逆重飞船的有效基架上待命——也就是起飞时会被一起带走的那片基架。
    /// 走到哪儿都行，但落点必须是基架上的格子，于是飞船一起飞它们就跟着走。
    ///
    /// 挂靠：Core 的 ThinkTreeDef「Mechanoid」，保卫模式那段的 JobGiver_AIDefendOverseer 之后。
    /// 位置在 AIDefendOverseer 之后、AIFollowOverseer 之前，所以开火的判断仍然优先；
    /// 只在飞船主脑有船可站时才接管走位，其余情况返回无效格，交给后面原版的跟随与游荡。
    /// </summary>
    public class JobGiver_WanderShipSubstructure : JobGiver_Wander
    {
        private const int SampleTries = 30;

        private static readonly List<IntVec3> ShipCells = new List<IntVec3>();

        public JobGiver_WanderShipSubstructure()
        {
            // 落点由 GetExactWanderDest 直接从基架格里挑，这里的半径不参与挑选；
            // 留一个正值，免得别处按半径判断时拿到 0。
            wanderRadius = 6f;
            ticksBetweenWandersRange = new IntRange(125, 200);
        }

        // 基架格直接由 GetExactWanderDest 给出，这个根只在原版拿它算「超出半径就快走」时才会用到。
        protected override IntVec3 GetWanderRoot(Pawn pawn)
        {
            return pawn.Position;
        }

        protected override IntVec3 GetExactWanderDest(Pawn pawn)
        {
            Map map = pawn.Map;
            Pawn overseer = pawn.GetOverseer();
            if (map == null || overseer == null)
                return IntVec3.Invalid;

            if (!PersonaCommand.TryCollectShipCells(overseer, ShipCells))
                return IntVec3.Invalid;

            Danger danger = PawnUtility.ResolveMaxDanger(pawn, base.maxDanger);
            int count = ShipCells.Count;
            for (int i = 0; i < SampleTries; i++)
            {
                IntVec3 cell = ShipCells[Rand.Range(0, count)];
                if (IsStandableShipCell(pawn, cell, danger))
                    return cell;
            }

            // 抽样没挑到：退到离她最近的那格基架附近再找一块能站的。
            IntVec3 nearest = IntVec3.Invalid;
            float nearestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float dist = pawn.Position.DistanceToSquared(ShipCells[i]);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = ShipCells[i];
                }
            }

            if (nearest.IsValid
                && CellFinder.TryFindRandomCellNear(nearest, map, 5, cell => IsStandableShipCell(pawn, cell, danger), out IntVec3 found))
            {
                return found;
            }

            return IntVec3.Invalid;
        }

        // 基架格能不能站。判定照着原版 RCellFinder.CanWanderToCell 的要害抄：能走、没被禁用、站得下、走得到、不是已知的陷阱。
        private static bool IsStandableShipCell(Pawn pawn, IntVec3 cell, Danger danger)
        {
            Map map = pawn.Map;
            return cell.InBounds(map)
                && cell.WalkableBy(map, pawn)
                && !cell.IsForbidden(pawn)
                && cell.Standable(map)
                && !PawnUtility.KnownDangerAt(cell, map, pawn)
                && pawn.CanReach(cell, PathEndMode.OnCell, danger);
        }

        protected override void DecorateGotoJob(Job job)
        {
            // 和原版的 JobGiver_WanderOverseer 一致：任务挂 120 tick，附近有敌人就提前重算。
            // 任务说明换成自己的词条，选中机械族时能看出它在守船，而不是原版的「护卫某人」。
            job.expiryInterval = 120;
            job.expireRequiresEnemiesNearby = true;
            job.reportStringOverride = "ShipCorePersona_GuardShipSubstructure".Translate();
        }
    }
}
