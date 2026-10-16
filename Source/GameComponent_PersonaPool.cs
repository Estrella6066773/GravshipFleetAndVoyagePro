using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 飞船主脑的专属人物池。飞船主脑未激活时待在这个人物池里，不进世界人物池，事件抽不到她。
    /// 关联的人格核心还在，就把飞船主脑留在这个人物池里。人格核心没了，才删除飞船主脑。
    /// </summary>
    public class GameComponent_PersonaPool : GameComponent, IThingHolder
    {
        private ThingOwner<Pawn> pawns;
        private List<int> mindIds = new List<int>();
        private List<int> buildingIds = new List<int>();

        public GameComponent_PersonaPool(Game game)
        {
            pawns = new ThingOwner<Pawn>(this);
        }

        public IThingHolder ParentHolder => null;

        public static GameComponent_PersonaPool Get()
        {
            return Current.Game?.GetComponent<GameComponent_PersonaPool>();
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return pawns;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref pawns, "pawns", this);
            Scribe_Collections.Look(ref mindIds, "mindIds", LookMode.Value);
            Scribe_Collections.Look(ref buildingIds, "buildingIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (pawns == null)
                    pawns = new ThingOwner<Pawn>(this);
                if (mindIds == null)
                    mindIds = new List<int>();
                if (buildingIds == null)
                    buildingIds = new List<int>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                AlignLists();
                RecoverFromWorld();
            }
        }

        /// <summary>
        /// 读档走完之后，把已经在档里的飞船主脑收成核心人格。
        /// 此时基因追踪器的 PostLoadInit 已经结束，空的异种人引用也已经被原版写成基线人，这里再改不会被那一步盖回去。
        /// 地图上的、专属人物池里的、以及还留在飞船电脑核心容器里的，都从这里走一遍。飞船电脑核心放上地图时，CompShipCorePersona.PostSpawnSetup 还会再走一次。
        /// </summary>
        public override void LoadedGame()
        {
            base.LoadedGame();
            // 没装 Gravship Voyage 时人格芯片不存在，飞船主脑也不可能存在。
            // 这时不要把她的异种人登记进世界，否则「核心人格」会出现在异种人界面里。
            if (!VoyageCompat.IsLoaded)
                return;

            EnsureKnownXenotypes();
        }

        /// <summary>
        /// 飞船主脑被删掉之后，别人身上还指着她。读档时这些引用对不上，心情和关系会报错。
        /// 只清掉已经对不上的引用。死人还在存档里的关系留下。
        /// </summary>
        public static void CleanupUnresolvedLinks()
        {
            EachPawn(pawn =>
            {
                if (pawn?.relations != null)
                {
                    List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
                    for (int i = relations.Count - 1; i >= 0; i--)
                    {
                        if (relations[i].otherPawn != null)
                            continue;
                        relations.RemoveAt(i);
                    }
                }

                MemoryThoughtHandler memories = pawn?.needs?.mood?.thoughts?.memories;
                if (memories == null)
                    return;

                foreach (Thought_Memory memory in memories.Memories.ToList())
                {
                    if (memory is Thought_PsychicHarmonizer thought && (thought.harmonizer == null || thought.harmonizer.pawn == null))
                        memories.RemoveMemory(thought);
                }
            });
        }

        private static void SeverLinks(Pawn persona)
        {
            EachPawn(pawn =>
            {
                if (pawn == null || pawn == persona)
                    return;

                if (pawn.relations != null)
                {
                    List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
                    for (int i = relations.Count - 1; i >= 0; i--)
                    {
                        DirectPawnRelation relation = relations[i];
                        if (relation.otherPawn == persona)
                            pawn.relations.TryRemoveDirectRelation(relation.def, persona);
                    }
                }

                MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
                if (memories == null)
                    return;

                foreach (Thought_Memory memory in memories.Memories.ToList())
                {
                    if (memory is Thought_PsychicHarmonizer thought && (thought.harmonizer == null || thought.harmonizer.pawn == persona || thought.otherPawn == persona))
                        memories.RemoveMemory(thought);
                }
            });
        }

        private static void EachPawn(System.Action<Pawn> action)
        {
            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    List<Pawn> pawns = map.mapPawns.AllPawns.ToList();
                    for (int i = 0; i < pawns.Count; i++)
                        action(pawns[i]);
                }
            }

            List<Pawn> world = Find.WorldPawns?.AllPawnsAliveOrDead;
            if (world == null)
                return;

            for (int i = 0; i < world.Count; i++)
                action(world[i]);
        }

        public override void GameComponentTick()
        {
            if (pawns == null || Find.TickManager == null || Find.TickManager.TicksGame % 60 != 0)
                return;
            // 没装 Gravship Voyage 时没有人格芯片，判断不了「心智是否还在世界上」。
            // 这时一律留着不删，避免把存档里已有的飞船主脑误删。
            if (!VoyageCompat.IsLoaded)
                return;

            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed)
                {
                    RemoveAt(i);
                    continue;
                }

                int mindId = mindIds[i];
                if (mindId != 0)
                {
                    if (!VoyageCompat.MindStillExists(mindId))
                        DestroyAt(i);
                    continue;
                }

                int buildingId = buildingIds[i];
                if (buildingId != 0 && !BuildingAlive(buildingId))
                    DestroyAt(i);
            }
        }

        public static bool Contains(Pawn pawn)
        {
            return Get()?.IndexOf(pawn) >= 0;
        }

        public static void Park(Pawn pawn, int mindId, int buildingId, DestroyMode despawnMode = DestroyMode.Vanish)
        {
            Get()?.Hold(pawn, mindId, buildingId, despawnMode);
        }

        public static Pawn Take(int mindId, int buildingId)
        {
            return Get()?.Release(mindId, buildingId);
        }

        public static void DestroyPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
                return;

            SeverLinks(pawn);
            GameComponent_PersonaPool pool = Get();
            int index = pool?.IndexOf(pawn) ?? -1;
            if (index >= 0)
                pool.RemoveAt(index);
            else if (pawn.holdingOwner != null)
                pawn.holdingOwner.Remove(pawn);

            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
            if (pawn.Spawned)
                pawn.DeSpawn();
            pawn.Destroy(DestroyMode.Vanish);
        }

        public static void ReleaseMind(int mindId)
        {
            GameComponent_PersonaPool pool = Get();
            if (pool == null || mindId == 0)
                return;

            for (int i = pool.pawns.Count - 1; i >= 0; i--)
            {
                if (pool.mindIds[i] == mindId)
                    pool.DestroyAt(i);
            }
        }

        public static void ReleaseBuilding(int buildingId)
        {
            GameComponent_PersonaPool pool = Get();
            if (pool == null || buildingId == 0)
                return;

            for (int i = pool.pawns.Count - 1; i >= 0; i--)
            {
                if (pool.buildingIds[i] == buildingId && pool.mindIds[i] == 0)
                    pool.DestroyAt(i);
            }
        }

        private void Hold(Pawn pawn, int mindId, int buildingId, DestroyMode despawnMode)
        {
            if (pawn == null || pawn.Destroyed)
                return;

            int existing = IndexOf(pawn);
            if (existing >= 0)
            {
                mindIds[existing] = mindId;
                buildingIds[existing] = buildingId;
                PullOffMap(pawn, despawnMode);
                return;
            }

            if (pawn.holdingOwner != null)
                pawn.holdingOwner.Remove(pawn);
            PullOffMap(pawn, despawnMode);
            if (!pawns.TryAdd(pawn))
            {
                Log.Error("[ShipCorePersona] 没能把飞船主脑放进专属人物池。");
                return;
            }

            mindIds.Add(mindId);
            buildingIds.Add(buildingId);
            PullOffMap(pawn, despawnMode);
        }

        private Pawn Release(int mindId, int buildingId)
        {
            int index = FindIndex(mindId, buildingId);
            if (index < 0)
                return null;

            Pawn pawn = pawns[index];
            RemoveAt(index);
            if (pawn != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
            return pawn;
        }

        private int FindIndex(int mindId, int buildingId)
        {
            if (mindId != 0)
            {
                for (int i = 0; i < mindIds.Count && i < pawns.Count; i++)
                {
                    if (mindIds[i] == mindId)
                        return i;
                }
            }

            if (buildingId == 0)
                return -1;

            for (int i = 0; i < buildingIds.Count && i < pawns.Count; i++)
            {
                if (buildingIds[i] == buildingId)
                    return i;
            }

            return -1;
        }

        private int IndexOf(Pawn pawn)
        {
            if (pawns == null || pawn == null)
                return -1;

            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i] == pawn)
                    return i;
            }

            return -1;
        }

        private void RecoverFromWorld()
        {
            List<Pawn> world = Find.WorldPawns?.AllPawnsAliveOrDead;
            if (world == null)
                return;

            List<Pawn> found = new List<Pawn>();
            for (int i = 0; i < world.Count; i++)
            {
                if (CorePersonaUtility.IsPersona(world[i]))
                    found.Add(world[i]);
            }

            for (int i = 0; i < found.Count; i++)
            {
                Pawn pawn = found[i];
                if (pawn == null)
                    continue;
                if (Find.WorldPawns.Contains(pawn))
                    Find.WorldPawns.RemovePawn(pawn);
                if (pawn.Spawned || pawn.Destroyed || Contains(pawn))
                    continue;
                Hold(pawn, 0, 0, DestroyMode.Vanish);
            }
        }

        private void DestroyAt(int index)
        {
            Pawn pawn = index >= 0 && index < pawns.Count ? pawns[index] : null;
            RemoveAt(index);
            if (pawn == null || pawn.Destroyed)
                return;
            SeverLinks(pawn);
            if (Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
            if (pawn.Spawned)
                pawn.DeSpawn();
            pawn.Destroy(DestroyMode.Vanish);
        }

        private void RemoveAt(int index)
        {
            if (index < 0 || index >= pawns.Count)
                return;

            Pawn pawn = pawns[index];
            if (pawn != null && !pawn.Destroyed)
                pawns.Remove(pawn);
            if (index < mindIds.Count)
                mindIds.RemoveAt(index);
            if (index < buildingIds.Count)
                buildingIds.RemoveAt(index);
        }

        private static void PullOffMap(Pawn pawn, DestroyMode mode)
        {
            if (pawn.Spawned)
            {
                pawn.jobs?.StopAll(false);
                // WillReplace 表示她是跟着飞船走的。默认的 Vanish 会让原版把她指挥的机械族全部取消征召。
                pawn.DeSpawn(mode);
            }

            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
        }

        private void AlignLists()
        {
            while (mindIds.Count < pawns.Count)
                mindIds.Add(0);
            while (buildingIds.Count < pawns.Count)
                buildingIds.Add(0);
            while (mindIds.Count > pawns.Count)
                mindIds.RemoveAt(mindIds.Count - 1);
            while (buildingIds.Count > pawns.Count)
                buildingIds.RemoveAt(buildingIds.Count - 1);
        }

        /// <summary>
        /// 读档后扫一遍还能找到的飞船主脑。人物池、地图、商队和装箱的飞船电脑核心各走一次。
        /// 已经是核心人格且基因为空时，EnsureXenotype 直接返回。
        /// </summary>
        private void EnsureKnownXenotypes()
        {
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                    PersonaAppearance.EnsureXenotype(pawns[i]);
            }

            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                    EnsureXenotypesOnMap(map);
            }

            if (Find.WorldObjects?.Caravans == null)
                return;

            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                foreach (Thing thing in caravan.AllThings)
                    EnsureXenotypeOnThing(thing);
            }
        }

        private static void EnsureXenotypesOnMap(Map map)
        {
            List<Pawn> all = map.mapPawns.AllPawns.ToList();
            for (int i = 0; i < all.Count; i++)
            {
                if (CorePersonaUtility.IsPersona(all[i]))
                    PersonaAppearance.EnsureXenotype(all[i]);
            }

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Ship_ComputerCore");
            if (def != null)
            {
                List<Thing> cores = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < cores.Count; i++)
                    EnsureXenotypeOnThing(cores[i]);
            }

            List<Thing> minified = map.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing);
            for (int i = 0; i < minified.Count; i++)
                EnsureXenotypeOnThing(minified[i]);
        }

        private static void EnsureXenotypeOnThing(Thing thing)
        {
            if (thing is MinifiedThing mini)
                thing = mini.InnerThing;

            CompShipCorePersona comp = thing?.TryGetComp<CompShipCorePersona>();
            if (comp?.Persona != null)
                PersonaAppearance.EnsureXenotype(comp.Persona);
        }

        private static bool BuildingAlive(int thingId)
        {
            if (thingId == 0)
                return false;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Ship_ComputerCore");
            foreach (Map map in Find.Maps)
            {
                if (def != null)
                {
                    List<Thing> cores = map.listerThings.ThingsOfDef(def);
                    for (int i = 0; i < cores.Count; i++)
                    {
                        if (cores[i].thingIDNumber == thingId && !cores[i].Destroyed)
                            return true;
                    }
                }

                List<Thing> minified = map.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing);
                for (int i = 0; i < minified.Count; i++)
                {
                    if (minified[i] is MinifiedThing mini && mini.InnerThing != null && mini.InnerThing.thingIDNumber == thingId && !mini.InnerThing.Destroyed)
                        return true;
                }
            }

            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                foreach (Thing thing in caravan.AllThings)
                {
                    if (thing.thingIDNumber == thingId && !thing.Destroyed)
                        return true;
                    if (thing is MinifiedThing mini && mini.InnerThing != null && mini.InnerThing.thingIDNumber == thingId && !mini.InnerThing.Destroyed)
                        return true;
                }
            }

            return VoyageCompat.GravshipHoldsBuilding(thingId);
        }
    }
}
