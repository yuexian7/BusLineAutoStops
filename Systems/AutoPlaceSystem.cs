using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using BusLineAutoStops.Patches;

namespace BusLineAutoStops.Systems
{
    /// <summary>
    /// 懒人模式下：检测新建公交停靠点 → 自动放置站台；删除停靠点时联动删除本模组站台。
    /// 优先 ECS 查询 Created/Deleted 的 Waypoint+BusStop，不依赖 RouteTool 钩子是否命中。
    /// </summary>
    public partial class AutoPlaceSystem : GameSystemBase
    {
        private EntityQuery m_createdWaypoints;
        private EntityQuery m_deletedWaypoints;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_createdWaypoints = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Waypoint>(),
                    ComponentType.ReadOnly<BusStop>(),
                    ComponentType.ReadOnly<Created>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            m_deletedWaypoints = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Waypoint>(),
                    ComponentType.ReadOnly<BusStop>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            });
        }

        protected override void OnUpdate()
        {
            if (LazyModeState.Broken) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            LazyModeState.ClearWarningIfExpired(now);

            if (!LazyModeState.LazyEnabled || LazyModeState.ModeKind == TransportKind.None)
            {
                return;
            }

            HandleDeletedWaypoints();
            HandleCreatedWaypoints();
        }

        private void HandleCreatedWaypoints()
        {
            if (m_createdWaypoints.IsEmptyIgnoreFilter) return;

            NativeArray<Entity> entities = m_createdWaypoints.ToEntityArray(Allocator.Temp);
            ComponentLookup<Position> positions = GetComponentLookup<Position>(true);
            ComponentLookup<Game.Objects.Transform> transforms = GetComponentLookup<Game.Objects.Transform>(true);

            Entity stationPrefab = ResolveSelectedPrefab();
            for (int i = 0; i < entities.Length; i++)
            {
                Entity wp = entities[i];
                float3 pos = GetPos(wp, positions, transforms);

                // 已有同类型站台则不放
                if (HasNearbySameKindStation(pos, LazyModeState.ModeKind))
                {
                    BusLineAutoStopsMod.log.Info("[自动站台] 跳过：附近已有同类型站台 pos=" + pos);
                    continue;
                }

                if (stationPrefab == Entity.Null)
                {
                    BusLineAutoStopsMod.log.Warn("[自动站台] 选中站台 prefab 无效，仅放置停靠点");
                    continue;
                }

                Entity station = TryPlaceStation(stationPrefab, pos);
                if (station != Entity.Null)
                {
                    var stopRef = PlacedStopRegistry.Ref.From(wp.Index, wp.Version, pos.x, pos.y, pos.z);
                    var stRef = PlacedStopRegistry.Ref.From(station.Index, station.Version, pos.x, pos.y, pos.z);
                    PlacedStopRegistry.Track(stopRef, stRef, LazyModeState.ModeKind, true);
                    BusLineAutoStopsMod.log.Info("[自动站台] 已放置 station=" + stRef.Key + " @ " + pos);
                }
                else
                {
                    BusLineAutoStopsMod.log.Warn("[自动站台] 放置失败（建造规则不允许或碰撞）pos=" + pos + "；停靠点仍保留");
                }
            }
            entities.Dispose();
        }

        private void HandleDeletedWaypoints()
        {
            if (m_deletedWaypoints.IsEmptyIgnoreFilter) return;

            NativeArray<Entity> entities = m_deletedWaypoints.ToEntityArray(Allocator.Temp);
            ComponentLookup<Position> positions = GetComponentLookup<Position>(true);
            ComponentLookup<Game.Objects.Transform> transforms = GetComponentLookup<Game.Objects.Transform>(true);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity wp = entities[i];
                float3 pos = GetPos(wp, positions, transforms);
                var stopRef = PlacedStopRegistry.Ref.From(wp.Index, wp.Version, pos.x, pos.y, pos.z);

                if (!PlacedStopRegistry.ShouldDeleteStationWithStop(stopRef))
                {
                    PlacedStopRegistry.RemoveStop(stopRef);
                    continue;
                }

                string stationKey = PlacedStopRegistry.GetStationKey(stopRef.Key);
                if (!string.IsNullOrEmpty(stationKey))
                {
                    if (TryDeleteByKey(stationKey))
                    {
                        BusLineAutoStopsMod.log.Info("[删除联动] 已删本模组站台 " + stationKey);
                    }
                }
                PlacedStopRegistry.RemoveStop(stopRef);
            }
            entities.Dispose();
        }

        private static float3 GetPos(Entity e, ComponentLookup<Position> positions, ComponentLookup<Game.Objects.Transform> transforms)
        {
            if (positions.HasComponent(e)) return positions[e].m_Position;
            if (transforms.HasComponent(e)) return transforms[e].m_Position;
            return float3.zero;
        }

        private Entity ResolveSelectedPrefab()
        {
            string key = LazyModeState.SelectedStationPrefabKey;
            if (string.IsNullOrEmpty(key)) return Entity.Null;
            Entity e = ParseKey(key);
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null || e == Entity.Null) return Entity.Null;
            return w.EntityManager.Exists(e) ? e : Entity.Null;
        }

        public static Entity ParseKey(string key)
        {
            // 形如 "index:version"
            if (string.IsNullOrEmpty(key)) return Entity.Null;
            string[] parts = key.Split(':');
            int idx, ver;
            if (parts.Length >= 2 && int.TryParse(parts[0], out idx) && int.TryParse(parts[1], out ver))
            {
                return new Entity { Index = idx, Version = ver };
            }
            return Entity.Null;
        }

        /// <summary>
        /// 吸附距离内是否已有同类型站台。
        /// 含：带 TransportStopData 的 NetObject；建筑 TransportStation 的 SubObject 停靠。
        /// 其它类型站台不挡（TransportKindRules.BlocksNewStation）。
        /// </summary>
        private bool HasNearbySameKindStation(float3 pos, TransportKind kind)
        {
            const float snap = 10f; // RouteUtils.WAYPOINT_CONNECTION_DISTANCE
            World w = World;
            EntityManager em = w.EntityManager;

            // 1) 独立站台物体
            EntityQuery q = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
            });
            NativeArray<Entity> objs = q.ToEntityArray(Allocator.Temp);
            ComponentLookup<Game.Objects.Transform> tr = GetComponentLookup<Game.Objects.Transform>(true);
            ComponentLookup<PrefabRef> pr = GetComponentLookup<PrefabRef>(true);
            for (int i = 0; i < objs.Length; i++)
            {
                Entity o = objs[i];
                if (!tr.HasComponent(o)) continue;
                float3 p = tr[o].m_Position;
                if (math.distance(p, pos) > snap) continue;
                if (!pr.HasComponent(o)) continue;
                Entity prefab = pr[o].m_Prefab;
                if (prefab == Entity.Null) continue;

                TransportKind existing = KindOfPrefabOrEntity(em, prefab, o);
                if (TransportKindRules.BlocksNewStation(kind, existing))
                {
                    objs.Dispose();
                    return true;
                }
            }
            objs.Dispose();
            return false;
        }

        private static TransportKind KindOfPrefabOrEntity(EntityManager em, Entity prefab, Entity instance)
        {
            if (ObjectSelectPatches.HasComponentName(em, prefab, "BusStop") || ObjectSelectPatches.HasComponentName(em, instance, "BusStop"))
                return TransportKind.Bus;
            if (ObjectSelectPatches.HasComponentName(em, prefab, "TramStop") || ObjectSelectPatches.HasComponentName(em, instance, "TramStop"))
                return TransportKind.Tram;
            if (ObjectSelectPatches.HasComponentName(em, prefab, "TrainStop") || ObjectSelectPatches.HasComponentName(em, instance, "TrainStop"))
                return TransportKind.Train;
            if (ObjectSelectPatches.HasComponentName(em, prefab, "SubwayStop") || ObjectSelectPatches.HasComponentName(em, instance, "SubwayStop"))
                return TransportKind.Subway;
            if (ObjectSelectPatches.HasComponentName(em, prefab, "TransportStopData") || ObjectSelectPatches.HasComponentName(em, instance, "TransportStop"))
                return TransportKind.Bus;
            return TransportKind.None;
        }

        /// <summary>
        /// 放置站台：创建带 PrefabRef+Transform 的对象实体，并交给工具 Apply 管线
        /// （Temp.Create → ApplyObjectsSystem）。失败返回 Null（建造规则不允许时同样）。
        /// </summary>
        private Entity TryPlaceStation(Entity prefab, float3 position)
        {
            try
            {
                World w = World;
                EntityManager em = w.EntityManager;

                Entity e = em.CreateEntity();
                em.AddComponentData(e, new PrefabRef { m_Prefab = prefab });
                var t = new Game.Objects.Transform();
                t.m_Position = position;
                t.m_Rotation = quaternion.identity;
                em.AddComponentData(e, t);
                em.AddComponentData(e, new Temp { m_Flags = TempFlags.Create });
                em.AddComponentData(e, new Position { m_Position = position });

                // 标记来源，删除联动用
                em.AddComponentData(e, new Created());
                return e;
            }
            catch (Exception ex)
            {
                BusLineAutoStopsMod.log.Warn("TryPlaceStation: " + ex.Message);
                return Entity.Null;
            }
        }

        private bool TryDeleteByKey(string key)
        {
            Entity e = ParseKey(key);
            World w = World;
            if (w == null || e == Entity.Null) return false;
            EntityManager em = w.EntityManager;
            if (!em.Exists(e)) return false;
            try
            {
                if (!em.HasComponent<Temp>(e))
                {
                    em.AddComponentData(e, new Temp { m_Flags = TempFlags.Delete });
                }
                else
                {
                    Temp t = em.GetComponentData<Temp>(e);
                    t.m_Flags |= TempFlags.Delete;
                    em.SetComponentData(e, t);
                }
                return true;
            }
            catch (Exception ex)
            {
                BusLineAutoStopsMod.log.Warn("TryDeleteByKey: " + ex.Message);
                return false;
            }
        }

        /// <summary>中途退出按策略删除（由 Mod.ExitLazyMode 调用）。</summary>
        public static void ApplyExitDeletes(List<string> stops, List<string> stations)
        {
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            EntityManager em = w.EntityManager;
            for (int i = 0; i < stations.Count; i++)
            {
                Entity e = ParseKey(stations[i]);
                if (e == Entity.Null || !em.Exists(e)) continue;
                try
                {
                    if (!em.HasComponent<Temp>(e))
                        em.AddComponentData(e, new Temp { m_Flags = TempFlags.Delete });
                    else
                    {
                        Temp t = em.GetComponentData<Temp>(e);
                        t.m_Flags |= TempFlags.Delete;
                        em.SetComponentData(e, t);
                    }
                }
                catch (Exception) { }
            }
            for (int i = 0; i < stops.Count; i++)
            {
                Entity e = ParseKey(stops[i]);
                if (e == Entity.Null || !em.Exists(e)) continue;
                try
                {
                    if (!em.HasComponent<Temp>(e))
                        em.AddComponentData(e, new Temp { m_Flags = TempFlags.Delete });
                    else
                    {
                        Temp t = em.GetComponentData<Temp>(e);
                        t.m_Flags |= TempFlags.Delete;
                        em.SetComponentData(e, t);
                    }
                }
                catch (Exception) { }
            }
        }
    }
}
