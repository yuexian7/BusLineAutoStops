using System.Collections.Generic;

namespace BusLineAutoStops.Tests
{
    /// <summary>
    /// 离线 T3：纯逻辑断言（零游戏 DLL）。编译本文件 + TransportKindRules.cs。
    /// </summary>
    public static class Program
    {
        private static int s_pass;
        private static int s_fail;

        private static void Check(bool cond, string name)
        {
            if (cond) { s_pass++; System.Console.WriteLine("PASS " + name); }
            else { s_fail++; System.Console.WriteLine("FAIL " + name); }
        }

        public static int Main()
        {
            // 类型支持范围（v0.5.0：公交 + 有轨电车）
            Check(TransportKindRules.IsLazyModeSupported(TransportKind.Bus), "bus supported");
            Check(TransportKindRules.IsLazyModeSupported(TransportKind.Tram), "tram supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.None), "none not supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.Train), "train not supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.Subway), "subway not supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.Ship), "ship not supported");
            Check(!TransportKindRules.IsLazyModeSupported(TransportKind.Airplane), "airplane not supported");

            // v0.6.0 需求 3：懒人模式不再绑定"当前线路工具的类别"，玩家点哪类站台资产就用哪类。
            // v0.9.0 需求 4：电车那一类从懒人模式里剥出来了 —— 分类仍然认得电车站（上面那组不变），
            // 但"能不能拿它跑懒人模式 / 能不能选它换类别"改由 IsLazyModeEnabled(…, tramEnabled) 说话。
            Check(TransportKindRules.IsLazyModeEnabled(TransportKind.Bus, false), "bus enabled even with tram off");
            Check(!TransportKindRules.IsLazyModeEnabled(TransportKind.Tram, false), "tram gated off in 0.9.0");
            Check(TransportKindRules.IsLazyModeEnabled(TransportKind.Tram, true), "tram enables again when the option opens");
            Check(!TransportKindRules.IsLazyModeEnabled(TransportKind.Train, true), "train never enabled");
            Check(!TransportKindRules.IsLazyModeEnabled(TransportKind.None, true), "none never enabled");

            Check(TransportKindRules.IsSelectableDuringLazy(TransportKind.Bus, false), "bus asset selectable");
            Check(!TransportKindRules.IsSelectableDuringLazy(TransportKind.Tram, false), "tram not selectable while gated");
            Check(TransportKindRules.IsSelectableDuringLazy(TransportKind.Tram, true), "tram selectable when opened");
            Check(!TransportKindRules.IsSelectableDuringLazy(TransportKind.None, true), "none not selectable");
            Check(!TransportKindRules.IsSelectableDuringLazy(TransportKind.Train, true), "train asset not selectable");
            Check(!TransportKindRules.IsSelectableDuringLazy(TransportKind.Subway, true), "subway not selectable");
            Check(!TransportKindRules.IsSelectableDuringLazy(TransportKind.Airplane, true), "airplane not selectable");

            Check(TransportKindRules.BlocksNewStation(TransportKind.Bus, TransportKind.Bus), "bus blocks bus");
            Check(!TransportKindRules.BlocksNewStation(TransportKind.Bus, TransportKind.Tram), "tram not block bus");
            Check(!TransportKindRules.BlocksNewStation(TransportKind.Bus, TransportKind.None), "none not block");
            Check(TransportKindRules.BlocksNewStation(TransportKind.Tram, TransportKind.Tram), "tram blocks tram");
            Check(!TransportKindRules.BlocksNewStation(TransportKind.Tram, TransportKind.Bus), "bus not block tram");

            // 需求 5：退出策略只剩两项，且只有"删本次自动落地的站台"这一种删除，绝不删停靠点。
            Check(!TransportKindRules.ShouldDeleteSessionStops(ExitDeletePolicy.KeepStops), "keep default");
            Check(TransportKindRules.ShouldDeleteSessionStops(ExitDeletePolicy.DeleteSessionStops), "delete session stops");
            Check((int)ExitDeletePolicy.KeepStops == 0, "keep is 0（老存档里的 0 仍然读作保留）");
            Check((int)ExitDeletePolicy.DeleteSessionStops == 1, "delete is 1");
            Check(System.Enum.GetValues(typeof(ExitDeletePolicy)).Length == 2, "下拉只有 2 项");

            // 需求 2（v0.12.0）：默认改成**不保留公交站**，且默认值只有一个来源。
            Check(TransportKindRules.DefaultExitPolicy() == ExitDeletePolicy.DeleteSessionStops,
                  "v0.12.0 需求2：默认不保留公交站");
            Check(TransportKindRules.ShouldDeleteSessionStops(TransportKindRules.DefaultExitPolicy()),
                  "v0.12.0 需求2：默认值走 ShouldDeleteSessionStops 就是删除");


            // v0.6.0 删掉了账本（PlacedStopLedger）：不再"给站台补缓冲 / 事后回填停靠点"，
            // 站台与线路的关系完全交回原版，所以这里没有账本可测了。

            // ===== BLG key 形状（BridgeTheLanguageGap Scope）=====
            const string inst = "BusLineAutoStops.BusLineAutoStops.BusLineAutoStopsMod";
            string pk = BlgKeys.PanelKey("WARN_SELECT_STATION");
            Check(pk.StartsWith("Options.", System.StringComparison.Ordinal), "panel key Options prefix");
            Check(BlgKeys.ExtractIdentifier(pk) != null
                  && BlgKeys.ExtractIdentifier(pk).StartsWith(inst, System.StringComparison.Ordinal),
                  "panel ident starts with setting.id");
            Check(BlgKeys.WouldClassifyAsModOptions(pk, inst), "panel as ModOptions full id");
            Check(BlgKeys.WouldClassifyAsModOptions(pk, "BusLineAutoStops"), "panel as ModOptions short id");
            Check(BlgKeys.WouldClassifyAsModOptions(pk, "BusLineAutoStops.BusLineAutoStops"), "panel as ModOptions ns id");

            string ak = BlgKeys.PanelActionKey("WARN_SELECT_STATION");
            Check(ak.StartsWith("Common.ACTION[", System.StringComparison.Ordinal), "action key prefix");
            Check(BlgKeys.WouldClassifyAsModOptions(ak, inst), "action as ModOptions");

            string ek = BlgKeys.EnumKey("ExitDeletePolicy", "KeepStops");
            Check(ek.StartsWith("Options." + inst + ".ExitDeletePolicy[", System.StringComparison.Ordinal),
                  "enum key shape");
            // 实测/推演：枚举成员 key 的 [] 内只有成员名（KeepStops），
            // BLG ExtractIdentifier 拿不到 setting.id ⇒ 归 GameCore，不是 ModOptions。
            // 下拉文案仍经 Translate（可被 BLG 的「游戏本体」范围翻译）。
            Check(!BlgKeys.WouldClassifyAsModOptions(ek, inst), "enum member is GameCore not ModOptions");
            Check(BlgKeys.WouldClassifyAsModOptions(pk, inst), "panel still ModOptions");

            Check(!BlgKeys.WouldClassifyAsModOptions("Assets.NAME[x]", inst), "asset name not ModOptions");
            Check(!BlgKeys.WouldClassifyAsModOptions("Options.X[OtherMod]", inst), "other mod not ours");
            Check(!BlgKeys.WouldClassifyAsModOptions("Hello world", inst), "bare text not ModOptions");

            // ===== v0.12.0 纯决策核（PlacementDecisions）=====
            // 需求 1：闪烁的真因不是"收回→补喂撞删除宽限期"，而是**原版每帧先毁后建定义**，
            // 同帧 GenerateObjectsSystem 才把刚标 Deleted 的幽灵回收续命；我们只在静止事件沿
            // 喂一次，所以凡是本帧原版要 Clear、而我们手上还欠着一枚预览，就必须**同帧补喂**。
            Check(!PlacementDecisions.ShouldRescueFeed(false, true, true), "rescue off: 本帧原版不 Clear");
            Check(!PlacementDecisions.ShouldRescueFeed(true, false, true), "rescue off: 手上没有欠着的预览");
            Check(!PlacementDecisions.ShouldRescueFeed(true, true, false), "rescue off: 玩家已离开沿用半径");
            Check(PlacementDecisions.ShouldRescueFeed(true, true, true), "rescue on: Clear + 欠着 + 仍在原位");
            {
                bool fed = false; int wait = 7;
                PlacementDecisions.RescueApplied(ref fed, ref wait);
                Check(fed && wait == 0, "rescue: 补喂后重新欠一次认领、等待帧数归零");
            }
            {
                // 补喂只改这两项簿记：让位阶梯（给"原版压根不生成"那一档的兜底）绝不在这条路上烧掉。
                bool fed = false; int wait = 3;
                PlacementDecisions.RescueApplied(ref fed, ref wait);
                Check(fed && wait == 0, "rescue: 幂等记账（第二次仍是 fed/0）");
                PlacementDecisions.RescueApplied(ref fed, ref wait);
                Check(fed && wait == 0, "rescue: 重复调用不累加等待帧");
            }

            // 需求 2：只有 Modify 态 + 原版这一帧真的 Apply，才算"拖拽松手完成"。
            Check((int)ToolLineState.Default == 0 && (int)ToolLineState.Create == 1
                  && (int)ToolLineState.Modify == 2 && (int)ToolLineState.Remove == 3,
                  "ToolLineState 整数值与原版 RouteToolSystem.State 对齐");
            Check(PlacementDecisions.DecideDragCommit(ToolLineState.Modify, ToolLineState.Default, true, false),
                  "drag commit: Modify + Apply");
            // 0.12.0 补漏：松手帧 applyMode 可能已被后面的分支（SnapJob/CheckPathUpdates）改写回 None，
            // 但上一帧仍是 Modify、本帧态已退回 Default —— 同样算松手，不能把这一笔整个丢掉。
            Check(PlacementDecisions.DecideDragCommit(ToolLineState.Modify, ToolLineState.Default, false, false),
                  "drag commit: Modify 退回 Default（applyMode 已到 None 的松手帧也算）");
            Check(!PlacementDecisions.DecideDragCommit(ToolLineState.Modify, ToolLineState.Modify, false, false),
                  "no commit: 还在拖（上一帧与本帧都是 Modify）");
            // Clear 单列出来挡掉：右键取消 / 拖到非法位置后松手那一档，绝不落站。
            Check(!PlacementDecisions.DecideDragCommit(ToolLineState.Modify, ToolLineState.Default, false, true),
                  "no commit: Clear（右键取消/非法位置）");
            Check(!PlacementDecisions.DecideDragCommit(ToolLineState.Modify, ToolLineState.Default, true, true),
                  "no commit: Apply+Clear 同帧仍以 Clear 为准");
            Check(!PlacementDecisions.DecideDragCommit(ToolLineState.Create, ToolLineState.Default, true, false),
                  "no commit: Create 走的是点击链");
            Check(!PlacementDecisions.DecideDragCommit(ToolLineState.Default, ToolLineState.Default, true, false),
                  "no commit: Default（本帧态不可信，判据只看上一帧）");
            Check(!PlacementDecisions.DecideDragCommit(ToolLineState.Remove, ToolLineState.Default, true, false),
                  "no commit: Remove（删线）");
            {
                int wait = 5; bool fed = true;
                PlacementDecisions.DragSettled(ref fed, ref wait);
                Check(!fed && wait == 0, "drag settled: 簿记归零，不把已转正实体当幽灵再删");
            }
            // "每次拖拽新增后都算完成，就跟原版一样无法右键取消回退" ⇒ 拖拽那一笔永不进撤销栈。
            Check(!PlacementDecisions.DragCommitIsUndoable(), "drag commit 不可回退（不进 m_placed）");

            // 需求 2 落地判据：松手后等第一个"安静帧"（v0.12.1 崩溃取证：Apply 帧绝不落地）。
            Check(PlacementDecisions.DecideDragRetry(true, false, false, false, 0, 30)
                      == PlacementDecisions.DragRetryOutcome.Commit,
                  "retry: 解析成功 + 帧安静 → 落地");
            Check(PlacementDecisions.DecideDragRetry(true, false, false, true, 0, 30)
                      == PlacementDecisions.DragRetryOutcome.Wait,
                  "retry: **Apply 帧绝不落地**（v0.12.1 原生崩溃取证）");
            Check(PlacementDecisions.DecideDragRetry(false, false, false, true, 29, 30)
                      == PlacementDecisions.DragRetryOutcome.Wait,
                  "retry: Apply 帧上连超时都不作废（先让这一帧过去）");
            Check(PlacementDecisions.DecideDragRetry(false, false, false, false, 0, 30)
                      == PlacementDecisions.DragRetryOutcome.Wait,
                  "retry: 没答案且没超时 → 再等一帧");
            Check(PlacementDecisions.DecideDragRetry(false, false, false, false, 29, 30)
                      == PlacementDecisions.DragRetryOutcome.Wait,
                  "retry: 第 29 帧仍在预算内");
            Check(PlacementDecisions.DecideDragRetry(false, false, false, false, 30, 30)
                      == PlacementDecisions.DragRetryOutcome.Abort,
                  "retry: 等满 30 帧作废");
            Check(PlacementDecisions.DecideDragRetry(true, true, false, false, 1, 30)
                      == PlacementDecisions.DragRetryOutcome.Abort,
                  "retry: 光标离开落点 → 作废（优先于 Commit）");
            Check(PlacementDecisions.DecideDragRetry(false, true, false, false, 1, 30)
                      == PlacementDecisions.DragRetryOutcome.Abort,
                  "retry: 光标离开落点 → 作废");
            Check(PlacementDecisions.DecideDragRetry(true, false, true, false, 1, 30)
                      == PlacementDecisions.DragRetryOutcome.Abort,
                  "retry: 工具开始新的一笔 → 作废（优先于 Commit）");
            Check(PlacementDecisions.DecideDragRetry(false, false, true, false, 1, 30)
                      == PlacementDecisions.DragRetryOutcome.Abort,
                  "retry: 工具开始新的一笔 → 作废");
            // 预算为 0 时：安静帧上也等不到答案，但"作废"必须来自超时而不是 Apply 帧。
            Check(PlacementDecisions.DecideDragRetry(false, false, false, false, 0, 0)
                      == PlacementDecisions.DragRetryOutcome.Abort,
                  "retry: maxFrames=0 立即作废");

            // ── v0.13.0 需求 1/3：预览闸门（拖拽前不预览 / 站台依附范围内不预览）──
            Check(PlacementDecisions.SuppressPreview(ToolLineState.Modify, false, false),
                  "preview 闸门: 拖拽中（Modify）不预览");
            Check(PlacementDecisions.SuppressPreview(ToolLineState.Remove, false, false),
                  "preview 闸门: 删点中（Remove）不预览");
            Check(PlacementDecisions.SuppressPreview(ToolLineState.Default, true, false),
                  "preview 闸门: Default 态压在既有路径点/路段上不预览（下一次左键是拖拽）");
            Check(PlacementDecisions.SuppressPreview(ToolLineState.Default, false, true),
                  "preview 闸门: 光标已在既有站台依附范围内不预览（原版会直接依附）");
            Check(PlacementDecisions.SuppressPreview(ToolLineState.Default, true, true),
                  "preview 闸门: 两种抑制同时命中仍然不预览");
            Check(!PlacementDecisions.SuppressPreview(ToolLineState.Default, false, false),
                  "preview 闸门: 空路上的静止照常预览");
            Check(!PlacementDecisions.SuppressPreview(ToolLineState.Create, true, false),
                  "preview 闸门: 画线途中（Create）即使吸到线路上也要预览 —— 那是加路径点，不是拖拽");
            Check(!PlacementDecisions.SuppressPreview(ToolLineState.Create, false, false),
                  "preview 闸门: 画线途中的空路照常预览");

            // ── v0.13.0 需求 2：拖拽落地后把被拖路径点接到新站台 ──
            Check(!PlacementDecisions.ShouldAttachDraggedWaypoint(false, false, false),
                  "拖拽依附: 找不到路径点 → 不写");
            Check(!PlacementDecisions.ShouldAttachDraggedWaypoint(true, true, false),
                  "拖拽依附: 已经连着这枚站台 → 幂等跳过（重试帧不写第二遍）");
            Check(!PlacementDecisions.ShouldAttachDraggedWaypoint(true, false, true),
                  "拖拽依附: 已依附在别的活实体上 → 不抢");
            Check(PlacementDecisions.ShouldAttachDraggedWaypoint(true, false, false),
                  "拖拽依附: 没连（或连着的已死）→ 接上");

            // ── v0.13.0 需求 2：松手后"光标跑开"要连续几帧才算数（0.12.1 实机被 1 帧跳变骗过）──
            Check(!PlacementDecisions.DragCursorLeft(0, 3), "drag away: 连续 0 帧 → 不算走开");
            Check(!PlacementDecisions.DragCursorLeft(2, 3), "drag away: 连续 2 帧仍在宽限内（松手帧采样会跳）");
            Check(PlacementDecisions.DragCursorLeft(3, 3), "drag away: 连续 3 帧 → 判为走开");
            Check(!PlacementDecisions.DragCursorLeft(0, 0), "drag away: 宽限 0 帧时不动就不算走开");
            Check(PlacementDecisions.DragCursorLeft(1, 0), "drag away: 宽限 0 帧时动 1 帧就算走开");

            System.Console.WriteLine("TOTAL pass=" + s_pass + " fail=" + s_fail);
            return s_fail == 0 ? 0 : 1;
        }
    }
}
