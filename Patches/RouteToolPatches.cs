using System;
using System.Reflection;
using Game.Tools;
using HarmonyLib;

namespace BusLineAutoStops.Patches
{
    /// <summary>
    /// RouteToolSystem 事件：停靠点创建/删除。
    /// 目标是 private Apply/Cancel 或 OnUpdate 后的控制点落地；签名以反编译为准，失败则降级为 ECS 查询（AutoPlaceSystem）。
    /// </summary>
    public static class RouteToolPatches
    {
        private static bool s_applied;
        private static Action s_onWaypointApplied;
        private static Action s_onWaypointRemoved;

        public static void SetHandlers(Action onApplied, Action onRemoved)
        {
            s_onWaypointApplied = onApplied;
            s_onWaypointRemoved = onRemoved;
        }

        public static int Install(Harmony harmony, out string error)
        {
            error = null;
            if (s_applied) return 1;
            int n = 0;
            try
            {
                // 优先 Hook Apply（左键落点提交）。方法名/签名随版本可能变，找不到就只靠 ECS Created 查询。
                MethodInfo apply = typeof(RouteToolSystem).GetMethod(
                    "Apply", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (apply != null)
                {
                    harmony.Patch(apply, prefix: new HarmonyMethod(typeof(RouteToolPatches), nameof(ApplyPrefix)));
                    n++;
                }
                MethodInfo cancel = typeof(RouteToolSystem).GetMethod(
                    "Cancel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (cancel != null)
                {
                    harmony.Patch(cancel, prefix: new HarmonyMethod(typeof(RouteToolPatches), nameof(CancelPrefix)));
                    n++;
                }
                s_applied = n > 0;
                if (n == 0) error = "RouteToolSystem.Apply/Cancel 未找到，改用 ECS 查询";
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            return n;
        }

        private static void ApplyPrefix()
        {
            try
            {
                Action h = s_onWaypointApplied;
                if (h != null) h();
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("RouteTool Apply 钩子异常：" + e.Message);
            }
        }

        private static void CancelPrefix()
        {
            try
            {
                Action h = s_onWaypointRemoved;
                if (h != null) h();
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("RouteTool Cancel 钩子异常：" + e.Message);
            }
        }

        /// <summary>进入懒人模式时切到公交线路创建工具。</summary>
        public static void ActivateBusLineTool()
        {
            // ToolSystem.selected / active tool 切换。公开面若不足则日志降级。
            var toolSystem = Unity.Entities.World.DefaultGameObjectInjectionWorld?
                .GetExistingSystemManaged<ToolSystem>();
            if (toolSystem == null)
            {
                BusLineAutoStopsMod.log.Warn("ToolSystem 不可用，未能切换线路工具");
                return;
            }

            var routeTool = Unity.Entities.World.DefaultGameObjectInjectionWorld?
                .GetExistingSystemManaged<RouteToolSystem>();
            if (routeTool == null)
            {
                BusLineAutoStopsMod.log.Warn("RouteToolSystem 不可用");
                return;
            }

            // ToolBaseSystem 通常有 SetPrefab / tool 切换入口；此处用反射找常见方法，失败则只改状态。
            try
            {
                MethodInfo setActive = typeof(ToolSystem).GetMethod(
                    "SetActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (setActive != null && setActive.GetParameters().Length == 1)
                {
                    setActive.Invoke(toolSystem, new object[] { routeTool });
                    BusLineAutoStopsMod.log.Info("已激活 RouteToolSystem");
                    return;
                }
            }
            catch (Exception e)
            {
                BusLineAutoStopsMod.log.Warn("SetActive 失败：" + e.Message);
            }
            BusLineAutoStopsMod.log.Info("请手动选择公交线路工具（自动切换入口未命中）");
        }
    }
}
