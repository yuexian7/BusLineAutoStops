using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BusLineAutoStops.Systems
{
    /// <summary>
    /// v0.8.1 的世界侧系统：需求 2/5 的"预览公交站 + 左键才落地"在这里实现。
    ///
    /// 机制（全部按 1.6.2 反编译取证，file:line 见开发笔记 v0.8.0/v0.8.1 两节）：
    ///
    /// 1) **预览**：悬停路径点（RouteToolSystem.GetControlPoints 的最后一个控制点，公开 API）
    ///    静止满 0.1s → 本系统喂一枚定义实体
    ///    `CreationDefinition{m_Prefab=站台资产, m_Attached=解析出的合法父级, m_Flags=Attach|Optional}
    ///    + ObjectDefinition + Updated`（走 ToolOutputBarrier，与原版工具同一条通道）。
    ///    帧内真实顺序（校正 #66/#67，别按 SystemUpdatePhase 的数字大小想）：MainLoop 里
    ///    ToolSystem.OnUpdate 先级联 PreTool→ToolUpdate（各工具 → 我们 → ToolOutputSystem→ClearTool/ApplyTool→PostTool），
    ///    然后 ModificationSystem 级联 Modification1（GenerateObjectsSystem 出 Temp 幽灵）→ Modification4
    ///    （AttachPositionSystem 吸人行道）→ ModificationEnd（ValidationSystem）。
    ///    所以**本帧喂的定义同帧就出幽灵**；光标继续不动时线路工具写 applyMode=None（RouteToolSystem.cs:2166），
    ///    Temp 留得住 = 预览显示；
    ///    光标一动写 Clear → ToolClearSystem 销毁所有 Temp（:218 查询只有 Temp）= 预览消失。
    ///    Optional 标记让原版校验**自动撤销**我们幽灵上的错误（ValidationSystem.cs:1659 CancelOptional），
    ///    预览永远不挡玩家的点击。喂出去下一帧把幽灵**认领**回来登记成"世上唯一那一枚"
    ///    （MaintainPreview：它还活着就一帧都不再喂；被原版 Clear 收走了才补喂），
    ///    并在认领那一刻按吸附后的实际落位做查重。
    ///    0.8.2 是无条件每帧喂 —— 光标停住时原版写 None 不清 Temp，一秒堆 60 枚幽灵，
    ///    玩家截图"一停就冒出一大排停靠点图标"，见 #78 的补充。
    ///
    /// 1b) **父级解析**（v0.8.1 的核心修正）：控制点的 `m_OriginalEntity` 是射线结果的 `m_Owner`
    ///    （ToolBaseSystem.cs:550-561），而且 SnapJob 会把它改写成"正在画的这条线"的 temp 线路
    ///    或既有线路实体（RouteToolSystem.cs:156-158、:183-191）。拿它当"能不能在路边放站台"的判据，
    ///    就是 0.8.0 实机把 27 次静止事件全拒、一个预览都没出的原因。现在按
    ///    `AttachPositionSystem.FixAttachedPosition`（:155-175）真正认的形态自己找父级：
    ///    工具给的 → 射线 m_Owner → 射线 m_HitEntity → 命中物 Owner 链，第一个"带 Node"或
    ///    "带 Composition+EdgeGeometry+Curve 且其 edge 有 NetCompositionArea 缓冲"的算数。
    ///
    /// 2) **左键落地**：点击帧线路工具写 Clear 并把点击位置收进控制点（cps.Length +1）。
    ///    本系统在 ToolOutputSystem 之前读到这次增长 → 也喂一枚定义（不带 Optional）；
    ///    同帧 Modification1 造出提交用 Temp；下一帧本系统找到它，先查 Game.Tools.Error
    ///    （有错就不转正，等原版清掉），没错误就照抄 ApplyObjectsSystem.cs:922-923 的转正：
    ///    移除 {Temp, Animation, BackSide}、加 {Applied, Created, Updated}（Game.Common.*）。
    ///    转正后它是普通永久建筑，ToolClearSystem 再也清不掉。
    ///
    /// 3) **路径点依附**：转正的当帧把控制点 cps[n-2] 的 m_OriginalEntity 改写成站台实体、
    ///    m_Position 改写成站台落位（GetControlPoints 返回的 NativeList 与工具共享缓冲，
    ///    写前 deps.Complete()）。线路完成那一下，RouteToolSystem.GetWaypointDefinition
    ///    （RouteToolSystem.cs:832）读到 m_OriginalEntity 身上有 ConnectedRoute 缓冲 →
    ///    WaypointDefinition.m_Connection = 站台 → 建成的线路停靠点真正连到站台
    ///    （WaypointConnectionSystem 反向回填 ConnectedRoute，:111-113）。
    ///    SnapJob 只处理最后一个控制点（RouteToolSystem.cs:132 index=Length-1），已提交点不会被碰。
    ///
    /// 4) **右键撤点**：Create 态 Cancel 是 RemoveAtSwapBack(最后一点)+覆盖悬停点
    ///    （RouteToolSystem.cs:1785-1800）→ cps.Length -1。长度 -1 = 撤一个停靠点：
    ///    先撤还没转正的待提交，再从已落地栈顶弹出一个，走 DeleteIfSafe（ConnectedRoute
    ///    活引用保护）。长度掉 ≥2 只可能是线路完成（Clear 清空+补悬停点，:1967 Apply）→
    ///    站台全部保留、账本清零。
    ///
    /// 热路径纪律（需求 6）：懒人模式没开一个 bool 早退；定义只在静止事件沿/点击沿各喂一次，
    /// 平稳帧零 ECS 读写；索引 0.5s（回退 2s）节流。
    /// </summary>
    public partial class LazyModeSystem : GameSystemBase
    {
        /// <summary>
        /// 静止阈值（秒）。v0.6.0 是 0.3s，v0.8.2 按需求降到 0.1s，v0.10.0 回到 **0.18s**：
        /// 实机（2026-10-08 16:10 会话）`[预览无幽灵] … 世界 Temp 数=0` 有 204 次，配合玩家的
        /// "看起来有闪烁感" —— 0.1s 会被拖鼠标途中的自然微停顿触发，一停就喂、一动就收，
        /// 于是每 100ms 亮一次。人手"我是想停下来放个站"的停顿通常在 150~300ms 之间，
        /// 0.18s 既躲开路过式微停，又不到"觉得卡"的程度（再往上一档是 0.25s，手感就钝了）。
        /// </summary>
        internal const float kSettleSeconds = 0.18f;

        /// <summary>落点位移超过这个值就算"还在移动"，静止计时重来。</summary>
        internal const float kMoveEpsilon = 0.5f;

        /// <summary>
        /// 预览的**淡出**时间（秒，需求 3 后半）：0.9.0 是"光标一动马上把幽灵标 Deleted 收走"，
        /// 结果幽灵活不到被认领（认领要等 2 帧）就被自己删了 → 闪烁 + 204 次"世界 Temp 数=0"。
        /// 现在改成连续移动满 0.15s 才收：微停顿（&lt;0.15s）幽灵原地不动，重新停住还沿用同一枚。
        /// </summary>
        internal const float kPreviewFadeSeconds = 0.15f;

        /// <summary>重新停住时，落点离"玩家正在看的那一枚"喂点不超过这个距离就直接沿用（米）。</summary>
        internal const float kPreviewKeepRadius = 2.5f;

        /// <summary>
        /// 射线真实命中点离工具悬停点最远允许多大（米），超过就当这条射线不可信、退回悬停点。
        /// 需求 1（v0.10.0）用命中点取回"光标真正在哪一侧"，但命中点偶尔会飞到建筑立面、
        /// 树冠或者另一条路上（悬停点已经被工具的 SnapJob 吸到线路上了），那种不能要。
        /// 四车道半宽 ~6m、八车道 ~13m，40m 足够宽又足够把飞点挡住。
        /// </summary>
        internal const float kCursorMaxDrift = 40f;

        /// <summary>喂出去之后**逐帧**认领，最多等这么多帧（0.9.0 只给 2 帧，卡一帧就算"没造出幽灵"）。</summary>
        internal const int kClaimMaxFrames = 10;

        /// <summary>
        /// "这里已经有同类站台了"的判距（米）。**量的是站台实际落位之间的距离，不是光标到站台**：
        /// 实机（2026-10-07 23:02 会话）预览幽灵的落位平均离悬停点 5~7m，四车道以上更宽，
        /// 拿悬停点量 10m 会把"隔着半条路"的对面站台算成重复，大路于是永远不放站。
        ///
        /// 0.9.0 再把 10m 收到 4m：实机（2026-10-08 03:22 会话）`[预览收回] … 相距 5.0m / 7.1m / 7.5m`
        /// —— 玩家刚放下一个站台，沿路再停 5~7 米想放第二个，就被这条 10m 判据当重复收回，
        /// 表现成"预览时现时不现、宽路上干脆不放"。这条规则的目的从来只是"别在同一处叠第二个"，
        /// 4m 足够；再远的间距归原版线路自己管（RouteData.m_MinWaypointDistance）。
        /// </summary>
        internal const float kSnapDistance = 4f;

        /// <summary>
        /// 需求 3（v0.13.0）："光标已进入既有站台的依附范围"的判距（米）。
        ///
        /// 为什么从 3m 放宽到 4m：实机 2026-10-21 01:08:18 的失败正是 3.07m —— 光标停在路口折线点上，
        /// 旁边 4m 内就是既有公交站，3m 的判据差一点点没拦住，于是喂了一枚模拟站台；那枚幽灵跟
        /// 站台/路面物件一碰就被 ValidationSystem 挂上 `Game.Tools.Error`，而
        /// `ToolBaseSystem.GetAllowApply()`（:541-543）只要全世界有任何 Error 就整帧为假 ——
        /// 玩家那一笔路径点（本该原版直接依附到站台上）被吞掉，表现成"在这个站台放路径点失败"。
        /// 4m 与 <see cref="kSnapDistance"/> 同值：反正落位在 4m 内也会被判成"同一处叠了两个"，不会误伤。
        /// 判断依据优先用**工具自己解析出来的实体**（`hover.m_OriginalEntity` 是带 ConnectedRoute 的站台
        /// = 原版 `FindWaypointLocation` 已经决定依附它），距离只是兜底。
        /// </summary>
        internal const float kStopAttachRadius = 4f;

        /// <summary>
        /// 需求 2（v0.13.0）：拖拽落地后，判定"哪一枚路径点是这次拖拽动到的"的半径（米）。
        /// 量的是路中央的 <c>Position</c>（拖拽落点就是它），宽路吸附后离光标横向差 5~10m 都属正常。
        /// </summary>
        internal const float kDragAttachRadius = 12f;

        /// <summary>
        /// 需求 2（v0.13.0）：松手后连续这么多帧都"光标不在落点"才作废。
        /// 松手帧原版先 Clear 控制点、再重新射线补一个（RouteToolSystem.cs:2039-2048），
        /// 采样点会跳 1~2 帧；0.12.1 实机（2026-10-21 01:08:28）就是被这一跳骗掉了整整一笔拖拽。
        /// </summary>
        internal const int kDragAwayGraceFrames = 3;

        /// <summary>
        /// 需求 2（v0.12.0）补提交的帧预算：松手那一帧解析不出父级时，最多再追这么多帧。
        /// 实机（2026-10-19 23:25:30 会话）松手帧的射线正压在另一枚站台上，下一帧同一个位置
        /// 就能解析出 `射线父级 … 预制体=Medium Road`；30 帧（约 0.5s）对"玩家还停在那儿"足够，
        /// 再多就会把站台落到玩家早就离开的地方。
        /// </summary>
        internal const int kDragRetryMaxFrames = 30;

        /// <summary>
        /// 补提交期间光标允许漂多远（米）：超过就认为玩家已经去忙别的了，这一笔作废
        /// （落点仍是松手处，但它已经不在光标下了，硬落就成了"凭空冒出一个站台"）。
        /// 6f = 一个站台的吸附量级（落位离悬停点通常 5~7m），再宽会把"顺手挪一挪"也算成没挪。
        /// </summary>
        internal const float kDragRetryRadius = 6f;

        /// <summary>
        /// 父级兜底（v0.12.0）：解析不到可依附父级时，若光标这么近的范围内有一枚同类站台，
        /// 就沿用它依附的那条路/那个节点 —— 站台本来就钉在玩家脚下这条路上。
        /// 只在失败路径上做一次索引查询（<see cref="StopIndex.Nearest"/>，无分配），不扫世界。
        /// </summary>
        internal const float kNearestStopParentRadius = 12f;

        /// <summary>
        /// 人行道被路灯 / 垃圾桶之类占住时的**沿路让位**阶梯（米，沿道路切向，0=原位）。
        /// 只在"原版根本没给出幽灵"这一档才往后退（见 ClaimPendingPreview）：
        /// 占位重叠在原版只是 Warning（站台照样能放，放下时障碍物自己消失），
        /// 0.9.0 把预览从 Optional 换成自控 Error 之后，Warning 已经不再让幽灵消失，
        /// 剩下要避的只有"原版直接不生成"这一种，让位阶梯是给它的兜底。
        ///
        /// 0.10.0 需求 1 把方向从**横向**改成**沿路**：横向让位会跨过道路中线，
        /// 光标在右、幽灵被让到左，正好把这条修正过的落位又弄回玩家报的那个 bug
        /// （实机 2026-10-08 16:10 会话 `[预览让位]` 814 次 —— 绝大多数是 0.9.0 那个
        /// 2 帧认领窗口误判出来的，见 kClaimMaxFrames，但阶梯本身的方向是错的）。
        /// 沿路挪一档同样能躲开"这一寸被占住"，而且永不换边。
        /// 绝不动世界里的任何物件：鼠标一移开，我们把幽灵标 Deleted 收走，障碍物一直都在原地。
        /// </summary>
        internal static readonly float[] kSlideNudges = { 0f, 2.5f, -2.5f, 5f, -5f };

        internal const float kRefreshSeconds = 0.5f;
        internal const float kFallbackRefreshSeconds = 2f;

        /// <summary>
        /// "这一类根本没有可用站台资产"的负缓存重试间隔（秒）。
        /// 没有这条的话，`TryGetStopPrefab` 走兜底分支时会**每帧**把 PrefabSystem 的全部资产
        /// 枚举一遍再逐条分类（`StationAssets.FindFallbackStop`），资产多的存档一帧就是上万次阅读 ——
        /// 与 BlueprintHub 崩溃交接里"404 没有负缓存 → 永久重试"完全同构，只是我们把重试做在了每帧。
        /// 类别一变 / 换资产 / 开新一场都会把 `m_fallbackKind` 清成 None 或把时刻归零，立刻恢复扫描。
        /// </summary>
        internal const float kFallbackRetrySeconds = 5f;

        /// <summary>
        /// 提交 Temp 的查找容差（米），**量的是"我们喂下去的那一处"到 Temp 实际落位**。
        /// 0.8.2 是 6m 且量的是路中央的停靠点：实机（2026-10-08 03:32）Large Road 上
        /// `[预览结果] 幽灵已出，离落点 8.6m` 紧跟着 `[落点提交] … 等不到原版生成，放弃`
        /// —— 幽灵早就造出来了，只是落在人行道，离路中央 8.6m，6m 半径根本没看见它。
        /// 六车道这条路于是"左键放不上站"，而小路（4.1m）一直正常。
        /// </summary>
        internal const float kCommitMatchTolerance = 20f;

        /// <summary>左键那一笔离"玩家看见的那枚幽灵"多远以内就直接用它转正（米）。</summary>
        internal const float kPreviewReuseRadius = 15f;

        /// <summary>停靠点匹配容差：依附改写要在控制点里认出"就是这一笔"（米）。</summary>
        internal const float kCpMatchTolerance = 8f;

        /// <summary>待提交最多等多少帧才放弃。</summary>
        internal const int kCommitMaxWaitFrames = 30;

        /// <summary>
        /// v0.12.0：原版工具**本帧**打算走哪条输出管线（<c>Game.Tools.ApplyMode</c>：None=0 / Apply=1 / Clear=2）。
        /// 三个数就是原版枚举的整数值（ApplyMode.cs:3-8），判断只用相等比较，绝不做位运算。
        ///
        /// 读的是 <c>ToolBaseSystem.applyMode</c> 这个**公开只读属性**（ToolBaseSystem.cs:182
        /// `public ApplyMode applyMode { get; protected set; }`）——setter 是 protected，
        /// 我们从外面连写都写不进去。0.5~0.7 那条"反射代按"链路（ToolApplyProxy + GetSetMethod）
        /// 依旧是退役状态，这两件事没有关系：本版只是**看一眼**原版这一帧要干什么，
        /// 既不注入、也不模拟玩家按键（见 pack-release.js 的"代按链路退役"钉子）。
        /// </summary>
        internal const int kPhaseNone = 0;
        internal const int kPhaseApply = 1;
        internal const int kPhaseClear = 2;

        private ToolSystem m_ToolSystem;
        private PrefabSystem m_PrefabSystem;

        private EntityQuery m_stopQuery;
        private EntityQuery m_objectQuery;
        private EntityQuery m_tempQuery;

        private ToolOutputBarrier m_OutputBarrier;

        internal static LazyModeSystem Instance;

        private readonly StopIndex m_stops = new StopIndex();
        private float m_nextRefresh;
        private bool m_useFallback;
        private bool m_stopQueryVerified;
        private TransportKind m_indexKind = TransportKind.None;

        private float3 m_lastPos = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
        private float m_settledSince;
        private bool m_settled;

        /// <summary>本静止事件里"跳过"的日志已经打过一次（失败原因每帧一样，不打第二遍刷屏）。</summary>
        private bool m_previewFailLogged;

        /// <summary>本静止事件里成功喂送的日志 + 探针已经做过一次。</summary>
        private bool m_previewAnnounced;

        /// <summary>
        /// 本静止事件里**认领结果**那一行（`[预览结果]`）已经打过一次。
        ///
        /// 为什么不能复用 <see cref="m_previewAnnounced"/>（v0.11.2 修的死分支，开发笔记待办 #1）：
        /// 喂送成功时在 FeedPreview 里就置位 true，而认领侧写在 ClaimPendingPreview 的
        /// `if (!m_previewAnnounced)` —— 两个闸门共用一个标志，认领那条永远进不去，
        /// 实机整场 `[预览结果]` 一条都不打。产物自检只验了字面量在 DLL 里（utf16Has），
        /// 分支死了照样绿，所以本版把两个闸门彻底分开。
        /// </summary>
        private bool m_previewResultLogged;

        /// <summary>本静止事件不再喂：原版没造出幽灵，或造出来的落位已经有别的同类站台。</summary>
        private bool m_previewRefused;

        /// <summary>父级判定抛异常只报一次（静默 false 会让"这条路上没预览"看起来像规则问题）。</summary>
        private bool m_attachFailLogged;

        /// <summary>原版射线系统（活跃工具的 mask 每帧由线路工具覆写，画线时命中本来就带人行道层）。</summary>
        private ToolRaycastSystem m_ToolRaycast;

        /// <summary>
        /// 我们那一枚预览幽灵（世上只留一枚：它还活着就一帧都不喂）。
        /// 0.8.2 每帧无条件喂，结果光标停住时原版根本不写 Clear、Temp 全留下来，
        /// 一秒堆 60 枚 —— 玩家截图里"非常快出现一大堆停靠点图标"就是这个。
        /// </summary>
        private Entity m_previewTemp;

        /// <summary>上一枚定义已经喂出去、还没认领到幽灵（认领改成逐帧追，见 kClaimMaxFrames）。</summary>
        private bool m_previewFed;

        /// <summary>喂出去之后等了几帧（0.9.0 的"固定 2 帧窗口"会漏认：卡一帧就当"原版没造出"）。</summary>
        private int m_previewWaitFrames;

        /// <summary>光标开始移动的时刻（0=幽灵不欠收）；连续移动满 kPreviewFadeSeconds 才真收。</summary>
        private float m_ghostMoveSince;

        /// <summary>"原版判定不通过（Error）"这条收回日志每个静止事件只打一次。</summary>
        private bool m_previewEmptyLogged;

        /// <summary>需求 2（v0.10.0）：拒绝删原版副本的日志整场只打一次（真出现就说明哪条路走错了）。</summary>
        private bool m_foreignTempGuardLogged;

        /// <summary>上一轮"找兜底站台"的全量枚举时刻（秒）；配合 kFallbackRetrySeconds 当负缓存用。</summary>
        private float m_fallbackScanAt = -999f;

        /// <summary>上一轮整表扫描**确认没有**可用站台的那一类别（负缓存的键；扫到了就归 None）。</summary>
        private TransportKind m_fallbackScanFail = TransportKind.None;

        /// <summary>
        /// BeginSession（快捷键 / UI 回调，在工具级联之外）挂进来的"收上一场残留幽灵"请求。
        /// 结构改动只能在系统帧里做，所以 OnUpdate 开头消费它。
        /// </summary>
        private bool m_beginCleanupPending;

        /// <summary>OnDestroy 已跑过：静态入口（BeginSession / RequestAssetRefresh）据此拒绝再碰这个已销毁的系统。</summary>
        private volatile bool m_destroyed;

        /// <summary>当前用的是第几档沿路让位（kSlideNudges 下标）。</summary>
        private int m_nudgeIndex;

        /// <summary>玩家看见的那一处落位：左键提交时照它喂，预览与落地才是同一个地方。</summary>
        private float3 m_previewSpot;

        /// <summary>这一枚预览用的是哪个资产（认领时按它找 Temp，省得每帧重新解析资产）。</summary>
        private Entity m_previewPrefab;

        private bool m_hasPreviewSpot;

        /// <summary>那一枚幽灵的依附父级（转正时给控制点改写用，也让"沿用预览"能复用同一条边）。</summary>
        private Entity m_previewParent;

        /// <summary>探测容差：AttachPositionSystem 会把幽灵横向吸到人行道，留足余量。</summary>
        internal const float kProbeTolerance = 25f;

        private bool m_haveCpCount;
        private int m_lastCpCount;

        /// <summary>
        /// 需求 2（v0.12.0）：上一帧我们看到的线路工具控制点状态。
        /// 拖拽松手那一帧原版已经先把 m_State 打回 Default（RouteToolSystem.cs:2039），
        /// 本帧读到的态不可信，判定只能用"上一帧是 Modify + 本帧原版真的写了 Apply"
        /// 这对组合（<see cref="PlacementDecisions.DecideDragCommit"/>）。
        /// </summary>
        private ToolLineState m_prevLineState = ToolLineState.Default;

        /// <summary>需求 1（v0.12.0）：<c>[预览续命]</c> 那条日志每个静止事件只打一次（Clear 帧可能连着来）。</summary>
        private bool m_rescueLogged;

        /// <summary>需求 1（v0.12.0）：本场补喂了多少次 —— 实机排查"还闪不闪"看它。</summary>
        private int m_rescueCount;

        /// <summary>
        /// 需求 2（v0.12.0）：松手帧没解析出父级的那一笔，转入补提交。
        /// 落点（松手处光标真实点 + 当次资产）在这里定格，后续帧只重试"解析父级 → 喂定义"，
        /// 绝不再跟着光标跑（否则就成了"鼠标在哪儿站台落在哪儿"，与拖拽无关了）。
        /// </summary>
        private bool m_dragLandingArmed;
        private float3 m_dragLandingSpot;
        private Entity m_dragLandingPrefab;
        private int m_dragLandingFrames;

        /// <summary>
        /// 需求 2（v0.12.0）：**每一帧**都记下的光标真实点。
        ///
        /// 为什么不能复用 <see cref="m_lastPos"/>：那是"静止判定"的锚点，只在光标**动了**的时候才更新
        /// （`distance &gt; kMoveEpsilon` 那条分支），于是玩家松手那一刻它记的是"上一次明显移动时的位置"，
        /// 不是松手处。松手帧若恰好没有悬停点（原版先 Clear 控制点再重新射线，没打中就是空的），
        /// 拿 m_lastPos 当落点会差出一次微移的距离；而拖拽落站偏偏就吃这个精度。
        /// 这里只做一个 float3 赋值，平稳帧成本可忽略。
        /// </summary>
        private float3 m_lastCursor;

        /// <summary>
        /// 需求 2（v0.12.0）：松手帧被 RetirePreview 清掉的那条线索 —— "玩家眼前那枚预览挂在哪条路上"。
        /// 补提交期间同样先用它（预览能显示 = 这处**已经**解析成功过），
        /// 它是"松手时光标压着站台/线路飘带"那类局面唯一的可靠答案（实机 2026-10-19 23:25:30）。
        /// </summary>
        private Entity m_dragLandingSeenParent;
        private float3 m_dragLandingSeenSpot;

        /// <summary>
        /// 需求 2（v0.13.0）：本笔拖拽动的是哪一条线（**真实** Route 实体，不是 Temp）。
        ///
        /// 为什么需要它：左键画线那条路能靠"改写控制点 m_OriginalEntity"让原版把停靠点连到站台
        /// （`RewriteControlPointAttachment`，笔记 #71），而拖拽这条路**没有控制点可改** ——
        /// 松手那一帧原版已经把线路改完、路径点也造出来了。要在事后把它接到我们刚落地的站台上，
        /// 就必须知道"哪条线、哪一枚路径点"，所以拖拽期间把工具悬停到的 Route 实体记下来。
        /// </summary>
        private Entity m_dragRoute;
        private int m_dragRouteIndex = -1;

        /// <summary>需求 2（v0.13.0）：松手后"光标不在落点"的连续帧数（<see cref="kDragAwayGraceFrames"/> 才作废）。</summary>
        private int m_dragAwayFrames;

        /// <summary>
        /// 需求 1（v0.13.0）：本静止事件已经做过一次"只解析父级、不显示幽灵"的记录。
        /// 拖拽前/拖拽中不预览，但落地仍然要父级 —— 所以静默解析一次，把答案存进
        /// m_previewSpot / m_previewParent / m_hasPreviewSpot（ArmDragLandingFields 用的就是它们）。
        /// </summary>
        private bool m_hintResolved;
        private bool m_hintLogged;

        /// <summary>需求 2（v0.13.0）：站台缺 ConnectedRoute 缓冲的告警只打一次（每场）。</summary>
        private bool m_stopBufferWarned;

        /// <summary>本帧光标真实点（落地判据用它判"玩家有没有走开"；松手帧之后再没有悬停点也能判）。</summary>
        private float3 m_previewCursorNow;

        /// <summary>已落地的会话站台（LIFO，右键撤点从栈顶删；线路完成时清空=全部保留）。</summary>
        private readonly List<Entity> m_placed = new List<Entity>();

        /// <summary>
        /// 需求 4（v0.14.0）：本会话里由模组自动放置的**全部**站台（点击落地 + 拖拽落地），
        /// 外加"它曾经连上过路径点吗"。用途只有一个：玩家右键删掉路径点时，把依附在那一枚
        /// 路径点上的站台一起删掉（判据在 <see cref="PlacementDecisions.SessionStopLostWaypoint"/>）。
        ///
        /// 为什么不能复用 <see cref="m_placed"/>：那是**撤销栈**，拖拽落地的一笔按 v0.12.0 的约定
        /// 刻意不进栈（<see cref="PlacementDecisions.DragCommitIsUndoable"/> 恒 false），
        /// 而玩家这次要的规则恰恰主要针对拖拽那一路；两者的生命周期也不同（m_placed 退出时
        /// 可能按策略被删，本账只管"还能不能跟着路径点一起删"）。
        ///
        /// 清空时机就是需求边界：**退出懒人模式**（RunExitCleanup）与**进入新会话**（BeginSession）。
        /// 清掉之后这条规则再也碰不到它们 —— "退出懒人模式后就算彻底放置好了"就是这么落实的。
        /// 只装我们自己的实体，所以玩家手摆的站台永远不会被这条规则删。
        /// </summary>
        private struct SessionStop
        {
            public Entity Stop;
            /// <summary>曾经见过至少一枚活着的路径点连着它（线路画完 / 拖拽依附之后才会 true）。</summary>
            public bool HadWaypoint;
        }
        private readonly List<SessionStop> m_sessionStops = new List<SessionStop>();

        /// <summary>已点击、等原版把 Temp 造出来的待提交。</summary>
        private struct PendingCommit
        {
            /// <summary>左键那一笔的停靠点位置（控制点匹配用，一直在路中央）。</summary>
            public float3 Position;

            /// <summary>我们把定义喂下去的那一处（人行道侧），Temp 查找按它量。</summary>
            public float3 FeedPos;

            /// <summary>非 Null = 直接拿玩家看见的那枚预览幽灵转正，不再二次喂定义。</summary>
            public Entity Temp;

            public Entity Parent;
            public Entity Prefab;
            public int WaitedFrames;

            /// <summary>需求 2（v0.12.0）：这一笔来自"拖拽松手"，落地后**不进 m_placed**（每次拖拽新增即完成，原版同样不允许右键回退，见 <see cref="PlacementDecisions.DragCommitIsUndoable"/>）。</summary>
            public bool DragCommit;

            /// <summary>
            /// 需求 2（v0.13.0）：这一笔拖拽动到的线路实体与路径点下标（只在 DragCommit 时有意义）。
            /// 落地后靠它们把那一枚已经造出来的路径点接到新站台（<see cref="AttachDraggedWaypoint"/>）。
            /// </summary>
            public Entity DragRoute;
            public int DragRouteIndex;
        }

        private readonly List<PendingCommit> m_pending = new List<PendingCommit>();

        /// <summary>站台资产兜底缓存（选中资产为空时才用；按类别失效）。</summary>
        private PrefabBase m_fallbackStop;
        private TransportKind m_fallbackKind = TransportKind.None;

        protected override void OnCreate()
        {
            base.OnCreate();
            // 静态状态是**进程级**的：换一个存档不会重载程序集，但实体会跟着旧世界一起销毁。
            // 于是 SelectedStationPrefab / ModeKind 这类可能留着"上一个世界里那块已被销毁的 PrefabBase"
            // 和"上一场还开着懒人模式"的状态混进新场 —— 拿它们去喂创建定义就是拿失效句柄调引擎 API。
            // 新世界建好系统时一律清一遍（OnCreate 每次世界创建只跑一次）。
            LazyModeState.ResetForNewWorld();

            m_ToolSystem = World.GetExistingSystemManaged<ToolSystem>();
            m_PrefabSystem = World.GetExistingSystemManaged<PrefabSystem>();
            m_OutputBarrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_ToolRaycast = World.GetExistingSystemManaged<ToolRaycastSystem>();
            Instance = this;

            m_stopQuery = GetEntityQuery(new EntityQueryDesc
            {
                // 站台标记类型是 Game.Routes.BusStop / TramStop（Game.Objects.TransportStop 不存在）
                //
                // v0.11.2（审查 P2-5，结论把这条提法本身否掉了）：原来这里还要求
                // `All = ConnectedRoute`，担心"手工放好、还没被线路连上的站台不进索引"。
                // 反编译取证：ConnectedRoute 缓冲与 BusStop/TramStop 标记是**同一份实例原型**给的，
                // 不由连线决定——Game.Prefabs.TransportStop.GetArchetypeComponents()（ilspycmd 1.6.2f1
                // 单类型 dump，research/TransportStop.cs:50-59）对 Bus 型一次给出
                // :57 ConnectedRoute、:59 BusStop（Tram 型 :85 ConnectedRoute、:87 TramStop），
                // ObjectPrefab.RefreshArchetype 把整套原型记进 ObjectData.m_Archetype
                // （research/ObjectPrefab.cs:43，物件实例必带 Transform：同文件 :22），建实体时
                // （GenerateObjectsSystem.cs:1232 CreateEntity(m_Archetype)）一次铺齐，
                // ApplyObjectsSystem.cs:669-685 转正只摘 Temp 动画类/加 Applied，不会补组件。
                // 反向也成立：原版所有写 ConnectedRoute 的地方
                // （WaypointConnectionSystem.cs:111-113、ApplyRoutesSystem.cs:66-72、
                // ConnectedRouteSystem.cs:34-36）都是 `HasBuffer` 判过之后往**已有缓冲**里塞元素，
                // 从不新建缓冲。
                // 所以那个担心不成立，这条 All 是纯冗余。现在按原版自己的口径来：
                // ResourceAvailabilitySystem.cs:1021（research/ 里有同一份 dump）认站台用的是
                // BusStop + Exclude<Deleted> + Exclude<Temp>。
                // 去掉 All 的实际收益：万一有第三方资产只带 BusStop 没带缓冲（这点**未取证**，
                // 只找到经 Game.Prefabs.TransportStop 授权这一条路），它照样进索引，
                // 查重就会拒绝在它旁边叠一枚——判据变宽的方向是"少放"，安全。
                All = new ComponentType[0],
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<Game.Routes.BusStop>(),
                    ComponentType.ReadOnly<Game.Routes.TramStop>(),
                },
                None = new ComponentType[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Game.Common.Deleted>(),
                },
            });

            m_objectQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new ComponentType[] { ComponentType.ReadOnly<Temp>() },
            });

            m_tempQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new ComponentType[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        protected override void OnDestroy()
        {
            // 世界被销毁（退出到主菜单 / 换存档）时这里跑一次：静态入口靠这个标志拒绝再碰已死的系统。
            m_destroyed = true;
            if (ReferenceEquals(Instance, this)) Instance = null;
            base.OnDestroy();
        }

        internal static LazyModeSystem Current { get { return Instance; } }

        /// <summary>需求 3：懒人模式进行中换了站台资产 → 索引与兜底资产按新类别重来（只置标志，结构改动在系统帧里）。</summary>
        public static void RequestAssetRefresh()
        {
            LazyModeSystem s = Instance;
            if (s == null || s.m_destroyed) return;
            s.m_stopQueryVerified = false;
            s.m_useFallback = false;
            s.m_fallbackStop = null;
            s.m_fallbackKind = TransportKind.None;
            s.m_fallbackScanFail = TransportKind.None;   // 换了资产就得重新扫一遍，负缓存立刻作废
            s.InvalidateStops();
        }

        internal void InvalidateStops()
        {
            // 类别一变（公交↔有轨电车）旧条目就成了"别类的站台"，会让 10m 规则误拒。
            // 索引本来就是靠查询整表重建的，清一次没有额外代价。
            m_stops.Clear();
            m_nextRefresh = 0f;
        }

        protected override void OnUpdate()
        {
            if (LazyModeState.Broken) return;

            // —— 需求 5（v0.14.0）：总开关关掉后，本系统一整帧只做这一次静态 bool 读。
            //    玩家要求"关闭总开关或卸载模组后对存档完全没影响、性能帧数都不要有影响"。
            //    这里必须越过 warning 计时与两次标志判断，所以放在最前面；但**两件收尾工作不能跳**：
            //    · PendingExitCleanup：关总开关那一刻排的"退出清理"（收幽灵 / 按策略处置本次站台）；
            //    · m_beginCleanupPending：进会话时排的"收上一场残留"。
            //    两者都只在切换的瞬间为真，稳态下就是三个 bool 读 + 返回，与"没装这个模组"等价。
            if (!LazyModeState.Enabled && !LazyModeState.PendingExitCleanup && !m_beginCleanupPending) return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            LazyModeState.ClearWarningIfExpired(now);

            if (LazyModeState.PendingExitCleanup)
            {
                LazyModeState.PendingExitCleanup = false;
                RunExitCleanup(LazyModeState.PendingExitPolicy, now);
                return;
            }

            // BeginSession 是在快捷键回调 / UI 绑定回调里跑的（工具级联之外），那里不能做结构改动；
            // 所以"收上一场残留的幽灵"排到这个标志里，由本帧（我们真的在级联里、并且马上 Flush 依赖）执行。
            if (m_beginCleanupPending)
            {
                m_beginCleanupPending = false;
                FlushPendingJobs();
                RetirePendingTemps();
                RetirePreview();
            }

            if (!LazyModeState.Enabled || !LazyModeState.LazyEnabled)
            {
                SettleDwell(false);
                m_haveCpCount = false;
                return;
            }

            if (m_ToolSystem == null) m_ToolSystem = World.GetExistingSystemManaged<ToolSystem>();
            if (m_PrefabSystem == null) m_PrefabSystem = World.GetExistingSystemManaged<PrefabSystem>();
            if (m_ToolRaycast == null) m_ToolRaycast = World.GetExistingSystemManaged<ToolRaycastSystem>();
            if (m_ToolSystem == null || m_PrefabSystem == null) return;

            ToolBaseSystem active;
            try { active = m_ToolSystem.activeTool; }
            catch (Exception) { active = null; }

            // —— 需求 3：进入懒人模式时玩家手里可能不是线路工具（比如他刚点过公交站资产，或者谁都没选）。
            //     这里把"切到对应类型的创建线路工具"放在系统帧里做，不在按钮回调里动工具。
            TransportKind want = LazyModeState.PendingLineKind;
            if (want != TransportKind.None)
            {
                LazyModeState.PendingLineKind = TransportKind.None;
                RouteToolSystem rt0 = active as RouteToolSystem;
                TransportKind now0 = rt0 != null ? LineKindOf(rt0) : TransportKind.None;
                if (now0 != want)
                {
                    PrefabBase line = StationAssets.FindFallbackLine(want);
                    if (line == null)
                    {
                        LazyModeState.ShowWarning(LocaleTable.PanelKey("WARN_UNSUPPORTED_LINE"), 3f, now);
                        BusLineAutoStopsMod.log.Warn("[线路工具] 找不到 " + want + " 线路资产，懒人模式不进入");
                        BusLineAutoStopsMod.Instance.RequestExitLazyMode("没有可用的线路资产");
                        return;
                    }
                    bool ok = false;
                    try { ok = m_ToolSystem.ActivatePrefabTool(line); }
                    catch (Exception e) { BusLineAutoStopsMod.log.Warn("[线路工具] 切换异常 " + e.GetType().Name); }
                    BusLineAutoStopsMod.log.Info("[线路工具] 已请求切到 " + StationAssets.SafeName(line) + " 结果=" + ok);
                    if (!ok)
                    {
                        BusLineAutoStopsMod.Instance.RequestExitLazyMode("切换线路工具失败");
                        return;
                    }
                    try { active = m_ToolSystem.activeTool; }
                    catch (Exception) { active = null; }
                }
            }

            // —— 需求 4：懒人模式必须在线上跑。离开线路工具（切别的工具、ESC 回默认工具）就退出，图标复原。
            RouteToolSystem route = active as RouteToolSystem;
            if (route == null)
            {
                SettleDwell(false);
                m_haveCpCount = false;
                m_prevLineState = ToolLineState.Default; // 需求 2：中途换工具，拖拽闩锁作废，防止回来后误判
                m_dragLandingArmed = false;                // 需求 2（v0.12.0）：补提交跟着会话走，换工具即弃
                string name = SafeToolName(active);
                BusLineAutoStopsMod.log.Info("[退出懒人模式] 当前工具不是线路工具（" + name + "）");
                if (BusLineAutoStopsMod.Instance != null) BusLineAutoStopsMod.Instance.RequestExitLazyMode("离开线路工具");
                return;
            }

            TransportKind kind = LineKindOf(route);
            if (!TransportKindRules.IsLazyModeEnabled(kind, LazyModeState.TramEnabled))
            {
                // 需求 4（0.9.0）：电车本版暂未上线 —— 画到电车线就退出懒人模式，并给一句"暂未上线"，
                // 不给一句"本模组不管这一类"（那是火车/地铁/机场/码头的话）。
                SettleDwell(false);
                LazyModeState.ShowWarning(BusLineAutoStopsMod.UnsupportedLineKey(kind), 3f, now);
                BusLineAutoStopsMod.log.Info("[退出懒人模式] 这条线是 " + kind + "，本版未放开（电车暂未上线="
                    + (kind == TransportKind.Tram) + "）");
                if (BusLineAutoStopsMod.Instance != null) BusLineAutoStopsMod.Instance.RequestExitLazyMode("本版未放开的线路类型");
                return;
            }

            if (LazyModeState.ModeKind != kind)
            {
                LazyModeState.ModeKind = kind;
                LazyModeState.UiSeq++;
                LazyModeState.InvalidateAssetFilter();
            }
            if (m_indexKind != kind)
            {
                m_indexKind = kind;
                m_fallbackStop = null;
                m_fallbackKind = TransportKind.None;
                InvalidateStops();
            }

            // —— 需求 2（v0.12.0）：本帧"原版打算干什么"只读一次（applyMode 公开属性，kPhase* 常量注释）。
            //     拖拽松手那一帧原版已经把 m_State 打回 Default（RouteToolSystem.cs:2039），所以
            //     判据是"上一帧看到 Modify + 本帧真的 Apply"（DecideDragCommit），读完立刻刷新闩锁。
            int phase = ToolPhase(route);
            ToolLineState lineState = SafeLineState(route);
            bool dragCommit = PlacementDecisions.DecideDragCommit(m_prevLineState, lineState,
                phase == kPhaseApply, phase == kPhaseClear);

            // 需求 2（v0.12.0）分诊：进出 Modify 态各打一行 —— 每笔拖拽正好两条，不是每帧。
            // 下一轮实机只要看到 `态 … → Modify` 却没有 `[拖拽落站] …`，就说明"检测没触发"；
            // 两条都没有、而玩家确实拖了，就说明"懒人模式当时没开"，问题不在拖拽链路上。
            // 0.11.2 的困局正是这两者分不清（实机日志 2026-10-20 05:48-05:53 全场 0 条 [拖拽落站]，
            // 而那次会话里懒人模式在 05:52:57 就因为"离开线路工具"自动退出了，拖拽根本不是模组在处理）。
            if (lineState != m_prevLineState
                && (lineState == ToolLineState.Modify || m_prevLineState == ToolLineState.Modify))
            {
                BusLineAutoStopsMod.log.Info("[线路工具] 态 " + m_prevLineState + " → " + lineState
                    + " 相位=" + phase + " 本帧判为拖拽提交=" + dragCommit);
            }
            // 需求 2（v0.13.0）：新的一笔拖拽开始 → 上一笔记下的"哪条线"立刻作废，
            // 免得把这一笔的路径点接到上一笔的线路上。
            if (lineState == ToolLineState.Modify && m_prevLineState != ToolLineState.Modify)
            {
                m_dragRoute = Entity.Null;
                m_dragRouteIndex = -1;
                m_dragAwayFrames = 0;
            }
            m_prevLineState = lineState;

            // —— 控制点：公开 API，与工具共享同一份缓冲。读写前先收完它自己的 job。
            NativeList<ControlPoint> cps;
            try
            {
                cps = route.GetControlPoints(out var deps);
                deps.Complete();
            }
            catch (Exception)
            {
                SettleDwell(false);
                return;
            }

            TrackControlPointCount(cps);
            ResolvePendingCommits(cps);

            // —— 需求 4（v0.14.0）：跟着路径点一起删。
            //     玩家："在修改已有线路时，该次懒人模式下自动放置的公交站，会随右键删除路径点而被删除。"
            //     Apply 帧一律不动世界（本项目里"同帧互斥结构改动"= 空托管栈原生崩溃的同义词，
            //     笔记 #63/#89），所以只在非 Apply 帧上做；删除动作本身在 MaintainSessionStops 里
            //     先 FlushPendingJobs 再 AddComponent<Deleted>。
            if (phase != kPhaseApply) MaintainSessionStops();

            // —— 需求 2（v0.12.0）：拖拽松手这一帧 = 原版把线路修改提交（Apply，RouteToolSystem.cs:2025-2052），
            //     公交站以前正是在这一步悄悄丢掉的（Modify 态控制点恒为 1，TrackControlPointCount 看不见）。
            //
            //     **必须排在 TryCurrentPoint 的早退之前**：松手帧原版是"先 Clear 控制点、再重新射线补一个"，
            //     射线没打中东西时（光标掠过 UI/水面/天空，或者刚好落在没有碰撞体的飘带上）控制点就是空的 ——
            //     0.12.0 以前的顺序会在下面 `if (!TryCurrentPoint)` 直接 return，这一笔连日志都不会留。
            //     松手帧没有悬停点不等于没有落点：用上一帧记住的光标真实点（m_lastPos）照样能落。
            //
            //     拖拽提交必须赶在 MaintainPreview 之前：本帧 applyMode==Apply，预览幽灵留着就会被
            //     ApplyObjectsSystem.Create() 顺手转正（不计费校验那一步全跳过），见 ArmDragLanding。
            bool hoverOk = TryCurrentPoint(cps, out ControlPoint hover);
            float3 cursor = hoverOk ? CursorGroundPos(hover.m_Position) : m_lastCursor;
            if (!math.isfinite(cursor.x) || !math.isfinite(cursor.z) || !math.any(cursor != float3.zero))
                cursor = m_lastCursor;
            // 有悬停点就记下本帧光标：下一帧若恰好没有悬停点（松手帧常见），这就是落点来源。
            if (hoverOk && math.any(cursor != float3.zero)) m_lastCursor = cursor;

            m_previewCursorNow = cursor;

            // —— 需求 2（v0.13.0）：拖拽期间把"这一笔动的是哪条线、哪一枚路径点"钉下来。
            //     Modify 态悬停在既有线路上时 m_OriginalEntity 就是 Route 实体（SnapJob 的 Modify 分支
            //     在 FindWaypointLocation 早退时会保留它），m_ElementIndex.x 是路径点下标
            //     （RaycastJobs 把命中路径点的 m_Index 放进 m_CellIndex.x）。松手后原版已经把线路改完、
            //     没有控制点可改（左键那条路靠 RewriteControlPointAttachment，笔记 #71），
            //     只能靠这两个值在落地后找回那一枚已经造出来的路径点。
            if (lineState == ToolLineState.Modify && hoverOk) TryCaptureDragRoute(hover);

            if (dragCommit && math.any(cursor != float3.zero))
            {
                ArmDragLanding(cursor);
                m_previewRefused = true; // 落点处不再原地预览（光标挪走后移动分支会复位闸门）
            }
            else if (dragCommit)
            {
                // 连上一帧的光标都没有（刚进懒人模式第一帧就松手）：记一笔，下一次不再沉默。
                BusLineAutoStopsMod.log.Info("[拖拽落站] 跳过：松手帧既没有悬停点也没有上一帧光标");
            }
            else if (m_dragLandingArmed)
            {
                // —— 需求 2（v0.12.0）补提交：松手那一帧解析不出父级（光标正压在别的站台上，射线与
                //     Owner 链全指向不可依附的实体）时，这一帧接着解析；光标离开落点、工具又开始新的
                //     一笔、或等满 kDragRetryMaxFrames 帧就作废（判定在 DecideDragRetry）。
                //     Apply 帧一律不补喂：那帧喂出去的幽灵会被 ApplyObjectsSystem.Create() 顺手转正
                //     （不计费、不过校验），宁可让这一帧过去，也不要一座白送的建筑。
                TryLandDragStop(hoverOk ? hover : default(ControlPoint), phase, lineState);
            }

            // —— 需求 2：落点 = 线路工具自己那个"还没按左键"的路径点
            if (!hoverOk)
            {
                ResetSettle(now);
                SettleDwell(false);
                RefreshStopsIfNeeded(now);
                return;
            }

            // 需求 1（v0.10.0）：本帧的"光标真实点"只取一次（射线结果每帧读一次就够，需求 5），
            // 之后静止判定、沿用判定、喂点全用它 —— 悬停点已经被吸到路中央，横向信息只剩在这儿。

            if (math.distance(cursor, m_lastPos) > kMoveEpsilon)
            {
                m_lastPos = cursor;
                m_settledSince = now;
                m_settled = false;
                m_previewFailLogged = false;
                m_previewAnnounced = false;
                m_previewResultLogged = false;
                m_previewRefused = false;
                m_previewEmptyLogged = false;
                m_attachFailLogged = false;
                m_nudgeIndex = 0;
                m_hintResolved = false;

                // 需求 3（v0.10.0）：动了**不立刻**收幽灵。0.9.0 是"一动就 AddComponent<Deleted>"，
                // 而幽灵要下一帧才被原版造出来、我们又要等 2 帧才去认领 —— 于是拖鼠标途中每次
                // 微停（≥0.1s）都"喂一枚 → 立刻被自己删掉"，玩家看到的正是闪烁，日志里
                // 204 次 `[预览无幽灵] 世界 Temp 数=0` 就是这么来的（Temp 从来没出现过）。
                // 规矩改成：连续移动满 kPreviewFadeSeconds 才真收（幽灵本身仍然只留一枚）。
                if (m_previewTemp != Entity.Null || m_previewFed)
                {
                    if (m_ghostMoveSince == 0f) m_ghostMoveSince = now;
                    else if (now - m_ghostMoveSince >= kPreviewFadeSeconds)
                    {
                        RetirePreview();
                        m_ghostMoveSince = 0f;
                    }
                }
                else
                {
                    m_ghostMoveSince = 0f;
                }
            }
            else
            {
                // 停回原处（或只是抖了一下）：上一枚幽灵还在就不欠收，把淡出计时清零。
                m_ghostMoveSince = 0f;
                if (!m_settled) m_settled = now - m_settledSince >= kSettleSeconds;
            }

            SettleDwell(m_settled);

            // 拖拽提交 / 补提交已经在本文前面（控制点读完、TryCurrentPoint 早退之前）处理过了 ——
            // 那一笔必须在松手帧无条件执行，不能受"本帧有没有悬停点"影响。

            // —— 需求 1/3（v0.13.0）：预览的前置闸门（玩家：拖拽前不该模拟生成公交站）。
            //     · 光标压在既有线路的路径点/路段上（下一次左键 = 进 Modify 拖拽）；
            //     · 工具已经在 Modify（拖拽中）/ Remove；
            //     · 光标已进入既有站台的依附范围（原版会把这一笔路径点直接依附到那枚站台上）。
            //     三种情形都不喂幽灵，而且要把手上的幽灵**收干净**：一枚还带着
            //     `Game.Tools.Error` 的幽灵会让 `ToolBaseSystem.GetAllowApply()`（:541-543）整帧为假，
            //     玩家那一笔会被原版悄悄吞掉（实机 2026-10-21 01:08 的"在这个站台放路径点失败"）。
            //     拖拽落地仍然需要父级，所以抑制期间做一次**只解析、不显示**的记录（NoteLandingHint）。
            bool hoverRouteElement = hoverOk && HoverOnRouteElement(hover);
            // 半径那一桩每帧要做一次站台索引查询（3×3 格），只在"幽灵真的可能被喂"的帧才做：
            // 静止事件（m_settled）或手上还挂着一枚幽灵。其余帧只做那几次组件读。
            bool hoverExistingStop = hoverOk
                && IsAttachStopHover(hover, cursor, m_settled || m_previewTemp != Entity.Null || m_previewFed);
            bool previewOk = !PlacementDecisions.SuppressPreview(lineState, hoverRouteElement, hoverExistingStop);
            // v0.14.0：**Apply 帧绝不喂定义**。0.13.0 把 Modify 也拦掉了，所以"拖拽松手那一帧
            // 手里还有一枚预览幽灵、而同帧又在喂新定义"这件事从来没发生过；0.14.0 把预览还给了
            // 拖拽（需求 1），这个组合就真的会出现 —— 而 ApplyObjectsSystem.Create() 对
            // Temp（m_Original == Null）是**不过校验、不计费**直接转正真建筑的（笔记 #71）。
            // 所以闸门先跟相位做与：Apply 帧只关闸、不喂、不收（收也留到下一帧，理由同下方 else 分支）。
            bool previewOkNow = previewOk && phase != kPhaseApply;
            if (previewOkNow)
            {
                // —— 需求 1（v0.12.0）"停着不动也偶尔闪一下"：原版停稳帧会间歇写 Clear（路径重算完成、
                //     悬停吸附搬家都会走到 ：2206-2229 那几个分支），ToolClearSystem 把整个 Temp 块标
                //     Deleted（:101），而我们只在静止开始时喂过一次定义 —— 幽灵这回是真死了（vanilla
                //     自己不闪是因为它每帧重喂定义，Deleted 幽灵当帧就被 GenerateObjectsSystem 回收复用）。
                //     同帧补喂同一处，让回收链路把它续上（详见 RescuePreviewIfCleared）。
                if (m_settled && !m_previewRefused) RescuePreviewIfCleared(phase, cursor);
                // 预览"世上只留一枚、丢了才补"（MaintainPreview 里解释为什么不能每帧无条件喂）。
                if (m_settled && !m_previewRefused) MaintainPreview(hover, cursor);
            }
            else
            {
                // Apply 帧上绝不改我们的幽灵（v0.12.1 的崩溃取证：那一帧 AddComponent<Deleted> 会跟
                // GenerateObjectsSystem 的"Deletes 回收复用"撞同一实体）。那一帧原版自己会清 Temp，
                // 我们只把闸门关上、下一帧再收。
                if (phase != kPhaseApply && (m_previewTemp != Entity.Null || m_previewFed)) RetirePreview();
                if (m_settled) NoteLandingHint(hover, cursor, hoverExistingStop, lineState);
            }

            RefreshStopsIfNeeded(now);
        }

        /// <summary>
        /// 需求 1（v0.13.0）：光标是不是压在**既有线路的路径点/路段**上 —— 也就是"下一次左键会进 Modify 拖拽"。
        /// 判据照抄原版 `Apply()` 的 Default 分支（RouteToolSystem.cs:1908-1912）：
        /// `HasComponent&lt;Route&gt;(cp.m_OriginalEntity) &amp;&amp; math.any(cp.m_ElementIndex &gt;= 0)`。
        /// 正在画的那条临时线路（带 Temp）不算：那是在加路径点，不是在拖拽。
        /// 只读 3 个组件，每帧一次，不扫世界。
        /// </summary>
        private bool HoverOnRouteElement(ControlPoint hover)
        {
            try
            {
                Entity e = hover.m_OriginalEntity;
                if (e == Entity.Null) return false;
                if (!math.any(hover.m_ElementIndex >= 0)) return false;
                if (EntityManager.HasComponent<Temp>(e)) return false;
                return EntityManager.HasComponent<Route>(e);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 需求 3（v0.13.0）：光标是不是已经落进既有同类站台的依附范围。
        /// ① 权威判据：工具自己解析出来的实体就是站台（`FindWaypointLocation` 的 ConnectedRoute 分支，
        ///    RouteToolSystem.cs:277-280 —— 命中即"这一笔路径点会直接依附它"）。
        /// ② 兜底：光标 <see cref="kStopAttachRadius"/> 米内有同类站台（站台索引查询，无分配）。
        /// </summary>
        private bool IsAttachStopHover(ControlPoint hover, float3 cursor, bool useRadius = true)
        {
            if (IsSameKindStop(hover.m_OriginalEntity)) return true;
            if (!useRadius) return false;
            float nearDist;
            return m_stops.Nearest(EntityManager, cursor, kStopAttachRadius, out nearDist) != Entity.Null;
        }

        /// <summary>这枚实体是不是"既有同类站台"（带 ConnectedRoute 缓冲的 BusStop/TramStop，且不是 Temp/已删）。</summary>
        private bool IsSameKindStop(Entity e)
        {
            try
            {
                if (e == Entity.Null || !EntityManager.Exists(e)) return false;
                if (EntityManager.HasComponent<Temp>(e)) return false;
                if (EntityManager.HasComponent<Game.Common.Deleted>(e)) return false;
                if (!EntityManager.HasBuffer<ConnectedRoute>(e)) return false;
                TransportKind kind = LazyModeState.ModeKind;
                if (kind == TransportKind.Bus) return EntityManager.HasComponent<BusStop>(e);
                if (kind == TransportKind.Tram) return EntityManager.HasComponent<TramStop>(e);
                return false;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 控制点数量变化 = 玩家的事件流：
        /// +1 = 左键加了停靠点（点击帧，原版已把它收进控制点）→ 排一个待提交；
        /// -1 = 右键撤掉一个停靠点 → 先撤还没转正的待提交，再从已落地栈顶删一个；
        /// -N(N≥2) = 线路完成（Clear 清空 + 补悬停点）→ 站台全部保留，账本清零。
        /// 注意 Modify/Remove 态控制点恒为 1 个，不会误触任何分支。
        /// </summary>
        private void TrackControlPointCount(NativeList<ControlPoint> cps)
        {
            int n = cps.Length;
            if (!m_haveCpCount)
            {
                m_haveCpCount = true;
                m_lastCpCount = n;
                return;
            }
            if (n == m_lastCpCount) return;

            int delta = n - m_lastCpCount;
            m_lastCpCount = n;

            if (delta > 0)
            {
                // 点击帧：新提交点在 n-2（n-1 是新的悬停点）。n==1 只是首个悬停点，不是点击。
                int idx = n - 2;
                if (idx < 0 || idx >= n) return;
                ControlPoint cp = cps[idx];
                // 需求 1（v0.10.0）：左键这一笔也必须按"光标真实点"落地，否则预览在右、
                // 点下去却去了左（控制点被工具的 SnapJob 吸到路中央，横向信息全丢了）。
                float3 clickCursor = CursorGroundPos(cp.m_Position);
                if (!TryGetStopPrefab(out PrefabBase pb, out Entity prefab))
                {
                    BusLineAutoStopsMod.log.Warn("[落点提交] 找不到可用站台资产，跳过");
                    return;
                }
                Entity clickParent;
                string clickWhy;

                // 先试"沿用玩家看见的那一枚"（0.9.0 需求 3）：预览与落地必须是同一个东西，
                // 而且世上同时有两枚同资产幽灵时，按位置找提交 Temp 会找错人。
                // 距离两侧都用光标真实点（0.10.0）—— 拿停靠点比的话，宽路上"看见的那枚"
                // 会因为隔着半条路而判成"不是同一处"，于是又喂一枚、落位的还不是玩家看的那枚。
                Entity reuse = ReusablePreviewGhost(clickCursor, prefab);
                if (reuse != Entity.Null)
                {
                    m_pending.Add(new PendingCommit
                    {
                        Position = cp.m_Position,
                        FeedPos = m_previewSpot,
                        Temp = reuse,
                        Parent = m_previewParent,
                        Prefab = prefab,
                        WaitedFrames = 0,
                    });
                    m_previewTemp = Entity.Null;   // 这一枚交给提交管线，预览不再认它
                    BusLineAutoStopsMod.log.Info("[落点提交] 沿用预览幽灵 @(" + Pos(m_previewSpot)
                        + ") 停靠点=(" + Pos(cp.m_Position) + ")");
                    return;
                }

                if (!TryResolveLanding(clickCursor, cp.m_OriginalEntity, out clickParent, out clickWhy))
                {
                    BusLineAutoStopsMod.log.Info("[落点提交] 跳过 @(" + Pos(clickCursor) + ") 原因：" + clickWhy);
                    return;
                }
                // 没有可沿用的幽灵（静止没到 0.18s 就点、或刚换资产、或那一枚已经太远）
                // → 自己现喂一枚。现喂之前必须先把手上那枚收走，否则世上同时两枚同资产幽灵，
                // FindCommitTemp 会找错人（0.9.0 需求 1 的"只留一枚"在这条分支上是漏的）。
                if (m_previewTemp != Entity.Null || m_previewFed) RetirePreview();
                float3 feedPos = SlidePos(clickCursor, clickParent, Nudge());
                // 提交定义必须在本帧就喂（与原版工具同一条通道；按 #67，本帧 ToolUpdate 之后的
                // Modification1 就会把它造成 Temp），不带 Optional：下一帧查到校验错误就不转正。
                FeedStopDefinition(prefab, feedPos, clickParent, optional: false);
                m_pending.Add(new PendingCommit
                {
                    Position = cp.m_Position,
                    FeedPos = feedPos,
                    Temp = Entity.Null,
                    Parent = clickParent,
                    Prefab = prefab,
                    WaitedFrames = 0,
                });
                return;
            }

            int removed = -delta;
            if (removed >= 2)
            {
                // 线路完成：控制点被 Clear 后补悬停点。站台已经归线路用，全部保留。
                if (m_placed.Count > 0 || m_pending.Count > 0)
                {
                    BusLineAutoStopsMod.log.Info("[线路完成] 本次落地的站台 " + m_placed.Count + " 个已保留");
                }
                m_placed.Clear();
                m_pending.Clear();
                return;
            }

            // 右键撤一个停靠点。
            if (m_pending.Count > 0)
            {
                PendingCommit last = m_pending[m_pending.Count - 1];
                m_pending.RemoveAt(m_pending.Count - 1);
                // 那一笔用的是沿用的预览幽灵：撤单就把它还给预览；预览已经有别人了就收走它，
                // 总之不给世上留一枚没人认领的幽灵。
                if (last.Temp != Entity.Null && last.Temp != m_previewTemp)
                {
                    if (m_previewTemp == Entity.Null && IsTempAlive(last.Temp)) m_previewTemp = last.Temp;
                    else RetireTemp(last.Temp);
                }
                return;
            }
            if (m_placed.Count > 0)
            {
                Entity e = m_placed[m_placed.Count - 1];
                m_placed.RemoveAt(m_placed.Count - 1);
                if (DeleteIfSafe(e))
                {
                    BusLineAutoStopsMod.log.Info("[撤点回退] 站台已删除（本次会话剩 " + m_placed.Count + " 个）");
                }
            }
        }

        /// <summary>下一帧：原版已按我们的定义造出提交 Temp → 查校验 → 转正 → 改写控制点依附。</summary>
        private void ResolvePendingCommits(NativeList<ControlPoint> cps)
        {
            if (m_pending.Count == 0) return;

            for (int i = m_pending.Count - 1; i >= 0; i--)
            {
                PendingCommit p = m_pending[i];

                // 沿用预览幽灵的那一笔：实体就在手上，只需确认它还活着（原版可能已经把它收走）。
                Entity temp = p.Temp;
                if (temp != Entity.Null && !IsTempAlive(temp))
                {
                    BusLineAutoStopsMod.log.Info("[落点提交] @(" + Pos(p.Position)
                        + ") 预览幽灵已被原版收走 → 改回现喂");
                    p.Temp = Entity.Null;
                    FeedStopDefinition(p.Prefab, p.FeedPos, p.Parent, optional: false);
                    m_pending[i] = p;
                    continue;
                }
                if (temp == Entity.Null) temp = FindCommitTemp(p);
                if (temp == Entity.Null)
                {
                    if (++p.WaitedFrames > kCommitMaxWaitFrames)
                    {
                        BusLineAutoStopsMod.log.Warn("[落点提交] @(" + Pos(p.FeedPos) + ") 等不到原版生成，放弃"
                            + "（世界 Temp 数=" + m_tempQuery.CalculateEntityCount() + "）");
                        m_pending.RemoveAt(i);
                    }
                    else
                    {
                        m_pending[i] = p;
                    }
                    continue;
                }

                m_pending.RemoveAt(i);

                if (EntityManager.HasComponent<Game.Tools.Error>(temp))
                {
                    // 原版校验不通过（Error 级；只有 Warning 级不在这儿，被灯/垃圾桶压住照样放）：
                    // 不转正，由我们标记删除（v0.4.0 同款闸门，原版不给放的位置一律不放）。
                    BusLineAutoStopsMod.log.Info("[放置被拒] @(" + Pos(p.FeedPos) + ")（原版校验不通过）");
                    RetireTemp(temp);
                    continue;
                }

                // 查重量在**吸附后的实际落位**上（悬停点在路中央，量它会隔着半条路误判成重复）。
                float3 landedAt;
                if (!TryEntityPos(temp, out landedAt)) landedAt = p.FeedPos;
                float dupDist;
                Entity dup = m_stops.Nearest(EntityManager, landedAt, kSnapDistance, out dupDist);
                if (dup != Entity.Null)
                {
                    BusLineAutoStopsMod.log.Info("[放置被拒] @(" + Pos(landedAt) + ") 已有同类站台 "
                        + Describe(dup) + " 相距 " + dupDist.ToString("0.0") + "m");
                    RetireTemp(temp);
                    continue;
                }

                if (ConvertTempToStop(temp))
                {
                    float3 landed = landedAt;
                    m_stops.Add(temp, landed);
                    // 需求 4（v0.14.0）：不管走的哪条路，都记进"本会话自动放置"的账。
                    RememberSessionStop(temp);
                    if (p.DragCommit && !PlacementDecisions.DragCommitIsUndoable())
                    {
                        // 需求 2（v0.12.0）：拖拽落地的那一笔**不进 m_placed** —— 每次拖拽新增都算完成，
                        // 与原版一致不允许右键回退；右键撤点 / 退出清理都碰不到它（m_placed 是撤销栈）。
                        // 控制点改写**不做**：此刻 cps 里只有 1 个悬停点（Modify 态控制点恒为 1，
                        // TrackControlPointCount 注释），改它的 m_OriginalEntity 只会让原版把我们的站台
                        // 复制成 hover 副本（"老站凭空消失"那类毛病）。
                        //
                        // 需求 2（v0.13.0）：改成**事后接上那一枚已经造出来的路径点** —— 玩家报
                        // "拖拽结束了、站台也生成了，但这个拖拽的路径点没有依附到这个公交站"。
                        // 左键画线那条路靠改写控制点让原版把 m_Connection 写进定义（笔记 #71），
                        // 拖拽这条路松手时原版已经把线路改完，只能按原版自己的数据形状补：
                        // `Connected` + `VehicleTiming` + 站台 `ConnectedRoute` + `Updated`
                        // （＝ ApplyRoutesSystem.Update(pos, connected) + WaypointConnectionSystem
                        //    的 Created 分支所做的事），剩下的车道/路径重算全交回原版。
                        BusLineAutoStopsMod.log.Info("[拖拽落站] @(" + Pos(landed)
                            + ") 站台已落地（拖拽新增，不可右键回退）");
                        AttachDraggedWaypoint(temp, p.DragRoute, p.DragRouteIndex, p.Position, landed);
                        m_dragRoute = Entity.Null;   // 这一笔用完了，别让下一笔误用
                        m_dragRouteIndex = -1;
                    }
                    else
                    {
                        m_placed.Add(temp);
                        RewriteControlPointAttachment(cps, p.Position, temp, landed);
                        // 需求 1 的实机取证：喂点与落点的有符号横向距离必须同号（同侧）。
                        BusLineAutoStopsMod.log.Info("[落点提交] @(" + Pos(landed) + ") 资产已落地（本次第 "
                            + m_placed.Count + " 个） 喂点横向=" + LateralText(p.Parent, p.FeedPos, EntityManager)
                            + " 落点横向=" + LateralText(p.Parent, landed, EntityManager));
                    }
                }
            }
        }

        /// <summary>
        /// 在 Temp 查询里找"同一资产 + 落点匹配"的那颗提交幽灵。
        /// 量的是 <c>p.FeedPos</c>（我们喂下去的那一处，人行道侧），不是路中央的停靠点：
        /// Large Road 上附着吸附会挪 8~9m，按停靠点量 6m 就永远"等不到原版生成"（需求 3 的根因）。
        /// 已经被我们标 Deleted 的、以及还在当预览的那一枚都不算。
        /// </summary>
        private Entity FindCommitTemp(PendingCommit p)
        {
            // 需求 5：同 FindStopTemp —— 一颗 Temp 都没有时不必分配、也不必扫。
            if (m_tempQuery.CalculateEntityCount() == 0) return Entity.Null;
            try
            {
                NativeArray<Entity> all = m_tempQuery.ToEntityArray(Allocator.Temp);
                try
                {
                    Entity best = Entity.Null;
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < all.Length; i++)
                    {
                        Entity e = all[i];
                        if (e == m_previewTemp) continue;
                        if (EntityManager.HasComponent<Game.Common.Deleted>(e)) continue;
                        // 需求 2（v0.10.0）：带 m_Original 的是原版的"复制/选中"副本，不是我们那一笔。
                        if (EntityManager.GetComponentData<Temp>(e).m_Original != Entity.Null) continue;
                        if (EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != p.Prefab) continue;
                        float3 pos = EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position;
                        float d = math.distance(pos, p.FeedPos);
                        if (d > kCommitMatchTolerance || d >= bestDist) continue;
                        best = e;
                        bestDist = d;
                    }
                    return best;
                }
                finally { all.Dispose(); }
            }
            catch (Exception)
            {
                return Entity.Null;
            }
        }

        /// <summary>还能用/还能转正的 Temp：在世、带 Temp、没被我们标记删除。</summary>
        private bool IsTempAlive(Entity e)
        {
            if (e == Entity.Null) return false;
            try
            {
                return EntityManager.Exists(e) && EntityManager.HasComponent<Temp>(e)
                    && !EntityManager.HasComponent<Game.Common.Deleted>(e);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 这枚 Temp 是不是**我们喂出来的那一种**。
        ///
        /// 需求 2（v0.10.0，实机 2026-10-08 16:10 会话 `[预览收回] 已有同类站台` 151 次的真因）：
        /// 线路工具自己会把"悬停点依附上的那枚既有站台"**复制一份 Temp 出来**
        /// （RouteToolSystem.cs:549 取 `m_ControlPoints[length-1].m_OriginalEntity` → :562-564
        /// CreateTempWaypointObject → :788-789 写 `m_Original=那枚站台` + CreationFlags.Select），
        /// 而 GenerateObjectsSystem.cs:967-969 会因此给**原件**挂 `Game.Tools.Hidden` 把它藏起来
        /// （这就是玩家看到的"鼠标一停，老公交站消失了"：原件被原版藏了，而我们把原版那枚副本
        /// 当成自己的幽灵 `AddComponent<Deleted>` 删了 → 两边都没了；再移上去原版又造副本 → 又出现）。
        /// 我们喂的定义从来不写 m_Original（LazyModeSystem 只写 m_Prefab/m_Attached），
        /// 所以 `Temp.m_Original == Entity.Null` 就是"这枚是我们的"的硬判据。
        /// </summary>
        private bool IsOurTemp(Entity e)
        {
            if (e == Entity.Null) return false;
            try
            {
                if (!EntityManager.Exists(e)) return false;
                if (!EntityManager.HasComponent<Temp>(e)) return false;
                if (EntityManager.HasComponent<Game.Common.Deleted>(e)) return false;
                return EntityManager.GetComponentData<Temp>(e).m_Original == Entity.Null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 左键这一笔能不能直接用玩家看见的那枚幽灵：在世 + 同一个资产 + 离落点不太远。
        /// 直接沿用 = 世上同时只有一枚同资产幽灵（不会找错人），也保证"看见哪儿就放在哪儿"。
        /// </summary>
        private Entity ReusablePreviewGhost(float3 clicked, Entity prefab)
        {
            Entity e = m_previewTemp;
            if (e == Entity.Null || !m_hasPreviewSpot) return Entity.Null;
            if (!IsOurTemp(e))
            {
                m_previewTemp = Entity.Null;
                return Entity.Null;
            }
            try
            {
                if (EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != prefab) return Entity.Null;
            }
            catch (Exception) { return Entity.Null; }
            if (math.distance(m_previewSpot, clicked) > kPreviewReuseRadius) return Entity.Null;
            return e;
        }

        /// <summary>
        /// 收走我们那一枚预览幽灵（需求 1 的后半）：光标停住时原版写的是 applyMode=None、
        /// 不写 Clear，玩家一移动鼠标就把旧幽灵丢在这儿不管 = 沿路拖出一串图标。
        /// 移动/换资产/退出懒人模式都走这里，用的正是 ToolClearSystem 同款的一刀
        /// （AddComponent&lt;Deleted&gt;，:88/:101）。
        /// </summary>
        private void RetirePreview()
        {
            Entity e = m_previewTemp;
            m_previewTemp = Entity.Null;
            m_previewFed = false;
            m_previewWaitFrames = 0;
            m_ghostMoveSince = 0f;
            m_hasPreviewSpot = false;
            m_previewParent = Entity.Null;
            m_previewPrefab = Entity.Null;
            RetireTemp(e);
        }

        /// <summary>
        /// ToolClearSystem 同款收幽灵（AddComponent&lt;Deleted&gt;，:88/:101）。
        ///
        /// 需求 2（v0.10.0）的硬保险：带 `Temp.m_Original` 的是**原版的复制/选中副本**
        /// （线路工具依附既有站台时自己造的，GenerateObjectsSystem.cs:967-969 因此把原件藏了），
        /// 那种实体只能由原版自己清（SubObjectHiddenSystem 在没有任何活 Temp 指回去的那一帧自动解除 Hidden）。
        /// 我们删它 = 原件被原版藏着、副本被我们删了 → 玩家眼里的"老公交站凭空消失"。
        /// 所以这里就算被误传进来也拒绝动手，并留一行日志。
        /// </summary>
        private void RetireTemp(Entity e)
        {
            if (e == Entity.Null) return;
            try
            {
                if (!EntityManager.Exists(e) || !EntityManager.HasComponent<Temp>(e)) return;
                Entity original = EntityManager.GetComponentData<Temp>(e).m_Original;
                if (original != Entity.Null)
                {
                    if (!m_foreignTempGuardLogged)
                    {
                        m_foreignTempGuardLogged = true;
                        BusLineAutoStopsMod.log.Info("[幽灵拒删] " + Describe(e) + " 是原版的副本（原件="
                            + Describe(original) + "）→ 交回原版自己清");
                    }
                    return;
                }
                // 需求 3（v0.13.0）：先摘 Error 再收（见 ClearGhostError：Deleted 实体要到帧末才销毁，
                // 中间那几帧的 Error 会把玩家的一整笔顶掉）。
                ClearGhostError(e);
                EntityManager.AddComponent<Game.Common.Deleted>(e);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 需求 3（v0.13.0）：收幽灵时顺手把原版挂上的 `Game.Tools.Error` 摘掉。
        ///
        /// `ToolBaseSystem.GetAllowApply()`（:541-543）查的是**全世界**的 Error（`m_ErrorQuery`
        /// 就是 `ReadOnly&lt;Error&gt;` 一个条件），而带 Deleted 的实体要到帧末 Cleanup 才销毁 ——
        /// 中间这一两帧里，我们那枚"放不下的预览幽灵"照样会把玩家的左键/松手整帧顶掉，
        /// 正是玩家说的"在这个站台放路径点会失败"。摘掉 Error 只影响这枚马上要死的幽灵，
        /// 不碰任何别的实体、不改校验结论（下一帧它已经不在了）。
        /// </summary>
        private void ClearGhostError(Entity e)
        {
            try
            {
                if (EntityManager.HasComponent<Game.Tools.Error>(e))
                    EntityManager.RemoveComponent<Game.Tools.Error>(e);
            }
            catch (Exception) { }
        }

        /// <summary>退出/重开一局时，把还没转正的待提交幽灵也一并收走（需求 7：不留挂账）。</summary>
        private void RetirePendingTemps()
        {
            for (int i = 0; i < m_pending.Count; i++) RetireTemp(m_pending[i].Temp);
        }

        /// <summary>照抄 ApplyObjectsSystem 转正那一下（:922-923），只作用于我们自己的这一颗。</summary>
        private bool ConvertTempToStop(Entity temp)
        {
            try
            {
                EntityManager.RemoveComponent<Temp>(temp);
                EntityManager.RemoveComponent<Game.Tools.Animation>(temp);
                EntityManager.RemoveComponent<Game.Buildings.BackSide>(temp);
                EntityManager.AddComponent<Game.Common.Applied>(temp);
                EntityManager.AddComponent<Game.Common.Created>(temp);
                EntityManager.AddComponent<Game.Common.Updated>(temp);
                return true;
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("[落点提交] 转正失败 " + e.GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// 把"这一个停靠点"的依附改写成刚落地的站台：m_OriginalEntity=站台、m_Position=站台落位。
        /// 线路完成时 GetWaypointDefinition 会把站台写进 WaypointDefinition.m_Connection（RouteToolSystem.cs:832），
        /// 公交才会真正进站。SnapJob 只重吸最后一个控制点，已提交点不会被原版改回去。
        /// </summary>
        private void RewriteControlPointAttachment(NativeList<ControlPoint> cps, float3 clickedPos, Entity stop, float3 landed)
        {
            try
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = cps.Length - 2; i >= 0; i--)
                {
                    float d = math.distance(cps[i].m_Position, clickedPos);
                    if (d >= bestD) continue;
                    bestD = d;
                    best = i;
                    if (d < 0.6f) break;
                }
                if (best < 0)
                {
                    BusLineAutoStopsMod.log.Warn("[依附改写] 控制点里找不到这一笔（cps="
                        + cps.Length + "）→ 停靠点留在路中，站台仍是真实建筑");
                    return;
                }
                if (bestD > kCpMatchTolerance)
                {
                    BusLineAutoStopsMod.log.Warn("[依附改写] 最近的控制点差 " + bestD.ToString("0.0")
                        + "m（超过 " + kCpMatchTolerance + "m）→ 停靠点留在路中");
                    return;
                }
                ControlPoint c = cps[best];
                c.m_OriginalEntity = stop;
                c.m_Position = landed;
                cps[best] = c;
                BusLineAutoStopsMod.log.Info("[依附改写] cps[" + best + "] 距离 " + bestD.ToString("0.0")
                    + "m → 依附=" + Describe(stop));
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("[落点提交] 依附改写失败（站台已建成，停靠点保持路中）" + e.GetType().Name);
            }
        }

        // ── 预览（需求 2：静止后显示公交站预览） ────────────────────────────────
        /// <summary>
        /// 预览的"保持"逻辑。0.8.2 的教训（实机截图：一停就是一大排站台幽灵）：
        /// **每帧无条件喂 = 每帧多一枚幽灵**。光标停住时线路工具写的是 `applyMode=None`
        /// （RouteToolSystem.cs:2166），`ToolClearSystem` 那一帧根本不清 Temp，
        /// 于是 60 枚/秒全留在世界里。原版幽灵之所以只有一枚，是因为工具每帧
        /// 先 DestroyDefinitions 再重建（#66），而我们每次喂的都是新实体。
        ///
        /// 规矩：**世上一枚、丢了才补** ——
        /// 我们那枚还活着就一帧都不喂（停回 2.5m 内算同一处，直接沿用 → 不闪）；
        /// 喂出去之后**逐帧**去 Temp 里认领，最多 10 帧（0.9.0 只给 2 帧，
        /// 卡一帧就误报"原版没造出幽灵"，实机 204 次）；认领到之后每帧只查它还在不在。
        /// </summary>
        private void MaintainPreview(ControlPoint hover, float3 cursor)
        {
            if (IsPreviewAlive())
            {
                // 沿用判据两侧都得是**光标真实点**：m_previewSpot 现在就是它算出来的，
                // 拿被吸到路中央的 hover.m_Position 去比会凭空差上半条路宽，
                // 于是"每帧收一枚再喂一枚"——正是 0.9.0 那个闪烁。
                if (m_hasPreviewSpot && math.distance(m_previewSpot, cursor) <= kPreviewKeepRadius) return;
                // 玩家挪到别处了：先收旧的一枚，再喂新的（世上仍然只有一枚）。
                RetirePreview();
            }

            m_previewTemp = Entity.Null;
            if (m_previewFed)
            {
                ClaimPendingPreview(hover, cursor);
                return;
            }

            FeedPreview(hover, cursor);
        }

        /// <summary>解析落点并喂一枚预览定义（日志每个静止事件只打一次）。</summary>
        /// <remarks>
        /// 需求 5（v0.10.0）性能说明：解析不出来（没资产 / 射线什么都没打到）时这里**不喂定义**、
        /// 也不置 m_previewFed，于是平稳帧里 MaintainPreview 每帧会重试一次。这条重试是有意的，
        /// 成本只有：一次网格邻域查（3×3 桶）+ 两次射线单例读 + 一次资产解析（已缓存），
        /// **零世界写入、零实体创建**；而"直接拒绝本静止事件"会让玩家必须挪一下鼠标才出预览。
        /// 真正会堆东西的那两条路（每帧喂定义、每帧删幽灵）都已经被钉死了。
        /// </remarks>
        private void FeedPreview(ControlPoint hover, float3 cursor)
        {
            bool first = !m_previewFailLogged && !m_previewAnnounced;

            // 需求 1（v0.10.0）：`cursor` 是本帧唯一一次射线读取的结果（OnUpdate 里算好传进来）。
            // 从这一行起，本系统所有"在哪儿"的判据都用它，不再用被工具吸到路中央的悬停点
            // （理由与 file:line 见 CursorGroundPos）。

            if (!TryGetStopPrefab(out PrefabBase pb, out Entity prefab))
            {
                if (first)
                {
                    m_previewFailLogged = true;
                    BusLineAutoStopsMod.log.Info("[预览跳过] @(" + Pos(cursor) + ") 原因：没有可用站台资产");
                }
                return;
            }

            Entity parent;
            string why;
            if (!TryResolveLanding(cursor, hover.m_OriginalEntity, out parent, out why))
            {
                if (first)
                {
                    m_previewFailLogged = true;
                    BusLineAutoStopsMod.log.Info("[预览跳过] @(" + Pos(cursor) + ") 原因：" + why);
                }
                return;
            }

            // 喂点 = 光标真实点（+ 让位档）。这一处决定站台落在道路的哪一侧，见 CursorGroundPos 的取证。
            float3 spot = SlidePos(cursor, parent, Nudge());
            // 0.9.0 需求 2：预览不带 Optional。Optional 的原版行为是"只要有任何一条错误数据就
            // Cancel+Hidden 这枚 Temp"（ValidationSystem.cs:1659-1681），连 Warning 级也cancel ——
            // 玩家看到的"人行道有路灯/垃圾桶就不显示预览"就是这么来的（那条只是 Overlap 警告，
            // 左键照样放得下，放下时原版自己把被占的物件删掉）。改成自控：只有 Error 级标记
            // 才收走幽灵（见 IsPreviewAlive），警告级的留着，预览与可放性终于一致。
            FeedStopDefinition(prefab, spot, parent, optional: false);
            m_previewSpot = spot;
            m_previewParent = parent;
            m_previewPrefab = prefab;
            m_hasPreviewSpot = true;
            m_previewFed = true;
            m_previewWaitFrames = 0;
            if (m_previewAnnounced) return;

            m_previewAnnounced = true;
            BusLineAutoStopsMod.log.Info("[预览站台] @(" + Pos(cursor) + ") 资产="
                + StationAssets.SafeName(pb) + " ← " + why
                + " 光标横向=" + LateralText(parent, cursor, EntityManager)
                + (Nudge() != 0f ? " 沿路让位" + Nudge().ToString("0.0") + "m" : "")
                + " 索引=" + m_stops.Count);
        }

        /// <summary>
        /// 需求 1（v0.12.0）：停稳时原版若在本帧写 Clear，同帧把同一处补喂一次，让幽灵续命。
        ///
        /// 为什么"停着不动也偶尔会闪"（玩家 0.11.2 反馈）：<c>RouteToolSystem.Update</c> 在停稳帧
        /// 仍会间歇写 <c>applyMode = Clear</c> —— 路径重算刚完成（:2191-2197 的
        /// <c>flag = CheckPathUpdates()</c>）、悬停吸附把控制点搬了家（:2182-2183）之后的
        /// :2206/:2213/:2220/:2229 都会走 Clear。那一刻 <c>ToolOutputSystem</c> 嵌套跑
        /// ClearTool，<c>ToolClearSystem.ClearEntitiesJob</c>（:101）把**整块 Temp 标 Deleted**，
        /// 我们那一枚预览幽灵也在里面。原版自己不闪，是因为它每帧 DestroyDefinitions 之后
        /// 立刻重喂定义（:2146-2235），被标 Deleted 的幽灵当帧就被
        /// <c>GenerateObjectsSystem.m_DeletedQuery</c>（Object+Deleted+Temp+PrefabRef，:1734）
        /// 收进 <c>FillOldObjectsJob</c>（:119-137），再由 <c>CollectCreationDataJob</c> 按
        /// {Prefab, SubPrefab, Original, Owner} + 最近位置（:745-783）配给我们新喂的定义，
        /// <c>CreateObject(existing)</c>（:1103-1130）拿掉 Deleted、补 Updated、重设 Transform ——
        /// 同一个实体原地复活。我们只在静止开始时喂过一次定义，续不上 → 幽灵真死 → 闪一下。
        ///
        /// 补喂**严格同位置、同父级、同资产**（玩家看见哪儿就还放哪儿），且不动
        /// <c>m_nudgeIndex</c>（让位档是 FeedPreview 的事，这里挪一格会让预览自己走位）。
        /// 只标 <c>m_previewFed</c>，不碰 m_previewTemp —— 认领仍由 ClaimPendingPreview 逐帧追。
        /// 触发条件收敛在 <see cref="PlacementDecisions.ShouldRescueFeed"/>（T3 已钉）。
        /// </summary>
        private void RescuePreviewIfCleared(int phase, float3 cursor)
        {
            bool outstanding = m_previewFed || m_previewTemp != Entity.Null;
            bool sameSpot = m_hasPreviewSpot && math.distance(m_previewSpot, cursor) <= kPreviewKeepRadius;
            if (!PlacementDecisions.ShouldRescueFeed(phase == kPhaseClear, outstanding, sameSpot)) return;
            if (m_previewPrefab == Entity.Null) return;

            // 结构改动前先收自己的 job（开发笔记 #63/#64：握着只读 handle 改结构 = 原生崩溃那一类）。
            FlushPendingJobs();
            FeedStopDefinition(m_previewPrefab, m_previewSpot, m_previewParent, optional: false);
            PlacementDecisions.RescueApplied(ref m_previewFed, ref m_previewWaitFrames);
            m_rescueCount++;
            if (!m_rescueLogged)
            {
                m_rescueLogged = true;
                BusLineAutoStopsMod.log.Info("[预览续命] @(" + Pos(m_previewSpot)
                    + ") 本帧原版写 Clear，同帧补喂同一处（本场第 " + m_rescueCount + " 次）");
            }
        }

        /// <summary>
        /// 需求 2（v0.12.0）：拖拽松手那一帧把公交站放下去。
        ///
        /// 以前为什么放不了：拖拽 = <c>RouteToolSystem.State.Modify</c>（:1910 从 Default 进，
        /// 射线命中带 <c>m_ElementIndex &gt;= 0</c> 的线路段），松手走 <c>Apply()</c> 的 Modify 分支
        /// （:2003-2056）：先 <c>m_State = State.Default</c>（:2039）再 <c>m_ControlPoints.Clear()</c>
        /// （:2040）。我们的 <c>TrackControlPointCount</c> 只看控制点数量差，而 Modify 态恒为 1 个、
        /// Clear 之后又补回 1 个（:2041-2044），差值永远是 0 → 我们从头到尾没被通知过，
        /// 玩家看到的预览幽灵只是"原版 Clear 之前我们自己喂的那一枚"，松手后它被原版的
        /// Apply 流程接管/清掉，站台自然落不了地。
        ///
        /// 本帧的正确做法（顺序不能换）：
        /// 1) <c>RetirePreview()</c> 先把预览幽灵收走。本帧 <c>applyMode == Apply</c>，而
        ///    <c>ApplyObjectsSystem.HandleTempEntitiesJob</c> 的 m_TempQuery（Temp+Object，:920）
        ///    **不排除 Deleted**，会把还活着的、m_Original 为 Null 的幽灵直接
        ///    <c>Create()</c> 成一座真建筑（:669-685：去掉 Temp/Animation/BackSide，加
        ///    Applied/Created/Updated）—— 不经校验、不经查重、不经依附改写，还会被
        ///    <c>ToolApplySystem.ApplyEntitiesJob</c>（:42-74）按 m_Cost 计费。绝不能让预览
        ///    在 Apply 帧上自己转正 —— 所以本帧先补 <c>TempFlags.Cancel</c> 再收
        ///    （<see cref="CancelTempsOnApplyFrame"/>）：只写 Deleted 既挡不住转正、也挡不住计费。
        /// 2) 在松手处喂一枚**新的**提交定义，交给 ResolvePendingCommits 走完整闸门
        ///    （Error 校验 / 同类查重 / 转正 / 记账），与原版左键点击完全同一条管线。
        /// 3) 记 <c>DragCommit = true</c>：落地后不进 m_placed（每次拖拽新增即完成，原版也不给
        ///    右键回退 —— <see cref="PlacementDecisions.DragCommitIsUndoable"/>）。
        ///
        /// 落点用本帧的悬停点（工具在 Apply() 里 Clear 后补进来的松手处射线结果），
        /// **不新增射线读取**（需求 5：全场只有 TryRaycast 一处 GetRaycastResult，pack 钉子）。
        /// </summary>
        private void ArmDragLanding(float3 cursor)
        {
            // 需求 2（v0.12.1）：**松手帧只记账，不动世界**。
            //
            // v0.12.0 的写法在松手帧（applyMode == Apply）就收幽灵 + 喂新定义，实机（2026-10-20 22:07:33）
            // 拖拽结束瞬间**原生崩溃**（Player.log: "Native Crash Reporting / Got a UNKNOWN while
            // executing native code" + 空托管栈）。机制上是两类改动撞在同一帧：
            //   ① 我们给预览幽灵补 TempFlags.Cancel 后又 AddComponent<Deleted>；
            //   ② 同一帧 GenerateObjectsSystem 的 m_DeletedQuery（Object+Deleted+Temp+PrefabRef，:1734）
            //      正是拿"带 Deleted 的幽灵"当**可复用的旧物件**，按 {Prefab, 最近位置} 配对后对同一实体
            //      RemoveComponent<Deleted>（:1103-1130）—— 而我们喂的新定义口径恰好是**同一个 prefab +
            //      同一个位置**（沿用预览父级 = 同一处人行道），于是两条路径对同一实体做互斥的组件增删。
            // 这类"同帧互斥结构改动"在本项目里就是空托管栈原生崩溃的同义词（笔记 #63/#89）。
            //
            // 所以拆成两拍：
            //   · **松手帧**：只写 TempFlags.Cancel（SetComponentData，非结构改动；挡住 Apply 帧的免费转正），
            //     把落点/父级/资产记进托管字段，然后 return —— 不建实体、不改结构、不 Complete 依赖。
            //   · **下一帧（非 Apply）**：TryLandDragStop 才收幽灵 + 喂定义 + 排待提交。那一帧与
            //     "预览喂定义"完全同构（预览从来没崩过），风险归零；代价只有一帧（约 16ms）。
            ArmDragLandingFields(cursor);

            // 唯一允许在 Apply 帧做的事：补 Cancel 位（非结构改动）。
            CancelTempsOnApplyFrame();

            BusLineAutoStopsMod.log.Info("[拖拽落站] @(" + Pos(cursor) + ") 松手：已排队，本帧不动世界"
                + "（下一帧落地）｜预览父级="
                + (m_dragLandingSeenParent != Entity.Null ? Describe(m_dragLandingSeenParent) : "无（现场再解析）"));
        }

        /// <summary>
        /// 把"玩家正看着的那枚预览"的路数存进补提交字段。
        /// 预览能显示，就说明上一帧我们在同一处**已经成功解析出过一个合法父级**
        /// （FeedPreview 里解析失败是不会喂的）—— 那是玩家脚下这条路最可靠的答案。
        /// 0.11.x/0.12.0 的死结正是这里：以前 RetirePreview() 会把这些字段清空，把唯一线索自己删了。
        /// </summary>
        private void ArmDragLandingFields(float3 cursor)
        {
            Entity seenParent = m_previewParent;
            float3 seenSpot = m_previewSpot;
            bool seenUsable = m_hasPreviewSpot && seenParent != Entity.Null
                && math.distance(seenSpot, cursor) <= kPreviewReuseRadius;

            m_dragLandingArmed = true;
            m_dragLandingFrames = 0;
            m_dragAwayFrames = 0;
            m_dragLandingSpot = cursor;
            m_dragLandingSeenParent = seenUsable ? seenParent : Entity.Null;
            m_dragLandingSeenSpot = seenSpot;
            m_dragLandingPrefab = Entity.Null;
            if (TryGetStopPrefab(out PrefabBase _, out Entity prefab)) m_dragLandingPrefab = prefab;
        }

        /// <summary>
        /// 需求 2（v0.13.0）：拖拽期间记下"这一笔改的是哪条线、压在哪一枚路径点上"。
        /// Drag 态悬停在既有线路上时 `m_OriginalEntity` 就是 Route 实体、`m_ElementIndex.x` 是路径点下标；
        /// 只在还没记下时写一次（第一帧最准），并且只认**真实**线路（Temp 是正在画的那条，不算）。
        /// </summary>
        private void TryCaptureDragRoute(ControlPoint hover)
        {
            if (m_dragRoute != Entity.Null) return;
            try
            {
                Entity e = hover.m_OriginalEntity;
                // 兜底：抓握那一帧的悬停点偶尔已经被 SnapJob 改写成路/站台（FindWaypointLocation 会沿
                // Owner 链走，走空了就置 Null），这时用射线父级补一次 —— 它正是线路飘带那枚 Route 实体。
                if (!IsRealRoute(e)) e = RaycastOwner();
                if (!IsRealRoute(e)) return;
                m_dragRoute = e;
                m_dragRouteIndex = hover.m_ElementIndex.x;
                BusLineAutoStopsMod.log.Info("[拖拽落站] 记下这一笔的线路 " + Describe(e)
                    + " 路径点下标=" + m_dragRouteIndex);
            }
            catch (Exception) { }
        }

        /// <summary>成型的线路实体（Route + RouteWaypoint 缓冲，且不是正在画的那条 Temp）。</summary>
        private bool IsRealRoute(Entity e)
        {
            try
            {
                if (e == Entity.Null || !EntityManager.Exists(e)) return false;
                if (EntityManager.HasComponent<Temp>(e)) return false;
                if (!EntityManager.HasComponent<Route>(e)) return false;
                return EntityManager.HasBuffer<RouteWaypoint>(e);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 需求 1（v0.13.0）：抑制预览时"只解析、不显示"地记一次父级。
        ///
        /// 为什么还要解析：拖拽落地（<see cref="ArmDragLandingFields"/>）首选"玩家眼前那枚预览挂的路"
        /// —— 而 v0.13.0 起拖拽前/拖拽中**不显示**预览了，这条线索得在这里补上，
        /// 否则松手帧一旦解析不出父级（光标压在别的站台/飘带上）就整笔落空。
        /// 每个静止事件只做一次（kStopAttachRadius 内一次索引查询 + 几次组件读），不建实体、不扫世界。
        /// </summary>
        private void NoteLandingHint(ControlPoint hover, float3 cursor, bool onExistingStop, ToolLineState state)
        {
            if (m_hintResolved) return;
            m_hintResolved = true;
            // 0) 光标就在既有站台上：原版会直接依附它，这一笔不需要站台落点，也不需要父级。
            if (onExistingStop) return;
            // 1) 只有拖拽态才值得记：Default 态的抑制是"光标在路径点上"，那时按左键是拖拽，
            //    拖起来之后每一帧还会再进来记一次（那时才是真正的落点父级）。
            if (state != ToolLineState.Modify) return;
            Entity parent;
            string why;
            if (!TryResolveLanding(cursor, hover.m_OriginalEntity, out parent, out why)) return;
            m_previewSpot = SlidePos(cursor, parent, 0f);
            m_previewParent = parent;
            m_hasPreviewSpot = true;
            if (!m_hintLogged)
            {
                m_hintLogged = true;
                BusLineAutoStopsMod.log.Info("[预览抑制] 拖拽中不显示幽灵，只记父级 ← " + why);
            }
        }

        /// <summary>
        /// 拖拽落点真正喂出去（松手帧与补提交帧共用）：收掉手上可能还在的预览幽灵 → 喂提交定义
        /// → 排一笔 <c>DragCommit</c> 待提交，交给 ResolvePendingCommits 走完整闸门
        /// （Error 校验 / 同类查重 / 转正 / 记账），与原版左键点击完全同一条管线。
        /// 落地后**不进 m_placed**：拖拽一次就算完成，右键撤点 / 退出清理都不回退它。
        /// </summary>
        private void FeedDragCommit(float3 spot, Entity prefab, PrefabBase pb,
            Entity parent, string why)
        {
            // 补提交帧上预览可能已经自己喂了一枚同一处的幽灵：先收走，世上只留一枚，
            // FindCommitTemp 才不会找错人（与左键那条分支同一条规矩）。
            FlushPendingJobs();
            if (m_previewTemp != Entity.Null || m_previewFed) RetirePreview();
            PlacementDecisions.DragSettled(ref m_previewFed, ref m_previewWaitFrames);

            float3 feedPos = SlidePos(spot, parent, Nudge());
            FeedStopDefinition(prefab, feedPos, parent, optional: false);
            m_pending.Add(new PendingCommit
            {
                Position = spot,
                FeedPos = feedPos,
                Temp = Entity.Null,
                Parent = parent,
                Prefab = prefab,
                WaitedFrames = 0,
                DragCommit = true,
                DragRoute = m_dragRoute,
                DragRouteIndex = m_dragRouteIndex,
            });
            // 幽灵刚被收走，认领侧不用再等；预览状态清空后 MaintainPreview 会按"丢了才补"重建。
            m_rescueLogged = false;
            BusLineAutoStopsMod.log.Info("[拖拽落站] @(" + Pos(spot) + ") 资产="
                + StationAssets.SafeName(pb) + " ← " + why
                + " 喂点横向=" + LateralText(parent, feedPos, EntityManager));
        }

        /// <summary>
        /// 需求 2（v0.13.0）：把这一笔拖拽动到的路径点接到刚落地的站台上。
        ///
        /// 为什么必须事后补：拖拽走 Modify 态，松手那一帧原版就把线路提交完了
        /// （RouteToolSystem.cs:2025-2052 → ApplyRoutesSystem 把临时线路的 WaypointDefinition 写回
        /// 真实路径点，ApplyRoutesSystem.cs:322-350），路径点是在**站台出现之前**造出来的，
        /// 它的 `Connected` 只能是空 —— 于是玩家看到"站台生成了，但路径点没依附上去、公交不靠站"。
        /// 左键画线那条路能靠改写控制点让定义带上连接（笔记 #71），拖拽没有控制点可改，
        /// 只能照原版自己的数据形状把连接补上（三处，都是原版自己会写的那种）：
        ///   · 路径点：`Connected`（+ `VehicleTiming` —— TransportLinePrefab 里"连站台的路径点"archetype
        ///     就是 Waypoint + Connected + VehicleTiming）并补 `Updated`；
        ///   · 站台：把这一枚路径点补进自己的 `ConnectedRoute` 缓冲（WaypointConnectionSystem 只在
        ///     Created 那一帧 Add，事后再加它不管，所以这一笔得我们自己写；同样是
        ///     ApplyRoutesSystem.PatchTempReferencesJob 的写法规矩）；
        ///   · 之后 `WaypointConnectionSystem.FindWaypointConnectionsJob` 会照 `Connected` 重算
        ///     AccessLane / RouteLane、把路径点搬到站台位置、发 PathTargetMoved 并重算路段路径
        ///     （WaypointConnectionSystem.cs:1170-1410）—— 全是原版自己的逻辑，我们不接管。
        /// 只在本笔拖拽的那条线里找那一枚路径点（下标优先、落点最近兜底），找不到就只留日志、不猜。
        /// </summary>
        private void AttachDraggedWaypoint(Entity stop, Entity route, int routeIndex, float3 spot, float3 landed)
        {
            try
            {
                if (route == Entity.Null)
                {
                    BusLineAutoStopsMod.log.Info("[拖拽依附] 这一笔没记下线路实体 → 路点留在路上（站台已是真实建筑）");
                    return;
                }
                if (!EntityManager.Exists(route) || EntityManager.HasComponent<Game.Common.Deleted>(route))
                {
                    BusLineAutoStopsMod.log.Info("[拖拽依附] 线路实体已不在世（" + Describe(route) + "）→ 跳过");
                    return;
                }
                if (EntityManager.HasComponent<Temp>(route) || !EntityManager.HasBuffer<RouteWaypoint>(route))
                {
                    BusLineAutoStopsMod.log.Info("[拖拽依附] " + Describe(route) + " 不是成型的线路 → 跳过");
                    return;
                }

                // 结构改动前先把在途 job 收干（本类既有的硬规矩；下面要 AddComponent / 改缓冲）。
                FlushPendingJobs();

                float3 waypointPos;
                Entity waypoint = FindDraggedWaypoint(route, routeIndex, spot, out waypointPos);
                bool found = waypoint != Entity.Null;

                bool onThisStop = false;
                bool elsewhere = false;
                if (found && EntityManager.HasComponent<Connected>(waypoint))
                {
                    Connected old = EntityManager.GetComponentData<Connected>(waypoint);
                    onThisStop = old.m_Connected == stop;
                    elsewhere = old.m_Connected != Entity.Null && !onThisStop
                        && EntityManager.Exists(old.m_Connected)
                        && !EntityManager.HasComponent<Game.Common.Deleted>(old.m_Connected);
                }
                if (!PlacementDecisions.ShouldAttachDraggedWaypoint(found, onThisStop, elsewhere))
                {
                    if (!found)
                    {
                        BusLineAutoStopsMod.log.Info("[拖拽依附] 线路 " + Describe(route)
                            + " 里找不到落点 ±" + kDragAttachRadius.ToString("0") + "m 内的路径点"
                            + "（站台落位=(" + Pos(landed) + ")）→ 路点留在路上");
                    }
                    else
                    {
                        BusLineAutoStopsMod.log.Info("[拖拽依附] 路点 " + Describe(waypoint)
                            + (onThisStop ? " 已经连着这枚站台" : " 已依附在别的实体上") + " → 不动");
                    }
                    return;
                }

                if (!EntityManager.HasComponent<Connected>(waypoint))
                {
                    // 原版 archetype：Connected 与 VehicleTiming 一起加（RoutePrefab.LateInitialize 的
                    // hashSet3 = Waypoint+Connected，TransportLinePrefab.GetArchetypeComponents 再补
                    // VehicleTiming）。一次结构改动补齐，别分两次。
                    EntityManager.AddComponent(waypoint, EntityManager.HasComponent<VehicleTiming>(waypoint)
                        ? new ComponentTypeSet(ComponentType.ReadWrite<Connected>())
                        : new ComponentTypeSet(ComponentType.ReadWrite<Connected>(), ComponentType.ReadWrite<VehicleTiming>()));
                }
                EntityManager.SetComponentData(waypoint, new Connected(stop));
                if (!EntityManager.HasComponent<Updated>(waypoint)) EntityManager.AddComponent<Updated>(waypoint);
                int added = AddStopRouteReference(stop, waypoint);

                BusLineAutoStopsMod.log.Info("[拖拽依附] 路点 " + Describe(waypoint) + " @(" + Pos(waypointPos)
                    + ") → 依附=" + Describe(stop) + "（站台缓冲+" + added
                    + "；原版会照这条连接重算车道与路径）");
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("[拖拽依附] 失败（站台已建成，路点留在路上）"
                    + e.GetType().Name + "：" + e.Message);
            }
        }

        /// <summary>
        /// 在本笔拖拽的那条线里找出"被拖动的这一枚路径点"：
        /// ① 拖拽前记下的 `RouteWaypoint` 下标（原版把被拖的路径点原地更新 —— ApplyRoutesSystem.Update
        ///    写的是 `temp.m_Original`，实体和下标都不变，这是最准的一条）；
        /// ② 拖路段会插入新点：按"离拖拽落点最近"找（只在这一条线自己的缓冲里，几条到几十条，不扫世界）。
        /// 两个入口都要过同一道位置闸：路径点必须还在落点 <see cref="kDragAttachRadius"/> 米内。
        /// </summary>
        private Entity FindDraggedWaypoint(Entity route, int index, float3 spot, out float3 pos)
        {
            pos = default(float3);
            DynamicBuffer<RouteWaypoint> wps = EntityManager.GetBuffer<RouteWaypoint>(route);
            if (index >= 0 && index < wps.Length)
            {
                Entity cand = wps[index].m_Waypoint;
                float3 candPos;
                if (IsWaypointNear(cand, spot, out candPos))
                {
                    pos = candPos;
                    return cand;
                }
            }
            Entity best = Entity.Null;
            float bestD = float.MaxValue;
            float3 bestPos = default(float3);
            for (int i = 0; i < wps.Length; i++)
            {
                Entity cand = wps[i].m_Waypoint;
                float3 candPos;
                if (!IsWaypointNear(cand, spot, out candPos)) continue;
                float d = math.distance(candPos.xz, spot.xz);
                if (d >= bestD) continue;
                bestD = d;
                best = cand;
                bestPos = candPos;
            }
            pos = bestPos;
            return best;
        }

        private bool IsWaypointNear(Entity e, float3 spot, out float3 pos)
        {
            pos = default(float3);
            try
            {
                if (e == Entity.Null || !EntityManager.Exists(e)) return false;
                if (!EntityManager.HasComponent<Waypoint>(e)) return false;
                if (EntityManager.HasComponent<Game.Common.Deleted>(e)) return false;
                if (!TryEntityPos(e, out pos)) return false;
                return math.distance(pos.xz, spot.xz) <= kDragAttachRadius;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 站台侧缓冲：把这一枚路径点记进 `ConnectedRoute`（不重复加）。
        /// 返回真正加进去的条数（0/1），用来进日志 —— 加不进去（资产缺缓冲）也别装作成功。
        /// </summary>
        private int AddStopRouteReference(Entity stop, Entity waypoint)
        {
            try
            {
                if (!EntityManager.HasBuffer<ConnectedRoute>(stop))
                {
                    if (!m_stopBufferWarned)
                    {
                        m_stopBufferWarned = true;
                        BusLineAutoStopsMod.log.Warn("[拖拽依附] 站台没有 ConnectedRoute 缓冲（资产缺件？）"
                            + " → 只写路径点侧连接");
                    }
                    return 0;
                }
                DynamicBuffer<ConnectedRoute> buf = EntityManager.GetBuffer<ConnectedRoute>(stop);
                for (int i = 0; i < buf.Length; i++)
                {
                    if (buf[i].m_Waypoint == waypoint) return 0;
                }
                buf.Add(new ConnectedRoute(waypoint));
                return 1;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// 需求 2（v0.12.1）：落地成功后与预览喂定义同构（见 TryLandDragStop / ArmDragLanding）。
        /// 每帧只做一次父级解析 + 一次就地查重（组件读 + 索引查询），不建实体、不扫世界、不新增
        /// 射线读取。判据全在 <see cref="PlacementDecisions.DecideDragRetry"/>：光标跑开落点、
        /// 工具又开始新的一笔、或等满 <see cref="kDragRetryMaxFrames"/> 帧就作废。
        /// </summary>
        /// <summary>
        /// 需求 2（v0.12.1）：松手后的第一个"安静帧"才真正落地。
        /// 判据全在 <see cref="PlacementDecisions.DecideDragRetry"/>：Apply 帧一律 Wait（那一帧做结构改动
        /// 正是 v0.12.0 崩掉的那一帧）、光标跑开 / 工具开始新的一笔 / 等满预算就作废，
        /// 解析出父级且帧安静才 Commit。
        /// 每帧成本：一次 IsAttachableParent（组件读）+ 失败路径上一次索引查询；不扫世界、不新增射线读取。
        /// </summary>
        private void TryLandDragStop(ControlPoint hover, int phase, ToolLineState lineState)
        {
            m_dragLandingFrames++;
            bool awayNow = math.distance(m_previewCursorNow.xz, m_dragLandingSpot.xz) > kDragRetryRadius;
            // 需求 2（v0.13.0）：松手帧原版先 Clear 控制点再重新射线补一个（RouteToolSystem.cs:2039-2048），
            // 采样点会跳 1~2 帧 —— 连续 kDragAwayGraceFrames 帧都不在落点才算"玩家走了"
            // （0.12.1 实机 2026-10-21 01:08:28 就是被这一跳骗掉整整一笔拖拽：28ms 后报"光标已离开落点"）。
            m_dragAwayFrames = awayNow ? m_dragAwayFrames + 1 : 0;
            bool movedAway = PlacementDecisions.DragCursorLeft(m_dragAwayFrames, kDragAwayGraceFrames);
            bool toolBusy = lineState != ToolLineState.Default;

            // 需求 3（v0.13.0）：松手处本来就落在既有同类站台的依附范围里 → 原版会把这一笔路径点
            // 直接依附到那枚站台上，这里**不新建**（新建只会叠出第二枚站台，还必然被 4m 查重挡回去）。
            if (IsAttachStopHover(hover, m_dragLandingSpot))
            {
                m_dragLandingArmed = false;
                m_dragLandingSeenParent = Entity.Null;
                BusLineAutoStopsMod.log.Info("[拖拽落站] @(" + Pos(m_dragLandingSpot)
                    + ") 松手处就在既有站台的依附范围内（工具父级=" + Describe(hover.m_OriginalEntity)
                    + "）→ 交给原版直接依附，不再新建");
                return;
            }

            // ① 先认"玩家眼前那枚预览"挂的那条路（只在落点旁边才认；玩家走开了就不该拿旧路乱放）。
            Entity parent = Entity.Null;
            string why = "";
            bool resolved = false;
            if (m_dragLandingSeenParent != Entity.Null
                && math.distance(m_dragLandingSeenSpot.xz, m_dragLandingSpot.xz) <= kPreviewReuseRadius
                && IsAttachableParent(m_dragLandingSeenParent))
            {
                parent = m_dragLandingSeenParent;
                why = "沿用玩家眼前那枚预览的父级 " + Describe(parent);
                resolved = true;
            }
            // ② 松手那处再现场解析一次（光标在空路上时这条最干脆）。
            else if (TryResolveLanding(m_dragLandingSpot, hover.m_OriginalEntity, out parent, out why))
            {
                resolved = true;
            }

            PlacementDecisions.DragRetryOutcome verdict = PlacementDecisions.DecideDragRetry(
                resolved, movedAway, toolBusy, phase == kPhaseApply, m_dragLandingFrames, kDragRetryMaxFrames);
            if (verdict == PlacementDecisions.DragRetryOutcome.Wait) return;

            m_dragLandingArmed = false;
            m_dragLandingSeenParent = Entity.Null;
            if (verdict == PlacementDecisions.DragRetryOutcome.Abort)
            {
                BusLineAutoStopsMod.log.Info("[拖拽落站] @(" + Pos(m_dragLandingSpot) + ") 补提交放弃："
                    + (movedAway ? "光标已离开落点"
                        : toolBusy ? "线路工具已开始新的一笔"
                        : "等满 " + kDragRetryMaxFrames + " 帧仍解析不到父级（" + why + "）"));
                return;
            }

            if (!TryGetStopPrefab(out PrefabBase pb, out Entity prefab)
                || (m_dragLandingPrefab != Entity.Null && prefab != m_dragLandingPrefab))
            {
                BusLineAutoStopsMod.log.Info("[拖拽落站] @(" + Pos(m_dragLandingSpot) + ") 补提交放弃：站台资产已换");
                return;
            }

            // —— 到这里才做真正的世界改动，而且是在一个**非 Apply 帧**上（与预览喂定义完全同构）。
            FlushPendingJobs();
            RetirePreview();
            PlacementDecisions.DragSettled(ref m_previewFed, ref m_previewWaitFrames);
            m_previewRefused = true; // 真实站台马上落地，这一处不再显示预览幽灵
            FeedDragCommit(m_dragLandingSpot, prefab, pb, parent,
                why + "（补提交第 " + m_dragLandingFrames + " 帧）");
        }

        /// <summary>
        /// 需求 2（v0.12.0）：Apply 帧上给我们的幽灵补一枚 <c>TempFlags.Cancel</c>。
        ///
        /// 为什么只写 Deleted 不够：本帧 <c>applyMode == Apply</c>，嵌套的 ApplyTool 里
        /// <c>ApplyObjectsSystem.HandleTempEntitiesJob</c> 的 m_TempQuery（Temp+Object，:920）
        /// **不看 Deleted**，无主 Temp 一律走 <c>Create()</c>（:432 → :669-685，去掉 Temp/Animation/
        /// BackSide、加 Applied/Created/Updated，且**不经校验**）；<c>ToolApplySystem.ApplyEntitiesJob</c>
        /// （:59-66）也照样把它的 m_Cost 算进本帧开销。只有 Cancel 位能让两边同时跳过
        /// （:337-341 走 <c>Cancel()</c>，:62 不计费），而 <c>Cancel()</c> 本身也只补一个 Deleted
        /// （:474-482），收尸仍旧是原版 Cleanup 的活 —— 我们既不 Destroy、也不碰原版的 Temp。
        ///
        /// 只碰"我们自己喂出来的"（<c>Temp.m_Original == Entity.Null</c>），与原版线路工具那枚
        /// 同资产同位置的复制 Temp 划清界限（见 IsTempAlive / RetireTemp 的同一条红线）。
        /// </summary>
        private void CancelTempsOnApplyFrame()
        {
            CancelTempFlag(m_previewTemp);
            for (int i = 0; i < m_pending.Count; i++) CancelTempFlag(m_pending[i].Temp);
        }

        private void CancelTempFlag(Entity e)
        {
            if (e == Entity.Null) return;
            try
            {
                Temp t;
                if (!EntityManager.Exists(e)) return;
                if (!EntityManager.TryGetComponent(e, out t)) return;
                if (t.m_Original != Entity.Null) return;
                if ((t.m_Flags & TempFlags.Cancel) != 0) return;
                t.m_Flags |= TempFlags.Cancel;
                EntityManager.SetComponentData(e, t);
            }
            catch (Exception) { }
        }

        /// <summary>当前这一档沿路让位量（米）。</summary>
        private float Nudge()
        {
            if (m_nudgeIndex < 0) return 0f;
            if (m_nudgeIndex >= kSlideNudges.Length) m_nudgeIndex = kSlideNudges.Length - 1;
            return kSlideNudges[m_nudgeIndex];
        }

        /// <summary>
        /// 某个点相对父级道路中线的**有符号横向距离**（米，正方向取 `MathUtils.Right(切向)`），
        /// 只给日志用：父级是节点（交叉口）或没有 Curve 时返回 NaN，调用处写成"无中线"。
        ///
        /// 为什么用 MathUtils.Right 而不是自己写 (-z, x)：`MathUtils.Right(f) = (f.y, -f.x)`
        /// （ilspycmd Colossal.Mathematics.MathUtils：2037-2040），而 SnapRouteLane 选车道用的
        /// 就是这个向量（AttachPositionSystem.cs:236）—— 于是日志里的正负号和**原版判定的正负号
        /// 是同一个轴**，"+7.4 / -8.1" 这种一行就能直接读出"我们的喂点和原版的落位不在同一侧"。
        ///
        /// 需求 1 的实机取证：`光标横向`（喂点）与 `落点横向`（吸附后的实际位置）必须同号。
        /// </summary>
        private static float LateralOf(Entity parent, float3 p, EntityManager em)
        {
            try
            {
                if (parent == Entity.Null || !em.Exists(parent)) return float.NaN;
                Curve curve;
                if (!em.TryGetComponent(parent, out curve)) return float.NaN;
                float t;
                MathUtils.Distance(curve.m_Bezier.xz, p.xz, out t);
                float3 on = MathUtils.Position(curve.m_Bezier, t);
                float2 right = MathUtils.Right(math.normalizesafe(MathUtils.Tangent(curve.m_Bezier, t).xz));
                return math.dot(right, p.xz - on.xz);
            }
            catch (Exception)
            {
                return float.NaN;
            }
        }

        private static string LateralText(Entity parent, float3 p, EntityManager em)
        {
            float v = LateralOf(parent, p, em);
            return float.IsNaN(v) ? "无中线" : (v >= 0f ? "+" : "") + v.ToString("0.0") + "m";
        }

        /// <summary>
        /// 沿**道路切向**把落点挪几米 —— 人行道那一寸被路灯/垃圾桶占住时，原版不给 Temp
        /// （我们只看到"没造出幽灵"），沿路让一档就能出。
        /// 0.10.0 需求 1：这一档从前是"横向"的，跨过中线就把站台让到马路对面去了
        /// （正是玩家报的"鼠标在右、站台在左"），现在只沿路挪，永不换边。
        /// 绝不动世界里的任何物件：鼠标一移开，原版 Clear 收走幽灵，障碍物一直都在原地。
        /// </summary>
        private float3 SlidePos(float3 basePos, Entity parent, float nudge)
        {
            if (nudge == 0f || parent == Entity.Null) return basePos;
            try
            {
                Curve curve;
                if (!EntityManager.TryGetComponent(parent, out curve)) return basePos;
                float t;
                MathUtils.Distance(curve.m_Bezier.xz, basePos.xz, out t);
                float2 tan = MathUtils.Tangent(curve.m_Bezier, t).xz;
                float len = math.lengthsq(tan);
                if (len < 1e-6f) return basePos;
                float2 dir = tan / math.sqrt(len);
                return new float3(basePos.x + dir.x * nudge, basePos.y, basePos.z + dir.y * nudge);
            }
            catch (Exception)
            {
                return basePos;
            }
        }

        /// <summary>
        /// 逐帧认领上一枚喂出去的幽灵（按 #67 同帧 Modification1 出 Temp、Modification4 吸人行道），
        /// 登记成"世上唯一那一枚"，并在这里做落位查重。
        /// 0.10.0：0.9.0 只给固定 2 帧窗口，卡一帧（模拟负载、切后台）就误判"原版没造出幽灵"，
        /// 实机一场 240 次里有 204 次报的是 `世界 Temp 数=0`（也就是根本没去认第二帧的账），
        /// 玩家看到的就是"预览一闪一闪"。现在改成最多 <c>kClaimMaxFrames</c> 帧逐帧追。
        /// </summary>
        private void ClaimPendingPreview(ControlPoint hover, float3 cursor)
        {
            if (++m_previewWaitFrames > kClaimMaxFrames)
            {
                m_previewFed = false;
                float3 giveUp = m_hasPreviewSpot ? m_previewSpot : cursor;
                if (!m_previewEmptyLogged)
                {
                    m_previewEmptyLogged = true;
                    BusLineAutoStopsMod.log.Info("[预览无幽灵] @(" + Pos(giveUp) + ") 等满 " + kClaimMaxFrames
                        + " 帧原版也没按我们的定义造出 Temp（世界 Temp 数="
                        + m_tempQuery.CalculateEntityCount() + "）");
                }
                // 这一寸根本没生成 → 沿路挪一档再试（最后一档还不成就停止本静止事件喂送）。
                if (m_nudgeIndex < kSlideNudges.Length - 1)
                {
                    m_nudgeIndex++;
                    BusLineAutoStopsMod.log.Info("[预览让位] 沿路 " + Nudge().ToString("0.0") + "m 再试 @("
                        + Pos(cursor) + ")");
                }
                else
                {
                    m_previewRefused = true;
                }
                return;
            }

            float3 anchor = m_hasPreviewSpot ? m_previewSpot : cursor;
            float dist;
            Entity found = FindStopTemp(m_previewPrefab, anchor, out dist);
            if (found == Entity.Null) return;   // 还没出来：下一帧接着认，别急着判"原版没造出"

            m_previewFed = false;
            m_previewTemp = found;

            float3 landed;
            if (!TryEntityPos(found, out landed)) landed = anchor;

            // 原版判定不通过（Error 级标记）→ 这枚幽灵留着只会挡玩家左键，当场收回并停止本静止事件。
            // Warning 级（被路灯/垃圾桶压住的那类）走不到这里：预览保留，左键照样放得下。
            if (HasBlockingError(found))
            {
                m_previewRefused = true;
                RetireTemp(found);
                m_previewTemp = Entity.Null;
                if (!m_previewFailLogged)
                {
                    m_previewFailLogged = true;
                    BusLineAutoStopsMod.log.Info("[预览收回] @(" + Pos(landed) + ") 原版判定不通过（Error）→ 本静止事件不再显示");
                }
                return;
            }

            float dupDist;
            Entity dup = m_stops.Nearest(EntityManager, landed, kSnapDistance, out dupDist);
            if (dup != Entity.Null)
            {
                // 需求 5"该处已有站台就别显示"：量的是**吸附后的实际落位**，不是路中央的悬停点。
                m_previewRefused = true;
                RetireTemp(found);
                m_previewTemp = Entity.Null;
                BusLineAutoStopsMod.log.Info("[预览收回] @(" + Pos(landed) + ") 已有同类站台 "
                    + Describe(dup) + " 相距 " + dupDist.ToString("0.0") + "m → 本静止事件不再显示");
                return;
            }

            // v0.11.2：认领结果的闸门必须与喂送日志的 m_previewAnnounced **分开**——
            // 喂送成功时那个标志已经是 true，共用它就等于这条永远不打（待办 #1 的死分支）。
            if (!m_previewResultLogged)
            {
                m_previewResultLogged = true;
                BusLineAutoStopsMod.log.Info("[预览结果] 幽灵已出，离喂点 " + dist.ToString("0.0")
                    + "m @(" + Pos(landed) + ")，判定="
                    + (EntityManager.HasComponent<Game.Tools.Warning>(found) ? "Warning（可放，障碍物会被替换）" : "通过")
                    + " 落点横向=" + LateralText(m_previewParent, landed, EntityManager)
                    + " 父级=" + Describe(GetAttachedParent(found)) + " 索引=" + m_stops.Count);
            }
        }

        /// <summary>原版给这枚 Temp 挂了 Error 级标记（Game.Tools.Error 与 Warning 是两个独立标记组件）。</summary>
        private bool HasBlockingError(Entity e)
        {
            try { return EntityManager.HasComponent<Game.Tools.Error>(e); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 我们那枚预览幽灵还在世吗（平稳帧只有几次组件读取，不做实体查询）。
        /// 0.9.0：判在世之后还要看原版有没有给它挂 Error —— 挂了就立刻收回，
        /// 免得这枚"放不下的幽灵"把玩家的左键一起挡掉（Optional 当年就是为这件事加的，
        /// 代价是警告级也吞预览，见 FeedPreview）。
        /// </summary>
        private bool IsPreviewAlive()
        {
            Entity e = m_previewTemp;
            if (!IsOurTemp(e))
            {
                // 不在了，或者已经不再是"我们的"那一枚（需求 2）：只脱手，不动手删。
                m_previewTemp = Entity.Null;
                return false;
            }
            if (!HasBlockingError(e)) return true;

            RetireTemp(e);
            m_previewTemp = Entity.Null;
            m_previewRefused = true;
            if (!m_previewFailLogged)
            {
                m_previewFailLogged = true;
                BusLineAutoStopsMod.log.Info("[预览收回] 原版判定不通过（Error）@(" + Pos(m_previewSpot) + ")");
            }
            return false;
        }

        /// <summary>
        /// 在 Temp 查询里找我们那枚站台幽灵（同资产、水平最近），并把距离带出来。
        /// 需求 5（v0.10.0）性能：认领改成逐帧之后，这里绝不能再每帧解析一次资产
        /// （TryGetStopPrefab 要读选中资产的分类 + PrefabSystem 查询），所以父级直接把它喂进来过
        /// 的那枚 prefab 传进来，本函数只做一次 Temp 查询。
        /// </summary>
        private Entity FindStopTemp(Entity prefab, float3 target, out float bestDist)
        {
            bestDist = float.MaxValue;
            if (prefab == Entity.Null) return Entity.Null;
            // 需求 5：世界一颗 Temp 都没有时直接不分配那颗 NativeArray。
            // 实机 2026-10-08 16:10 会话里 `[预览无幽灵]` 240 次有 204 次报的就是"世界 Temp 数=0" ——
            // 逐帧认领把这 10 帧的分配全做了一遍，纯属白干。
            if (m_tempQuery.CalculateEntityCount() == 0) return Entity.Null;

            Entity found = Entity.Null;
            try
            {
                NativeArray<Entity> all = m_tempQuery.ToEntityArray(Allocator.Temp);
                try
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        Entity e = all[i];
                        if (EntityManager.HasComponent<Game.Common.Deleted>(e)) continue;   // 我们已收回的不认领
                        // 需求 2：只认"我们自己喂出来的"Temp（Temp.m_Original==Null）。
                        // 线路工具那枚"把既有站台复制一份"的 Temp 同资产、同位置，绝不能认领、更不能删。
                        if (EntityManager.GetComponentData<Temp>(e).m_Original != Entity.Null) continue;
                        if (EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != prefab) continue;
                        float3 t;
                        if (!TryEntityPos(e, out t)) continue;
                        float d = math.distance(new float2(t.x, t.z), new float2(target.x, target.z));
                        if (d > kProbeTolerance || d >= bestDist) continue;
                        bestDist = d;
                        found = e;
                    }
                }
                finally { all.Dispose(); }
            }
            catch (Exception)
            {
                return Entity.Null;
            }
            return found;
        }

        /// <summary>temp 幽灵身上的 Attached.m_Parent（调试用，拿不到就 Null）。</summary>
        private Entity GetAttachedParent(Entity e)
        {
            try
            {
                Game.Objects.Attached a;
                if (EntityManager.TryGetComponent(e, out a)) return a.m_Parent;
            }
            catch (Exception) { }
            return Entity.Null;
        }

        /// <summary>
        /// 走原版同一条定义通道喂一枚物件定义。Attach 让 GenerateObjectsSystem 写 Attached
        /// （父级 = ResolveAttachParent 解析出的 Edge/Node），Modification4 的 AttachPositionSystem
        /// 负责把它吸到人行道。
        ///
        /// 0.9.0 起预览与提交**都不带 Optional**：Optional 的代价是"任何一条错误数据（含 Overlap
        /// 警告）都会把这枚 Temp 标 Cancel+Hidden"（ValidationSystem.cs:1659-1681），
        /// 玩家看到的"人行道有路灯就不出预览"就是它；挡点击这件事改由 IsPreviewAlive 里
        /// 逐帧检查 Error 级标记来做（只有真不给放才收走幽灵）。
        /// </summary>
        private void FeedStopDefinition(Entity prefab, float3 pos, Entity parent, bool optional = true)
        {
            try
            {
                EntityCommandBuffer ecb = m_OutputBarrier.CreateCommandBuffer();
                Entity e = ecb.CreateEntity();

                CreationDefinition cd = default(CreationDefinition);
                cd.m_Prefab = prefab;
                cd.m_Attached = parent;
                cd.m_Flags = CreationFlags.Attach | (optional ? CreationFlags.Optional : 0);
                cd.m_RandomSeed = UnityEngine.Random.Range(1, int.MaxValue);
                ecb.AddComponent(e, cd);

                ObjectDefinition od = default(ObjectDefinition);
                od.m_Position = pos;
                od.m_Rotation = RotationForParent(pos, parent, out int parentMesh, out float elevation);
                od.m_ParentMesh = parentMesh;
                od.m_Elevation = elevation;
                od.m_Probability = 100;
                od.m_PrefabSubIndex = -1;
                od.m_Scale = 1f;
                od.m_Intensity = 1f;
                ecb.AddComponent(e, od);

                ecb.AddComponent(e, default(Updated));
            }
            catch (Exception e2)
            {
                BusLineAutoStopsMod.log.Warn("[预览站台] 定义喂送失败 " + e2.GetType().Name);
            }
        }

        /// <summary>旋转与标高照 ObjectToolBaseSystem 的规则：父级带 Curve 就贴路向，带 Net.Elevation 就补标高（#44）。</summary>
        private quaternion RotationForParent(float3 pos, Entity parent, out int parentMesh, out float elevation)
        {
            parentMesh = -1;
            elevation = 0f;
            quaternion rot = quaternion.identity;
            try
            {
                if (parent != Entity.Null)
                {
                    if (EntityManager.TryGetComponent<Curve>(parent, out Curve curve))
                    {
                        // MathUtils.Distance 返回距离值并带出最近点参数 t（FillCreationListJob 同款用法）
                        MathUtils.Distance(curve.m_Bezier.xz, pos.xz, out float t);
                        float3 tangent = MathUtils.Tangent(curve.m_Bezier, t);
                        float yaw = math.atan2(tangent.x, tangent.z);
                        rot = quaternion.RotateY(yaw);
                    }
                    if (EntityManager.TryGetComponent<Game.Net.Elevation>(parent, out Game.Net.Elevation elev))
                    {
                        parentMesh = 0;
                        elevation = math.csum(elev.m_Elevation) * 0.5f;
                    }
                }
            }
            catch (Exception) { }
            return rot;
        }

        /// <summary>
        /// 落点判定（v0.8.1，替代 0.8.0 那个把所有悬停一律拒掉的 PlacementAllowed）。这里只做两件便宜的事：
        /// 1) 光标基本压在已有同类站台上（3m 内）→ 不喂定义（需求 5"该处已有站台就别显示"的省事版前置；
        ///    真正的 10m 查重量在预览幽灵**吸附后的实际落位**上，见 ClaimPendingPreview——
        ///    悬停点在路中央，量它 10m 会把对面的站台算成重复，四车道以上就永远不放站）；
        /// 2) 解析不出合法依附父级 → 不喂。
        /// 0.8.0 还额外拒"父级带 Route/Waypoint/RouteWaypoint"——那是错的：线路工具的 SnapJob 会把
        /// 悬停点吸到"正在画的那条线"的 temp 线路或既有线路上（RouteToolSystem.cs:183-191、:156-158），
        /// m_OriginalEntity 于是变成线路实体，跟"能不能在路边放站台"毫无关系。父级改由
        /// ResolveAttachParent 自己从射线命中物解析，不再拿工具的吸附上报当父级。
        /// why 直接进日志，别再写死一句骗人的话。
        /// </summary>
        private bool TryResolveLanding(float3 pos, Entity toolParent, out Entity attach, out string why)
        {
            attach = Entity.Null;
            why = "";
            float nearDist;
            try
            {
                // 需求 3（v0.13.0）：光标已经落进既有同类站台的依附范围 —— 原版 `FindWaypointLocation`
                // 的 ConnectedRoute 分支（RouteToolSystem.cs:277-280）会把这一笔路径点**直接依附**上去，
                // 这里再放第二枚站台既没有意义，还会让幽灵跟站台/物件碰撞出 Error、把玩家那一笔顶掉。
                if (IsSameKindStop(toolParent))
                {
                    why = "光标已在既有站台的依附范围内（" + Describe(toolParent)
                        + "）→ 原版会直接把路径点依附上去，不新建";
                    return false;
                }
                // 悬停点就地查重只用小半径（3m→4m，见 kStopAttachRadius）：真正的"这里已经有站了"判据放到
                // 预览幽灵的**实际落位**上量（ClaimPendingPreview），隔着半条路不算重复。
                Entity near = m_stops.Nearest(EntityManager, pos, kStopAttachRadius, out nearDist);
                if (near != Entity.Null)
                {
                    why = "光标就压在已有同类站台上（" + Describe(near) + " 距 "
                        + nearDist.ToString("0.0") + "m）｜工具父级=" + Describe(toolParent)
                        + "｜索引=" + m_stops.Count;
                    return false;
                }

                string via;
                attach = ResolveAttachParent(pos, toolParent, out via);
                if (attach == Entity.Null)
                {
                    why = "解析不到合法父级：工具父级=" + Describe(toolParent)
                        + "｜射线命中=" + Describe(RaycastHitEntity())
                        + "｜射线父级=" + Describe(RaycastOwner())
                        + "｜索引=" + m_stops.Count;
                    return false;
                }
                why = via;
                return true;
            }
            catch (Exception e)
            {
                why = "判定异常 " + e.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// 光标下的**真实世界点**（需求 1，v0.10.0 的核心一行）。
        ///
        /// 为什么不能用控制点：线路工具的 SnapJob 把悬停点吸到"正在画的那条线"的车行道上，
        /// 并且 RouteToolSystem.cs:234-235 紧接着把 `m_HitPosition` 覆写成 `m_Position` ——
        /// 于是控制点里只剩"路中央"，**横向信息只在射线结果里还活着**。
        /// 而落位在哪一侧完完全全由喂下去的那个点决定（AttachPositionSystem.cs）：
        ///   · SnapRouteLane :236-241 用 `dot(MathUtils.Right(tangent), 喂点 - 曲线点)` 选车道；
        ///   · SnapSegmentAreas :284-333 用 `distancesq(人行道中线点, 喂点)` 选可建造区，
        ///     `:205` 又按 `m_CurvePosition < 0.5` 只喂线段一半的区域。
        /// 喂路中央 = 两侧等距 = 掷硬币，这正是玩家说的"鼠标在右边、原版路径点也在右边，
        /// 站台却去了左边；四车道要很偏右才放右侧"。
        ///
        /// y 一律取悬停点的：射线打在灯杆/建筑上时那个 y 是物件表面高度，不是地面。
        /// 只做一次结构体读取，且整段兜异常；射线不可信（没命中/飞点）就退回悬停点。
        /// </summary>
        private float3 CursorGroundPos(float3 fallback)
        {
            try
            {
                RaycastResult r;
                if (!TryRaycast(out r)) return fallback;
                float3 hit = r.m_Hit.m_HitPosition;
                // net48 参考程序集没有 float.IsFinite（netstandard2.1 才有），用 Unity.Mathematics 的
                // math.isfinite 两条构建链（官方工具链 / scripts csc 直编）都能编
                if (!math.all(math.isfinite(hit.xz))) return fallback;
                if (math.distance(hit.xz, fallback.xz) > kCursorMaxDrift) return fallback;
                return new float3(hit.x, fallback.y, hit.z);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>本帧活跃工具（线路工具）那次射线结果（读一次，三个取用者共用）。</summary>
        private bool TryRaycast(out RaycastResult r)
        {
            r = default(RaycastResult);
            try
            {
                if (m_ToolRaycast != null) return m_ToolRaycast.GetRaycastResult(out r);
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>
        /// 这枚实体能不能当 Attached.m_Parent：AttachPositionSystem.FixAttachedPosition
        /// 只有"带 Node"或"带 Composition（其 edge 要有 NetCompositionArea 缓冲）+ EdgeGeometry + Curve"
        /// 才会正确吸附（AttachPositionSystem.cs:155-175，实机取证见开发笔记 #61 之后的探针行
        /// `组件{Edge|Composition|SubLane[]|EdgeGeometry|Curve}`）。
        /// </summary>
        private bool IsAttachableParent(Entity e)
        {
            try
            {
                if (e == Entity.Null || !EntityManager.Exists(e)) return false;
                if (EntityManager.HasComponent<Game.Net.Node>(e)) return true;

                Game.Net.Composition comp;
                if (!EntityManager.TryGetComponent(e, out comp)) return false;
                if (!EntityManager.HasComponent<Game.Net.EdgeGeometry>(e)) return false;
                if (!EntityManager.HasComponent<Game.Net.Curve>(e)) return false;

                Entity edge = comp.m_Edge != Entity.Null ? comp.m_Edge : e;
                if (!EntityManager.Exists(edge)) return false;
                return EntityManager.HasBuffer<Game.Prefabs.NetCompositionArea>(edge);
            }
            catch (Exception ex)
            {
                // 这里静默 false 过一次就等于"这条路不放站"，必须留一行证据（每静止事件一次，不刷屏）。
                if (!m_attachFailLogged)
                {
                    m_attachFailLogged = true;
                    BusLineAutoStopsMod.log.Warn("[父级判定异常] " + ex.GetType().Name + "：" + ex.Message
                        + " 实体=" + (e == Entity.Null ? "Null" : e.Index + ":" + e.Version));
                }
                return false;
            }
        }

        /// <summary>
        /// 父级解析顺序：工具给的 → 射线给的（父级/命中物）→ 命中物的 Owner 链（Lane→Edge 那类）
        /// → **依附兜底**（v0.12.0，需求 2 实机取证）：
        /// ① 候选物自己不是路、但它**依附在**某条路上 —— 玩家拖拽松手时常正指着另一枚站台
        ///   （站台永远钉在路边，它的 <c>Attached.m_Parent</c> 就是脚下那条路）；
        /// ② 落点近旁（<see cref="kNearestStopParentRadius"/> 米）若有一枚同类站台，沿用它的依附父级。
        /// via 带进日志，方便下次一眼看出是哪条路数通的。
        /// </summary>
        private Entity ResolveAttachParent(float3 pos, Entity toolParent, out string via)
        {
            via = "";
            if (IsAttachableParent(toolParent))
            {
                via = "工具父级 " + Describe(toolParent);
                return toolParent;
            }

            Entity owner = RaycastOwner();
            if (IsAttachableParent(owner))
            {
                via = "射线父级 " + Describe(owner);
                return owner;
            }

            Entity hit = RaycastHitEntity();
            if (IsAttachableParent(hit))
            {
                via = "射线命中 " + Describe(hit);
                return hit;
            }

            Entity e = hit;
            for (int i = 1; i <= 3 && e != Entity.Null; i++)
            {
                Entity next = Entity.Null;
                try
                {
                    Game.Common.Owner o;
                    if (EntityManager.TryGetComponent(e, out o)) next = o.m_Owner;
                }
                catch (Exception) { next = Entity.Null; }
                e = next;
                if (IsAttachableParent(e))
                {
                    via = "命中物第 " + i + " 级父 " + Describe(e);
                    return e;
                }
            }

            // —— 依附兜底①：候选物是"依附体"（站台/依附建筑），沿 Attached.m_Parent 摸回它脚下的路。
            //    只做 TryGetComponent 读，不扫世界、不新增射线读取（需求 5 红线）。
            //    实机 2026-10-19 23:25:30 松手帧：工具父级=射线父级=408903{PrefabRef|ConnRoute[]|}
            //    预制体=Pack7-BusStop01，全链无路 —— 408903 是站台，它的依附父级就是答案。
            Entity probe = GetAttachedParent(toolParent);
            if (IsAttachableParent(probe))
            {
                via = "工具父级依附 " + Describe(probe);
                return probe;
            }
            probe = GetAttachedParent(owner);
            if (IsAttachableParent(probe))
            {
                via = "射线父级依附 " + Describe(probe);
                return probe;
            }
            probe = GetAttachedParent(hit);
            if (IsAttachableParent(probe))
            {
                via = "射线命中依附 " + Describe(probe);
                return probe;
            }

            // —— 依附兜底②：近旁有同类站台就借用它的路（光标还在站旁，路就是那一条）。
            //    只在前面全失败时才走到这里（正常路径零额外查询），Nearest 是索引查询、无分配。
            float nearStopDist;
            Entity nearStop = m_stops.Nearest(EntityManager, pos, kNearestStopParentRadius, out nearStopDist);
            if (nearStop != Entity.Null)
            {
                Entity nearParent = GetAttachedParent(nearStop);
                if (IsAttachableParent(nearParent))
                {
                    via = "近旁站台(" + nearStopDist.ToString("0.0") + "m)依附 " + Describe(nearParent);
                    return nearParent;
                }
            }
            return Entity.Null;
        }

        /// <summary>
        /// 本帧活跃工具（线路工具）那次射线命中的实体。
        /// Raycast 相位（SystemUpdatePhase 序号 30）在 ToolUpdate（18）之后，所以读到的是上一帧
        /// Raycast 产出、也正是在用这一帧控制点的那份结果；写入早已在帧末收干，只做读且整段 try 包起来。
        /// </summary>
        private Entity RaycastHitEntity()
        {
            RaycastResult r;
            return TryRaycast(out r) ? r.m_Hit.m_HitEntity : Entity.Null;
        }

        /// <summary>射线结果的 m_Owner（物件工具把它直接当控制点 m_OriginalEntity 用）。</summary>
        private Entity RaycastOwner()
        {
            RaycastResult r;
            return TryRaycast(out r) ? r.m_Owner : Entity.Null;
        }

        /// <summary>
        /// 实体身份 + 组件构成，写成一行日志（排查"为什么没预览"全靠它）。
        /// 实机教训（2026-10-07 23:02 会话）：整段包一个 try 的写法被一枚 `ArgumentException`
        /// 一把否决，343 行日志全变成"描述失败ArgumentException"，最该看清的那几个实体反而没看清。
        /// 所以每个组件单独判、单独兜异常，坏一个不影响其余，还能看见是哪个坏的。
        /// </summary>
        private string Describe(Entity e)
        {
            if (e == Entity.Null) return "Null";
            try
            {
                if (!EntityManager.Exists(e)) return e.Index + ":" + e.Version + "(已不存在)";
            }
            catch (Exception ex)
            {
                return "Exists?" + ex.GetType().Name;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(96);
            sb.Append(e.Index).Append(':').Append(e.Version).Append('{');
            Flag<Game.Net.Node>(e, sb, "Node");
            Flag<Game.Net.Composition>(e, sb, "Comp");
            Flag<Game.Net.EdgeGeometry>(e, sb, "EdgeGeo");
            Flag<Game.Net.Curve>(e, sb, "Curve");
            Flag<Game.Net.Edge>(e, sb, "Edge");
            Flag<Game.Net.Lane>(e, sb, "Lane");
            Flag<Route>(e, sb, "Route");
            Flag<Game.Routes.Waypoint>(e, sb, "Waypoint");
            Flag<Game.Common.Owner>(e, sb, "Owner");
            Flag<Temp>(e, sb, "Temp");
            Flag<PrefabRef>(e, sb, "PrefabRef");
            BufFlag<Game.Net.SubLane>(e, sb, "SubLane[]");
            BufFlag<ConnectedRoute>(e, sb, "ConnRoute[]");
            BufFlag<Game.Prefabs.NetCompositionArea>(e, sb, "CompArea[]");
            sb.Append('}');
            string extra = "";
            try
            {
                PrefabRef pr = EntityManager.GetComponentData<PrefabRef>(e);
                if (pr.m_Prefab != Entity.Null) extra = " 预制体=" + DescribePrefabName(pr.m_Prefab);
            }
            catch (Exception) { }
            return sb.ToString() + extra;
        }

        /// <summary>资产名（拿得到 PrefabBase 就用真名，拿不到只报实体号）。</summary>
        private string DescribePrefabName(Entity prefab)
        {
            try
            {
                if (m_PrefabSystem != null)
                {
                    PrefabBase pb = m_PrefabSystem.GetPrefab<PrefabBase>(prefab);
                    if (pb != null) return StationAssets.SafeName(pb);
                }
            }
            catch (Exception) { }
            return prefab.Index + ":" + prefab.Version;
        }

        private void Flag<T>(Entity e, System.Text.StringBuilder sb, string name) where T : struct, IComponentData
        {
            try
            {
                if (EntityManager.HasComponent<T>(e)) sb.Append(name).Append('|');
            }
            catch (Exception ex)
            {
                sb.Append(name).Append('!').Append(ex.GetType().Name).Append('|');
            }
        }

        private void BufFlag<T>(Entity e, System.Text.StringBuilder sb, string name) where T : struct, IBufferElementData
        {
            try
            {
                if (EntityManager.HasBuffer<T>(e)) sb.Append(name).Append('|');
            }
            catch (Exception ex)
            {
                sb.Append(name).Append('!').Append(ex.GetType().Name).Append('|');
            }
        }

        /// <summary>预览/提交用哪颗站台资产：优先玩家选中的同类资产，否则按类别兜底（缓存，不刷日志）。</summary>
        private bool TryGetStopPrefab(out PrefabBase pb, out Entity prefab)
        {
            prefab = Entity.Null;
            pb = LazyModeState.SelectedStationPrefab;
            TransportKind kind = LazyModeState.ModeKind;
            TransportKind k;
            if (pb == null || !StationAssets.TryClassifyStop(pb, out k) || k != kind)
            {
                if (m_fallbackStop == null || m_fallbackKind != kind)
                {
                    // 负缓存（kFallbackRetrySeconds）：上一轮已经判定"这一类没有可用站台资产"时，
                    // 5 秒内不再整表重扫 —— FindFallbackStop 会把 PrefabSystem 的全部资产过一遍
                    // 并逐条分类，资产多的存档一帧上万次读取。换资产/换类别/开新一场都会把
                    // m_fallbackKind 清成 None，那时立刻重扫。
                    float t = UnityEngine.Time.realtimeSinceStartup;
                    bool blocked = m_fallbackScanFail == kind && t - m_fallbackScanAt < kFallbackRetrySeconds;
                    if (!blocked)
                    {
                        m_fallbackStop = StationAssets.FindFallbackStop(kind);
                        m_fallbackKind = kind;
                        m_fallbackScanAt = t;
                        m_fallbackScanFail = m_fallbackStop == null ? kind : TransportKind.None;
                    }
                }
                pb = m_fallbackStop;
            }
            if (pb == null) return false;
            prefab = StationAssets.PrefabEntity(pb);
            return prefab != Entity.Null;
        }

        /// <summary>线路工具正在画哪一类线路（拿不到就 None，绝不因为识别失败影响功能）。</summary>
        private TransportKind LineKindOf(RouteToolSystem route)
        {
            try { return StationAssets.TryClassifyLine(route.GetPrefab(), out TransportKind k) ? k : TransportKind.None; }
            catch (Exception) { return TransportKind.None; }
        }

        private static string SafeToolName(ToolBaseSystem t)
        {
            try { return t == null ? "None" : t.GetType().Name; }
            catch (Exception) { return "Unknown"; }
        }

        /// <summary>
        /// 原版这一帧打算走哪条输出管线（kPhase* 三个整数，语义见常量注释）。
        ///
        /// 读的是 <c>ToolBaseSystem.applyMode</c> 这个公开只读属性 —— 本模组**只读不写**，
        /// 反射代按（ToolApplyProxy / GetSetMethod）依旧退役；拿不到就当 None（最保守：
        /// 既不会去补喂，也不会误判成拖拽提交）。
        /// </summary>
        private static int ToolPhase(ToolBaseSystem tool)
        {
            try
            {
                if (tool == null) return kPhaseNone;
                return (int)tool.applyMode;
            }
            catch (Exception) { return kPhaseNone; }
        }

        /// <summary>线路工具当前状态（与 <c>RouteToolSystem.state</c> 同值；拿不到当 Default）。</summary>
        private static ToolLineState SafeLineState(RouteToolSystem route)
        {
            try
            {
                if (route == null) return ToolLineState.Default;
                return (ToolLineState)(int)route.state;
            }
            catch (Exception) { return ToolLineState.Default; }
        }

        /// <summary>
        /// 当前那个悬着的路径点。控制点依赖已在 OnUpdate 里 Complete；
        /// 这里做"还没打到东西"防御（校正 #22：工具刚激活时控制点是 default，位置 (0,0,0)）。
        /// </summary>
        private static bool TryCurrentPoint(NativeList<ControlPoint> cps, out ControlPoint cp)
        {
            cp = default(ControlPoint);
            if (cps.Length == 0) return false;
            cp = cps[cps.Length - 1];
            if (cp.Equals(default(ControlPoint))) return false;
            return math.isfinite(cp.m_Position.x) && math.isfinite(cp.m_Position.z)
                && math.any(cp.m_Position != float3.zero);
        }

        /// <summary>静止就绪状态给前端（懒人模式图标提示用）。</summary>
        private void SettleDwell(bool ready)
        {
            if (LazyModeState.DwellReady == ready) return;
            LazyModeState.DwellReady = ready;
            LazyModeState.UiSeq++;
        }

        private void ResetSettle(float now)
        {
            ResetSettleTimers(now);
            RetirePreview();
        }

        /// <summary>
        /// 只归零计时与日志闸门，**不碰世界**。给"在系统帧之外被叫到"的入口用：
        /// BeginSession 是快捷键回调 / UI 绑定回调里调的，那里不是做结构改动的地方
        /// （本模组在 ExitLazyMode 里已经把这条规矩写死了：只挂请求，改动在系统帧里做）。
        /// </summary>
        private void ResetSettleTimers(float now)
        {
            m_settled = false;
            m_settledSince = now;
            m_lastPos = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
            m_lastCursor = float3.zero;
            m_previewFailLogged = false;
            m_previewAnnounced = false;
            m_previewResultLogged = false;
            m_previewRefused = false;
            m_previewEmptyLogged = false;
            m_attachFailLogged = false;
            m_nudgeIndex = 0;
            m_rescueLogged = false;      // 需求 1（v0.12.0）：新的静止事件，续命日志可以再打一条
            // 注意：这里**不能**再归零 m_prevLineState（0.11.x 的隐藏坑）。ResetSettle 走的是
            // "本帧没有悬停点"那条早退路径（OnUpdate 里采样**之后**才可能进来），拖拽进行中
            // 若有一帧没采到悬停点，闩锁就被洗成 Default，下一帧真正松手时判据永远差一拍
            // （实机 2026-10-19 会话全场只有一条 [拖拽落站] 就是这个原因）。
            // 闩锁本来就每帧重采样（OnUpdate :581），换工具/开新会话的归零点在
            // OnUpdate 的 route==null 分支与 BeginSession —— 那两处才是语义正确的时机。
        }

        // ── 站台索引（需求 6：节流 + 增量） ──────────────────────────────────────
        private void RefreshStopsIfNeeded(float now)
        {
            if (now < m_nextRefresh) return;
            m_nextRefresh = now + (m_useFallback ? kFallbackRefreshSeconds : kRefreshSeconds);

            if (!m_stopQueryVerified)
            {
                m_stopQueryVerified = true;
                if (m_stopQuery.IsEmptyIgnoreFilter)
                {
                    m_useFallback = true;
                    BusLineAutoStopsMod.log.Info("[索引] 站台标签查询为空 → 改用全物体路径（间隔 "
                        + kFallbackRefreshSeconds + "s）");
                }
            }

            try
            {
                if (!m_useFallback)
                {
                    NativeArray<Entity> all = m_stopQuery.ToEntityArray(Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < all.Length; i++) AddIndexEntity(all[i], m_indexKind);
                    }
                    finally { all.Dispose(); }
                }
                else
                {
                    NativeArray<Entity> all = m_objectQuery.ToEntityArray(Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < all.Length; i++) AddIndexEntity(all[i], m_indexKind);
                    }
                    finally { all.Dispose(); }
                }
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("[索引] 重建失败：" + e.GetType().Name + " " + e.Message);
            }
        }

        private void AddIndexEntity(Entity e, TransportKind want)
        {
            if (!EntityManager.Exists(e)) return;
            TransportKind k;
            if (!TryEntityKind(e, out k) || k != want) return;
            float3 p;
            if (!TryEntityPos(e, out p)) return;
            m_stops.Add(e, p);
        }

        private bool TryEntityKind(Entity e, out TransportKind kind)
        {
            kind = TransportKind.None;
            try
            {
                if (!EntityManager.HasComponent<PrefabRef>(e)) return false;
                Entity prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                if (prefab == Entity.Null || !EntityManager.HasComponent<TransportStopData>(prefab)) return false;
                kind = StationAssets.KindOfTransportType(
                    EntityManager.GetComponentData<TransportStopData>(prefab).m_TransportType);
                return kind != TransportKind.None;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 实体在世界里的落位。0.8.0 这里读的是 `Game.Routes.Position`——那是**路径点专用**组件
        /// （GenerateWaypointsSystem.cs:445 只给 waypoint 实体写它），站台物体身上没有，
        /// 于是索引整表为空、"10m 内已有同类站台"这条规则从 0.5.0 起一次都没生效过。
        /// 改成物件的 `Game.Objects.Transform`（RouteToolSystem 自己的 SnapJob 也是读它，:356-358），
        /// 再退回 `Game.Routes.Position` 兼容路径点形态的条目。
        /// </summary>
        private bool TryEntityPos(Entity e, out float3 pos)
        {
            pos = default(float3);
            try
            {
                Game.Objects.Transform t;
                if (EntityManager.TryGetComponent(e, out t))
                {
                    pos = t.m_Position;
                    return math.isfinite(pos.x) && math.isfinite(pos.z);
                }
                Position p;
                if (EntityManager.TryGetComponent(e, out p))
                {
                    pos = p.m_Position;
                    return math.isfinite(pos.x) && math.isfinite(pos.z);
                }
            }
            catch (Exception) { }
            return false;
        }

        // ── 退出清理（需求 5：保留 / 不保留本次会话的站点） ──────────────────────
        /// <summary>
        /// 结构改动（AddComponent&lt;Deleted&gt;）之前必须先把还在跑的 job 收干净：
        /// 我们这个槽紧挨在 ToolOutputSystem 之前，此刻原版线路/物件工具的 job 很可能还在飞行中，
        /// 而"持有只读组件句柄期间改结构"正是空托管栈原生崩溃的典型来源（开发笔记 #63/#64）。
        /// </summary>
        private void FlushPendingJobs()
        {
            try { Dependency.Complete(); }
            catch (Exception) { }
            try
            {
                RouteToolSystem route = m_ToolSystem != null ? m_ToolSystem.activeTool as RouteToolSystem : null;
                if (route != null)
                {
                    route.GetControlPoints(out var deps);
                    deps.Complete();
                }
            }
            catch (Exception) { }
        }

        private void RunExitCleanup(ExitDeletePolicy policy, float now)
        {
            SettleDwell(false);
            // v0.11.2（P1-2）：顺序与上面 FlushPendingJobs 的注释对齐——**先收干净 job，再做结构改动**。
            // 旧写法把 ResetSettle(now)（里面有 RetirePreview → AddComponent<Deleted>）排在
            // FlushPendingJobs() 之前，等于在原版线路/物件工具的 job 还在飞行中就改结构，
            // 正是"持有句柄期间改结构 → 空托管栈原生崩溃"那一类隐患。
            // BeginSession 那条路径（OnUpdate 里 m_beginCleanupPending）本来就是这个顺序，现在两条一致。
            // SettleDwell 只写托管状态，不碰世界，留在最前面没有影响。
            FlushPendingJobs();
            ResetSettle(now);

            int deleted = 0, kept = 0;
            bool doDelete = TransportKindRules.ShouldDeleteSessionStops(policy);
            if (doDelete && m_placed.Count > 0)
            {
                // 需求 4（v0.10.0）：先把依附在这些自动站台上的末尾停靠点撤掉，再删站台，
                // 否则路上只剩一枚孤零零的路径点标志（站台没了，停靠点还挂在原地）。
                int cpRemoved = RemoveAttachedControlPoints();
                if (cpRemoved > 0)
                {
                    BusLineAutoStopsMod.log.Info("[退出清理] 顺带撤掉 " + cpRemoved
                        + " 个依附在自动站台上的停靠点（遇到不是自动放置的就停手）");
                }
                for (int i = 0; i < m_placed.Count; i++)
                {
                    if (DeleteIfSafe(m_placed[i])) deleted++;
                    else kept++;
                }
            }
            else
            {
                kept = m_placed.Count;
            }

            if (m_placed.Count > 0)
            {
                BusLineAutoStopsMod.log.Info("[懒人模式] 退出 policy=" + policy
                    + " 本次会话站点=" + (deleted + kept) + " 删除=" + deleted + " 保留=" + kept);
            }
            m_placed.Clear();
            // 需求 4（v0.14.0）：会话结束 = 这条"跟着路径点一起删"的规则到此为止。
            // 账一清，这些站台就**彻底放置好了**：再进懒人模式也删不到它们（玩家明确要求）。
            // 注意这里只清账、不删站台：拖拽落地的那一路本来就不在 m_placed 里，按 v0.12.0 的
            // 约定"拖拽一次即完成"，退出策略也不该把它们收走。
            if (m_sessionStops.Count > 0)
            {
                BusLineAutoStopsMod.log.Info("[懒人模式] 退出：本会话 " + m_sessionStops.Count
                    + " 枚自动站台转为永久（右键盘点删除规则结束）");
                m_sessionStops.Clear();
            }
            RetirePendingTemps();
            m_pending.Clear();
            m_dragLandingArmed = false; // 需求 2（v0.12.0）：会话结束，补提交作废（落点也随清理一起消失）
            m_haveCpCount = false;
            InvalidateStops();
            LazyModeState.ResetSessionCounters();
        }

        /// <summary>
        /// 需求 4（v0.10.0）：撤掉"依附在本次会话自动站台上"的**末尾**停靠点，
        /// 从最后一个已提交点往前删，遇到第一个不是我们放的（或没依附的）就停手。
        /// 只在当前这条还在画的线上做：别的线路、已经完成的线路、玩家自己放的站台一律不碰。
        ///
        /// 证据与限制（开发笔记 #88，全部 file:line 级）：
        /// · 原版自己撤点是**私有** Cancel（RouteToolSystem.cs:1777-1833：RemoveAtSwapBack 掉悬停槽、
        ///   applyMode=Clear、用新射线回填最后一槽、UpdateDefinitions），模组够不着；
        ///   公开面上只有 :1345 `GetControlPoints(out deps)` 交出来的同一份缓冲，没有 TryRemoveNode
        ///   这类 API（RemoveWaypoint 只是枚举/动作名 :52/:984）。
        /// · 所以只能在 `state == Create` 时自己 RemoveAt，并且三条硬约束：
        ///   ① 从 Length-2 起、绝不动 Length-1（那是悬停位）；
        ///   ② 剩到 3 个就收手（:1190/:1957 直接取 Length-2/Length-1，越少越界）；
        ///   ③ 先删控制点、再删站台 —— 删站台这件事本身会让原版置 forceUpdate
        ///   （ToolBaseSystem.cs:565 m_OriginalDeletedSystem），下一帧它按新的控制点列表
        ///   自己重建预览线路，我们不需要、也不该去碰它的私有定义管线。
        /// 依附关系就在 `ControlPoint.m_OriginalEntity`（:20）：我们改写依附时写的正是它（#83），
        /// 而线路完成时原版也是拿它去连站台（:839-841），所以这个判据和原版的口径一致。
        /// </summary>
        private int RemoveAttachedControlPoints()
        {
            try
            {
                RouteToolSystem route = m_ToolSystem != null ? m_ToolSystem.activeTool as RouteToolSystem : null;
                if (route == null) return 0;
                if (route.state != RouteToolSystem.State.Create) return 0;

                NativeList<ControlPoint> cps = route.GetControlPoints(out var deps);
                deps.Complete();

                int removed = 0;
                for (int i = cps.Length - 2; i >= 1; i--)
                {
                    if (cps.Length <= 3) break;
                    Entity owner = cps[i].m_OriginalEntity;
                    if (owner == Entity.Null || !IsSessionStop(owner)) break;
                    cps.RemoveAt(i);
                    removed++;
                }
                return removed;
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("[退出清理] 撤停靠点失败（站台照常处理）" + e.GetType().Name);
                return 0;
            }
        }

        /// <summary>这个实体是不是本次会话由模组自动落地、并且即将被删的站台。</summary>
        private bool IsSessionStop(Entity e)
        {
            for (int i = 0; i < m_placed.Count; i++) if (m_placed[i] == e) return true;
            return false;
        }

        /// <summary>
        /// 需求 4（v0.14.0）：把"本会话由模组落地的站台"记进账。点击落地与拖拽落地**都记**，
        /// 因为玩家要的规则是"该次懒人模式下自动放置的公交站"，不分来路；而
        /// <see cref="m_placed"/> 作为撤销栈只收点击那一路（见 <see cref="SessionStop"/> 的注释）。
        /// 已在账里就只更新（幂等）：补提交重试可能同一枚站台走到这里两次。
        /// </summary>
        private void RememberSessionStop(Entity stop)
        {
            if (stop == Entity.Null) return;
            for (int i = 0; i < m_sessionStops.Count; i++)
            {
                if (m_sessionStops[i].Stop != stop) continue;
                return;
            }
            m_sessionStops.Add(new SessionStop { Stop = stop, HadWaypoint = false });
        }

        /// <summary>这枚站台还挂着几枚**活着的**路径点（Deleted 标记的算死）。</summary>
        private int CountLiveWaypoints(Entity stop)
        {
            if (!EntityManager.HasComponent<ConnectedRoute>(stop)) return 0;
            DynamicBuffer<ConnectedRoute> buf = EntityManager.GetBuffer<ConnectedRoute>(stop);
            int live = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                Entity wp = buf[i].m_Waypoint;
                if (!EntityManager.Exists(wp)) continue;
                if (EntityManager.HasComponent<Game.Common.Deleted>(wp)) continue;
                live++;
            }
            return live;
        }

        /// <summary>
        /// 需求 4（v0.14.0）：本会话自动放置的站台，**路径点被右键删掉时跟着删**。
        ///
        /// 为什么用"站台自己的 ConnectedRoute 缓冲"当判据、而不是去抓右键事件：
        /// 原版删路径点走的是 `State.Remove` → ApplyRoutesSystem 删实体，随后
        /// `WaypointConnectionSystem` 的 Deleted 分支会把那一枚从站台的 `ConnectedRoute` 里
        /// `RemoveValue` 掉。也就是说"站台曾经连着、现在一枚活的都不剩"本身就是原版留下的
        /// 证据 —— 不用我们猜玩家点了哪一枚、也不用碰原版的输入与状态机。
        ///
        /// 成本：账里只有本会话自动放置的那几枚站台，每帧每人一次 Group 存在性 + 一个小缓冲遍历，
        /// 与"扫世界"无关；账空时是一个 `Count == 0` 的早退。
        /// </summary>
        private void MaintainSessionStops()
        {
            if (m_sessionStops.Count == 0) return;
            try
            {
                for (int i = m_sessionStops.Count - 1; i >= 0; i--)
                {
                    SessionStop rec = m_sessionStops[i];
                    Entity stop = rec.Stop;
                    if (!EntityManager.Exists(stop) || EntityManager.HasComponent<Game.Common.Deleted>(stop))
                    {
                        m_sessionStops.RemoveAt(i);   // 站台已经没了（玩家拆了 / 退出清理删了）
                        continue;
                    }

                    int live = CountLiveWaypoints(stop);
                    if (live > 0)
                    {
                        if (!rec.HadWaypoint)
                        {
                            rec.HadWaypoint = true;
                            m_sessionStops[i] = rec;
                        }
                        continue;
                    }
                    if (!PlacementDecisions.SessionStopLostWaypoint(rec.HadWaypoint, live))
                    {
                        continue;   // 还没连上过：刚落地、线路还没画完，不能当成"被删了"
                    }

                    // 结构改动前先收干在途 job（本类硬规矩）。
                    FlushPendingJobs();
                    if (!DeleteIfSafe(stop))
                    {
                        // 还挂着别的活路径点（另一条线在用）→ 不能删。标记为"已失去依附"的
                        // 状态没必要保留，下一帧 live 仍为 0 会再判一次；这里直接留着观测。
                        continue;
                    }
                    m_sessionStops.RemoveAt(i);
                    int placedIdx = m_placed.IndexOf(stop);
                    if (placedIdx >= 0) m_placed.RemoveAt(placedIdx);
                    BusLineAutoStopsMod.log.Info("[右键撤点] 站台 " + Describe(stop)
                        + " 的路径点已被删除 → 站台一起删除（本会话剩 " + m_sessionStops.Count + " 枚）");
                }
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("[右键撤点] 检查失败：" + e.GetType().Name + " " + e.Message);
            }
        }

        /// <summary>还挂着活路径点的站点不删（原版线路在用，删了会把别人的线弄断）。</summary>
        private bool DeleteIfSafe(Entity e)
        {
            try
            {
                if (!EntityManager.Exists(e)) return false;
                if (EntityManager.HasComponent<ConnectedRoute>(e))
                {
                    DynamicBuffer<ConnectedRoute> buf = EntityManager.GetBuffer<ConnectedRoute>(e);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        if (EntityManager.Exists(buf[i].m_Waypoint)) return false;
                    }
                }
                EntityManager.AddComponent<Game.Common.Deleted>(e);
                return true;
            }
            catch (Exception ex)
            {
                BusLineAutoStopsMod.log.Warn("[退出清理] 删除失败 " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 进入懒人模式时重置本会话状态。**这里一行世界代码都不写**：
        /// 本方法是被 `BusLineAutoStopsMod.OnToggleLazy` 叫的，而那两个调用点
        /// （快捷键 `ProxyAction.onInteraction` 回调、UI 绑定 CallToggle 回调）都在工具级联之外，
        /// 而收幽灵用的 `AddComponent&lt;Game.Common.Deleted&gt;` 是**结构改动** ——
        /// 原版自己也是在这个回调里只转发事件、不动世界（`ToolBaseSystem.cs:384-392`）。
        /// 本模组的退出路径早就按这条规矩写了（`ExitLazyMode` 只挂 `PendingExitCleanup`，
        /// 真正的删除在 OnUpdate 里 `RunExitCleanup`），进入路径以前偷偷在回调里删实体，是同一个
        /// 崩溃类（"持有句柄期间改结构"= 空托管栈原生崩溃）的隐患，现在改成排队。
        /// 见 开发笔记「v0.10.0 安全自查（BlueprintHub 崩溃交接对照）」一节（不改版本号，等玩家定）。
        /// </summary>
        public static void BeginSession()
        {
            LazyModeState.ResetSessionCounters();
            LazyModeSystem s = Instance;
            if (s == null || s.m_destroyed) return;
            s.m_placed.Clear();
            s.m_sessionStops.Clear();          // 需求 4（v0.14.0）：新会话不继承上一场的账
            s.m_pending.Clear();
            s.m_dragLandingArmed = false;      // 新会话不继承上一场的补提交
            s.m_prevLineState = ToolLineState.Default;
            s.m_haveCpCount = false;
            s.m_fallbackStop = null;
            s.m_fallbackKind = TransportKind.None;
            s.m_fallbackScanAt = -999f;   // 见 kFallbackRetrySeconds：允许下一帧就重新扫一次
            s.m_fallbackScanFail = TransportKind.None;
            s.ResetSettleTimers(UnityEngine.Time.realtimeSinceStartup);
            s.SettleDwell(false);
            s.InvalidateStops();
            // 上一场可能留下的"还没转正的提交幽灵 / 手上那枚预览幽灵"由下一帧的系统帧去收
            // （OnUpdate 里先 FlushPendingJobs 再 RetirePendingTemps/RetirePreview）。
            s.m_beginCleanupPending = true;
        }

        private static string Pos(float3 p)
        {
            return p.x.ToString("0.0") + "," + p.y.ToString("0.0") + "," + p.z.ToString("0.0");
        }
    }
}
