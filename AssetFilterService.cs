using System;
using System.Collections.Generic;
using System.Text;
using Game.Prefabs;
using Unity.Entities;

namespace BusLineAutoStops
{
    /// <summary>
    /// 需求 3（v0.6.0）：懒人模式开着时，资产面板只显示**同类**站台资产（公交模式只给公交站，
    /// 电车模式只给有轨电车站），其它站台的资产被藏起来；退出懒人模式自动恢复。
    ///
    /// 只藏"站台资产"这一类：`universe` 里只放站台资产，线路资产（Bus Line 那种）根本不在名单里，
    /// 所以不会被藏 —— 这是有意的。本版懒人模式不再占用任何工具，玩家放完站台要当场切到
    /// 线路工具去连线，把线路入口藏掉就等于逼他先退出懒人模式。
    ///
    /// 为什么名单走 call() 而不是每帧的 GetterValueBinding：
    /// - C# 侧 RawValueBinding.Update() 每帧都会把 getter 的返回值序列化并推给视图
    ///   （Colossal.UI.Binding 反编译里 Update() 没有任何 diff，只有 writer.BeginEvent(...))；
    /// - 前端 bindValue 的 store 会 `e !== this._value` 才通知 React（bundle 里 ml.onUpdate），
    ///   所以重复推同一个字符串不会重绘，但**序列化 + 传输每帧都在**。
    /// 名单可能有几百上千条，绝不能塞进每帧 JSON。于是：
    ///   每帧的 GetUiState 里只多一个整数 `filterSeq`，前端发现序号变了才 call() 取一次名单。
    ///
    /// 注意这条链的起点是"状态绑定真的在轮询"：0.5.0 实机 [资产过滤] 一行都没有，
    /// 根因是 UiCommandSystem.OnUpdate 没调 base.OnUpdate()（Game.UI/UISystemBase.cs:36-42），
    /// 前端收不到 enabled/filterSeq 变化，于是从没发起过这次 call()。
    /// </summary>
    public static class AssetFilterService
    {
        private static readonly StringBuilder s_sb = new StringBuilder(4096);
        private static readonly List<string> s_allow = new List<string>(256);
        private static readonly List<string> s_universe = new List<string>(512);

        /// <summary>线路资产条目（面板里要留着，玩家得能从面板切到画线）。</summary>
        private static readonly List<string> s_lines = new List<string>(64);
        private static readonly HashSet<string> s_dedup = new HashSet<string>(StringComparer.Ordinal);

        private static string s_json = "";
        private static int s_builtSeq = -1;
        private static bool s_builtOn;
        private static TransportKind s_builtKind = TransportKind.None;

        /// <summary>
        /// 供 CallBinding 调用：返回当前过滤集 JSON。同一组 (seq, on, kind) 只算一次。
        /// </summary>
        public static string BuildJson()
        {
            bool on = LazyModeState.Enabled && LazyModeState.LazyEnabled;
            TransportKind kind = LazyModeState.ModeKind;
            int seq = LazyModeState.AssetFilterSeq;

            if (seq == s_builtSeq && on == s_builtOn && kind == s_builtKind && s_json.Length > 0)
            {
                return s_json;
            }
            s_builtSeq = seq;
            s_builtOn = on;
            s_builtKind = kind;

            if (!on)
            {
                s_json = "{\"on\":false,\"kind\":0,\"seq\":" + seq + ",\"allow\":[],\"universe\":[],\"lines\":[]}";
                return s_json;
            }

            s_allow.Clear();
            s_universe.Clear();
            s_lines.Clear();
            s_dedup.Clear();

            List<PrefabBase> all = StationAssets.AllCached();
            for (int i = 0; i < all.Count; i++)
            {
                TransportKind k;
                Entity e = StationAssets.PrefabEntity(all[i]);
                if (e == Entity.Null) continue;
                // 前端 ItemGrid 的条目主键就是 `${index}:${version}`（bundle 里的 Fr()）
                string key = e.Index + ":" + e.Version;

                if (StationAssets.TryClassifyStop(all[i], out k) && k != TransportKind.None)
                {
                    if (s_dedup.Add(key))
                    {
                        s_universe.Add(key);
                        if (k == kind) s_allow.Add(key);
                    }
                    continue;
                }

                // 线路资产（公交线 / 电车线…）：需求 6 要"只留同类站台 + 线路工具入口"，
                // 所以线路条目必须一起留着，否则玩家没法从面板切线路。
                TransportKind lk;
                if (StationAssets.TryClassifyLine(all[i], out lk) && lk != TransportKind.None
                    && s_dedup.Add(key))
                {
                    s_lines.Add(key);
                }
            }

            // 一个同类资产都没扫到（兜底也失败时）就别过滤：否则前端会把整栏站台全藏起来，
            // 玩家反而什么都选不了。
            bool actuallyOn = s_allow.Count > 0;
            s_json = "{\"on\":" + (actuallyOn ? "true" : "false")
                     + ",\"kind\":" + (int)kind
                     + ",\"seq\":" + seq
                     + ",\"allow\":[" + Join(s_allow) + "]"
                     + ",\"lines\":[" + Join(s_lines) + "]"
                     + ",\"universe\":[" + Join(s_universe) + "]}";

            BusLineAutoStopsMod.log.Info("[资产过滤] kind=" + kind + " 同类=" + s_allow.Count
                + " 全部站台=" + s_universe.Count + " 线路=" + s_lines.Count + " 生效=" + (actuallyOn ? "Y" : "N"));
            return s_json;
        }

        private static string Join(List<string> items)
        {
            s_sb.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) s_sb.Append(',');
                s_sb.Append('"').Append(items[i]).Append('"');
            }
            return s_sb.ToString();
        }
    }
}
