using System;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;

namespace BusLineAutoStops
{
    /// <summary>
    /// 每帧识别两件事（旧版都做错过，这里按反编译结论重写）：
    /// 1) 当前激活工具是不是“线路工具”，画的是哪种交通线：RouteToolSystem.prefab 是 RoutePrefab，
    ///    真值在 TransportLineData.m_TransportType（RouteToolSystem.cs:1974 就是这么取的）。
    /// 2) 玩家是否选中了站台资产：ToolBaseSystem.GetPrefab() 返回 PrefabBase，
    ///    真值在 TransportStopData.m_TransportType（SnapJob.ValidateStop 用的同一字段）。
    ///
    /// 旧版把 ToolSystem.selected 当成“选中的资产”、并用组件名字符串匹配 BusStop，两条都不成立；
    /// 而且在懒人模式开启后直接 return，导致实机日志里看不到任何有效信息。
    ///
    /// v0.5.0 性能（需求 6，对照 AccessAnarchy 那份"每帧白干"的教训）：
    /// 这个 Poll 在选项总开关开着时**每帧都跑**，所以稳态成本必须接近零：
    /// - 系统引用按 World 缓存，不再每帧 GetExistingSystemManaged（世界重建时才重取）；
    /// - 先按引用比较 (activeTool, activePrefab)，只有真的换了才去做 prefab 组件查询、
    ///   才拼诊断字符串 —— 之前每帧都在 `GetType().Name + SafeName + 字符串拼接`，纯 GC 压力。
    /// </summary>
    public static class StationSelectTracker
    {
        private static World s_world;
        private static ToolSystem s_toolSystem;
        private static ToolBaseSystem s_lastActive;
        private static PrefabBase s_lastPrefab;
        private static bool s_haveLast;

        public static void Poll(World world)
        {
            if (world == null) return;
            try
            {
                if (!ReferenceEquals(s_world, world) || s_toolSystem == null)
                {
                    s_world = world;
                    s_toolSystem = world.GetExistingSystemManaged<ToolSystem>();
                    s_haveLast = false;
                    s_lastActive = null;
                    s_lastPrefab = null;
                }
                if (s_toolSystem == null) return;

                ToolBaseSystem active;
                try { active = s_toolSystem.activeTool; }
                catch (Exception) { active = null; }

                PrefabBase toolPrefab = null;
                if (active != null)
                {
                    try { toolPrefab = active.GetPrefab(); }
                    catch (Exception) { toolPrefab = null; }
                }

                // 稳态：工具与资产都没换 → 什么都不做（不进资产分类，不拼字符串，不打日志）
                if (s_haveLast && ReferenceEquals(active, s_lastActive)
                    && ReferenceEquals(toolPrefab, s_lastPrefab))
                {
                    return;
                }
                s_haveLast = true;
                s_lastActive = active;
                s_lastPrefab = toolPrefab;

                // —— 线路工具：当前画的是哪类线路 ——
                TransportKind lineKind = TransportKind.None;
                PrefabBase linePrefab = null;
                if (active is RouteToolSystem && StationAssets.TryClassifyLine(toolPrefab, out lineKind))
                {
                    linePrefab = toolPrefab;
                }
                else
                {
                    lineKind = TransportKind.None;
                }
                LazyModeState.ActiveLineKind = lineKind;
                LazyModeState.ActiveLinePrefab = linePrefab;

                // v0.6.0 删掉了"退出线路视图 → 下次进同类交通工具时自动切回站台资产视图"那段：
                // 懒人模式不再占用线路工具，也就没有"替他恢复视图"这件事可做了。

                // —— 站台资产选择 ——
                TransportKind stopKind;
                if (StationAssets.TryClassifyStop(toolPrefab, out stopKind))
                {
                    if (!ReferenceEquals(toolPrefab, LazyModeState.SelectedStationPrefab))
                    {
                        LazyModeState.SelectedStationPrefab = toolPrefab;
                        LazyModeState.SelectedStationKind = stopKind;
                        BusLineAutoStopsMod.log.Info("[选中资产] kind=" + stopKind
                            + " asset=" + StationAssets.SafeName(toolPrefab));
                    }

                    // 需求 3（v0.6.0）：懒人模式进行中换了站台资产 → 面板过滤类别立刻跟着走。
                    // 本版不再要求"和当前线路工具同类"：公交站和电车站都放开，玩家点哪类就筛哪类、
                    // 自动落地也按哪类判"这里已经有站"。跨类切换同样不退出懒人模式。
                    // 需求 4（0.9.0）：电车暂未上线 → 选电车资产**不**把类别切成电车，
                    // 否则系统帧立刻因"这条线不支持"退掉懒人模式，看起来就像"一选电车站就关闭"。
                    if (LazyModeState.LazyEnabled
                        && TransportKindRules.IsSelectableDuringLazy(stopKind, LazyModeState.TramEnabled)
                        && LazyModeState.ModeKind != stopKind)
                    {
                        LazyModeState.ModeKind = stopKind;
                        LazyModeState.UiSeq++;
                        LazyModeState.InvalidateAssetFilter();
                        BusLineAutoStopsMod.log.Info("[切换资产] 懒人模式类别跟随 " + stopKind
                            + "，当前资产=" + StationAssets.SafeName(toolPrefab));
                        Systems.LazyModeSystem.RequestAssetRefresh();
                    }
                }
                else if (!LazyModeState.LazyEnabled && LazyModeState.SelectedStationPrefab != null)
                {
                    LazyModeState.SelectedStationPrefab = null;
                    LazyModeState.SelectedStationKind = TransportKind.None;
                    BusLineAutoStopsMod.log.Info("[选中资产] 清空（当前工具资产不是站台）");
                }

                // —— 诊断：只在工具/资产真的换了时记一行（这个分支本来就已经是"变化"才进来）——
                string dbg = "tool=" + (active != null ? active.GetType().Name : "-")
                             + " asset=" + (toolPrefab != null ? StationAssets.SafeName(toolPrefab) : "-")
                             + " lineKind=" + lineKind;
                BusLineAutoStopsMod.log.Info("[工具] " + dbg);
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("StationSelectTracker: " + e.GetType().Name + " " + e.Message);
            }
        }

        public static void EnsureFresh()
        {
            try { Poll(World.DefaultGameObjectInjectionWorld); }
            catch (Exception) { }
        }
    }
}
