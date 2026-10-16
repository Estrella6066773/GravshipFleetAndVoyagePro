using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 生成飞船主脑。幼年背景是人格核心，成年背景是飞船主脑。
    /// 10 点手工写在成年背景上，生成时就会加上，这里不再改手工等级。
    /// 头型和发型交给生成器。性别在生成请求里定为女性。
    /// 体型从「女性」「纤细」「魁梧」里取一个，不取「肥胖」。
    /// 生成请求不带发色、肤色和衣物材料。发色和肤色等生成完再写上。生成完再把她的衣服换成一套合成纤维的裤子和衬衫。
    /// 名字不在这里取。调用方沿用人格核心上已有的名字。
    /// 异种人是核心人格。EnsureXenotype 在生成、读档、从核心取出、再放回去时把她收成这个异种人，并清掉异种基因和系谱基因。
    /// </summary>
    public static class PersonaAppearance
    {
        private static readonly Color HairColor = new Color(0.55f, 0.68f, 0.74f);
        private static readonly Color SkinColor = new Color(0.95f, 0.86f, 0.78f);

        public static Pawn Generate()
        {
            PawnKindDef kind = ShipCorePersonaDefOf.ShipCorePersona;
            var request = new PawnGenerationRequest(kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer);
            request.FixedGender = Gender.Female;
            request.ForceBodyType = BodyTypeWithoutFat();
            request.MustBeCapableOfViolence = false;
            request.AllowDead = false;
            request.AllowDowned = false;
            request.CanGeneratePawnRelations = false;
            request.ForbidAnyTitle = true;
            request.ForceGenerateNewPawn = true;
            request.OnlyUseForcedBackstories = true;
            request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
            if (ModsConfig.BiotechActive)
                request.ForcedXenotype = ShipCorePersonaDefOf.ShipCorePersonaXenotype;
            if (Faction.OfPlayer?.ideos?.PrimaryIdeo != null)
                request.FixedIdeo = Faction.OfPlayer.ideos.PrimaryIdeo;

            Pawn persona = PawnGenerator.GeneratePawn(request);
            EnsureXenotype(persona);
            EnsureIdentity(persona);
            ApplyAppearance(persona);
            ClearInjuriesAndTraits(persona);
            EnsureColonistComponents(persona);
            persona.health.AddHediff(ShipCorePersonaDefOf.ShipCorePersonaBound);
            return persona;
        }

        /// <summary>
        /// 把飞船主脑收成核心人格，并清掉异种基因和系谱基因。不往基因列表里加基因。
        /// 调用处：PersonaAppearance.Generate、CompShipCorePersona.PostSpawnSetup、EnsurePersona（从人物池放回核心）、DeactivatePersona（从核心收进人物池）、GameComponent_PersonaPool.LoadedGame（读档结束）。
        /// Pawn_GeneTracker.SetXenotype 在定义的基因列表为空时仍记下这个异种人，不会改回基线人。
        /// 读档时 Pawn_GeneTracker.ExposeData 只有异种人引用是空的才写成 Baseliner。
        /// PawnGenerator.GenerateGenes 在 SetXenotype 之后仍会补上肤色和发色两种系谱基因，所以这里要再清一次。
        /// 清基因会走到 Notify_GenesChanged，可能改写发色、肤色、体型、头型和技能热情。外观和热情先记下，清完再写回。
        /// 技能等级、背景、机械师身份和植入物不在这里改。
        /// </summary>
        public static void EnsureXenotype(Pawn persona)
        {
            if (!ModsConfig.BiotechActive || persona == null)
                return;

            if (persona.genes == null)
                persona.genes = new Pawn_GeneTracker(persona);

            if (AlreadyCorePersona(persona))
                return;

            Color hair = default;
            Color skinBase = default;
            Color? skinOverride = null;
            BodyTypeDef body = null;
            HeadTypeDef head = null;
            HairDef hairDef = null;
            FurDef fur = null;
            BeardDef beard = null;
            bool restoreLook = persona.story != null;
            if (restoreLook)
            {
                hair = persona.story.HairColor;
                skinBase = persona.story.SkinColorBase;
                skinOverride = persona.story.skinColorOverride;
                body = persona.story.bodyType;
                head = persona.story.headType;
                hairDef = persona.story.hairDef;
                fur = persona.story.furDef;
                beard = persona.style?.beardDef;
            }

            List<SkillDef> skillDefs = null;
            List<Passion> passions = null;
            if (persona.skills?.skills != null)
            {
                skillDefs = new List<SkillDef>();
                passions = new List<Passion>();
                for (int i = 0; i < persona.skills.skills.Count; i++)
                {
                    SkillRecord skill = persona.skills.skills[i];
                    if (skill?.def == null)
                        continue;
                    skillDefs.Add(skill.def);
                    passions.Add(skill.passion);
                }
            }

            persona.genes.SetXenotype(ShipCorePersonaDefOf.ShipCorePersonaXenotype);
            persona.genes.hybrid = false;
            RemoveGenes(persona.genes.Xenogenes, persona.genes);
            RemoveGenes(persona.genes.Endogenes, persona.genes);

            if (restoreLook)
            {
                persona.story.HairColor = hair;
                persona.story.SkinColorBase = skinBase;
                persona.story.skinColorOverride = skinOverride;
                if (body != null)
                    persona.story.bodyType = body;
                if (head != null)
                    persona.story.headType = head;
                if (hairDef != null)
                    persona.story.hairDef = hairDef;
                persona.story.furDef = fur;
                if (persona.style != null)
                    persona.style.beardDef = beard;
                persona.Drawer?.renderer?.SetAllGraphicsDirty();
            }

            if (skillDefs == null)
                return;

            for (int i = 0; i < skillDefs.Count; i++)
            {
                SkillRecord skill = persona.skills.GetSkill(skillDefs[i]);
                if (skill != null)
                    skill.passion = passions[i];
            }
        }

        private static bool AlreadyCorePersona(Pawn persona)
        {
            Pawn_GeneTracker genes = persona.genes;
            return genes.Xenotype == ShipCorePersonaDefOf.ShipCorePersonaXenotype
                && genes.xenotypeName.NullOrEmpty()
                && genes.iconDef == null
                && !genes.hybrid
                && genes.Xenogenes.Count == 0
                && genes.Endogenes.Count == 0;
        }

        private static void RemoveGenes(List<Gene> genes, Pawn_GeneTracker tracker)
        {
            if (genes == null || genes.Count == 0)
                return;

            List<Gene> copy = genes.ToList();
            for (int i = 0; i < copy.Count; i++)
                tracker.RemoveGene(copy[i]);
        }

        public static void EnsureIdentity(Pawn persona)
        {
            if (persona?.story == null)
                return;

            BackstoryDef childhood = DefDatabase<BackstoryDef>.GetNamedSilentFail("ShipCorePersona_Childhood");
            BackstoryDef adulthood = DefDatabase<BackstoryDef>.GetNamedSilentFail("ShipCorePersona_Adulthood");
            if (childhood != null)
                persona.story.Childhood = childhood;
            if (adulthood != null)
                persona.story.Adulthood = adulthood;
        }

        /// <summary>
        /// 飞船主脑的名字用人格核心上的名字。人格核心上已经有名字，就用那个名字。
        /// 人格芯片还在飞船电脑核心里、名字还空着时，让人格核心取一次名字，飞船主脑用同一个名字。
        /// 人格核心上还没有名字时，用幼年背景的标题「人格核心」给飞船主脑起名。
        /// </summary>
        public static void ApplyCoreName(Pawn persona, Thing core)
        {
            if (persona == null)
                return;

            string name = VoyageCompat.CoreName(core);
            if (string.IsNullOrEmpty(name))
            {
                BackstoryDef childhood = DefDatabase<BackstoryDef>.GetNamedSilentFail("ShipCorePersona_Childhood");
                name = childhood?.title;
            }

            if (string.IsNullOrEmpty(name))
                return;
            if (persona.Name is NameSingle current && current.Name == name)
                return;

            persona.Name = new NameSingle(name);
        }

        private static void ApplyAppearance(Pawn persona)
        {
            if (persona.story == null)
                return;

            persona.story.HairColor = HairColor;
            persona.story.skinColorOverride = SkinColor;
            EnsureBodyType(persona);
            WearSynthreadSet(persona);
            persona.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        /// <summary>
        /// 生成请求里的 ForceBodyType 会被后来的基因改掉。基因把体型改成肥胖时，在这里再换成「女性」「纤细」或「魁梧」。
        /// </summary>
        public static void EnsureBodyType(Pawn persona)
        {
            if (persona?.story == null)
                return;
            if (persona.story.bodyType != null && persona.story.bodyType != BodyTypeDefOf.Fat)
                return;

            persona.story.bodyType = BodyTypeWithoutFat();
            persona.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        /// <summary>
        /// 旧档里飞船主脑的健康状态带着隐形组件。读档后卸掉这份健康状态，再重新加上飞船主脑状态，隐形效果就不会留下。
        /// </summary>
        public static void ClearInvisibility(Pawn persona)
        {
            Hediff bound = persona?.health?.hediffSet?.GetFirstHediffOfDef(ShipCorePersonaDefOf.ShipCorePersonaBound);
            if (bound?.TryGetComp<HediffComp_Invisibility>() == null)
                return;

            persona.health.RemoveHediff(bound);
            persona.health.AddHediff(ShipCorePersonaDefOf.ShipCorePersonaBound);
        }

        private static BodyTypeDef BodyTypeWithoutFat()
        {
            BodyTypeDef[] allowed = { BodyTypeDefOf.Female, BodyTypeDefOf.Thin, BodyTypeDefOf.Hulk };
            return allowed[Rand.Range(0, allowed.Length)];
        }

        private static void WearSynthreadSet(Pawn persona)
        {
            if (persona.apparel == null)
                return;

            ThingDef synthread = DefDatabase<ThingDef>.GetNamedSilentFail("Synthread");
            if (synthread == null)
                return;

            persona.apparel.DestroyAll(DestroyMode.Vanish);
            Wear(persona, "Apparel_Pants", synthread);
            Wear(persona, "Apparel_CollarShirt", synthread);
        }

        private static void Wear(Pawn persona, string defName, ThingDef stuff)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
                return;

            var apparel = (Apparel)ThingMaker.MakeThing(def, stuff);
            persona.apparel.Wear(apparel, dropReplacedApparel: false, locked: true);
        }

        private static void ClearInjuriesAndTraits(Pawn persona)
        {
            if (persona.story?.traits != null)
            {
                List<Trait> traits = persona.story.traits.allTraits.ToList();
                for (int i = 0; i < traits.Count; i++)
                    persona.story.traits.RemoveTrait(traits[i]);
            }

            if (persona.health == null)
                return;

            List<Hediff> bad = persona.health.hediffSet.hediffs.Where(hediff => hediff.def.isBad).ToList();
            for (int i = 0; i < bad.Count; i++)
                persona.health.RemoveHediff(bad[i]);
        }

        private static void EnsureColonistComponents(Pawn persona)
        {
            PawnComponentsUtility.AddComponentsForSpawn(persona);
            PawnComponentsUtility.AddAndRemoveDynamicComponents(persona, false);

            if (persona.drafter == null)
                persona.drafter = new Pawn_DraftController(persona);
            if (persona.playerSettings == null)
                persona.playerSettings = new Pawn_PlayerSettings(persona);
            if (persona.workSettings == null)
                persona.workSettings = new Pawn_WorkSettings(persona);

            persona.workSettings.DisableAll();
            persona.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
        }
    }
}
