using System;
using Colossal;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using Game.Settings;
using Game.UI.Widgets;
using UnityEngine;

namespace BusLineAutoStops
{
    /// <summary>
    /// 选项页：最上面的总开关 + 懒人模式设置 + 「关于」（版本 / 作者 / 三个外链按钮）。
    ///
    /// 总开关那条用 `[SettingsUISection(kTabGeneral, kGroupMaster)]`，而 kGroupMaster
    /// **不写进** `[SettingsUIShowGroupName]` —— 原版 OptionsUISystem.cs:165-172 只给列出的组画标题，
    /// 没列出的组只有分隔线，正好是需求 4 要的"单独一个板块、不要名称"。
    /// 组顺序由类上的 `[SettingsUIGroupOrder(...)]` 决定（AutomaticSettings.cs:1005-1015）。
    /// 下面的懒人模式条目用 `[SettingsUIDisableByCondition]` 变灰（原版 GeneralSettings.cs:91-99
    /// 的 autoSave / autoSaveInterval 就是同一对写法）；注意这条框架规则：
    /// **带 DisableByCondition 的属性默认不落盘**，必须加 `[SettingsUIForceSave]`，
    /// 否则玩家选的删除策略重启就丢。
    /// </summary>
    [SettingsUIKeyboardAction(kActionToggleLazy)]
    [SettingsUIGroupOrder(kGroupMaster, kGroupMain, kGroupAbout)]
    [SettingsUIShowGroupName(kGroupMain, kGroupAbout)]
    public class Setting : ModSetting
    {
        public const string kActionToggleLazy = "ToggleLazyMode";

        public const string kTabGeneral = "General";
        /// <summary>总开关所在组：不出组名。</summary>
        public const string kGroupMaster = "Master";
        public const string kGroupMain = "Main";
        public const string kGroupAbout = "About";
        public const string kLinkGroup = "Links";

        public const string kSettingId = "BusLineAutoStops.BusLineAutoStops.BusLineAutoStopsMod";

        public const string kVersion = "0.10.0";
        public const string kAuthor = "yuexian";

        public const string kKoFiUrl = "https://ko-fi.com/yuexian7";
        /// <summary>论坛入口：Paradox 官方论坛本模组帖子（v0.2.2 起指这里；GitHub 仓库仍在 README）。</summary>
        public const string kForumUrl = "https://forum.paradoxplaza.com/forum/threads/busline-autostops.1942674/";
        public const string kRainbowUrl = "https://rainbow-series-hvpma89wi25.qoder.zone/#top";

        public static Setting Instance;

        public Setting(IMod mod) : base(mod) { }

        private ExitDeletePolicy m_ExitPolicy = ExitDeletePolicy.KeepStops;
        private bool m_AutoPlaceEnabled = true;

        /// <summary>需求 4（0.9.0）：本版恒 false —— 有轨电车那条是置灰的预留条目。</summary>
        private bool m_TramEnabled = false;

        /// <summary>
        /// 总开关。关的时候：左上角按钮消失、快捷键无效、懒人模式立刻退出（并撤掉跟手站台）。
        /// </summary>
        [SettingsUISection(kTabGeneral, kGroupMaster)]
        public bool AutoPlaceEnabled
        {
            get { return m_AutoPlaceEnabled; }
            set
            {
                bool v = value;
                if (m_AutoPlaceEnabled == v) return;
                m_AutoPlaceEnabled = v;
                LazyModeState.Enabled = v;
                LazyModeState.UiSeq++;
                BusLineAutoStopsMod.log.Info("[总开关] 站台自动放置 = " + v);
                if (!v && BusLineAutoStopsMod.Instance != null)
                {
                    BusLineAutoStopsMod.Instance.TurnOffIfLazy();
                }
            }
        }

        /// <summary>DisableByCondition 的条件方法：返回 true = 这条目变灰不可选。</summary>
        public bool LazyOptionsDisabled()
        {
            return !m_AutoPlaceEnabled;
        }

        /// <summary>
        /// 退出懒人模式时怎么处置本次自动落地的公交站（需求 5：只有两项，默认保留）。
        /// 老存档里可能还存着 0.5.0 那套四项枚举的值（2/3 已经不存在了）：这里一律归一到
        /// "保留公交站"，绝不让一个玩家没选过的删除行为发生。
        /// </summary>
        [SettingsUISection(kTabGeneral, kGroupMain)]
        [SettingsUIForceSave]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(LazyOptionsDisabled))]
        public ExitDeletePolicy ExitPolicy
        {
            get { return m_ExitPolicy; }
            set
            {
                ExitDeletePolicy v = value == ExitDeletePolicy.DeleteSessionStops
                    ? ExitDeletePolicy.DeleteSessionStops
                    : ExitDeletePolicy.KeepStops;
                if (m_ExitPolicy == v) return;
                m_ExitPolicy = v;
                LazyModeState.ExitPolicy = v;
            }
        }

        [SettingsUISection(kTabGeneral, kGroupMain)]
        [SettingsUIKeyboardBinding(BindingKeyboard.None, kActionToggleLazy)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(LazyOptionsDisabled))]
        public ProxyBinding ToggleLazyBinding { get; set; }

        /// <summary>
        /// 需求 4（0.9.0）：有轨电车站自动落地**从懒人模式里剥出来单列一条**，本版恒 false 且置灰不可选
        /// （条目留着，注明"暂未上线"，后面版本开放）。
        ///
        /// 为什么要剥：公交线路工具的路径点能落在道路单侧人行道上，静止 0.1s 我们的落点解析拿得到
        /// 合法父级；电车线路工具只能"从一个电车站连到下一个电车站"，悬停点的 m_OriginalEntity 给的是
        /// 线路/车站实体（实机 2026-10-08 03:32 会话 `[预览跳过] … 工具父级=…Integrated Tram Stop`、
        /// `父级={Route|PrefabRef|}`），按道路吸附的那套落点规则在这条工具上根本不成立，
        /// 所以玩家看到的是"静止了也没有电车预览"。与其放一个半坏的开关，不如先关掉。
        /// </summary>
        [SettingsUISection(kTabGeneral, kGroupMain)]
        [SettingsUIForceSave]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(TramOptionDisabled))]
        public bool TramEnabled
        {
            get { return m_TramEnabled; }
            set
            {
                bool v = value;
                if (m_TramEnabled == v) return;
                m_TramEnabled = v;
                LazyModeState.TramEnabled = v;
                BusLineAutoStopsMod.log.Info("[选项] 有轨电车（暂未上线）= " + v);
            }
        }

        /// <summary>本版恒 true：这条目永远变灰不可勾选（需求 4"暂时先变灰不可选取"）。</summary>
        public bool TramOptionDisabled()
        {
            return true;
        }

        // —— 关于 ——

        /// <summary>只读 string → 只读行（原版 About 同款）。</summary>
        [SettingsUISection(kTabGeneral, kGroupAbout)]
        public string Version => kVersion;

        [SettingsUISection(kTabGeneral, kGroupAbout)]
        public string Author => kAuthor;

        [SettingsUISection(kTabGeneral, kGroupAbout)]
        [SettingsUIButtonGroup(kLinkGroup)]
        public bool OpenKoFi
        {
            set => OpenLink(kKoFiUrl, "ko-fi");
        }

        [SettingsUISection(kTabGeneral, kGroupAbout)]
        [SettingsUIButtonGroup(kLinkGroup)]
        public bool OpenForum
        {
            set => OpenLink(kForumUrl, "forum");
        }

        [SettingsUISection(kTabGeneral, kGroupAbout)]
        [SettingsUIButtonGroup(kLinkGroup)]
        public bool OpenRainbow
        {
            set => OpenLink(kRainbowUrl, "rainbow");
        }

        private static void OpenLink(string url, string tag)
        {
            try
            {
                Application.OpenURL(url);
                BusLineAutoStopsMod.log.Info("Opened " + tag + " link: " + url);
            }
            catch (Exception ex)
            {
                BusLineAutoStopsMod.log.Warn("OpenLink(" + tag + ") failed: " + ex.Message);
            }
        }

        public static string ExitPolicyEnumKey(ExitDeletePolicy p)
        {
            return BlgKeys.EnumKey(typeof(ExitDeletePolicy).Name, p.ToString());
        }

        public override void SetDefaults()
        {
            m_AutoPlaceEnabled = true;
            m_ExitPolicy = ExitDeletePolicy.KeepStops;
            ExitPolicy = ExitDeletePolicy.KeepStops;
            LazyModeState.ExitPolicy = ExitDeletePolicy.KeepStops;
            LazyModeState.Enabled = true;
            m_TramEnabled = false;
            LazyModeState.TramEnabled = false;
        }

        public void SyncToState()
        {
            LazyModeState.Enabled = m_AutoPlaceEnabled;
            LazyModeState.ExitPolicy = m_ExitPolicy;
            // 需求 4（0.9.0）：本版强制关。老存档或手改 config 里塞了 true 也不放开——
            // "暂未上线"的功能不能因为一个玩家没勾选过的值就跑起来。
            m_TramEnabled = false;
            LazyModeState.TramEnabled = false;
        }
    }
}
