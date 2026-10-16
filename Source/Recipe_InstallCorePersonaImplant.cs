using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 用手术给飞船主脑安装机械师植入物。
    /// 标准控制子链加到 3 级为止；高级控制子链从 3 级加到上限。规则与原版自行安装相同。
    /// </summary>
    public class Recipe_InstallCorePersonaImplant : Recipe_Surgery
    {
        private const int StandardSublinkCap = 3;

        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (thing is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return false;
            BodyPartRecord brain = CorePersonaUtility.BrainOf(pawn);
            if (part != null && brain != null && part != brain)
                return false;
            if (recipe.addsHediff == null)
                return false;

            return CanInstallAnother(pawn);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (!CanInstallAnother(pawn))
                return;

            if (part == null)
                part = CorePersonaUtility.BrainOf(pawn);

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(recipe.addsHediff);
            if (existing is Hediff_Level level)
                level.SetLevelTo(level.level + 1);
            else
                pawn.health.AddHediff(HediffMaker.MakeHediff(recipe.addsHediff, pawn, part), part);

            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn, false);
            pawn.mechanitor?.Notify_BandwidthChanged();
        }

        private bool CanInstallAnother(Pawn pawn)
        {
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(recipe.addsHediff);
            int level = existing is Hediff_Level leveled ? leveled.level : 0;

            if (recipe.defName == "ShipCorePersona_InstallControlSublink")
                return level < StandardSublinkCap;
            if (recipe.defName == "ShipCorePersona_InstallControlSublinkHigh")
                return level >= StandardSublinkCap && level < (int)recipe.addsHediff.maxSeverity;

            int cap = (int)recipe.addsHediff.maxSeverity;
            if (cap <= 0)
                cap = 1;
            return level < cap;
        }
    }
}
