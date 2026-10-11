using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Prefabs;
using Game.Routes;
using Unity.Entities;

namespace BusLineAutoStops
{
    /// <summary>
    /// 资产识别：只用游戏的权威数据，不再用「组件名字符串包含 BusStop」这类猜测匹配。
    ///
    /// 反编译依据（1.6.2，Game.dll）：
    /// - Game.Prefabs.TransportStopData.m_TransportType —— 站台资产类型（Bus/Train/Tram/…）唯一真值；
    ///   RouteToolSystem.SnapJob.ValidateStop 用的就是它（prefab TransportStopData.m_TransportType
    ///   == line TransportLineData.m_TransportType），见 RouteToolSystem.cs:239-252。
    /// - Game.Prefabs.TransportLineData.m_TransportType —— 线路资产类型。
    /// - Game.Prefabs.PlaceableObjectData —— 可放置（否则只是逻辑 prefab，玩家放不出来）。
    /// - PrefabSystem.HasComponent&lt;T&gt;/TryGetComponentData&lt;T&gt;/IsAvailable/GetEntity 直接吃 PrefabBase
    ///   （PrefabSystem.cs:482/713/723/728/703）。
    /// </summary>
    public static class StationAssets
    {
        private static readonly PropertyInfo s_prefabsProp = typeof(PrefabSystem).GetProperty(
            "prefabs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static string s_fallbackStopName = "";
        private static string s_fallbackLineName = "";

        public static TransportKind KindOfTransportType(TransportType t)
        {
            switch (t)
            {
                case TransportType.Bus: return TransportKind.Bus;
                case TransportType.Tram: return TransportKind.Tram;
                case TransportType.Train: return TransportKind.Train;
                case TransportType.Subway: return TransportKind.Subway;
                case TransportType.Ship:
                case TransportType.Ferry: return TransportKind.Ship;
                case TransportType.Airplane:
                case TransportType.Helicopter: return TransportKind.Airplane;
                default: return TransportKind.None;
            }
        }

        /// <summary>站台资产：带 TransportStopData 且可放置。类型取 m_TransportType。</summary>
        public static bool TryClassifyStop(PrefabBase pb, out TransportKind kind)
        {
            kind = TransportKind.None;
            if (pb == null) return false;
            PrefabSystem ps = PrefabSys();
            if (ps == null) return false;
            TransportStopData data;
            if (!ps.TryGetComponentData(pb, out data)) return false;
            if (!ps.HasComponent<PlaceableObjectData>(pb)) return false;
            kind = KindOfTransportType(data.m_TransportType);
            return kind != TransportKind.None;
        }

        /// <summary>线路资产：带 TransportLineData（公交/电车…线路工具用的 RoutePrefab）。</summary>
        public static bool TryClassifyLine(PrefabBase pb, out TransportKind kind)
        {
            kind = TransportKind.None;
            if (pb == null) return false;
            PrefabSystem ps = PrefabSys();
            if (ps == null) return false;
            TransportLineData data;
            if (!ps.TryGetComponentData(pb, out data)) return false;
            kind = KindOfTransportType(data.m_TransportType);
            return kind != TransportKind.None;
        }

        public static Entity PrefabEntity(PrefabBase pb)
        {
            PrefabSystem ps = PrefabSys();
            if (ps == null || pb == null) return Entity.Null;
            try { return ps.GetEntity(pb); }
            catch (Exception) { return Entity.Null; }
        }

        /// <summary>
        /// 需求 3（v0.8.0）：把前端 ItemGrid 条目的实体键（原版 Fr() 的 "index:version" 格式）
        /// 解析回 prefab。原版 ToolbarUISystem.BindAsset 写进 JSON 的 entity 就是
        /// PrefabSystem 里可反查的 prefab 实体（BindAsset 用 GetPrefab&lt;PrefabBase&gt;(entity) 取回名字）。
        /// </summary>
        public static PrefabBase PrefabFromEntityKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int colon = key.IndexOf(':');
            if (colon <= 0 || colon == key.Length - 1) return null;
            if (!int.TryParse(key.Substring(0, colon), out int index)) return null;
            if (!int.TryParse(key.Substring(colon + 1), out int version)) return null;
            PrefabSystem ps = PrefabSys();
            if (ps == null) return null;
            try { return ps.GetPrefab<PrefabBase>(new Entity { Index = index, Version = version }); }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// 玩家没有选中站台资产时的兜底：扫一个可用（IsAvailable）的公交站资产。
        /// 这一步是为了消灭旧版「未选择站台资产 → 拒绝进入懒人模式」的死锁。
        /// 偏好造价最低的那个（通常就是原版小站台），并把名字打进日志方便玩家指认。
        /// </summary>
        public static PrefabBase FindFallbackStop(TransportKind kind)
        {
            List<PrefabBase> all = AllPrefabs();
            PrefabSystem ps = PrefabSys();
            PrefabBase best = null;
            uint bestCost = uint.MaxValue;

            foreach (PrefabBase pb in all)
            {
                TransportKind k;
                if (!TryClassifyStop(pb, out k) || k != kind) continue;
                try
                {
                    if (ps != null && !ps.IsAvailable(pb)) continue;
                }
                catch (Exception) { }

                uint cost = uint.MaxValue;
                try
                {
                    if (ps != null) cost = ps.GetComponentData<PlaceableObjectData>(pb).m_ConstructionCost;
                }
                catch (Exception) { }

                if (best == null || cost < bestCost)
                {
                    best = pb;
                    bestCost = cost;
                }
            }
            s_fallbackStopName = best != null ? SafeName(best) : "";
            if (best != null)
            {
                BusLineAutoStopsMod.log.Info("[兜底站台] 选用 " + s_fallbackStopName + " 造价=" + bestCost);
            }
            return best;
        }

        /// <summary>找一个公交“线路”资产，用于把工具切到画公交线。</summary>
        public static PrefabBase FindFallbackLine(TransportKind kind)
        {
            List<PrefabBase> all = AllPrefabs();
            PrefabBase best = null;
            foreach (PrefabBase pb in all)
            {
                TransportKind k;
                if (!TryClassifyLine(pb, out k) || k != kind) continue;
                if (best == null || SafeName(pb).Length < SafeName(best).Length) best = pb;
            }
            s_fallbackLineName = best != null ? SafeName(best) : "";
            return best;
        }

        public static string FallbackStopName { get { return s_fallbackStopName; } }
        public static string FallbackLineName { get { return s_fallbackLineName; } }

        public static string SafeName(PrefabBase pb)
        {
            if (pb == null) return "";
            try { return pb.name ?? ""; }
            catch (Exception) { return ""; }
        }

        private static World s_psWorld;
        private static PrefabSystem s_ps;

        /// <summary>
        /// 按 World 缓存 PrefabSystem（需求 6）：以前每次分类都要走一遍
        /// `World.DefaultGameObjectInjectionWorld` + `GetExistingSystemManaged`，
        /// 而分类是每帧两次的。存档重载会换世界，所以比引用、换了就重取。
        /// </summary>
        private static PrefabSystem PrefabSys()
        {
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null) return null;
            if (ReferenceEquals(s_psWorld, w) && s_ps != null) return s_ps;
            try
            {
                s_ps = w.GetExistingSystemManaged<PrefabSystem>();
                s_psWorld = w;
            }
            catch (Exception) { s_ps = null; s_psWorld = w; }
            return s_ps;
        }

        private static readonly List<PrefabBase> s_cache = new List<PrefabBase>();
        private static float s_cacheTime = -999f;
        private static World s_cacheWorld;

        /// <summary>
        /// 需求 5：给 AssetFilterService 用的枚举入口，走下面同一个 5 秒缓存。
        /// 外部不要在这里做重活：这个列表可能有上万个资产（mod 多的存档）。
        /// </summary>
        public static List<PrefabBase> AllCached()
        {
            return AllPrefabs();
        }

        /// <summary>
        /// PrefabSystem.prefabs 是 internal，只能反射取；结果按 5 秒缓存，避免每帧枚举。
        /// 缓存必须**认世界**：换存档/回主菜单会销毁旧世界，但那批 PrefabBase 还在 5 秒窗口里
        /// 留在静态列表上 —— 拿已销毁世界的 PrefabBase 去问 PrefabSystem 属于失效句柄调用
        /// （BlueprintHub 崩溃交接自查第 3 条：缓存要有生命周期）。世界一换就整表作废。
        /// </summary>
        private static List<PrefabBase> AllPrefabs()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            World w = World.DefaultGameObjectInjectionWorld;
            if (s_cache.Count > 0 && !ReferenceEquals(s_cacheWorld, w))
            {
                s_cache.Clear();
                s_cacheTime = -999f;
            }
            if (s_cache.Count > 0 && now - s_cacheTime < 5f) return s_cache;

            s_cache.Clear();
            PrefabSystem ps = PrefabSys();
            if (ps == null || s_prefabsProp == null) return s_cache;
            try
            {
                object raw = s_prefabsProp.GetValue(ps, null);
                IEnumerable<PrefabBase> seq = raw as IEnumerable<PrefabBase>;
                if (seq == null) return s_cache;
                foreach (PrefabBase pb in seq)
                {
                    if (pb != null) s_cache.Add(pb);
                }
                s_cacheTime = now;
                s_cacheWorld = w;
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("枚举 prefabs 失败：" + e.GetType().Name + " " + e.Message);
            }
            return s_cache;
        }
    }
}
