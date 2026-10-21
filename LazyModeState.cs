using System;
using Game.Prefabs;

namespace BusLineAutoStops
{
    /// <summary>
    /// 热路径状态镜像（Playbook 硬规则 15：每帧不读设置属性链）。
    /// 由 StationSelectTracker / Setting / UI 写入，Systems 只读。
    ///
    /// v0.6.0 精简：懒人模式不再"占用线路工具"，所以跟着那套设计走的字段全删了
    ///  · ActiveStationPrefab —— 现在直接看当前工具资产（StationSelectTracker 记的 SelectedStationPrefab）
    ///  · PendingAssetView*   —— 不再有"退出线路视图后下次回到站台资产视图"这件事
    ///  · PlacedThisSession / RepairedThisSession —— 会话计数在 AutoPlaceSystem 自己手里，
    ///    "给站台补 ConnectedRoute 缓冲"这种对抗原版的事也不做了
    /// 资产身份直接持有 PrefabBase（旧版把 Entity 序列化成 "index:version" 字符串再解析，
    /// 既无必要也会在域重载后失效）。
    /// </summary>
    public static class LazyModeState
    {
        /// <summary>
        /// 总开关（设置页最上面的「开启公交站自动放置」，默认开）。关掉时：
        /// 左上角按钮不出现、快捷键无效、懒人模式立即退出，系统一律早退。
        /// </summary>
        public static volatile bool Enabled = true;

        /// <summary>懒人模式（左上角按钮/快捷键那个开关）当前是否开着。</summary>
        public static volatile bool LazyEnabled;

        /// <summary>
        /// 资产面板过滤与自动落地认的类别。进入懒人模式时给一个起点，
        /// 之后玩家点了哪类站台资产（公交站/有轨电车站）就由 StationSelectTracker 改成哪类。
        /// </summary>
        public static volatile TransportKind ModeKind = TransportKind.None;

        /// <summary>玩家当前选中的站台资产（ObjectTool 的 activePrefab）；可为 null。</summary>
        public static volatile PrefabBase SelectedStationPrefab;
        public static volatile TransportKind SelectedStationKind = TransportKind.None;

        /// <summary>当前激活的"线路工具"在画哪类线路（RouteToolSystem.prefab）；未在用线路工具则为 None。</summary>
        public static volatile TransportKind ActiveLineKind = TransportKind.None;
        public static volatile PrefabBase ActiveLinePrefab;

        /// <summary>
        /// 退出懒人模式时的处置策略（需求 5：只有保留 / 不保留两项）。
        ///
        /// 默认值**只在 <see cref="BusLineAutoStopsSetting.SetDefaults"/> 一处决定**（v0.12.0 起是"不保留"）。
        /// 这里的字段初值只是"热路径镜像从没被同步过"时的死值，正常流程一定会被
        /// <see cref="BusLineAutoStopsSetting.SyncToState"/> 覆盖，所以刻意写成与 SetDefaults 同值只为读代码不误导。
        /// </summary>
        public static volatile ExitDeletePolicy ExitPolicy = TransportKindRules.DefaultExitPolicy();

        /// <summary>
        /// 需求 4（0.9.0）：有轨电车是否放开。本版恒 false（BusLineAutoStopsSetting.SyncToState 强制归零），
        /// 设置页那条是置灰的"有轨电车站自动放置（暂未上线）"。
        /// 它只影响"懒人模式能用哪些类别"，不影响资产识别 / 面板过滤 / 查重这些**分类**逻辑。
        /// </summary>
        public static volatile bool TramEnabled = false;

        /// <summary>
        /// 需求 3（v0.7.0）：进入懒人模式时想要哪一类线路工具。由按钮/快捷键回调置上，
        /// 真正 `ActivatePrefabTool` 在 LazyModeSystem 帧内做（改工具不是世界改动，但放系统帧里
        /// 才能拿到稳定的 activeTool 复查与失败回退）。None = 不请求切换。
        /// </summary>
        public static volatile TransportKind PendingLineKind = TransportKind.None;

        /// <summary>
        /// 需求 2（v0.7.0）：线路工具里那个还没按左键的路径点，已经在同一个落点停满 0.1s。
        /// 本版只给前端一个"模组认下这个落点"的显示，不改世界、不点击。
        /// </summary>
        public static volatile bool DwellReady;

        /// <summary>
        /// 退出待执行的清理（在系统帧内消费，不在按键回调里改世界）。
        /// <see cref="PendingExitPolicy"/> 是排队那一刻写上的真实策略，初值同样是死值
        /// （退出请求一定先写它再置 PendingExitCleanup），默认值只在 SetDefaults 一处决定。
        /// </summary>
        public static volatile bool PendingExitCleanup;
        public static volatile ExitDeletePolicy PendingExitPolicy = TransportKindRules.DefaultExitPolicy();

        /// <summary>前端按钮高亮 / 警告文案 key（空=无警告）。</summary>
        public static volatile string WarningLocaleKey = "";
        public static volatile float WarningUntilRealtime;

        /// <summary>系统注册等致命失败：置了以后所有热路径一律早退，绝不半吊子介入游戏。</summary>
        public static volatile bool Broken;

        /// <summary>UI 推送用序号，前端 bindValue 轮询（真正的轮询靠 UiCommandSystem 调 base.OnUpdate）。</summary>
        public static int UiSeq;

        /// <summary>
        /// 需求 3：资产面板过滤集的序号。前端只在它变化时用 call() 重新取一次名单
        /// （名单可能有几百条，绝不能塞进每帧推送的 GetUiState 里）。
        /// </summary>
        public static int AssetFilterSeq;

        public static void InvalidateAssetFilter()
        {
            AssetFilterSeq++;
        }

        public static void ShowWarning(string localeKey, float durationSeconds, float nowRealtime)
        {
            WarningLocaleKey = localeKey ?? "";
            WarningUntilRealtime = nowRealtime + durationSeconds;
            UiSeq++;
        }

        public static void ClearWarningIfExpired(float nowRealtime)
        {
            if (WarningLocaleKey.Length > 0 && nowRealtime > WarningUntilRealtime)
            {
                WarningLocaleKey = "";
                UiSeq++;
            }
        }

        public static bool HasWarning(float nowRealtime)
        {
            return WarningLocaleKey.Length > 0 && nowRealtime <= WarningUntilRealtime;
        }

        /// <summary>
        /// 新世界建立（进城 / 换存档 / 重载模组）时由 <c>LazyModeSystem.OnCreate</c> 调用一次。
        /// 这些是**进程级静态**：CS2 换一个世界不会重新加载模组程序集，不清就会把上一场的状态带进新场。
        /// 最危险的一条是 <see cref="SelectedStationPrefab"/> —— 它是上个世界已经被销毁的 PrefabBase，
        /// 拿它去解析 prefab 实体、再喂给原版的创建管线，属于"对失效对象调引擎 API"这一类崩溃源头
        /// （BlueprintHub 崩溃交接自查第 3 条：缓存的生命周期）。
        /// <see cref="Enabled"/> 是玩家的总开关、来自设置文件，本来就该跨场存在，所以**不清它**。
        /// </summary>
        public static void ResetForNewWorld()
        {
            LazyEnabled = false;
            PendingExitCleanup = false;
            // 不重置 PendingExitPolicy：它只在退出请求排队那一刻与 PendingExitCleanup 一起写
            // （BusLineAutoStopsMod.ExitLazyMode），而这里已经把 PendingExitCleanup 清成 false，
            // 那一行就是死赋值。默认值只在 BusLineAutoStopsSetting.SetDefaults 一处决定。
            PendingLineKind = TransportKind.None;
            ModeKind = TransportKind.None;
            SelectedStationPrefab = null;
            SelectedStationKind = TransportKind.None;
            ActiveLinePrefab = null;
            ActiveLineKind = TransportKind.None;
            DwellReady = false;
            WarningLocaleKey = "";
            WarningUntilRealtime = 0f;
            UiSeq++;
            AssetFilterSeq++;
        }

        /// <summary>会话边界复位（本版没有跨会话计数，留着这个口子给 UI 序号对齐）。</summary>
        public static void ResetSessionCounters()
        {
            // 会话边界把提示清空；真的清掉了东西才 UiSeq++，
            // 不然前端会停在旧 JSON 上、把提示继续显示着。
            bool changed = false;
            if (WarningLocaleKey.Length > 0)
            {
                WarningLocaleKey = "";
                changed = true;
            }
            if (changed) UiSeq++;
        }
    }
}
