using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 重型地面和逆重飞船基架都能放飞船电脑核心。
    /// 原版的地面要求只能填一种，所以改在这里同时判断这两种地面。
    /// 这次只判断地面能不能放飞船电脑核心。连上逆重飞船，要看整座飞船电脑核心是不是都在这艘船的基架上。
    /// </summary>
    public class PlaceWorker_ShipComputerCore : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            foreach (IntVec3 cell in GenAdj.OccupiedRect(loc, rot, checkingDef.Size))
            {
                if (!cell.InBounds(map))
                    return "ShipCorePersona_NeedsHeavyOrSubstructure".Translate();

                if (!CellAcceptsCore(cell, map))
                    return "ShipCorePersona_NeedsHeavyOrSubstructure".Translate();
            }

            return true;
        }

        public override IEnumerable<TerrainAffordanceDef> DisplayAffordances()
        {
            yield return TerrainAffordanceDefOf.Heavy;

            TerrainAffordanceDef substructure = DefDatabase<TerrainAffordanceDef>.GetNamedSilentFail("Substructure");
            if (substructure != null)
                yield return substructure;
        }

        private static bool CellAcceptsCore(IntVec3 cell, Map map)
        {
            if (cell.GetAffordances(map).Contains(TerrainAffordanceDefOf.Heavy))
                return true;

            TerrainDef surface = map.terrainGrid.TerrainAt(cell);
            if (surface != null && surface.IsSubstructure)
                return true;

            // 基架上再铺普通地板时，地表不再是基架，下面的地基仍是基架。
            TerrainDef foundation = map.terrainGrid.FoundationAt(cell);
            return foundation != null && foundation.IsSubstructure;
        }
    }
}
