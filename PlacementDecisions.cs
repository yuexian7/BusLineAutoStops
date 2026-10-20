using System;

namespace BusLineAutoStops
{
    /// <summary>线路工具 <c>RouteToolSystem.State</c> 的镜像（Default=0 Create=1 Modify=2 Remove=3）。
    /// 数值与原版枚举逐位对齐，离线断言钉"我们依赖的整数值"，实机侧做一次强转即得。</summary>
    public enum ToolLineState
    {
        Default = 0,
        Create = 1,
        Modify = 2,
        Remove = 3,
    }

    /// <summary>
    /// v0.12.0 两条需求的**纯决策核**（零游戏类型，离线 T3 直接编译断言）。
    ///
    /// · 需求 1（预览闪烁）。反编译取证的完整链条（research/decompiled 1.6.x）：
    ///   - 光标停住时线路工具**并不总是**写 `applyMode=None`：`RouteToolSystem.cs:2191-2197`
    ///     （射线点没变但 `CheckPathUpdates()` 为真 —— 临时线路的路径重算）与 `:2176-2184`
    ///     （SnapJob 把悬停点挪了）都写 **Clear**，`:2146-2153` 的 `forceUpdate` 同理。
    ///   - Clear → `ToolOutputSystem.cs:24-26` 触发 ClearTool 相 → `ToolClearSystem.cs:101`
    ///     给**整个 chunk 的每一枚 Temp** 加 `Deleted`，我们的预览幽灵一起中招。
    ///   - 原版自己的幽灵为什么**不闪**？因为工具每帧 `UpdateDefinitions` = 先毁后建定义
    ///     （#66），同帧 Modification1 的 `GenerateObjectsSystem` 用
    ///     `m_DeletedQuery = Object+Deleted+Temp+PrefabRef`（:1734）把刚标 Deleted 的那枚当"旧物件"
    ///     回收：`CollectCreationDataJob` 按 {Prefab, SubPrefab, Original, Owner} + 最近位置配对
    ///     （:745-783），`CreateObject` 带着 oldEntity 时 **RemoveComponent&lt;Deleted&gt; + Updated**
    ///     （:1103-1130）—— 于是它在帧末 Cleanup 之前就被复活，玩家完全看不出来。
    ///   - 我们只在静止事件沿喂**一次**定义，所以 Clear 帧没有"同帧的新定义"去回收幽灵：
    ///     幽灵真的没了，下一帧才补喂 → **停着不动也闪**。
    ///   ⇒ 修法只能是"同帧补喂"，而不是"发现丢了再补"。`ShouldRescueFeed` 就是这一判据。
    ///     补喂的位置必须是**玩家正看着的那一处**（沿用半径内），且**绝不许动让位阶梯**
    ///     （阶梯是给"原版压根不生成"那一档的兜底，被 Clear 吃掉不算）。
    ///
    /// · 需求 2（拖拽不落地）。拖拽走的是 Modify 态，控制点**恒为 1 个**，
    ///   `TrackControlPointCount` 的数量增量看不见它（#72），所以 0.11.x 整个链路没反应。
    ///   松手那一帧 `Apply()` 的 State.Modify 分支（:2025-2052）先写 `ApplyMode.Apply`、
    ///   **同一帧**就把 `m_State` 打回 Default 并清空控制点再补悬停点 —— 所以检测必须用
    ///   "上一帧我们看到的态 + 本帧原版真的 Apply"这一对（`DecideDragCommit`）。
    ///   落地点就是那一帧的悬停点（工具已经把它换成松手处的射线结果了）。
    ///   右键撤销 / 退出清理**不回退**它（"每次拖拽新增后都算完成"）⇒ 不进 m_placed。
    /// </summary>
    public static class PlacementDecisions
    {
        /// <summary>
        /// 需求 1：静止帧里原版这一帧要把 Temp 清干净（applyMode==Clear），
        /// 而我们手上还有一枚"欠着认领或正显示着"的预览、且玩家仍停在喂点沿用半径内
        /// → 就在**本帧**（ToolOutputSystem 之前）把同一处再喂一遍，让原版回收链路把幽灵续上。
        /// </summary>
        /// <param name="toolClears">本帧原版工具写的是 Clear。</param>
        /// <param name="previewOutstanding">幽灵在世，或定义已喂出还没认领到。</param>
        /// <param name="sameSpot">玩家仍停在上次喂点的沿用半径内（挪开了就该走正常的收+喂）。</param>
        public static bool ShouldRescueFeed(bool toolClears, bool previewOutstanding, bool sameSpot)
        {
            return toolClears && previewOutstanding && sameSpot;
        }

        /// <summary>补喂之后：重新欠一次认领、等待帧数归零（让位阶梯**不动**）。</summary>
        public static void RescueApplied(ref bool previewFed, ref int waitFrames)
        {
            previewFed = true;
            waitFrames = 0;
        }

        /// <summary>
        /// 需求 2：本帧是不是"拖拽松手、原版把线路修改提交（Apply）"那一下。
        /// 只看"上一帧的态 == Modify"：Clear（拖到非法位置后松手 / 右键取消）、
        /// Create 首尾闭合的 Apply、Remove 删线的 Apply 都不算，绝不重复落站。
        ///
        /// 0.12.0 实机补漏：松手帧 `applyMode` 不一定还是 Apply —— 同一帧里 RouteToolSystem 写完
        /// `Apply` 还可能被后面的分支（SnapJob / CheckPathUpdates）改写成 None，那时上一帧仍是
        /// Modify、本帧态已经是 Default，按老判据就整个丢掉这一笔。所以再加一条：
        /// "上一帧 Modify + 本帧退回 Default + 本帧不是 Clear"同样算松手。
        /// Clear 单列出来挡掉，右键取消 / 拖到非法位置那一档就不会误落站。
        /// </summary>
        public static bool DecideDragCommit(ToolLineState previousState, ToolLineState currentState,
            bool toolApplies, bool toolClears)
        {
            if (previousState != ToolLineState.Modify) return false;
            if (toolClears) return false;
            return toolApplies || currentState == ToolLineState.Default;
        }

        /// <summary>
        /// 拖拽落地之后必须归零的预览簿记：幽灵已经交给提交管线（或已被原版清掉），
        /// 预览侧不再欠它一次认领，免得下一帧把已转正的实体当幽灵标 Deleted。
        /// 调用方还要自己把手上的实体句柄置空 —— 那是游戏类型 <c>Entity</c>，
        /// 本决策核刻意零游戏依赖，所以不接这个参数。
        /// </summary>
        public static void DragSettled(ref bool previewFed, ref int waitFrames)
        {
            previewFed = false;
            waitFrames = 0;
        }

        /// <summary>补提交（松手帧解析不出父级 → 后续帧接着解析）的三态。</summary>
        public enum DragRetryOutcome
        {
            /// <summary>还没答案，下一帧再解析。</summary>
            Wait = 0,

            /// <summary>解析成功，就在这一帧补喂并排待提交。</summary>
            Commit = 1,

            /// <summary>别等了：光标离开落点 / 工具又开始新的一笔 / 等满帧数。</summary>
            Abort = 2,
        }

        /// <summary>
        /// 需求 2 的落地判据（v0.12.1，含实机崩溃取证）。
        ///
        /// **松手帧不落地**，等第一个"安静帧"：实机 2026-10-20 22:07:33 拖拽结束瞬间原生崩溃
        /// （Player.log：`Native Crash Reporting / Got a UNKNOWN while executing native code` + 空托管栈）。
        /// 机制：松手帧 `applyMode == Apply`，我们在那一帧收幽灵（`AddComponent&lt;Deleted&gt;`）又喂新定义，
        /// 而同一帧 `GenerateObjectsSystem` 的 `m_DeletedQuery`（Object+Deleted+Temp+PrefabRef，:1734）
        /// 正把"带 Deleted 的幽灵"当可复用的旧物件、对同一实体 `RemoveComponent&lt;Deleted&gt;`
        /// （:1103-1130）；我们喂的新定义又恰好同 prefab + 同位置（沿用预览父级）→
        /// 两条路径对同一实体做互斥的组件增删 = 空托管栈原生崩溃（笔记 #63/#89 同类）。
        /// 所以 Apply 帧一律 Wait：松手帧只记落点，下一帧（与预览喂定义同构）才真正落地。
        ///
        /// 作废的三种情形（都必须作废，否则会把站台落在玩家早就不要的地方）：
        /// 光标离开落点（<paramref name="movedAway"/>）、工具进入新的一笔（<paramref name="toolBusy"/>）、
        /// 等满 <paramref name="maxFrames"/> 帧。参数化帧数是为了让离线断言能跑边界。
        /// </summary>
        public static DragRetryOutcome DecideDragRetry(bool landingResolved, bool movedAway, bool toolBusy,
            bool toolApplies, int waitedFrames, int maxFrames)
        {
            if (movedAway || toolBusy) return DragRetryOutcome.Abort;
            // Apply 帧绝不落地（崩溃取证见上）：等这一帧过去。
            if (toolApplies) return DragRetryOutcome.Wait;
            if (landingResolved) return DragRetryOutcome.Commit;
            if (waitedFrames >= maxFrames) return DragRetryOutcome.Abort;
            return DragRetryOutcome.Wait;
        }

        /// <summary>
        /// 拖拽落地的那一笔要不要参与"右键撤点/退出清理"回退。永远不要：
        /// 拖拽一次就算完成（与原版一致），所以它不进 m_placed 栈。
        /// </summary>
        public static bool DragCommitIsUndoable()
        {
            return false;
        }

        /// <summary>
        /// 需求 1/3（v0.13.0）：这一帧该不该显示预览幽灵。
        ///
        /// 玩家反馈："鼠标移到路径点上还没左键按下去拖拽就已经开始模拟生成一个公交站了"。
        /// 三档判据都取原版自己的判定，不靠猜：
        /// · <paramref name="state"/> == Modify / Remove：工具已经在拖拽 / 在删点，
        ///   这时左键的含义是"改线路"（RouteToolSystem.cs:1908-1920 进 Modify、
        ///   :2025-2052 松手提交），喂出去的幽灵只会跟着光标乱闪。
        /// · Default 态 + <paramref name="hoverOnRouteElement"/>：光标压在既有线路的路径点/路段上。
        ///   原版 `Apply()` 的 Default 分支判据是
        ///   `HasComponent&lt;Route&gt;(cp.m_OriginalEntity) &amp;&amp; math.any(cp.m_ElementIndex &gt;= 0)`
        ///   —— 满足它 = 下一次左键进入 Modify 拖拽，此刻预览一片站台纯属误导。
        /// · <paramref name="hoverOnExistingStop"/>：光标已落进既有站台的依附范围
        ///   （`FindWaypointLocation` 的 ConnectedRoute 分支 / 吸附半径），原版会把这一笔路径点
        ///   **直接依附**到那枚站台上。这时绝不能喂第二枚模拟站台：幽灵跟站台/AI 物件一碰就带
        ///   `Game.Tools.Error`，而 `ToolBaseSystem.GetAllowApply()`（:541-543）只要全世界存在任何
        ///   Error 就整帧为假 —— 玩家那一笔会被原版直接吞掉（实机 2026-10-21 01:08 就是这样失败的）。
        /// </summary>
        public static bool SuppressPreview(ToolLineState state, bool hoverOnRouteElement, bool hoverOnExistingStop)
        {
            if (state == ToolLineState.Modify || state == ToolLineState.Remove) return true;
            if (hoverOnExistingStop) return true;
            return state == ToolLineState.Default && hoverOnRouteElement;
        }

        /// <summary>
        /// 需求 2（v0.13.0）：拖拽落地后，这一枚路径点该不该接到我们刚落地的站台上。
        /// 四种情形一律不动：
        /// · 没找到路径点 → 由调用方打日志，路径点留在路上（不猜、不扫世界）；
        /// · 已经连着**我们这枚**站台 → 幂等（补提交帧重试不会写第二遍）；
        /// · 已经连着别的活实体 → 那是原版自己的依附结果（松手处正好有别的站台），不抢；
        /// · 其余（没连、或连着的实体已经死亡）→ 接上去。
        /// </summary>
        public static bool ShouldAttachDraggedWaypoint(bool waypointFound, bool alreadyOnThisStop, bool connectedElsewhere)
        {
            if (!waypointFound) return false;
            if (alreadyOnThisStop) return false;
            return !connectedElsewhere;
        }

        /// <summary>
        /// 需求 2（v0.13.0）：松手后"光标跑开"必须连续 <paramref name="graceFrames"/> 帧才算数。
        /// 松手帧原版是"先 Clear 控制点、再重新射线补一个"（RouteToolSystem.cs:2039-2048），
        /// 采样点会在 1~2 帧里跳一下；0.12.1 实机（2026-10-21 01:08:28）就因为这一跳，
        /// 把一笔本来已经解析成功的拖拽判成"光标已离开落点"（松手后 28ms 就放弃）。
        /// </summary>
        public static bool DragCursorLeft(int awayFrames, int graceFrames)
        {
            if (graceFrames <= 0) return awayFrames > 0;
            return awayFrames >= graceFrames;
        }
    }
}
