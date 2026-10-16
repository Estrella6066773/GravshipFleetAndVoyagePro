using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 本模组用到的定义。
    /// </summary>
    [DefOf]
    public static class ShipCorePersonaDefOf
    {
        public static PawnKindDef ShipCorePersona;
        public static HediffDef ShipCorePersonaBound;

        // 核心人格。基因列表是空的。图标是异种人编辑器新建时默认选中的 XenotypeIconDef Basic。
        public static XenotypeDef ShipCorePersonaXenotype;
        public static JobDef StoreShipCoreImplant;
        public static JobDef RetrieveShipCoreImplant;

        static ShipCorePersonaDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ShipCorePersonaDefOf));
        }
    }
}
