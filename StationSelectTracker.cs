using System;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;

namespace BusLineAutoStops
{
    /// <summary>
    /// 每帧轮询 ToolSystem.selected，判定当前是否选中「公交站台」资产。
    /// 不依赖 SetPrefab 钩子是否命中（实机日志证明钩子路径不可靠）。
    /// </summary>
    public static class StationSelectTracker
    {
        private static float s_nextLogTime;
        private static string s_lastKey = "";

        public static void Poll(World world)
        {
            if (world == null || LazyModeState.LazyEnabled) return;
            try
            {
                ToolSystem toolSystem = world.GetExistingSystemManaged<ToolSystem>();
                if (toolSystem == null) return;

                Entity selected = toolSystem.selected;
                EntityManager em = world.EntityManager;

                if (selected == Entity.Null || !em.Exists(selected))
                {
                    SetNone();
                    return;
                }

                // 站台资产 prefab 上带 TransportStopData
                if (!em.HasComponent<TransportStopData>(selected))
                {
                    SetNone();
                    return;
                }

                TransportKind kind = KindFromTags(em, selected);
                if (kind == TransportKind.None) kind = TransportKind.Bus; // 有 TransportStopData 默认公交

                if (!TransportKindRules.IsLazyModeSupported(kind))
                {
                    SetNone();
                    return;
                }

                string key = selected.Index + ":" + selected.Version;
                LazyModeState.SelectedStationPrefabKey = key;
                LazyModeState.SelectedStationKind = kind;

                if (key != s_lastKey)
                {
                    s_lastKey = key;
                    BusLineAutoStopsMod.log.Info("[选中站台] kind=" + kind + " prefab=" + key);
                }
            }
            catch (Exception e)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now > s_nextLogTime)
                {
                    s_nextLogTime = now + 5f;
                    BusLineAutoStopsMod.log.Warn("StationSelectTracker: " + e.Message);
                }
            }
        }

        private static void SetNone()
        {
            if (s_lastKey.Length > 0)
            {
                s_lastKey = "";
                BusLineAutoStopsMod.log.Info("[选中站台] 清空");
            }
            LazyModeState.SelectedStationPrefabKey = "";
            LazyModeState.SelectedStationKind = TransportKind.None;
        }

        private static TransportKind KindFromTags(EntityManager em, Entity prefab)
        {
            if (HasTag(em, prefab, "BusStop")) return TransportKind.Bus;
            if (HasTag(em, prefab, "TramStop")) return TransportKind.Tram;
            if (HasTag(em, prefab, "TrainStop")) return TransportKind.Train;
            if (HasTag(em, prefab, "SubwayStop")) return TransportKind.Subway;
            if (HasTag(em, prefab, "ShipStop") || HasTag(em, prefab, "FerryStop")) return TransportKind.Ship;
            if (HasTag(em, prefab, "AirplaneStop")) return TransportKind.Airplane;
            return TransportKind.None;
        }

        private static bool HasTag(EntityManager em, Entity e, string typeName)
        {
            try
            {
                var types = em.GetComponentTypes(e);
                for (int i = 0; i < types.Length; i++)
                {
                    string s = types[i].ToString();
                    if (s != null && s.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>快捷键入口：同步刷新一次再读状态。</summary>
        public static void EnsureFresh()
        {
            try
            {
                Poll(Unity.Entities.World.DefaultGameObjectInjectionWorld);
            }
            catch (Exception) { }
        }
    }
}
