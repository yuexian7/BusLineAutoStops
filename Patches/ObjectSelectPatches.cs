using System;
using System.Reflection;
using Game.Prefabs;
using Game.Tools;
using HarmonyLib;
using Unity.Entities;

namespace BusLineAutoStops.Patches
{
    /// <summary>
    /// 跟踪当前选中的站台资产（ObjectTool / ToolSystem.selected prefab）。
    /// 判定是否站台：prefab 带 TransportStopData；类型用 TransportType 映射到 TransportKind。
    /// </summary>
    public static class ObjectSelectPatches
    {
        private static bool s_applied;

        public static int Install(Harmony harmony, out string error)
        {
            error = null;
            if (s_applied) return 1;
            int n = 0;
            try
            {
                // ToolSystem.selected 赋值处或 ObjectToolSystem.SetPrefab
                MethodInfo setPrefab = typeof(ObjectToolSystem).GetMethod(
                    "SetPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (setPrefab != null)
                {
                    harmony.Patch(setPrefab, postfix: new HarmonyMethod(typeof(ObjectSelectPatches), nameof(SetPrefabPostfix)));
                    n++;
                }
                else
                {
                    // 兜底：ToolBaseSystem.TrySetPrefab
                    MethodInfo trySet = typeof(ToolBaseSystem).GetMethod(
                        "TrySetPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (trySet != null)
                    {
                        harmony.Patch(trySet, postfix: new HarmonyMethod(typeof(ObjectSelectPatches), nameof(TrySetPrefabPostfix)));
                        n++;
                    }
                }
                s_applied = n > 0;
                if (n == 0) error = "站台选择钩子未找到，将轮询 ToolSystem.selected";
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            return n;
        }

        private static void SetPrefabPostfix(object __instance)
        {
            try { RefreshFromTool(__instance as ToolBaseSystem); }
            catch (Exception e) { BusLineAutoStopsMod.log.Warn("SetPrefab 后处理：" + e.Message); }
        }

        private static void TrySetPrefabPostfix(object __instance)
        {
            try { RefreshFromTool(__instance as ToolBaseSystem); }
            catch (Exception e) { BusLineAutoStopsMod.log.Warn("TrySetPrefab 后处理：" + e.Message); }
        }

        public static void RefreshFromTool(ToolBaseSystem tool)
        {
            if (tool == null) return;
            Entity prefab = Entity.Null;
            try
            {
                PropertyInfo p = tool.GetType().GetProperty("prefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) prefab = (Entity)p.GetValue(tool, null);
                else
                {
                    MethodInfo g = tool.GetType().GetMethod("GetPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (g != null) prefab = (Entity)g.Invoke(tool, null);
                }
            }
            catch (Exception) { }

            if (prefab == Entity.Null)
            {
                LazyModeState.SelectedStationPrefabKey = "";
                LazyModeState.SelectedStationKind = TransportKind.None;
                return;
            }

            ClassifyPrefab(prefab);
        }

        /// <summary>把 prefab 归类为站台类型；非站台则清空选择。</summary>
        public static void ClassifyPrefab(Entity prefab)
        {
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            EntityManager em = w.EntityManager;

            if (!em.HasComponent<PrefabRef>(prefab) && em.HasComponent<PlaceableObjectData>(prefab) == false)
            {
                // prefab 实体本身通常带 Prefab 组件与数据组件
            }

            TransportKind kind = TransportKind.None;
            bool isStation = false;

            // TransportStopData 在 Game.Prefabs（部分版本未完整反编译，用 TryGetComponent）
            try
            {
                var lookup = em.GetComponentTypeHandle<TransportStopData>(true);
            }
            catch (Exception) { }

            // 用 ComponentLookup 探测常见标签
            if (HasComponentName(em, prefab, "TransportStopData"))
            {
                isStation = true;
                kind = KindFromStopData(em, prefab);
            }

            // 建筑站台：TransportStation / PublicTransportStation + 子停靠
            if (!isStation && (HasComponentName(em, prefab, "TransportStation") || HasComponentName(em, prefab, "PublicTransportStation")))
            {
                // 建筑类站台本期也认；类型需从 Stop 子对象或 RouteConnection 推断，先按 Bus 预留
                isStation = true;
                kind = KindFromStopData(em, prefab);
                if (kind == TransportKind.None) kind = TransportKind.Bus;
            }

            if (!isStation || kind == TransportKind.None)
            {
                // 不是站台或未知类型：不更新「当前选中站台」（允许中途切到别的工具），
                // 但若懒人模式已开则保留原选择，避免误清。
                if (!LazyModeState.LazyEnabled)
                {
                    LazyModeState.SelectedStationPrefabKey = "";
                    LazyModeState.SelectedStationKind = TransportKind.None;
                }
                return;
            }

            LazyModeState.SelectedStationPrefabKey = PrefabKey(prefab);
            LazyModeState.SelectedStationKind = kind;
            BusLineAutoStopsMod.log.Info("[选中站台] kind=" + kind + " key=" + LazyModeState.SelectedStationPrefabKey);
        }

        public static string PrefabKey(Entity e)
        {
            return e.Index + ":" + e.Version;
        }

        public static bool HasComponentName(EntityManager em, Entity e, string typeName)
        {
            try
            {
                foreach (ComponentType t in em.GetComponentTypes(e))
                {
                    if (t.TypeIndex == 0) continue;
                    string n = t.GetManagedType() != null ? t.GetManagedType().Name : t.ToString();
                    if (n != null && n.IndexOf(typeName, StringComparison.Ordinal) >= 0) return true;
                }
            }
            catch (Exception) { }
            // ComponentType.ToString 形如 "Game.Prefabs.TransportStopData"
            try
            {
                var types = em.GetComponentTypes(e);
                for (int i = 0; i < types.Length; i++)
                {
                    string s = types[i].ToString();
                    if (s != null && s.IndexOf(typeName, StringComparison.Ordinal) >= 0) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        private static TransportKind KindFromStopData(EntityManager em, Entity prefab)
        {
            // 空标签组件：BusStop / TramStop / …
            if (HasComponentName(em, prefab, "BusStop") || HasComponentName(em, prefab, "RouteConnectionData"))
            {
                // RouteConnectionData 不区分类型；优先显式标签
            }
            if (HasComponentName(em, prefab, "BusStop")) return TransportKind.Bus;
            if (HasComponentName(em, prefab, "TramStop")) return TransportKind.Tram;
            if (HasComponentName(em, prefab, "TrainStop")) return TransportKind.Train;
            if (HasComponentName(em, prefab, "SubwayStop")) return TransportKind.Subway;
            if (HasComponentName(em, prefab, "ShipStop") || HasComponentName(em, prefab, "FerryStop")) return TransportKind.Ship;
            if (HasComponentName(em, prefab, "AirplaneStop")) return TransportKind.Airplane;

            // TransportStopData 是 struct，不在此装箱反射；有显式 *Stop 标签时以上面分支为准。
            // 仅有 TransportStopData 时按本期范围默认公交。

            // 仅有 TransportStopData 时默认当公交（本期范围），实机可用日志校正
            return TransportKind.Bus;
        }
    }
}
