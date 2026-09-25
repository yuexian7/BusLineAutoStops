using System;
using System.Collections.Generic;

namespace BusLineAutoStops
{
    /// <summary>线路/站台类型。本期只启用 Bus；其余为架构预留。</summary>
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

    /// <summary>中途退出懒人模式时的删除策略（选项下拉框，默认不做任何删除）。</summary>
    public enum ExitDeletePolicy
    {
        DoNothing = 0,
        DeleteStopsAndStations = 1,
        DeleteStationsOnly = 2,
        DeleteStopsOnly = 3,
    }

    /// <summary>
    /// 纯逻辑：类型是否允许进入懒人模式、站台是否算「同类型占用」、删除策略要删什么。
    /// 零游戏类型依赖，供离线 T3 直接编译。
    /// </summary>
    public static class TransportKindRules
    {
        /// <summary>本期实现范围：只有公交。</summary>
        public static bool IsLazyModeSupported(TransportKind kind)
        {
            return kind == TransportKind.Bus;
        }

        /// <summary>懒人模式下可切换的站台必须与当前线路类型一致。</summary>
        public static bool CanSelectDuringLazy(TransportKind modeKind, TransportKind assetKind)
        {
            return modeKind != TransportKind.None && modeKind == assetKind && IsLazyModeSupported(modeKind);
        }

        /// <summary>
        /// 已有站台是否挡住新站台预览/放置。
        /// 只看同类型；电车/火车等其它站台不挡公交预览。
        /// </summary>
        public static bool BlocksNewStation(TransportKind modeKind, TransportKind existingKind)
        {
            return existingKind != TransportKind.None && existingKind == modeKind;
        }

        public struct ExitDeleteFlags
        {
            public bool DeleteStops;
            public bool DeleteStations;
        }

        public static ExitDeleteFlags ResolveExitDelete(ExitDeletePolicy policy)
        {
            switch (policy)
            {
                case ExitDeletePolicy.DeleteStopsAndStations:
                    return new ExitDeleteFlags { DeleteStops = true, DeleteStations = true };
                case ExitDeletePolicy.DeleteStationsOnly:
                    return new ExitDeleteFlags { DeleteStops = false, DeleteStations = true };
                case ExitDeletePolicy.DeleteStopsOnly:
                    return new ExitDeleteFlags { DeleteStops = true, DeleteStations = false };
                default:
                    return new ExitDeleteFlags { DeleteStops = false, DeleteStations = false };
            }
        }
    }

    /// <summary>
    /// 停靠点 ↔ 本模组站台 关联账本（纯逻辑部分）。
    /// 实体身份用字符串键（实机侧用 Index+Version+坐标哈希 规范化，见 PlacedStopRegistry）。
    /// </summary>
    public sealed class PlacedStopLedger
    {
        public sealed class Entry
        {
            public string StopKey;
            public string StationKey;
            public TransportKind Kind;
            public bool ByThisMod;
        }

        private readonly Dictionary<string, Entry> _byStop = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public int Count { get { return _byStop.Count; } }

        public void Track(string stopKey, string stationKey, TransportKind kind, bool byThisMod)
        {
            if (string.IsNullOrEmpty(stopKey)) return;
            _byStop[stopKey] = new Entry
            {
                StopKey = stopKey,
                StationKey = stationKey,
                Kind = kind,
                ByThisMod = byThisMod,
            };
        }

        public bool TryGet(string stopKey, out Entry entry)
        {
            return _byStop.TryGetValue(stopKey, out entry);
        }

        /// <summary>右键删停靠点：只有本模组生成的站台才允许连删。</summary>
        public bool ShouldDeleteStationWithStop(string stopKey)
        {
            Entry e;
            return _byStop.TryGetValue(stopKey, out e) && e.ByThisMod && !string.IsNullOrEmpty(e.StationKey);
        }

        public string GetStationKey(string stopKey)
        {
            Entry e;
            return _byStop.TryGetValue(stopKey, out e) ? e.StationKey : null;
        }

        public bool Remove(string stopKey)
        {
            return _byStop.Remove(stopKey);
        }

        public List<Entry> Snapshot()
        {
            return new List<Entry>(_byStop.Values);
        }

        /// <summary>按退出策略收集要删的键。</summary>
        public void CollectExitDeletes(ExitDeletePolicy policy, List<string> stops, List<string> stations)
        {
            TransportKindRules.ExitDeleteFlags f = TransportKindRules.ResolveExitDelete(policy);
            if (!f.DeleteStops && !f.DeleteStations) return;
            foreach (Entry e in _byStop.Values)
            {
                if (!e.ByThisMod) continue;
                if (f.DeleteStops && !string.IsNullOrEmpty(e.StopKey)) stops.Add(e.StopKey);
                if (f.DeleteStations && !string.IsNullOrEmpty(e.StationKey)) stations.Add(e.StationKey);
            }
        }

        public void Clear()
        {
            _byStop.Clear();
        }
    }
}
