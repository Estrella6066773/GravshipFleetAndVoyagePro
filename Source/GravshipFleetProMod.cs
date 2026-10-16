using UnityEngine;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 逆重舰队 Pro 的设置项。
    ///
    /// 这里只放玩法开关与手感参数，所有界面文字都走 Keyed 词条（见
    /// Languages/&lt;语言&gt;/Keyed/GravshipFleetPro.xml），不在代码里写死。
    /// </summary>
    public class GravshipFleetProSettings : ModSettings
    {
        /// <summary>地图上还有另一台己方逆重引擎时，是否保留地图。关掉就退回原版「起飞即弃图」。</summary>
        public bool retainMapWhileAnotherEngineRemains = true;

        /// <summary>起飞结束后延迟多少 tick 才让厚岩顶塌落。原版一秒是 60 tick。</summary>
        public int collapseDelayTicks = 60;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref retainMapWhileAnotherEngineRemains, "retainMapWhileAnotherEngineRemains", defaultValue: true);
            Scribe_Values.Look(ref collapseDelayTicks, "collapseDelayTicks", 60);
            base.ExposeData();
        }
    }

    /// <summary>
    /// 模组入口（设置部分）。
    ///
    /// 补丁仍然挂在 ModEntry 的 [StaticConstructorOnStartup] 里，与人格内容共用一份，
    /// 这里只负责读设置、给设置界面。两者不冲突：Mod 子类的构造函数在定义加载之前跑，
    /// 不适合做需要 DefDatabase 的登记工作。
    /// </summary>
    public class GravshipFleetProMod : Mod
    {
        public static GravshipFleetProSettings Settings;

        public GravshipFleetProMod(ModContentPack content)
            : base(content)
        {
            Settings = GetSettings<GravshipFleetProSettings>();
        }

        public static bool RetainMapWhileAnotherEngineRemains => Settings?.retainMapWhileAnotherEngineRemains ?? true;

        /// <summary>延迟 tick 数。上限一小时游戏时间，避免设置里填出离谱的值。</summary>
        public static int CollapseDelayTicks => Mathf.Clamp(Settings?.collapseDelayTicks ?? 60, 0, 3600);

        public override string SettingsCategory()
        {
            return "GravshipFleetPro_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            // 舰队一半没生效时，这一页的设置都不会起作用，直接说清楚。
            if (!ModHalves.FleetActive)
            {
                listing.Label("GravshipFleetPro_Setting_NeedsFleet".Translate());
                listing.End();
                return;
            }

            listing.CheckboxLabeled("GravshipFleetPro_Setting_RetainMap".Translate(), ref Settings.retainMapWhileAnotherEngineRemains,
                "GravshipFleetPro_Setting_RetainMapDesc".Translate());

            listing.Label("GravshipFleetPro_Setting_CollapseDelay".Translate((Settings.collapseDelayTicks / 60f).ToString("0.0")));
            Settings.collapseDelayTicks = (int)listing.Slider(Settings.collapseDelayTicks, 0f, 600f);
            listing.Label("GravshipFleetPro_Setting_CollapseDelayDesc".Translate());

            listing.End();
        }
    }
}
