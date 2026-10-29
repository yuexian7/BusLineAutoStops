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
    ///
    /// —— v0.14.0：修「设置项每次重启都回默认」（玩家 0.13.0 反馈 #2/#3）——
    /// 两个真根因都在**落盘寻址**上，跟 UI 渲染无关：
    ///
    /// 1) **类名必须全局唯一**。`Setting.ApplyAndSave()`（Game.Settings/Setting.cs:151-155）写的是
    ///    `SaveSpecificSetting(GetType().Name)`，而 `AssetDatabase.SaveSpecificSetting` →
    ///    `GetTargetSetting(settingName, …)`（AssetDatabase.cs:820-840）**只按
    ///    `fragment.source.GetType().Name` 找目标**，命中第一个就 break。本类原来就叫 `Setting`，
    ///    而本机同时装着 `AccessAnarchy.Setting`（`ilspycmd -l c AccessAnarchy.dll` 实测有）——
    ///    两个 `GetType().Name` 都是 "Setting"，于是我们的保存请求可能整个落到别人的 `.coc` 上
    ///    （随后 `GetSettingsInSameFile` 只写那一个文件里的片段），我们自己的值一次都写不进去。
    ///    实机取证：玩家 2026-10-21 23:04 连按四次总开关，`FallbackSettings.coc` 的 mtime 仍停在
    ///    2026-10-20 05:15 —— 一行都没写进去。
    ///    → 类名改成 `BusLineAutoStopsSetting`。`id` 不受影响（`ModSetting.id` 用的是 **Mod 类**：
    ///      `assembly + "." + Mod 命名空间 + "." + Mod 类名`，仍是 kSettingId 那一串）；
    ///      `.coc` 里的片段名也不受影响（那是 `LoadSettings(nameof(BusLineAutoStops), …)` 传的字符串）。
    ///      唯一会变的是 `name`，它只进 `Options.OPTION[...]` / `Options.OPTION_DESCRIPTION[...]` 两条 key，
    ///      而 LocaleTable 是用 `setting.GetOptionLabelLocaleID(...)` 现算的，两边同步变、不会错位。
    /// 2) **自己一个 `.coc`**。没有 `[FileLocation]` 时框架把片段塞进公共的 `FallbackSettings.coc`
    ///    （`AssetDatabase.LoadSettings` 里 `assetName` 的兜底值），跟几十个模组挤一个文件：
    ///    任何一次别人的保存都会把整份文件重写一遍，我们的片段在不在、值是多少全看别人什么时候存。
    ///    → 加 `[FileLocation("BusLineAutoStops")]`（`FileLocationAttribute` 会把扩展名改成 .coc）。
    ///    但它只决定**新建**片段落在哪个文件（`AssetDatabase.CreateSettingsAsset`）：老玩家数据库里
    ///    已经有同名片段时，`LoadSettings` 直接复用那一份，`ApplyAndSave` 也跟着写回它原来所在的文件
    ///    （`SaveSpecificSetting` → `GetTargetSetting` 按片段找 `fragment.source`），
    ///    所以老玩家的值仍住在 `FallbackSettings.coc` —— `[FileLocation]` 既不会把值搬走、也不会重置它。
    ///
    /// 3) **改默认值必须换持久化键名**（v0.14.2，玩家 0.14.1 反馈「下拉框还是回到保留」）。
    ///    落盘里存的是**属性名**（`"ExitPolicy":"KeepStops"`），读回时 `JSON.WriteInto` 逐键调
    ///    `TryGetDecodableMember`：命中就写、没命中就**静默跳过**（JSON.cs:624-637，不抛异常、不影响别的键）。
    ///    而 0.5.0–0.11.x 的老默认就是 `KeepStops`，于是老玩家的文件里几乎都躺着
    ///    `"ExitPolicy":"KeepStops"` —— 它跟"玩家真的选过保留"在文件里**长得一模一样**，
    ///    靠值判断永远分不出来；只要这个键还在，`SetDefaults` 就永远轮不到执行（键存在就不用默认）。
    ///    → 属性改名 `StopPolicy`：老键成了未知键被跳过，值回落到字段初值
    ///      （`m_ExitPolicy = TransportKindRules.DefaultExitPolicy()`，与 `SetDefaults` 取同一个常量 =「不保留」）；
    ///      第一次保存时 `DiffWithPersistent` 按属性名回填老键，找不到同名属性，老键顺手被丢掉、不会复活。
    ///      **以后凡是改默认值，都必须同时换键名**，否则老玩家永远看不到新默认。
    /// </summary>
    [SettingsUIKeyboardAction(kActionToggleLazy)]
    [SettingsUIGroupOrder(kGroupMaster, kGroupMain, kGroupAbout)]
    [SettingsUIShowGroupName(kGroupMain, kGroupAbout)]
    [Colossal.IO.AssetDatabase.FileLocation("BusLineAutoStops")]
    public class BusLineAutoStopsSetting : ModSetting
    {
        public const string kActionToggleLazy = "ToggleLazyMode";

        public const string kTabGeneral = "General";
        /// <summary>总开关所在组：不出组名。</summary>
        public const string kGroupMaster = "Master";
        public const string kGroupMain = "Main";
        public const string kGroupAbout = "About";
        public const string kLinkGroup = "Links";

        public const string kSettingId = "BusLineAutoStops.BusLineAutoStops.BusLineAutoStopsMod";

        // 版本五处统一（Mod.cs kVersion / BusLineAutoStopsSetting.kVersion / Frontend mod.json /
        // PublishConfiguration.xml / releases 快照）：0.10.0 时这里曾落后两版，
        // 玩家在设置页看到错版本号、报日志误判——pack-release 的版本钉子已把本文件纳入
        public const string kVersion = "0.14.2";
        public const string kAuthor = "yuexian";

        public const string kKoFiUrl = "https://ko-fi.com/yuexian7";
        /// <summary>论坛入口：Paradox 官方论坛本模组帖子（v0.2.2 起指这里；GitHub 仓库仍在 README）。</summary>
        public const string kForumUrl = "https://forum.paradoxplaza.com/forum/threads/busline-autostops.1942674/";
        public const string kRainbowUrl = "https://rainbow-series-hvpma89wi25.qoder.zone/#top";

        public static BusLineAutoStopsSetting Instance;

        public BusLineAutoStopsSetting(IMod mod) : base(mod) { }

        /// <summary>
        /// 退出策略的**唯一决策点**是 <see cref="SetDefaults"/>（玩家没动过选项时用它）。
        /// 这个字段初值只在"设置文件缺失 / 读取失败"、框架拿不到 SetDefaults 时兜底，
        /// 所以与 SetDefaults 取同一个常量（同一个来源，不会各自漂掉）。
        /// </summary>
        private ExitDeletePolicy m_ExitPolicy = TransportKindRules.DefaultExitPolicy();
        private bool m_AutoPlaceEnabled = true;

        /// <summary>需求 4（0.9.0）：本版恒 false —— 有轨电车那条是置灰的预留条目。</summary>
        private bool m_TramEnabled = false;

        /// <summary>
        /// 需求 1（v0.12.0）：总开关，改名 **「启用模组」**。
        ///
        /// 语义（玩家原话）："开启后游戏内左上角的图标按钮就显示，关闭就不显示，整个模组完全关闭
        /// 恢复原版行为，总开关无法通过快捷键开启。开启总开关后，懒人模式可以通过快捷键或图标按钮
        /// 进入或退出。"
        ///
        /// 所以这里管的是**模组本身在不在**，不是懒人模式：
        /// · 关：左上角图标不出现（前端 `master !== false` 才渲染）、快捷键 action 停用、
        ///   懒人模式立刻退出并撤掉跟手站台、世界侧系统一行都不写（LazyModeState.Enabled 早退）。
        /// · 开：图标出现、快捷键按"有没有绑了可用的键"重新接上（见 BusLineAutoStopsMod.TurnOnHotkey）。
        /// · **快捷键只切懒人模式，永远不切总开关** —— 回调在 OnToggleLazy 里被
        ///   `if (!LazyModeState.Enabled) return;` 拦住（见 BusLineAutoStopsMod.RegisterHotkeys），
        ///   所以总开关关着时按快捷键只会给一句提示，不会把模组打开。
        /// </summary>
        [SettingsUISection(kTabGeneral, kGroupMaster)]
        // v0.14.0：显式 ForceSave。这条没有 DisableByCondition，本不必强制写；但只要落盘寻址
        // 出过一次问题（见类注释第 1 条），"默认值 = 不写"就成了放大镜：默认是 true，
        // 玩家关掉总开关那一刻恰好写得进去才留得下。加上了就与"总开关状态必须跨重启保持"对齐。
        [SettingsUIForceSave]
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
                BusLineAutoStopsMod.log.Info("[总开关] 启用模组 = " + v);
                if (BusLineAutoStopsMod.Instance != null)
                {
                    // 两个方向都要跟：关掉要停 action + 退懒人模式；打开要把 action 接回来。
                    if (v) BusLineAutoStopsMod.Instance.TurnOnHotkey();
                    else BusLineAutoStopsMod.Instance.TurnOffIfLazy();
                }
            }
        }

        /// <summary>DisableByCondition 的条件方法：返回 true = 这条目变灰不可选。</summary>
        public bool LazyOptionsDisabled()
        {
            return !m_AutoPlaceEnabled;
        }

        /// <summary>
        /// 退出懒人模式时怎么处置本次自动落地的公交站（需求 5：只有两项，默认**不保留**）。
        ///
        /// v0.14.2：属性名从 `ExitPolicy` 改成 `StopPolicy` —— 换的是**落盘键名**，语义一点没动。
        /// 理由见类注释第 3 条：老玩家文件里那句 `"ExitPolicy":"KeepStops"`（0.5.0–0.11.x 的旧默认）
        /// 跟"玩家真的选过保留"在文件里无法区分，只有换键才能让新默认真正落到老玩家身上。
        ///
        /// 老存档里可能还存着 0.5.0 那套四项枚举的值（2/3 已经不存在了），或者有人手改过文件：
        /// 这里一律归一到两项之一（只认 DeleteSessionStops，其余全按 KeepStops），
        /// 绝不让一个玩家没选过的删除行为发生。
        /// </summary>
        [SettingsUISection(kTabGeneral, kGroupMain)]
        [SettingsUIForceSave]
        [SettingsUIDisableByCondition(typeof(BusLineAutoStopsSetting), nameof(LazyOptionsDisabled))]
        public ExitDeletePolicy StopPolicy
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
                // 读取落盘与玩家改选都会走到这里，所以措辞中性：实机日志里出现这一行，
                // 就说明"值确实进了设置对象"，再配上 LogLoaded 的收尾行即可判断存没存住。
                BusLineAutoStopsMod.log.Info("[选项] 退出懒人模式时 = " + v);
            }
        }

        [SettingsUISection(kTabGeneral, kGroupMain)]
        [SettingsUIKeyboardBinding(BindingKeyboard.None, kActionToggleLazy)]
        [SettingsUIDisableByCondition(typeof(BusLineAutoStopsSetting), nameof(LazyOptionsDisabled))]
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
        [SettingsUIDisableByCondition(typeof(BusLineAutoStopsSetting), nameof(TramOptionDisabled))]
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
            // 需求 1（v0.12.0）：总开关默认**开**（模组装上就能用，图标就在左上角）。
            m_AutoPlaceEnabled = true;
            // 需求 2（v0.12.0）：退出策略默认改成**不保留公交站**。
            // 默认值只在"玩家没动过选项"时生效；老存档里明确选过保留的仍是保留（值读回来就是 0）。
            m_ExitPolicy = TransportKindRules.DefaultExitPolicy();
            m_TramEnabled = false;
            // v0.14.0：这里以前是四条"顺手写静态"（ExitPolicy=LazyModeState 等）。语义上
            // SetDefaults 只该决定**字段默认值**，把值搬进运行时状态是 SyncToState 的活儿；
            // 两处各写一遍，一旦框架在 `LoadSettings` 之后再叫一次 SetDefaults（玩家点"恢复默认"
            // 就会），就会出现"文件里明明是保留、运行时却是删除"这种两份真值打架。
            // 现在统一走 SyncToState：一份来源、一条路径。
            SyncToState();
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

        /// <summary>
        /// v0.14.0：把"真正读到的值"打进日志。设置类问题在实机里只能靠这一行分辨
        /// （文件没读回来 / 读回来了又被谁覆盖 / 根本没写下去），比事后猜快得多。
        /// 只在 OnLoad 调一次，零每帧成本。
        /// </summary>
        public void LogLoaded(string where)
        {
            BusLineAutoStopsMod.log.Info("[设置] " + where
                + " 启用模组=" + m_AutoPlaceEnabled
                + " 退出策略=" + m_ExitPolicy
                + " 有轨电车=" + m_TramEnabled
                + " 键位=" + (ToggleLazyBinding != null && !string.IsNullOrEmpty(ToggleLazyBinding.path)
                    ? ToggleLazyBinding.path : "（未绑定）")
                + " 文件=" + GetSettingsFileHint());
        }

        /// <summary>落盘文件名的"人话"提示：加了 FileLocation 之后是自己的 .coc。</summary>
        private static string GetSettingsFileHint()
        {
            return "BusLineAutoStops.coc";
        }
    }
}
