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
            int patched = PatchAssembly(harmony, Assembly.GetExecutingAssembly());

            if (ModHalves.VoyageActive)
            {
                // 把飞船电脑核心登记进逆重引擎可以连接的设施。人格芯片落位之后才有意义，定义加载完才能做。
                GravshipEngineWhitelist.Register();
                // 与 Gravship Voyage 程序的联动，走反射，不引用那个程序集。
                VoyageCompat.TryPatch(harmony);
            }

            Log.Message("[GravshipFleetPro] 已挂载：" + (ModHalves.FleetActive ? "舰队" : "（未装 Gravship Fleet，舰队一半停用）")
                + " / " + (ModHalves.VoyageActive ? "人格" : "（未装 Gravship Voyage，人格一半停用）")
                + "，补丁类 " + patched + " 个");
        }

        /// <summary>
        /// 把程序集里带 [HarmonyPatch] 的类逐个挂上，按半挑选，返回挂上的补丁类个数。
        /// 缺哪一半模组，就跳过标着对应标记的那些补丁类。
        ///
        /// 不要按 type.IsAbstract 过滤：C# 的静态类在元数据里就是 abstract sealed，
        /// 而本模组的补丁类全是静态类，一按 IsAbstract 跳过就会把它们全部漏掉，
        /// 结果一个补丁都挂不上（人格一半与舰队一半会一起停摆，而且只在日志里留下一句「已挂载」）。
        /// 除了泛型定义挂不上，其余照 Harmony.PatchAll 的做法一并交给 CreateClassProcessor 处理。
        /// </summary>
        private static int PatchAssembly(Harmony harmony, Assembly assembly)
        {
            int patched = 0;
            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsGenericTypeDefinition)
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
                patched++;
            }

            return patched;
        }
    }
}
