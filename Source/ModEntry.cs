using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 模组入口。按「装了哪一份模组」决定挂哪些补丁，然后做需要定义已经加载完的登记工作。
    ///
    /// 这里不调用 Harmony.PatchAll：本模组横跨两份软依赖模组，整个程序集里的补丁要按半挑选
    /// （见 ModHalves）。逐个类挂载还能顺带跳过没标 [HarmonyPatch] 的类型，行为与 PatchAll 一致。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class ModEntry
    {
        // 与 About/About.xml 的 packageId 一致。
        public const string HarmonyId = "Estrella6066.GravshipFleetPro";

        static ModEntry()
        {
            Harmony harmony = new Harmony(HarmonyId);
            PatchAssembly(harmony, Assembly.GetExecutingAssembly());

            if (ModHalves.VoyageActive)
            {
                // 把飞船电脑核心登记进逆重引擎可以连接的设施。人格芯片落位之后才有意义，定义加载完才能做。
                GravshipEngineWhitelist.Register();
                // 与 Gravship Voyage 程序的联动，走反射，不引用那个程序集。
                VoyageCompat.TryPatch(harmony);
            }

            Log.Message("[GravshipFleetPro] 已挂载：" + (ModHalves.FleetActive ? "舰队" : "（未装 Gravship Fleet，舰队一半停用）")
                + " / " + (ModHalves.VoyageActive ? "人格" : "（未装 Gravship Voyage，人格一半停用）"));
        }

        /// <summary>
        /// 把程序集里带 [HarmonyPatch] 的类逐个挂上，按半挑选。
        /// 缺哪一半模组，就跳过标着对应标记的那些补丁类。
        /// </summary>
        private static void PatchAssembly(Harmony harmony, Assembly assembly)
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition || type.IsNested)
                {
                    continue;
                }
                if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: true).Length == 0)
                {
                    continue;
                }
                if (type.GetCustomAttributes(typeof(RequiresFleetAttribute), inherit: true).Length > 0 && !ModHalves.FleetActive)
                {
                    continue;
                }
                if (type.GetCustomAttributes(typeof(RequiresVoyageAttribute), inherit: true).Length > 0 && !ModHalves.VoyageActive)
                {
                    continue;
                }

                harmony.CreateClassProcessor(type).Patch();
            }
        }
    }
}
