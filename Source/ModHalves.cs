using System;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 本模组横跨两份模组，功能也据此分成两半。两份模组都是软依赖（写在 About.xml 的 loadAfter 里）：
    /// 装了哪一份，就只让对应的那一半生效；缺一份时另外一半照常工作，不报错、不写日志报怨。
    ///
    ///   舰队一半（需要 Gravship Fleet）——多艘逆重飞船各飞各的。
    ///   人格一半（需要 Gravship Voyage）——飞船电脑核心里的飞船主脑。
    ///
    /// 补丁类用 [RequiresFleet] / [RequiresVoyage] 声明自己属于哪一半，ModEntry 按这两个标记决定挂不挂。
    /// 没有标记的补丁只需要 Odyssey，是原版逆重飞船的修补，与这两份模组无关。
    ///
    /// 定义部分（Defs 与 Languages）不分半，始终加载：它们是惰性的，只有人格存在时才会被用到，
    /// 而让人格存在的部件全在补丁里。这样做还避开一个坑——ShipCorePersonaDefOf 是 [DefOf] 类，
    /// 定义没加载时读它会直接报错。
    /// </summary>
    internal static class ModHalves
    {
        public const string FleetPackageId = "asunib.gravshipfleet";
        public const string VoyagePackageId = "asunib.gravshipvoyage";

        /// <summary>Gravship Fleet 是否启用。没有它，一辆车的地图上不会出现第二台逆重引擎。</summary>
        public static bool FleetActive => ModsConfig.IsActive(FleetPackageId);

        /// <summary>Gravship Voyage 是否启用。人格芯片、AI 核心、机械师身份都来自它。</summary>
        public static bool VoyageActive => ModsConfig.IsActive(VoyagePackageId);
    }

    /// <summary>标在补丁类上：只有启用了 Gravship Fleet，这个补丁才挂上。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    internal sealed class RequiresFleetAttribute : Attribute
    {
    }

    /// <summary>标在补丁类上：只有启用了 Gravship Voyage，这个补丁才挂上。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    internal sealed class RequiresVoyageAttribute : Attribute
    {
    }
}
