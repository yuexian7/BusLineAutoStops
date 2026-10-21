using System;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using Game.Tools;
using Unity.Entities;
using UnityEngine.InputSystem;
using BusLineAutoStops.Systems;

namespace BusLineAutoStops
{
    /// <summary>
    /// BusLine AutoStops 入口。
    ///
    /// v0.2.1 关键修正：
    /// - 顺序：RegisterKeyBindings() 必须在 LoadSettings() 之前。ModSetting.ApplyKeyBindings 是
    ///   [AfterDecode]，且开头就是 if (keyBindingRegistered)（ModSetting.cs:269-277）；
    ///   旧顺序导致解码时 keyBindingRegistered 还是 false，玩家存的键（C）从没写进 InputManager
    ///   → 按快捷键完全没反应（实机日志 0 条 [懒人模式]）。
    /// - 去掉了 Harmony 补丁：RouteToolSystem.Apply/Cancel 是私有状态机方法，钩它们只能“观察”不能改结果；
    ///   ObjectToolSystem.SetPrefab 直接抛 “Method has no body”。本版全部走 public 的
    ///   RouteToolSystem / ToolSystem 面（无补丁，跟 TTE 一致）。
    /// - 进入懒人模式不再强制“先选中站台资产”：线路类型取自线路工具本身
    ///   （TransportLineData.m_TransportType），站台资产缺省时自动挑一个可用公交站资产。
    /// </summary>
    public class BusLineAutoStopsMod : IMod
    {
        public const string kVersion = "0.14.0";

        public static ILog log = LogManager.GetLogger("BusLineAutoStops").SetShowsErrorsInUI(false);

        public static BusLineAutoStopsMod Instance;

        private BusLineAutoStopsSetting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;
            log.Info("BusLine AutoStops v" + kVersion + " loading...");

            m_Setting = new BusLineAutoStopsSetting(this);
            BusLineAutoStopsSetting.Instance = m_Setting;

            // 1) 先建 action（keyBindingRegistered=true）
            try { m_Setting.RegisterKeyBindings(); }
            catch (Exception e) { log.Warn("RegisterKeyBindings 失败：" + e.GetType().Name + " " + e.Message); }

            // 2) 再读设置：[AfterDecode] ApplyKeyBindings 这时才会把存过的键位推给 InputManager
            try
            {
                Colossal.IO.AssetDatabase.AssetDatabase.global.LoadSettings(
                    nameof(BusLineAutoStops), m_Setting, new BusLineAutoStopsSetting(this));
            }
            catch (Exception e)
            {
                log.Warn("读取设置失败，用默认值：" + e.GetType().Name);
            }
            m_Setting.SyncToState();
            // v0.14.0：把读到的真值打出来。设置类问题在实机里只能靠这一行分辨
            // （没读回来 / 读回来又被覆盖 / 压根没写下去），位置紧贴 LoadSettings 之后。
            m_Setting.LogLoaded("载入后");

            // 3) 多语言
            RegisterLocaleSources();

            // 4) 选项页
            if (World.DefaultGameObjectInjectionWorld != null)
            {
                try { m_Setting.RegisterInOptionsUI(); log.Info("选项页已注册"); }
                catch (Exception e) { log.Warn("选项页注册失败：" + e.GetType().Name + " " + e.Message); }
            }
            else
            {
                log.Warn("世界未就绪，选项页未注册");
            }

            RegisterHotkeys();
            ReportKeyBindings("加载后");

            try
            {
                // 注册只能有一次！2026-10-03 闪退取证的直接结论（开发笔记 #63）：
                // Game/UpdateSystem.cs:271-277（UpdateAt 进 m_Systems）与 :278-291（UpdateAfter/Before
                // 进 m_RefMap[另一个系统]）是**两张互不相干的表**，Refresh/AddSystemUpdate（:382-455）
                // 只管把 m_Updates.Add(systemData) 追加进去，**没有"同一个系统只加一次"的去重**。
                // 所以 UpdateAt + UpdateAfter + UpdateBefore 三行注册 = 一帧里 OnUpdate 跑三遍。
                // v0.6.1 就是这么把自己打死的：A 槽写 Apply，B/C 槽的"残留 Apply 自我保险"下一秒把它清了，
                // ToolOutputSystem 永远吃不到（实机 34 次代按 0 次转正、m_staleCleared 一路涨到 60），
                // 而且反射写 + 原版落地 job 被三倍频率反复触发，最后原生崩溃时空托管栈。
                //
                // 现在只留一个槽：排在 ToolOutputSystem 之前。ToolOutputSystem 自己是用 +1000000
                // 的排序键落在该相位最后（Game.Common/SystemOrder.cs:695），所以"紧挨在它前面"
                // 自动等价于"所有原版工具都跑完了"——读线路工具的控制点、以及下一版喂 temp 定义，
                // 都要靠这个唯一合法的缝隙。
                try { updateSystem.UpdateBefore<LazyModeSystem, ToolOutputSystem>(SystemUpdatePhase.ToolUpdate); }
                catch (Exception e) { log.Warn("LazyModeSystem 注册失败（ToolOutputSystem 之前）：" + e.GetType().Name); }

                // UiCommandSystem 同理只注册一次（UIUpdate 相位里没有顺序需求，UpdateAt 就够）
                updateSystem.UpdateAt<UiCommandSystem>(SystemUpdatePhase.UIUpdate);
                log.Info("系统已注册：LazyMode(ToolUpdate：仅一个槽，紧挨 ToolOutputSystem 之前) / UiCommand(UIUpdate)");
            }
            catch (Exception e)
            {
                LazyModeState.Broken = true;
                log.Error("系统注册失败，本模组不介入：" + e.GetType().Name + " " + e.Message);
            }
        }

        private void RegisterLocaleSources()
        {
            try
            {
                string[] locales = LocaleTable.kLocales;
                string active = "en-US";
                try
                {
                    string[] supported = GameManager.instance.localizationManager.GetSupportedLocales();
                    if (supported != null && supported.Length > 0) locales = supported;
                    active = GameManager.instance.localizationManager.activeLocaleId;
                }
                catch (Exception) { }
                LocaleTable.SetActiveLocale(active);

                for (int i = 0; i < locales.Length; i++)
                {
                    GameManager.instance.localizationManager.AddSource(locales[i], new SettingLocaleSource(m_Setting, locales[i]));
                    GameManager.instance.localizationManager.AddSource(locales[i], new PanelLocaleSource(locales[i]));
                }
                log.Info("语言源已注册 " + locales.Length + " 份，当前=" + active + " setting.id=" + m_Setting.id);
            }
            catch (Exception e)
            {
                log.Warn("注册语言源失败：" + e.GetType().Name + " " + e.Message);
            }
        }

        /// <summary>
        /// 模组被卸载（退出到主菜单 / 关游戏）时调用。**这里故意不注销**语言源与选项页：
        /// · 注销 API 其实是有的——`LocalizationManager.RemoveSource(localeId, source)`
        ///   （ilspycmd Colossal.Localization/LocalizationManager.cs:357）与
        ///   `ModSetting.UnregisterInOptionsUI()`（Game.Modding/ModSetting.cs:51），
        ///   但 RemoveSource 命中当前语言时会 `activeDictionary.Clear()` + 重载 + 广播变更
        ///   （同文件 :368-372），而 OnDispose 属于拆除阶段：UI/订阅者可能还活着，
        ///   在拆除期做这类广播改动是本模组一直回避的崩溃族（开发纪律「拆除期不做重活」）。
        /// · 现实口径：游戏禁用模组要重启进程才生效，这些注册项的寿命 = 进程寿命，不会跨重启残留。
        /// 已知限制（v0.11.2 审查 P3-2 定案：记录，不改）：同一场进程内禁用本模组后，
        /// 选项页仍留着本模组那一栏（里面的开关已不再联动世界），重启游戏才消失。
        /// </summary>
        public void OnDispose()
        {
            BusLineAutoStopsSetting.Instance = null;
            Instance = null;
            log.Info("BusLine AutoStops disposed");
        }

        private void RegisterHotkeys()
        {
            try
            {
                ProxyAction a = m_Setting.GetAction(BusLineAutoStopsSetting.kActionToggleLazy);
                if (a == null)
                {
                    log.Warn("[快捷键] 未取得 action（setting.id=" + m_Setting.id + "）");
                    return;
                }

                ProxyBinding b = m_Setting.ToggleLazyBinding;
                string path = b != null ? b.path : null;
                a.shouldBeEnabled = IsBindablePath(path) && LazyModeState.Enabled;
                if (!LazyModeState.Enabled)
                {
                    log.Info("[快捷键] 总开关已关，暂不启用 action");
                }

                a.onInteraction += (action, phase) =>
                {
                    if (phase != InputActionPhase.Started) return;
                    if (!LazyModeState.Enabled) return;
                    try { OnToggleLazy("快捷键"); }
                    catch (Exception e) { log.Warn("快捷键处理失败：" + e.Message); }
                };
                log.Info("[快捷键] ToggleLazyMode 已注册 path=" + (path ?? "(empty)"));
            }
            catch (Exception e)
            {
                log.Warn("注册快捷键失败：" + e.GetType().Name);
            }
        }

        /// <summary>
        /// 需求 1（v0.12.0）的自愈闸门：每帧（<see cref="UiCommandSystem"/> 调）比对一次
        /// "action 现在是否启用"与"它应该是否启用"，不一致就立刻纠正。
        ///
        /// 为什么除了修 setter 还要有这么一条：`ProxyAction.shouldBeEnabled` 的 **getter 读的是
        /// `m_DefaultActivator.enabled`**，而这个 activator 是**别的系统**（InputManager 的
        /// 设备/场景解析、原版工具切换时的 SetAction）也会改写的共享状态，我们写进去的值
        /// 并不独占。玩家报的"开一下关一下快捷键就废了"如果只是某条我们没覆盖到的路径
        /// （例如设置页重建、重绑键、切场景）把 activator 停掉，光修 setter 是不够的。
        ///
        /// 代价：每帧一次 `bool` 读 + 一次字符串判空，且**只在真的不一致时**才写（热路径无写）。
        /// 只做"应该开却没开 / 应该关却没关"两个方向的纠正，绝不在这里改动别的状态。
        /// </summary>
        internal void KeepHotkeyInSync()
        {
            try
            {
                if (m_Setting == null) return;
                ProxyAction a = m_Setting.GetAction(BusLineAutoStopsSetting.kActionToggleLazy);
                if (a == null) return;
                ProxyBinding b = m_Setting.ToggleLazyBinding;
                bool want = LazyModeState.Enabled && IsBindablePath(b != null ? b.path : null);
                if (a.shouldBeEnabled == want) return;
                a.shouldBeEnabled = want;
                log.Info("[快捷键] 自愈：shouldBeEnabled 被外部改成了 " + (!want) + " → 纠正为 " + want);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 键位有三层：盘上的 m_Path / 属性里的 ProxyBinding / 实际生效的 InputBinding。
        /// 旧版只打了第一层，所以“日志说有键、按下去没反应”看不出来。这里三层都打。
        /// </summary>
        private void ReportKeyBindings(string when)
        {
            try
            {
                ProxyAction a = m_Setting.GetAction(BusLineAutoStopsSetting.kActionToggleLazy);
                if (a == null) { log.Warn("[键位] " + when + "：action 为空"); return; }
                log.Info("[键位] " + when + " enabled=" + a.enabled + " shouldBeEnabled=" + a.shouldBeEnabled);
                if (a.bindings == null) return;
                foreach (ProxyBinding b in a.bindings)
                {
                    string p = string.IsNullOrEmpty(b.path) ? "(empty)" : b.path;
                    string c = "none";
                    try
                    {
                        var vs = b.conflicts;
                        if (vs != null && vs.Count > 0)
                        {
                            c = "";
                            for (int i = 0; i < vs.Count; i++)
                            {
                                ProxyBinding other = vs[i];
                                c += (i > 0 ? " ; " : "") + other.mapName + "/" + other.actionName
                                    + "[" + other.name + "]=" + other.path
                                    + (other.isBuiltIn ? "(built-in)" : "(mod)");
                            }
                        }
                    }
                    catch (Exception) { }
                    log.Info("[键位] " + when + " device=" + b.device + " path=" + p
                        + " isSet=" + b.isSet + " hasConflicts=" + b.hasConflicts + " vs " + c);
                }
            }
            catch (Exception e)
            {
                log.Warn("[键位] 诊断失败：" + e.GetType().Name + " " + e.Message);
            }
        }

        /// <summary>
        /// 进游戏/编辑器场景后再打一次键位状态。加载时 enabled=False 往往只是场景还没就绪，
        /// 只有"已经在场景里、activeTool 存在、enabled 仍是 False"才说明快捷键真的不会触发；
        /// 0.3.1 分不清这两种情况，所以按了没反应时无从判断断在哪一层。
        /// </summary>
        public void ReportKeysInGame()
        {
            try
            {
                ProxyAction a = m_Setting.GetAction(BusLineAutoStopsSetting.kActionToggleLazy);
                if (a == null)
                {
                    log.Warn("[键位] 进游戏后：action 为空");
                    return;
                }
                log.Info("[键位] 进游戏后 enabled=" + a.enabled + " shouldBeEnabled=" + a.shouldBeEnabled);
            }
            catch (Exception e)
            {
                log.Warn("[键位] 进游戏后诊断失败：" + e.GetType().Name);
            }
            ReportKeyBindings("进游戏后");
        }

        /// <summary>未绑键路径不得启用 action（Playbook §3.2 幻影触发）。</summary>
        public static bool IsBindablePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Trim();
            if (p.Length == 0) return false;
            if (p == "<Keyboard>/" || p == "<Keyboard>") return false;
            int slash = p.IndexOf('/');
            if (slash < 0 || slash >= p.Length - 1) return false;
            return true;
        }

        /// <summary>快捷键 / 前端按钮 共用入口。</summary>
        public void OnToggleLazy(string source = "快捷键")
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            StationSelectTracker.EnsureFresh();
            log.Info("[入口] OnToggleLazy source=" + source + " lazy=" + LazyModeState.LazyEnabled
                + " enabled=" + LazyModeState.Enabled
                + " lineKind=" + LazyModeState.ActiveLineKind
                + " station=" + StationAssets.SafeName(LazyModeState.SelectedStationPrefab));

            if (!LazyModeState.Enabled)
            {
                LazyModeState.ShowWarning(LocaleTable.PanelKey("WARN_MASTER_OFF"), 2.5f, now);
                return;
            }

            if (LazyModeState.LazyEnabled)
            {
                ExitLazyMode();
                return;
            }

            // 需求 3（v0.6.0）：进入懒人模式**不需要先选中站台，也不再切换任何工具**。
            // 这里只是给资产面板的过滤定一个起点类别：优先玩家当前选中的站台资产类别，
            // 其次线路工具正在画的类别，都没有就按公交算（需求 5，0.9.0：这条已经被实机确认——
            // 空手按快捷键也会 `[线路工具] 已请求切到 Bus Line 结果=True`）。玩家随后点了别的
            // 同类资产，LazyModeSystem / StationSelectTracker 会让类别跟着走。
            // 需求 4（0.9.0）：判定用 IsLazyModeEnabled —— 电车本版暂未上线，画电车线时按快捷键
            // 不进入懒人模式（给专门的提示），而不是把电车当成"支持的类别"跑半套逻辑。
            TransportKind kind = TransportKind.Bus;
            if (TransportKindRules.IsLazyModeEnabled(LazyModeState.SelectedStationKind, LazyModeState.TramEnabled)
                && LazyModeState.SelectedStationKind != TransportKind.None)
            {
                kind = LazyModeState.SelectedStationKind;
            }
            else if (TransportKindRules.IsLazyModeEnabled(LazyModeState.ActiveLineKind, LazyModeState.TramEnabled)
                && LazyModeState.ActiveLineKind != TransportKind.None)
            {
                kind = LazyModeState.ActiveLineKind;
            }

            if (!TransportKindRules.IsLazyModeEnabled(kind, LazyModeState.TramEnabled))
            {
                // 火车/地铁/机场/码头走的是车站（Station）体系，放置规则跟道路无关，不放开；
                // 有轨电车是"机制还没做完"（见 BusLineAutoStopsSetting.TramEnabled），单独一句话告诉玩家为什么。
                LazyModeState.ShowWarning(UnsupportedLineKey(kind), 3f, now);
                log.Info("[懒人模式] 拒绝进入：当前类型 " + kind + " 本版未放开（公交=开，电车="
                    + LazyModeState.TramEnabled + "）");
                return;
            }

            LazyModeState.ModeKind = kind;
            // 需求 4（0.9.0）：起点类别和"手上选中的站台资产"不是一类时（电车暂未上线却选了电车站、
            // 或画着公交线手里拿的是电车站），把手上的选择让位给类别。懒人模式只按 ModeKind 取资产，
            // 绝不拿跨类资产去预览或落地——那会让"看到的"和"放下的"不是同一种东西。
            if (LazyModeState.SelectedStationKind != kind)
            {
                LazyModeState.SelectedStationPrefab = null;
                LazyModeState.SelectedStationKind = TransportKind.None;
            }

            // 需求 3：进入懒人模式必须在创建线路工具下进行，切工具这件事交给系统帧做
            LazyModeState.PendingLineKind = kind;
            LazyModeState.LazyEnabled = true;
            LazyModeState.UiSeq++;
            // 需求 3：每次进入都重算资产面板过滤集（换存档/换资产包后站台实体的 index:version 会变，
            // 用缓存的旧名单会藏错东西）。AssetFilterService 的缓存就按这个序号失效。
            LazyModeState.InvalidateAssetFilter();
            LazyModeSystem.BeginSession();
            log.Info("[懒人模式] 开启 kind=" + kind + " 当前工具=" + ActiveToolName()
                + " 面板起点类别=" + kind);
        }

        /// <summary>
        /// 这类线路不让进懒人模式时给玩家看哪句话（需求 4，0.9.0）：
        /// 有轨电车是"暂未上线"，火车/地铁/机场/码头才是"本模组不管这一类"。
        /// </summary>
        internal static string UnsupportedLineKey(TransportKind kind)
        {
            return LocaleTable.PanelKey(kind == TransportKind.Tram ? "WARN_TRAM_OFF" : "WARN_UNSUPPORTED_LINE");
        }

        /// <summary>日志用：当前工具叫什么（拿不到就写 None，绝不因为记日志影响功能）。</summary>
        private string ActiveToolName()
        {
            try
            {
                if (World.DefaultGameObjectInjectionWorld == null) return "None";
                ToolSystem ts = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<ToolSystem>();
                ToolBaseSystem t = ts != null ? ts.activeTool : null;
                return t == null ? "None" : t.GetType().Name;
            }
            catch (Exception) { return "Unknown"; }
        }

        /// <summary>
        /// 需求 4：世界侧系统发现"已经不在创建线路工具里"就走这里退出。
        /// 与玩家手动关闭的区别只有日志里的原因，处置策略仍然照玩家选的走。
        /// </summary>
        public void RequestExitLazyMode(string why)
        {
            if (!LazyModeState.LazyEnabled) return;
            log.Info("[懒人模式] 自动退出：" + why);
            ExitLazyMode();
        }

        private void ExitLazyMode()
        {
            ExitDeletePolicy policy = LazyModeState.ExitPolicy;
            LazyModeState.LazyEnabled = false;
            LazyModeState.ModeKind = TransportKind.None;
            LazyModeState.UiSeq++;

            // 世界改动只能在系统帧内做（原版工具的输出窗口），这里只挂请求
            LazyModeState.PendingExitPolicy = policy;
            LazyModeState.PendingExitCleanup = true;

            log.Info("[懒人模式] 手动关闭，退出清理请求已排队 policy=" + policy);
        }

        /// <summary>
        /// 总开关被关掉：懒人模式如果在开着就退出（走玩家自己选的退出策略），
        /// 并把快捷键 action 停掉，避免"按钮没了、键还能用"。
        /// </summary>
        public void TurnOffIfLazy()
        {
            ApplyMasterToHotkey();
            if (LazyModeState.LazyEnabled)
            {
                ExitLazyMode();
            }
        }

        /// <summary>
        /// 需求 1（v0.12.0）：总开关**打开**时也要把快捷键接回来。
        ///
        /// 0.11.x 的真 bug（玩家报"每次开启或关闭总开关后快捷键就没用了"）：只有关的那条路径
        /// 调了 <see cref="ApplyMasterToHotkey"/>（`shouldBeEnabled=false`），打开时**没有任何一处**
        /// 把它设回 true。于是：
        ///   · 关一次 → action 停用；
        ///   · 再开 → 图标回来了、按钮能点了，但 action 一直停着，按 G 毫无反应；
        ///   · 每次关/开都把这条路再确认一遍，看起来就像"用一次就坏"。
        /// 实机日志 2026-10-20 05:53:00/05:53:08/05:53:17/05:53:26 四行 `[总开关]` 只在关的那两次
        /// 后面跟了 `[快捷键] 跟随总开关 shouldBeEnabled=False`，开的那两次一条都没有 —— 取证一致。
        /// </summary>
        public void TurnOnHotkey()
        {
            ApplyMasterToHotkey();
        }

        /// <summary>总开关关掉时禁用快捷键；打开时按"有没有绑了可用的键"决定。</summary>
        private void ApplyMasterToHotkey()
        {
            try
            {
                ProxyAction a = m_Setting.GetAction(BusLineAutoStopsSetting.kActionToggleLazy);
                if (a == null) return;
                ProxyBinding b = m_Setting.ToggleLazyBinding;
                a.shouldBeEnabled = LazyModeState.Enabled && IsBindablePath(b != null ? b.path : null);
                log.Info("[快捷键] 跟随总开关 shouldBeEnabled=" + a.shouldBeEnabled);
            }
            catch (Exception e)
            {
                log.Warn("[快捷键] 更新启用状态失败：" + e.GetType().Name);
            }
        }

        // v0.6.0 删掉了 ExitLazyModeCompleted(bool applied)：那是"懒人模式占用线路工具、线路画完
        // 自动退出"那套设计的产物。本版懒人模式不拥有任何工具（只是代替玩家按下放置键），
        // 所以退出只发生在图标/快捷键/总开关上；线路自己完成或取消都跟懒人模式无关
        // （需求 5：原版"线路没完成就退出时停靠点本来就不保留"，我们不对抗、也不参与判定）。
    }
}
