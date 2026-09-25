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
    /// 选项页：常规设置 + 快捷键 + 「关于」（版本 / 作者 / 三个外链按钮）。
    /// 按钮写法照抄 AccessAnarchy：只写不读的 bool 即渲染为按钮；同 SettingsUIButtonGroup 合并一行。
    /// </summary>
    [SettingsUIKeyboardAction(kActionToggleLazy)]
    [SettingsUIGroupOrder(kGroupMain, kGroupAbout)]
    [SettingsUIShowGroupName(kGroupMain, kGroupAbout)]
    public class Setting : ModSetting
    {
        public const string kActionToggleLazy = "ToggleLazyMode";

        public const string kTabGeneral = "General";
        public const string kGroupMain = "Main";
        public const string kGroupAbout = "About";
        public const string kLinkGroup = "Links";

        public const string kSettingId = "BusLineAutoStops.BusLineAutoStops.BusLineAutoStopsMod";

        public const string kVersion = "0.1.1";
        public const string kAuthor = "yuexian";

        public const string kKoFiUrl = "https://ko-fi.com/yuexian7";
        public const string kForumUrl = "https://forum.paradoxplaza.com/forum/threads/access-anarchy.1941285/latest";
        public const string kRainbowUrl = "https://rainbow-series-hvpma89wi25.qoder.zone/#top";

        public static Setting Instance;

        public Setting(IMod mod) : base(mod) { }

        private ExitDeletePolicy m_ExitPolicy = ExitDeletePolicy.DoNothing;

        /// <summary>中途退出懒人模式时如何处理本次新建内容。默认不做任何删除。</summary>
        [SettingsUISection(kTabGeneral, kGroupMain)]
        public ExitDeletePolicy ExitPolicy
        {
            get { return m_ExitPolicy; }
            set
            {
                if (m_ExitPolicy == value) return;
                m_ExitPolicy = value;
                LazyModeState.ExitPolicy = value;
            }
        }

        [SettingsUISection(kTabGeneral, kGroupMain)]
        [SettingsUIKeyboardBinding(BindingKeyboard.None, kActionToggleLazy)]
        public ProxyBinding ToggleLazyBinding { get; set; }

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
            m_ExitPolicy = ExitDeletePolicy.DoNothing;
            ExitPolicy = ExitDeletePolicy.DoNothing;
            LazyModeState.ExitPolicy = ExitDeletePolicy.DoNothing;
        }

        public void SyncToState()
        {
            LazyModeState.ExitPolicy = m_ExitPolicy;
        }
    }
}
