/**
 * v0.7.0 接线：把世界侧系统从 AutoPlaceSystem 换成 LazyModeSystem，
 * 并补上入口需要的 RequestExitLazyMode / RequestAssetRefresh / PendingLineKind。
 * 每处锚点都核对命中次数，对不上就不写。
 */
const fs = require("fs");
const path = require("path");
const root = path.join(__dirname, "..");

const edits = [];
const E = (file, name, re, to, expect) => edits.push({ file, name, re, to, expect });

// —— 模组入口 ——
E("BusLineAutoStopsMod.cs", "注册：UpdateAt",
  /updateSystem\.UpdateAt<AutoPlaceSystem>\(SystemUpdatePhase\.ToolUpdate\);/,
  "updateSystem.UpdateAt<LazyModeSystem>(SystemUpdatePhase.ToolUpdate);", 1);
E("BusLineAutoStopsMod.cs", "注册：夹在线路工具之后、输出之前",
  /try \{ updateSystem\.UpdateAfter<AutoPlaceSystem, ObjectToolSystem>\(SystemUpdatePhase\.ToolUpdate\); \}/,
  "try { updateSystem.UpdateAfter<LazyModeSystem, RouteToolSystem>(SystemUpdatePhase.ToolUpdate); }", 1);
E("BusLineAutoStopsMod.cs", "注册注释相位说明",
  / {16}\/\/ 相位很关键：我们必须在 ObjectToolSystem\.Update 之后、ToolOutputSystem 之前跑，\r?\n.*\r?\n.*\r?\n.*\r?\n/,
  `                // 本版读线路工具自己的控制点（公开 GetControlPoints），仍然要求排在它之后、
                // ToolOutputSystem 之前：下一版的模拟放置要往原版产出管线喂定义，那个缝隙是唯一入口。
                // 证据：Game.Common/SystemOrder.cs:704（工具在 ToolUpdate）、:695
                // （ToolOutput 被排到该相位最后）、:715（ApplyObjects 在 ApplyTool）。
`, 1);
E("BusLineAutoStopsMod.cs", "注册：UpdateBefore",
  /try \{ updateSystem\.UpdateBefore<AutoPlaceSystem, ToolOutputSystem>\(SystemUpdatePhase\.ToolUpdate\); \}/,
  "try { updateSystem.UpdateBefore<LazyModeSystem, ToolOutputSystem>(SystemUpdatePhase.ToolUpdate); }", 1);
E("BusLineAutoStopsMod.cs", "会话开始",
  /AutoPlaceSystem\.BeginSession\(\);/,
  "LazyModeSystem.BeginSession();", 1);
E("BusLineAutoStopsMod.cs", "类别跟随注释",
  /\/\/ AutoPlaceSystem \/ StationSelectTracker 会让类别跟着走。/,
  "// LazyModeSystem / StationSelectTracker 会让类别跟着走。", 1);

// —— 进入懒人模式：请求切到对应类型的创建线路工具（需求 3） ——
E("BusLineAutoStopsMod.cs", "进入时挂上线路工具请求",
  /LazyModeState\.ModeKind = kind;\r?\n {12}LazyModeState\.LazyEnabled = true;/,
  "LazyModeState.ModeKind = kind;\n            // 需求 3：进入懒人模式必须在创建线路工具下进行，切工具这件事交给系统帧做\n            LazyModeState.PendingLineKind = kind;\n            LazyModeState.LazyEnabled = true;", 1);

// —— 退出：给系统用的公开入口（带原因，日志能指认是哪条规则退的） ——
E("BusLineAutoStopsMod.cs", "RequestExitLazyMode",
  / {8}private void ExitLazyMode\(\)/,
  `        /// <summary>
        /// 需求 4：世界侧系统发现"已经不在创建线路工具里"就走这里退出。
        /// 与玩家手动关闭的区别只有日志里的原因，处置策略仍然照玩家选的走。
        /// </summary>
        public void RequestExitLazyMode(string why)
        {
            if (!LazyModeState.LazyEnabled) return;
            log.Info("[懒人模式] 自动退出：" + why);
            ExitLazyMode();
        }

        private void ExitLazyMode()`, 1);

// —— 资产切换后的索引重算（StationSelectTracker 调的是这个静态口） ——
E("Systems/LazyModeSystem.cs", "RequestAssetRefresh",
  / {8}internal void InvalidateStops\(\)/,
  `        /// <summary>需求 3：懒人模式进行中换了站台资产 → 索引按新类别重来（只置标志，结构改动得在系统帧里）。</summary>
        public static void RequestAssetRefresh()
        {
            LazyModeSystem s = Instance;
            if (s == null) return;
            s.m_stopQueryVerified = false;
            s.m_useFallback = false;
            s.InvalidateStops();
        }

        internal void InvalidateStops()`, 1);

// —— 站台标记类型：Game.Objects.TransportStop 不存在，原版是 Game.Routes.BusStop / TramStop ——
E("Systems/LazyModeSystem.cs", "站台查询",
  /m_stopQuery = GetEntityQuery\(new EntityQueryDesc\s*\{[\s\S]*?\n\s*\}\);/,
  `m_stopQuery = GetEntityQuery(new EntityQueryDesc
            {
                // 站台标记类型是 Game.Routes.BusStop / TramStop（Game.Objects.TransportStop 不存在）
                All = new ComponentType[] { ComponentType.ReadOnly<ConnectedRoute>() },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<Game.Routes.BusStop>(),
                    ComponentType.ReadOnly<Game.Routes.TramStop>(),
                },
                None = new ComponentType[] { ComponentType.ReadOnly<Temp>() },
            });`, 1);

const changed = new Map();
const errs = [];
for (const e of edits) {
  const p = path.join(root, e.file);
  let s = changed.has(e.file) ? changed.get(e.file) : fs.readFileSync(p, "utf8");
  const m = s.match(e.re);
  const n = m === null ? 0 : 1;
  if (n !== e.expect) { errs.push(e.file + " :: " + e.name + " 命中 " + n + "（预期 " + e.expect + "）"); continue; }
  changed.set(e.file, s.replace(e.re, () => e.to));
}
if (errs.length) { console.error("未写入：\n  " + errs.join("\n  ")); process.exit(1); }
for (const e of changed) {
  fs.writeFileSync(path.join(root, e[0]), e[1]);
  console.log("  wrote " + e[0]);
}
console.log("0.7.0 接线完成");
