using Game.UI;
using Colossal.UI.Binding;
using Game;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;

namespace BusLineAutoStops.Systems
{
    /// <summary>
    /// 向前端推送：懒人模式开关状态、警告词条 key；接收按钮点击。
    /// 前端 GameTopLeft 按钮只读这些绑定；文案 id 走 cs2/l10n / Translate（BLG 可翻）。
    ///
    /// 两个绑定的类型都不能写错（0.4.1 实机就是这么坏的）：
    /// - 状态用 <see cref="GetterValueBinding{T}"/> 且必须 <c>AddUpdateBinding</c>：
    ///   `AddBinding` 只注册不轮询（Game.UI/UISystemBase.cs:48-58 —— AddUpdateBinding 才会进
    ///   m_UpdateBindings），于是前端拿到的是挂载那一刻的 JSON 并**永远不变** → 图标不会变红。
    /// - 点击用 <see cref="TriggerBinding{T}"/>：JS 侧 `trigger()` 只会打在 TriggerBinding 上
    ///   （TriggerBinding.Attach → view.RegisterForEvent(path)）。0.4.1 注册的是 CallBinding，
    ///   前端却调 trigger()，于是点击静默丢失：实机 UI.log 有 4 次 `button clicked`，
    ///   模组日志 0 次 `[入口] UI 按钮点击`。CallBinding 要用 JS 的 call()，我们不用它。
    ///   （同机验证过的做法见 BlueprintHub：TriggerBinding&lt;string&gt; + trigger(GROUP, CMD, payload)。）
    /// </summary>
    public partial class UiCommandSystem : UISystemBase
    {
        public const string kGroup = "BusLineAutoStops";

        /// <summary>前端 trigger 用的名字，改它必须同时改 Frontend/src/index.tsx。</summary>
        public const string kToggleTrigger = "ToggleLazy";

        /// <summary>需求 5：前端 call() 取资产面板过滤集（见 AssetFilterService 的说明）。</summary>
        public const string kAssetFilterCall = "GetAssetFilter";

        /// <summary>
        /// 需求 3（v0.8.0）：懒人模式中点面板里的站台资产，前端不再走原版 selectAsset
        /// （那会把工具切成物件工具，按需求 4 立刻退出懒人模式），改为把实体键发给我们。
        /// 这里把它记成"预览/提交用的站台资产"，工具保持在线路工具上。
        /// </summary>
        public const string kSelectStopAsset = "SelectStopAsset";

        /// <summary>
        /// 状态兜底拉取：前端每秒 call() 一次拿同一份 JSON。
        /// 为什么要有它——图标"开/关"变红这件事在 0.5.0/0.6.0 之间反复失败，失败点都是
        /// **推送通道**（AddUpdateBinding 的轮询），而不是状态本身：状态一直在变，
        /// 前端停在挂载那一刻的 JSON 上。0.6.0 已经按取证把根因修了（OnUpdate 里补 base.OnUpdate()），
        /// 但这条通道归游戏管，万一某个版本又不轮询了，玩家看到的还是"点了没变红"。
        /// 有了兜底拉取：seq 不一致时前端自己纠正，并且 UI.log 会留下
        /// "poll 取到更新的 seq" —— 这行出现就说明推送通道又坏了，一眼分诊。
        /// </summary>
        public const string kStateCall = "PullUiState";

        /// <summary>
        /// 面板过滤自检：前端把它那边的匹配计数（items/keyed/allowed/foreign + 条目字段名样本）
        /// 发回来进模组日志。0.8.1 实机 `[资产过滤] … 生效=Y` 但玩家看不出任何筛选，
        /// 而前端的降级是静默的（匹配不到就原样返回），console 里的日志玩家不会给我看——
        /// 所以这条通道把"到底匹配上没有"落到 BusLineAutoStops.log 里。
        /// </summary>
        public const string kFilterDiag = "FilterDiag";

        private GetterValueBinding<string> m_stateBinding;
        private int m_lastSeq = -1;
        private string m_lastJson = "";

        /// <summary>
        /// v0.11.0 需求 1：懒人模式面板高亮要的"当前站台资产实体键"。
        ///
        /// 为什么值得单独一套缓存：GetUiState 每帧都被 AddUpdateBinding 的轮询调一次
        /// （Game.UI/UISystemBase.cs:36-42），稳态必须只有一次引用比较，不能每帧
        /// 去拼 `index:version` 字符串、更不能每帧去解析实体。
        /// 资产引用没换就直接复用上一个键；换了才算一次。
        /// </summary>
        private PrefabBase m_assetKeyPrefab;
        private string m_assetKey = "";

        /// <summary>已经随 JSON 推出去的键。键变了但序号没动时靠它发现"该重新推了"。</summary>
        private string m_sentAssetKey = "";

        /// <summary>
        /// 懒人模式正在用的那枚站台资产的实体键（`index:version`，与前端 ItemGrid 的
        /// 条目主键同一种格式）；不在懒人模式或没有资产时是空串。
        ///
        /// 只在 <see cref="LazyModeState.LazyEnabled"/> 为真时给值：面板高亮是懒人模式
        /// 的区分特性（线路工具那一条由原版自己点亮，站台资产这一条原版永远不点亮 ——
        /// 懒人模式里点站台资产走 SelectStopAsset，工具不切、原版选择不换）。
        /// </summary>
        private string LazyAssetKey()
        {
            PrefabBase pb = LazyModeState.SelectedStationPrefab;
            if (!ReferenceEquals(pb, m_assetKeyPrefab))
            {
                m_assetKeyPrefab = pb;
                m_assetKey = "";
                if (LazyModeState.LazyEnabled && pb != null)
                {
                    try
                    {
                        Entity e = StationAssets.PrefabEntity(pb);
                        if (e != Entity.Null) m_assetKey = e.Index + ":" + e.Version;
                    }
                    catch (System.Exception)
                    {
                        m_assetKey = "";   // 解析失败就当没有：前端只会退回原版高亮
                    }
                }
            }
            return m_assetKey;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            // AddUpdateBinding 而不是 AddBinding：见类注释第一条。
            AddUpdateBinding(m_stateBinding = new GetterValueBinding<string>(
                kGroup, "GetUiState", GetUiState));

            AddBinding(new TriggerBinding<string>(kGroup, kToggleTrigger, _ => CallToggle()));

            // 需求 3（v0.8.0）：懒人模式中点站台资产 → 换预览资产，不切工具（见 kSelectStopAsset 注释）。
            AddBinding(new TriggerBinding<string>(kGroup, kSelectStopAsset, key => CallSelectStopAsset(key)));

            // 面板过滤自检进模组日志（前端是"匹配不到就原样返回"的静默降级，console 玩家看不到）。
            AddBinding(new TriggerBinding<string>(kGroup, kFilterDiag, payload =>
            {
                BusLineAutoStopsMod.log.Info("[面板过滤·前端] " + payload);
            }));

            // 过滤集可能有几百条，只在 filterSeq 变化时由前端 call() 取一次，不进每帧 JSON。
            AddBinding(new CallBinding<string, string>(kGroup, kAssetFilterCall, _ =>
            {
                try { return AssetFilterService.BuildJson(); }
                catch (System.Exception e)
                {
                    BusLineAutoStopsMod.log.Warn("资产过滤名单生成失败：" + e.GetType().Name + " " + e.Message);
                    return "{\"on\":false,\"kind\":0,\"seq\":-1,\"allow\":[],\"universe\":[]}";
                }
            }));

            // 状态兜底：内容就是 GetUiState 那一小串（seq 没变就直接吃缓存，几乎零成本）。
            AddBinding(new CallBinding<string, string>(kGroup, kStateCall, _ =>
            {
                try { return GetUiState(); }
                catch (System.Exception e)
                {
                    BusLineAutoStopsMod.log.Warn("PullUiState 失败：" + e.GetType().Name + " " + e.Message);
                    return "";
                }
            }));
        }

        private string GetUiState()
        {
            // 轻量 JSON，避免额外依赖
            int seq = LazyModeState.UiSeq;

            // v0.11.0 需求 1：同类站台之间换资产不碰 ModeKind、也不 bump UiSeq
            // （CallSelectStopAsset 与 StationSelectTracker 都只在类别变化时 bump），
            // 而面板要的"点亮哪一枚站台"恰恰只有这个实体键。所以序号没动、键却变了的时候
            // 在这里主动 bump 一次，让 JSON 重新生成并推给前端。只有"前端已经持有旧 JSON"
            // （m_lastJson 非空且序号没变）时才需要补这一下，否则紧接着的重建本身就带着新键。
            string assetKey = LazyAssetKey();
            if (assetKey != m_sentAssetKey)
            {
                bool needPush = m_lastJson.Length > 0 && seq == m_lastSeq;
                m_sentAssetKey = assetKey;
                if (needPush)
                {
                    LazyModeState.UiSeq++;
                    seq = LazyModeState.UiSeq;
                }
            }
            if (seq == m_lastSeq && m_lastJson.Length > 0) return m_lastJson;

            float now = UnityEngine.Time.realtimeSinceStartup;
            bool warn = LazyModeState.HasWarning(now);
            string warnKey = warn ? LazyModeState.WarningLocaleKey : "";
            string json = "{\"enabled\":"
                + (LazyModeState.LazyEnabled ? "true" : "false")
                + ",\"master\":"
                + (LazyModeState.Enabled ? "true" : "false")
                + ",\"seq\":" + seq
                + ",\"warningKey\":\"" + Escape(warnKey) + "\""
                + ",\"modeKind\":" + (int)LazyModeState.ModeKind
                // filterSeq 必须是**名单自己的**版本号：拿 UiSeq（图标状态/警告那种小变化）
                // 顶替会让前端每隔几十毫秒重取一次名单并重复自检上报（实机一次会话 542 行）。
                + ",\"filterSeq\":" + LazyModeState.AssetFilterSeq
                // v0.11.0 需求 1：懒人模式当前站台资产的实体键，前端拿它在资产面板里
                // 点亮这一枚（键为空 = 不在懒人模式，前端照原版语义只点亮一个）。
                + ",\"lazyAsset\":\"" + Escape(assetKey) + "\""
                + "}";
            m_lastSeq = seq;
            m_lastJson = json;
            return json;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>前端点击按钮时调用。</summary>
        private void CallToggle()
        {
            // 入口留一行日志：实机如果看不到这条，说明点击根本没到 C#（按钮被盖住 / 没绑上），
            // 而不是懒人模式逻辑有问题——0.3.1 就是因为分不清这两层才绕了远路。
            BusLineAutoStopsMod.log.Info("[入口] UI 按钮点击 → CallToggleLazy");
            try
            {
                if (BusLineAutoStopsMod.Instance != null) BusLineAutoStopsMod.Instance.OnToggleLazy("UI按钮");
            }
            catch (System.Exception e)
            {
                BusLineAutoStopsMod.log.Warn("UI Toggle 失败：" + e.Message);
            }
        }

        /// <summary>
        /// 需求 3（v0.8.0）：懒人模式中点面板里的站台资产 → 只换"预览/提交用的资产"，
        /// 绝不动工具。类别跟着资产走（公交↔电车），过滤集与索引随之失效。
        /// </summary>
        private void CallSelectStopAsset(string key)
        {
            try
            {
                if (!LazyModeState.Enabled || !LazyModeState.LazyEnabled) return;
                PrefabBase pb = StationAssets.PrefabFromEntityKey(key);
                if (pb == null)
                {
                    BusLineAutoStopsMod.log.Warn("[选中资产·懒人] 实体键解析失败：" + key);
                    return;
                }
                if (!StationAssets.TryClassifyStop(pb, out TransportKind kind)
                    || !TransportKindRules.IsSelectableDuringLazy(kind, LazyModeState.TramEnabled))
                {
                    return; // 不是放开的站台资产（理论上过滤集拦住了，不会走到这）
                }
                LazyModeState.SelectedStationPrefab = pb;
                LazyModeState.SelectedStationKind = kind;
                BusLineAutoStopsMod.log.Info("[选中资产·懒人] kind=" + kind
                    + " asset=" + StationAssets.SafeName(pb) + "（工具保持线路工具）");
                if (LazyModeState.ModeKind != kind)
                {
                    LazyModeState.ModeKind = kind;
                    LazyModeState.UiSeq++;
                    LazyModeState.InvalidateAssetFilter();
                    LazyModeSystem.RequestAssetRefresh();
                }
            }
            catch (System.Exception e)
            {
                BusLineAutoStopsMod.log.Warn("SelectStopAsset 失败：" + e.GetType().Name + " " + e.Message);
            }
        }

        private bool m_inGameReported;

        protected override void OnUpdate()
        {
            // 实机取证（v0.5.0 → 0.6.0）：Game.UI/UISystemBase.cs:36-42 的 OnUpdate 才是
            // 遍历 m_UpdateBindings 调 Update() 的地方。我们覆盖 OnUpdate 却没调 base，等于
            // GetterValueBinding 一次都没被轮询：前端停在挂载那一刻的 JSON 上。
            // 0.5.0 实机 UI.log 只有 1 行 `state init enabled=false`、模组日志却有 8 次开关，
            // 就是这条 —— 图标不变红（需求 4）和资产面板没筛选（需求 3）是同一个根因，
            // 因为过滤集的拉取也是拿 enabled/filterSeq 起头的。
            base.OnUpdate();

            LazyModeState.ClearWarningIfExpired(UnityEngine.Time.realtimeSinceStartup);

            // 总开关关了就不做任何轮询识别（本系统每帧都跑，省一次是一次）
            if (!LazyModeState.Enabled) return;

            StationSelectTracker.Poll(World);

            // 场景真的就绪了（activeTool 存在）才按键位诊断，否则看到的 enabled=False 只是噪音。
            if (!m_inGameReported)
            {
                try
                {
                    if (m_tool == null) m_tool = World.GetExistingSystemManaged<ToolSystem>();
                    if (m_tool != null && m_tool.activeTool != null)
                    {
                        m_inGameReported = true;
                        if (BusLineAutoStopsMod.Instance != null) BusLineAutoStopsMod.Instance.ReportKeysInGame();
                    }
                }
                catch (System.Exception) { }
            }
        }

        private ToolSystem m_tool;
    }
}
