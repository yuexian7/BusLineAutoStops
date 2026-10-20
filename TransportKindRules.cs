using System;

namespace BusLineAutoStops
{
    /// <summary>线路/站台类型。本期启用 Bus 与 Tram；其余为架构预留。</summary>
    public enum TransportKind
    {
        None = 0,
        Bus = 1,
        Tram = 2,
        Train = 3,
        Subway = 4,
        Ship = 5,
        Airplane = 6,
    }

    /// <summary>
    /// 退出懒人模式时怎么处置本次自动落地的公交站（选项下拉框，就两项）。
    ///
    /// v0.6.0 需求 5：原版行为是"线路没完成就退出，这条线的停靠点本来就不会保留"，
    /// 我们不去对抗它，所以选项里不再有"删停靠点"这类项——只管站台资产本身：
    /// 要么留着（玩家接着拿它们连线），要么把本次模组自动落地的那些删掉。
    /// </summary>
    public enum ExitDeletePolicy
    {
        /// <summary>保留公交站（默认）：自动落地的站台留在地图上。</summary>
        KeepStops = 0,

        /// <summary>不保留公交站：删掉本次会话里由模组自动落地的站台（仍被线路停靠点引用的不删）。</summary>
        DeleteSessionStops = 1,
    }

    /// <summary>
    /// 纯逻辑：哪些类型能用、面板该显示哪一类、退出时要不要删站台。
    /// 零游戏类型依赖，供离线 T3 直接编译。
    /// </summary>
    public static class TransportKindRules
    {
        /// <summary>
        /// **分类**范围：公交（Bus）与有轨电车（Tram）——资产识别、面板过滤、站台查重要认得这两类。
        /// 注意这条不是"懒人模式能用哪些类"，那是 <see cref="IsLazyModeEnabled"/>（0.9.0 起电车在那边是关的）。
        ///
        /// 0.6.0 起这条链就是**原版物件工具的放置管线**：这两类的资产（公交站 / 有轨电车站）
        /// 都是靠道路人行道或分界线放置的 Net 附属物（TransportStopData + PlaceableObjectData +
        /// Attach），模组只喂一枚预览，落点/吸附/校验/扣费全归原版。
        /// 火车/地铁/机场/码头用的是车站（Station）体系，放置规则完全不同，不放开。
        /// </summary>
        public static bool IsLazyModeSupported(TransportKind kind)
        {
            return kind == TransportKind.Bus || kind == TransportKind.Tram;
        }

        /// <summary>
        /// 懒人模式**这一版实际放开**的类型（需求 4，0.9.0）。
        ///
        /// 分类能力（IsLazyModeSupported）不动：资产识别、面板过滤、站台查重都还要认得电车站，
        /// 这里只管"能不能拿它跑懒人模式"。电车本版关掉的原因是它的落点规则和公交不是一套
        /// （电车线路只能从电车站连到电车站，悬停点给的是线路/车站实体，按道路吸附那套走不通），
        /// 细节见 Setting.TramEnabled 的注释。
        /// </summary>
        public static bool IsLazyModeEnabled(TransportKind kind, bool tramEnabled)
        {
            if (kind == TransportKind.Tram) return tramEnabled;
            return IsLazyModeSupported(kind);
        }

        /// <summary>
        /// 懒人模式进行中能不能用这个站台资产。
        /// 0.6.0 改口径：不再要求"和当前线路工具同类型"（懒人模式已经不碰线路工具了），
        /// 只要是放开的类型就能选，选了以后**过滤面板跟着换类**（见 LazyModeState.ModeKind）。
        /// 0.9.0 需求 4：电车暂未上线时，选电车资产不把模式切成电车（切了也只会被系统帧退掉，
        /// 表现出来就是"一选电车站懒人模式就自己关了"）。
        /// </summary>
        public static bool IsSelectableDuringLazy(TransportKind assetKind, bool tramEnabled)
        {
            return IsLazyModeEnabled(assetKind, tramEnabled);
        }

        /// <summary>
        /// 已有站台是否算"这个点已经有站了"，用来避免自动落地在同一个位置堆第二个。
        /// 只看同类型：电车/火车等其它站台不挡公交的自动落地。
        /// </summary>
        public static bool BlocksNewStation(TransportKind modeKind, TransportKind existingKind)
        {
            return existingKind != TransportKind.None && existingKind == modeKind;
        }

        /// <summary>退出时要不要回收本次自动落地的站台。</summary>
        public static bool ShouldDeleteSessionStops(ExitDeletePolicy policy)
        {
            return policy == ExitDeletePolicy.DeleteSessionStops;
        }

        /// <summary>
        /// 玩家没动过选项时用的处置策略（需求 2，v0.12.0 的默认值改成**不保留**）。
        ///
        /// 为什么默认要删：懒人模式落地的站台是"跟着这次画线临时放下的"，玩家想要的往往是
        /// "这次没画完就退出，别在地图上留一串孤儿站台"。保留反而是需要主动选的那一项
        /// （想接着拿这些站台连线）。这个默认值只有在**本次会话里真的由模组放过站**时才有意义。
        ///
        /// 单独抽成纯函数是为了让离线 T3 能断言"默认值就是删除"，避免默认值散落在
        /// Setting.SetDefaults / LazyModeState 字段初始化 / ResetForNewWorld 三处各写一遍而漂掉。
        /// </summary>
        public static ExitDeletePolicy DefaultExitPolicy()
        {
            return ExitDeletePolicy.DeleteSessionStops;
        }
    }
}
