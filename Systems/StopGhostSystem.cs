using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BusLineAutoStops.Systems
{
    /// <summary>
    /// 懒人模式悬停预览：在「将要放置的停靠点」位置创建当前站台 Temp 虚影。
    /// 不自写碰撞/坡度/水面：交给原版 ValidationSystem / 渲染（可建=正常色，违规=红虚影）。
    /// 附近已有同类型站台时不建虚影。
    /// </summary>
    public partial class StopGhostSystem : GameSystemBase
    {
        private Entity m_ghost = Entity.Null;
        private float3 m_lastPos;
        private bool m_hasLast;

        protected override void OnUpdate()
        {
            if (LazyModeState.Broken) return;
            if (!LazyModeState.LazyEnabled || LazyModeState.ModeKind == TransportKind.None)
            {
                ClearGhost();
                return;
            }

            if (string.IsNullOrEmpty(LazyModeState.SelectedStationPrefabKey))
            {
                ClearGhost();
                return;
            }

            float3 hover;
            if (!TryGetHoverStopPosition(out hover))
            {
                ClearGhost();
                return;
            }

            if (m_hasLast && math.distance(m_lastPos, hover) < 0.25f && m_ghost != Entity.Null && EntityManager.Exists(m_ghost))
            {
                return;
            }

            ClearGhost();

            // 已有同类型站台 → 不显示
            var auto = World.GetExistingSystemManaged<AutoPlaceSystem>();
            // 复用 AutoPlace 的判定：用一个轻量内联（距离 + 类型）
            if (HasNearby(hover))
            {
                m_hasLast = true;
                m_lastPos = hover;
                return;
            }

            Entity prefab = AutoPlaceSystem.ParseKey(LazyModeState.SelectedStationPrefabKey);
            if (prefab == Entity.Null || !EntityManager.Exists(prefab))
            {
                return;
            }

            try
            {
                Entity e = EntityManager.CreateEntity();
                EntityManager.AddComponentData(e, new PrefabRef { m_Prefab = prefab });
                var t = new Game.Objects.Transform { m_Position = hover, m_Rotation = quaternion.identity };
                EntityManager.AddComponentData(e, t);
                EntityManager.AddComponentData(e, new Temp { m_Flags = TempFlags.Create | TempFlags.Optional });
                EntityManager.AddComponentData(e, new Position { m_Position = hover });
                m_ghost = e;
                m_lastPos = hover;
                m_hasLast = true;
            }
            catch (System.Exception ex)
            {
                BusLineAutoStopsMod.log.Warn("StopGhost: " + ex.Message);
            }
        }

        private bool HasNearby(float3 pos)
        {
            const float snap = 10f;
            EntityQuery q = GetEntityQuery(ComponentType.ReadOnly<Game.Objects.Transform>());
            NativeArray<Entity> objs = q.ToEntityArray(Allocator.Temp);
            var tr = GetComponentLookup<Game.Objects.Transform>(true);
            var pr = GetComponentLookup<PrefabRef>(true);
            for (int i = 0; i < objs.Length; i++)
            {
                Entity o = objs[i];
                if (o == m_ghost) continue;
                if (!tr.HasComponent(o)) continue;
                if (math.distance(tr[o].m_Position, pos) > snap) continue;
                if (!pr.HasComponent(o)) continue;
                Entity prefab = pr[o].m_Prefab;
                if (Patches.ObjectSelectPatches.HasComponentName(EntityManager, prefab, "TransportStopData")
                    || Patches.ObjectSelectPatches.HasComponentName(EntityManager, o, "BusStop")
                    || Patches.ObjectSelectPatches.HasComponentName(EntityManager, prefab, "BusStop"))
                {
                    objs.Dispose();
                    return true;
                }
            }
            objs.Dispose();
            return false;
        }

        /// <summary>
        /// 从 RouteTool 控制点/预览停靠点取悬停位置。
        /// RouteToolSystem 的 control points 是 NativeList&lt;ControlPoint&gt;；公开面不足时用反射读一次。
        /// </summary>
        private bool TryGetHoverStopPosition(out float3 pos)
        {
            pos = float3.zero;
            try
            {
                var routeTool = World.GetExistingSystemManaged<RouteToolSystem>();
                if (routeTool == null) return false;

                var field = routeTool.GetType().GetField(
                    "m_ControlPoints",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                // ToolBaseSystem 常有 GetControlPoints
                var method = routeTool.GetType().GetMethod(
                    "GetControlPoints",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (method != null && method.ReturnType.Name.Contains("NativeList"))
                {
                    object list = method.Invoke(routeTool, null);
                    if (list != null)
                    {
                        var lenProp = list.GetType().GetProperty("Length");
                        int len = lenProp != null ? (int)lenProp.GetValue(list, null) : 0;
                        if (len > 0)
                        {
                            var indexer = list.GetType().GetProperty("Item");
                            object last = indexer != null ? indexer.GetValue(list, new object[] { len - 1 }) : null;
                            if (last != null)
                            {
                                var mPos = last.GetType().GetField("m_Position") ?? last.GetType().GetField("m_HitPosition");
                                if (mPos != null)
                                {
                                    pos = (Unity.Mathematics.float3)mPos.GetValue(last);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception) { }
            return false;
        }

        private void ClearGhost()
        {
            if (m_ghost != Entity.Null && EntityManager.Exists(m_ghost))
            {
                try { EntityManager.DestroyEntity(m_ghost); }
                catch (System.Exception) { }
            }
            m_ghost = Entity.Null;
            m_hasLast = false;
        }

        protected override void OnDestroy()
        {
            ClearGhost();
            base.OnDestroy();
        }
    }
}
