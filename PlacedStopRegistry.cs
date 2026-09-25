using System;
using System.Collections.Generic;

namespace BusLineAutoStops
{
    /// <summary>
    /// 停靠点 ↔ 站台 关联账本（实机侧）。跨帧引用用稳定身份，不用数组下标（Playbook 硬规则 21）。
    /// </summary>
    public static class PlacedStopRegistry
    {
        public struct Ref
        {
            public int Index;
            public int Version;
            public float X, Y, Z;

            public string Key
            {
                get { return Index + ":" + Version + ":" + X.ToString("0.###") + "," + Y.ToString("0.###") + "," + Z.ToString("0.###"); }
            }

            public static Ref From(int index, int version, float x, float y, float z)
            {
                return new Ref { Index = index, Version = version, X = x, Y = y, Z = z };
            }
        }

        private static readonly object s_lock = new object();
        private static readonly PlacedStopLedger s_ledger = new PlacedStopLedger();

        public static PlacedStopLedger Ledger { get { return s_ledger; } }

        public static void Track(Ref stop, Ref station, TransportKind kind, bool byThisMod)
        {
            lock (s_lock) s_ledger.Track(stop.Key, station.Key, kind, byThisMod);
        }

        public static bool TryGetStation(Ref stop, out string stationKey)
        {
            lock (s_lock)
            {
                PlacedStopLedger.Entry e;
                if (s_ledger.TryGet(stop.Key, out e))
                {
                    stationKey = e.StationKey;
                    return true;
                }
                stationKey = null;
                return false;
            }
        }

        public static string GetStationKey(string stopKey)
        {
            lock (s_lock) return s_ledger.GetStationKey(stopKey);
        }

        public static bool ShouldDeleteStationWithStop(Ref stop)
        {
            lock (s_lock) return s_ledger.ShouldDeleteStationWithStop(stop.Key);
        }

        public static bool RemoveStop(Ref stop)
        {
            lock (s_lock) return s_ledger.Remove(stop.Key);
        }

        public static void ClearSession()
        {
            lock (s_lock) s_ledger.Clear();
        }

        public static List<PlacedStopLedger.Entry> Snapshot()
        {
            lock (s_lock) return s_ledger.Snapshot();
        }

        public static void CollectExitDeletes(ExitDeletePolicy policy, List<string> stops, List<string> stations)
        {
            lock (s_lock) s_ledger.CollectExitDeletes(policy, stops, stations);
        }
    }
}
