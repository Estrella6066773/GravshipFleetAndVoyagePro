using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShipCorePersona
{
    public class CompProperties_ShipCorePersona : CompProperties
    {
        public CompProperties_ShipCorePersona()
        {
            compClass = typeof(CompShipCorePersona);
        }
    }

    /// <summary>
    /// 飞船主脑生成在飞船电脑核心所占的格子上。绘制时用建筑中心，看起来飞船主脑在飞船电脑核心里面。
    /// 她仍然生成在地图上，能力才能放出来。她保持清醒。
    /// 不能把移动设成 0，否则原版会把她当成倒地、失去知觉。
    /// 旧档里她还在容器中。读档后把她挪到旁边那一格。容器只用来把旧档里的飞船主脑迁出来。
    /// retrievals 的下标对应取出工作的 count，不能从中间删掉取出记录。
    /// 人格核心离开飞船电脑核心，或飞船电脑核心暂时离开地图时，飞船主脑离开地图，收进专属人物池，不删除飞船主脑。
    /// 关联的人格核心没了，才删除飞船主脑。storedImplants 只在第一次生成时用。飞船主脑已经不在时，不要清空这份植入物记录。
    /// </summary>
    public class CompShipCorePersona : ThingComp, IThingHolder
    {
        private ThingOwner<Pawn> inner;
        private Pawn linked;
        private List<string> retrievals = new List<string>();
        private List<StoredCoreImplant> storedImplants = new List<StoredCoreImplant>();

        public CompShipCorePersona()
        {
            inner = new ThingOwner<Pawn>(this);
        }

        public Pawn Persona
        {
            get
            {
                if (linked != null && !linked.Destroyed)
                    return linked;
                return inner != null && inner.Count > 0 ? inner[0] : null;
            }
        }

        public new IThingHolder ParentHolder => parent?.Map;

        public ThingOwner GetDirectlyHeldThings()
        {
            return inner;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (inner == null)
                inner = new ThingOwner<Pawn>(this);

            EnsurePersona();
            PlaceBesideCore();
            if (Persona != null)
            {
                PersonaAppearance.EnsureIdentity(Persona);
                PersonaAppearance.EnsureXenotype(Persona);
                PersonaAppearance.EnsureBodyType(Persona);
                PersonaAppearance.ClearInvisibility(Persona);
                PersonaAppearance.ApplyCoreName(Persona, parent);
                Persona.Notify_DisabledWorkTypesChanged();
                WakeIfDowned(Persona);
            }
            VoyageCompat.ClaimHost(this);
            CorePersonaUtility.RetireAutoMechlink(Persona);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref linked, "personaSpawned");
            Scribe_Deep.Look(ref inner, "personaContainer", this);
            Scribe_Collections.Look(ref retrievals, "implantRetrievals", LookMode.Value);
            Scribe_Collections.Look(ref storedImplants, "storedImplants", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (inner == null)
                    inner = new ThingOwner<Pawn>(this);
                if (retrievals == null)
                    retrievals = new List<string>();
                if (storedImplants == null)
                    storedImplants = new List<StoredCoreImplant>();
            }
        }

        public ThingDef RetrievalAt(int index)
        {
            if (retrievals == null || index < 0 || index >= retrievals.Count)
                return null;
            return DefDatabase<ThingDef>.GetNamedSilentFail(retrievals[index]);
        }

        public int EnqueueRetrieval(ThingDef item)
        {
            if (retrievals == null)
                retrievals = new List<string>();
            retrievals.Add(item.defName);
            return retrievals.Count - 1;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            DeactivatePersona(mode);
            base.PostDeSpawn(map, mode);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            int mindId = VoyageCompat.BoundMindId(parent);
            bool chipStillOut = VoyageCompat.IsLoaded && !VoyageCompat.ChipSeated(parent);
            if (!chipStillOut)
            {
                GameComponent_PersonaPool.DestroyPawn(linked);
                if (inner != null)
                {
                    for (int i = inner.Count - 1; i >= 0; i--)
                        GameComponent_PersonaPool.DestroyPawn(inner[i]);
                }

                GameComponent_PersonaPool.ReleaseBuilding(parent.thingIDNumber);
                GameComponent_PersonaPool.ReleaseMind(mindId);
            }

            linked = null;
            base.PostDestroy(mode, previousMap);
        }

        public override void CompTick()
        {
            if (!parent.Spawned)
                return;
            if (!VoyageCompat.ChipSeated(parent))
            {
                DeactivatePersona();
                return;
            }

            if (Persona == null)
                EnsurePersona();

            Pawn persona = Persona;
            if (persona == null || persona.Destroyed)
                return;

            PlaceBesideCore();
            WakeIfDowned(persona);
            if (parent.IsHashIntervalTick(60))
                CorePersonaUtility.RetireAutoMechlink(persona);
            Hediff_ShipCorePersonaBound.Restore(persona);

            if (persona.Spawned)
                return;

            try
            {
                if (persona.jobs?.curJob == null && persona.jobs?.jobQueue != null && persona.jobs.jobQueue.Count > 0)
                    persona.jobs.CheckForJobOverride(0f, false);

                if (persona.jobs?.curDriver != null)
                {
                    persona.jobs.JobTrackerTick();
                    persona.jobs.JobTrackerTickInterval(1);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[ShipCorePersona] 飞船主脑执行工作时出错：" + ex, persona.thingIDNumber ^ 0x51C0);
            }
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.CompFloatMenuOptions(selPawn))
                yield return option;

            Pawn persona = Persona;
            if (persona == null || parent.Faction != Faction.OfPlayer || selPawn == null)
                yield break;

            foreach (ThingDef item in ImplantAccess.Retrievable(persona))
            {
                yield return ImplantOption(selPawn, "ShipCorePersona_RetrieveImplant".Translate(item.LabelCap), delegate
                {
                    Job job = JobMaker.MakeJob(ShipCorePersonaDefOf.RetrieveShipCoreImplant, parent);
                    job.count = EnqueueRetrieval(item);
                    selPawn.jobs.TryTakeOrderedJob(job);
                });
            }

            foreach (Thing carried in ImplantAccess.CarriedImplants(selPawn))
            {
                if (!ImplantAccess.CanStore(persona, carried.def))
                    continue;

                Thing item = carried;
                yield return ImplantOption(selPawn, "ShipCorePersona_StoreImplant".Translate(item.def.LabelCap), delegate
                {
                    Job job = JobMaker.MakeJob(ShipCorePersonaDefOf.StoreShipCoreImplant, parent, item);
                    selPawn.jobs.TryTakeOrderedJob(job);
                });
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            Pawn persona = Persona;
            if (persona == null || parent.Faction != Faction.OfPlayer)
                yield break;

            Gizmo select = Building.SelectContainedItemGizmo(parent, persona);
            if (select != null)
                yield return select;

            if (persona.drafter != null)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "ShipCorePersona_Draft".Translate(),
                    defaultDesc = "ShipCorePersona_DraftDesc".Translate(),
                    icon = TexCommand.Draft,
                    hotKey = KeyBindingDefOf.Command_ColonistDraft,
                    isActive = () => persona.drafter.Drafted,
                    toggleAction = () => persona.drafter.Drafted = !persona.drafter.Drafted
                };
            }

            // Gravship Voyage 已经显示机械师按钮时，这里不再显示第二套机械师按钮。能力按钮留在飞船电脑核心上，施法判定按飞船电脑核心的位置来算。
            if (persona.mechanitor != null && !VoyageCompat.ShowsMechanitorGizmos(parent))
            {
                foreach (Gizmo gizmo in persona.mechanitor.GetGizmos())
                    yield return gizmo;
            }

            if (persona.abilities != null)
            {
                foreach (Gizmo gizmo in persona.abilities.GetGizmos())
                    yield return gizmo;
            }
        }

        public void EnsurePersona()
        {
            if (parent.Faction != Faction.OfPlayer || Faction.OfPlayer == null)
                return;
            if (!ModsConfig.BiotechActive)
                return;
            if (!VoyageCompat.ChipSeated(parent))
            {
                DeactivatePersona();
                return;
            }
            if (Persona != null)
                return;

            int mindId = VoyageCompat.BoundMindId(parent);
            Pawn persona = GameComponent_PersonaPool.Take(mindId, parent.thingIDNumber);
            bool generated = false;
            if (persona == null)
            {
                persona = PersonaAppearance.Generate();
                generated = true;
            }

            PersonaAppearance.ApplyCoreName(persona, parent);
            PersonaAppearance.EnsureXenotype(persona);
            persona.SetFaction(Faction.OfPlayer);
            linked = persona;
            if (generated)
                RestoreStoredImplants(persona);
            PlaceBesideCore();
        }

        /// <summary>
        /// 人格核心已经离开，或飞船电脑核心暂时不在地图上。飞船主脑离开地图，收进专属人物池。
        /// 飞船主脑已经不在时什么都不做，避免把飞船电脑核心上的植入物记录清掉。
        /// </summary>
        public void DeactivatePersona(DestroyMode reason = DestroyMode.Vanish)
        {
            Pawn persona = Persona;
            if (persona == null)
                return;

            PersonaAppearance.EnsureXenotype(persona);
            RememberImplants(persona);
            VoyageCompat.ClearHost(parent, persona);
            int mindId = VoyageCompat.BoundMindId(parent);
            linked = null;
            // 飞船把电脑核心收走时，她已经在飞船的人物名单里。名单里再留一份，落地会再生成一次。
            if (reason == DestroyMode.WillReplace)
                VoyageCompat.DropFromTravelingGravship(persona);
            DestroyMode despawn = reason == DestroyMode.WillReplace ? DestroyMode.WillReplace : DestroyMode.Vanish;
            GameComponent_PersonaPool.Park(persona, mindId, parent.thingIDNumber, despawn);
        }

        private void RememberImplants(Pawn persona)
        {
            storedImplants = new List<StoredCoreImplant>();
            if (persona?.health == null)
                return;

            foreach (Hediff hediff in persona.health.hediffSet.hediffs)
            {
                if (!ImplantAccess.IsMechanitorImplantHediff(hediff))
                    continue;

                storedImplants.Add(new StoredCoreImplant
                {
                    defName = hediff.def.defName,
                    level = hediff is Hediff_Level level ? level.level : 1
                });
            }
        }

        private void RestoreStoredImplants(Pawn persona)
        {
            if (persona?.health == null || storedImplants == null)
                return;

            for (int i = 0; i < storedImplants.Count; i++)
            {
                StoredCoreImplant record = storedImplants[i];
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(record?.defName);
                if (def == null || def == HediffDefOf.MechlinkImplant)
                    continue;

                Hediff existing = persona.health.hediffSet.GetFirstHediffOfDef(def);
                if (existing == null)
                    existing = persona.health.AddHediff(def, CorePersonaUtility.BrainOf(persona));
                if (existing is Hediff_Level level)
                {
                    int target = record.level < 1 ? 1 : record.level;
                    int guard = 0;
                    while (level.level < target && level.level < level.def.maxSeverity && guard++ < 12)
                    {
                        int before = level.level;
                        level.ChangeLevel(1);
                        if (level.level <= before)
                            break;
                    }
                }

            }

            PawnComponentsUtility.AddAndRemoveDynamicComponents(persona, false);
            persona.mechanitor?.Notify_BandwidthChanged();
        }

        private FloatMenuOption ImplantOption(Pawn worker, string label, Action action)
        {
            if (!worker.CanReach(parent, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption(label + ": " + "NoPath".Translate().CapitalizeFirst(), null);

            return new FloatMenuOption(label, action);
        }

        private void PlaceBesideCore()
        {
            Pawn persona = Persona;
            if (persona == null || persona.Destroyed || parent.Map == null)
                return;

            linked = persona;
            IntVec3 cell = parent.Position;
            if (persona.ParentHolder is ThingOwner owner && owner.Owner == this)
                owner.Remove(persona);

            if (!persona.Spawned)
            {
                GenSpawn.Spawn(persona, cell, parent.Map);
                return;
            }

            // 能力正在前摇时，不要把她拉回飞船电脑核心旁边，以免打断已经开始的能力。
            if (persona.stances?.curStance is Stance_Warmup)
                return;
            if (persona.Position != cell)
                persona.Position = cell;
        }

        private static void WakeIfDowned(Pawn persona)
        {
            if (persona.Dead || !persona.Downed || persona.health == null || persona.health.ShouldBeDowned())
                return;

            Hediff bound = persona.health.hediffSet.GetFirstHediffOfDef(ShipCorePersonaDefOf.ShipCorePersonaBound);
            persona.health.CheckForStateChange(null, bound);
        }

    }
}
