using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Entities;

namespace BusLineAutoStops.Systems
{
    /// <summary>
    /// 站台位置的均匀网格索引。
    ///
    /// 存在的唯一理由：v0.3.x 每帧用 `ToEntityArray` 把全世界所有 Object 实体扫一遍来判断
    /// "光标附近有没有站台"，在几十万实体的城市里直接把游戏拖成个位数帧率（实机：20 分钟只放了
    /// 5 个站台，玩家报"开了就卡爆"）。改成
    ///   · 只索引公交站（Game.Routes.BusStop 标签，数量级是"城里有多少站台"而不是"城里有多少实体"）
    ///   · 最快 0.5 秒重建一次
    ///   · 每帧只做一次邻域查询（3×3 格子）
    ///
    /// v0.5.0（需求 6，对照 AccessAnarchy 那种"每帧白干"的教训）再加两条：
    ///   · 我们自己放/撤的站台走**增量 Add**，不再为了它重建整表；
    ///   · 索引里允许短暂存在死条目（刚被我们或玩家删掉的），查询时用 EntityManager.Exists 过滤。
    ///     于是"重建"只按时间节流，鼠标每动一下就重扫一次的做法被彻底消除。
    /// </summary>
    internal sealed class StopIndex
    {
        /// <summary>格子边长（米）。查询半径 10m → 只查 3×3 格。</summary>
        private const float kCell = 16f;

        private readonly List<Entity> m_entities = new List<Entity>();
        private readonly List<float3> m_positions = new List<float3>();
        private readonly Dictionary<int, List<int>> m_cells = new Dictionary<int, List<int>>();

        /// <summary>防止同一个实体在一次索引生命周期里被 Add 两遍（增量 Add 与重建会重叠）。</summary>
        private readonly HashSet<Entity> m_seen = new HashSet<Entity>();

        /// <summary>
        /// 桶复用池（需求 6）：重建是 0.5~2 秒一次的常规操作，每次给每个格子 `new List&lt;int&gt;`
        /// 会把垃圾压给主线程 GC。重建前把旧桶收进池子，Add 时再从池子里拿。
        /// </summary>
        private readonly List<List<int>> m_pool = new List<List<int>>();

        private const int kPoolCap = 2048;

        public int Count { get { return m_entities.Count; } }

        public void Clear()
        {
            m_entities.Clear();
            m_positions.Clear();
            RecycleCells();
            m_seen.Clear();
        }

        public void BeginBuild(int capacityHint)
        {
            m_entities.Clear();
            m_positions.Clear();
            RecycleCells();
            m_seen.Clear();
            if (capacityHint > 0 && capacityHint < 4096)
            {
                m_entities.Capacity = capacityHint;
                m_positions.Capacity = capacityHint;
            }
        }

        private void RecycleCells()
        {
            if (m_cells.Count == 0) return;
            foreach (KeyValuePair<int, List<int>> kv in m_cells)
            {
                if (m_pool.Count < kPoolCap) m_pool.Add(kv.Value);
            }
            m_cells.Clear();
        }

        /// <summary>
        /// 加一个站台。重建路径和"我们自己刚放下一个站台"都用它，所以是幂等的。
        /// </summary>
        public void Add(Entity e, float3 pos)
        {
            if (e == Entity.Null || !m_seen.Add(e)) return;
            int idx = m_entities.Count;
            m_entities.Add(e);
            m_positions.Add(pos);
            int key = CellKey(pos);
            List<int> bucket;
            if (!m_cells.TryGetValue(key, out bucket))
            {
                if (m_pool.Count > 0)
                {
                    int last = m_pool.Count - 1;
                    bucket = m_pool[last];
                    m_pool.RemoveAt(last);
                    bucket.Clear();
                }
                else
                {
                    bucket = new List<int>(4);
                }
                m_cells[key] = bucket;
            }
            bucket.Add(idx);
        }

        public void FinishBuild() { }

        /// <summary>
        /// 半径内最近的**还活着**的站台。索引最长滞后一个重建间隔（0.5s / 回退路径 2s），
        /// 期间被铲掉的旧条目在这里用 Exists 过滤掉，所以不必为"某个站台没了"重建整表。
        /// 只做读操作，因此可以在任意相位调用。
        /// </summary>
        public Entity Nearest(EntityManager em, float3 pos, float radius)
        {
            float d;
            return Nearest(em, pos, radius, out d);
        }

        /// <summary>同上，并把找到的最近距离（平方根，米）带出来给日志用。</summary>
        public Entity Nearest(EntityManager em, float3 pos, float radius, out float dist)
        {
            dist = float.MaxValue;
            if (m_entities.Count == 0) return Entity.Null;

            float r2 = radius * radius;
            Entity best = Entity.Null;
            float bestD = r2;

            int cx = (int)math.floor(pos.x / kCell);
            int cz = (int)math.floor(pos.z / kCell);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    List<int> bucket;
                    if (!m_cells.TryGetValue(Hash(cx + dx, cz + dz), out bucket)) continue;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        int idx = bucket[i];
                        Entity e = m_entities[idx];
                        if (!em.Exists(e)) continue;
                        // 只比水平距离：站台的实际落位与悬停点本来就差着一段"路宽 + 标高"，
                        // 把 y 算进去会让四车道大路的查重彻底失真。
                        float2 a = m_positions[idx].xz;
                        float2 b = pos.xz;
                        float d = math.distancesq(a, b);
                        if (d >= bestD) continue;
                        bestD = d;
                        best = e;
                    }
                }
            }
            if (best != Entity.Null) dist = (float)System.Math.Sqrt(bestD);
            return best;
        }

        private static int CellKey(float3 p)
        {
            return Hash((int)math.floor(p.x / kCell), (int)math.floor(p.z / kCell));
        }

        private static int Hash(int cx, int cz)
        {
            // 与 Unity.Entities 的 Int2Hashable 同族做法：两个质数混合，负坐标也稳定
            unchecked
            {
                return (cx * 73856093) ^ (cz * 19349663);
            }
        }
    }
}
