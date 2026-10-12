/**
 * 打包发布目录 releases/<version>/：BusLineAutoStops.dll + BusLineAutoStops.mjs + mod.json
 * 并在打包后立刻自检三处版本号与关键代码标记，避免把旧 dist 或旧 DLL 发上去
 * （v0.2.2 那轮就吃过「deploy 只拷 dist、源码改动没编译」的亏）。
 *
 * 用法：
 *   node scripts/pack-release.js 0.4.0              → 这次要上传：XML 的 ModVersion 必须已经是 0.4.0
 *   node scripts/pack-release.js 0.4.1 --xml=0.4.0  → 只出本地产物：线上仍是 0.4.0，
 *                                                   本地 DLL/mjs/mod.json 标 0.4.1，XML 故意不改
 *                                                   （这样实机日志能区分在跑哪一版，又不会误传旧描述）
 */
const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const root = path.join(__dirname, "..");
const version = process.argv[2];
if (!/^\d+\.\d+\.\d+$/.test(version || "")) {
  console.error("usage: node scripts/pack-release.js <x.y.z> [--xml=<线上版本>]");
  process.exit(2);
}
const xmlArg = (process.argv.slice(3).find((a) => a.indexOf("--xml=") === 0) || "").split("=")[1] || version;
if (!/^\d+\.\d+\.\d+$/.test(xmlArg)) {
  console.error("--xml 要写成三位版本号，或者干脆别写（表示这次就要上传）");
  process.exit(2);
}
// 本地版本不高于"线上版本"却要声明线上是另一版 → 参数写反了，直接停
if (xmlArg !== version && version.localeCompare(xmlArg, "en", { numeric: true }) <= 0) {
  console.error("本地 " + version + " 必须高于 --xml 指的线上 " + xmlArg + "（否则就直接用不带 --xml 的写法）");
  process.exit(2);
}
if (xmlArg !== version) {
  console.log("NOTE 本地产物 " + version + "，线上仍是 " + xmlArg + " —— 只本地测试，不要跑 NewVersion");
}

// 1) 先跑完整构建（deploy-local.js 里含 Roslyn 直编 + webpack + 写 Mods/）
execFileSync(process.execPath, [path.join(root, "scripts", "deploy-local.js")], {
  cwd: root,
  encoding: "utf8",
  stdio: "inherit",
});

const src = path.join(root, "bin", "deploy");
const outDir = path.join(root, "releases", version);
fs.mkdirSync(outDir, { recursive: true });

for (const f of ["BusLineAutoStops.dll", "BusLineAutoStops.mjs", "mod.json"]) {
  const p = path.join(src, f);
  if (!fs.existsSync(p)) {
    // mjs/mod.json 由 deploy 直接写进 Mods/，bin/deploy 里可能没有 → 回退到 Frontend 产物
    const alt =
      f === "BusLineAutoStops.mjs"
        ? path.join(root, "Frontend", "dist", "BusLineAutoStops.mjs")
        : path.join(root, "Frontend", "mod.json");
    if (!fs.existsSync(alt)) {
      console.error("missing artifact:", f);
      process.exit(1);
    }
    fs.copyFileSync(alt, path.join(outDir, f));
  } else {
    fs.copyFileSync(p, path.join(outDir, f));
  }
}

// 图标目录必须一起进发布包：漏了的话线上玩家看到的按钮没有图案（本地却正常，最难查的那种 bug）
const imgSrc = path.join(root, "Frontend", "images");
const imgOut = path.join(outDir, "images");
fs.mkdirSync(imgOut, { recursive: true });
for (const f of fs.readdirSync(imgSrc)) {
  if (/\.(svg|png)$/i.test(f)) fs.copyFileSync(path.join(imgSrc, f), path.join(imgOut, f));
}

// 2) 自检
const dll = fs.readFileSync(path.join(outDir, "BusLineAutoStops.dll"));
const mjs = fs.readFileSync(path.join(outDir, "BusLineAutoStops.mjs"), "utf8");
const mj = JSON.parse(stripBom(fs.readFileSync(path.join(outDir, "mod.json"), "utf8")));
/** Windows 编辑器常给 mod.json 加 BOM，JSON.parse 会直接抛。 */
function stripBom(t) { return t.charCodeAt(0) === 0xfeff ? t.slice(1) : t; }

const checks = [];
const push = (name, ok, extra) => checks.push({ name, ok, extra: extra || "" });

push("DLL 内含版本号", dll.includes(Buffer.from(version, "utf8")) || utf16Has(dll, version), version);
push("mjs 横幅存在 /*!", mjs.startsWith("/*!"));
push("mjs 横幅版本一致", new RegExp("Version:\\s*" + version.replace(/\./g, "\\.") + "\\b").test(mjs));
push("mod.json 版本一致", mj.version === version, mj.version);
push("按钮走原版 floating", mjs.includes('variant: "floating"'));
push("图标用原版 src 分支", mjs.includes("coui://ui-mods/images/BusLineAutoStopsIcon.svg") && mjs.includes('src:'));
push("图标文件已进包",
  fs.existsSync(path.join(imgOut, "BusLineAutoStopsIcon.svg")) &&
  fs.existsSync(path.join(imgOut, "BusLineAutoStopsIconOn.svg")));
// 0.3.1 白方块事故 + 0.4.0 点击失效事故：既不能用 data: URI 当 mask，也不能塞内联 <svg>
push("不再用 data URI 当 mask", !mjs.includes("WebkitMaskImage") && !mjs.includes("GLYPH_URL"));
push("不再用内联 SVG 当按钮内容", !mjs.includes('createElement("svg"'));
// 需求 5：开启态改的是图案颜色，不是蓝底
push("不再覆盖蓝底背景色", !/#e95f4a/i.test(mjs) && !/#c8402f/i.test(mjs));
push("总开关前端可见性已接", mjs.includes("master"));
push("点击分诊日志存在", mjs.includes("button clicked") && mjs.includes("data-blas-toggle"));
push("位置探针存在", mjs.includes("layout probe"));
push("没有绝对定位按钮", !/position:\s*"absolute"/.test(mjs));
push("无 cs2 裸 import（全走 externals）", !/^import\s/m.test(mjs));
push("DLL 里有总开关属性", utf8Has(dll, "AutoPlaceEnabled"));
push("DLL 里有站台索引", utf8Has(dll, "StopIndex") || utf8Has(dll, "NearestStop"));

// 4) 发布配置：属性里夹一个 ASCII 双引号就会让 XML 解析失败（PowerShell 直接报"缺少终止标记"），
//    而发布器只是把它当成解析错误退出 —— 这一类问题必须在打包阶段拦住。
const xmlPath = path.join(root, "Properties", "PublishConfiguration.xml");
const xmlRaw = fs.readFileSync(xmlPath, "utf8");
const xmlLines = xmlRaw.split("\n");
const xmlProblems = [];
for (let n = 0; n < xmlLines.length; n++) {
  const l = xmlLines[n];
  if (l.indexOf('Value="') < 0) continue;
  const pairs = (l.match(/="/g) || []).length;
  const quotes = (l.match(/"/g) || []).length;
  if (quotes !== pairs * 2) xmlProblems.push("L" + (n + 1) + " 引号不配对（属性值里有裸 \"）");
}
const xmlDoc = /<ModVersion Value="([^"]+)"/.exec(xmlRaw);
push("发布配置 XML 属性引号配对", xmlProblems.length === 0, xmlProblems.join(" ; "));
push(xmlArg === version ? "发布配置版本与本地一致" : "发布配置版本仍是线上那一版",
  !!xmlDoc && xmlDoc[1] === xmlArg,
  "XML " + (xmlDoc ? xmlDoc[1] : "(没有 ModVersion)") + " / 本地 " + version + " / 线上应为 " + xmlArg);
// 注意：别在这里做全局 replace 归一化 —— /\s*\/>/ 只会替换文档里第一个匹配
// （<ModId ... />），AccessLevel 那行的空格还在，检查就会假失败。直接写宽松正则。
push("线上仍是 Private", /<AccessLevel\s+Value="Private"\s*\/>/.test(xmlRaw), /<AccessLevel[^>]*>/.exec(xmlRaw)?.[0]);
push("英文名未被改动（需求 4：只改中文名）", /<DisplayName Value="BusLine AutoStops" \/>/.test(xmlRaw),
  /<DisplayName[^>]*>/.exec(xmlRaw)?.[0]);
// ChangeLog 描述的是"线上那一版"：--xml 模式（只出本地版、不上传）时线上文案必须还是旧的，
// 拿本地版本号去要求 ChangeLog 会假失败（0.4.1 那次就是这么卡住的）。
push(xmlArg === version ? "ChangeLog 写的是本版" : "ChangeLog 仍是线上那一版",
  new RegExp("v" + xmlArg.replace(/\./g, "\\.") + " ").test(xmlRaw), "");
// 发布器实测会因 shortDescription 超 200 直接整次 NewVersion 失败（0.9.0 第一次就折在这儿，
// 而且它是把文件传完才报错的），所以这条必须在打包阶段就拦住。
const shortM = /<ShortDescription Value="([^"]*)"/.exec(xmlRaw);
push("ShortDescription 不超 200（发布器硬上限）", !!shortM && shortM[1].length <= 200,
  shortM ? "len=" + shortM[1].length : "没读到 ShortDescription");

// ── v0.5.0 需求逐条验产物：见文件末尾"需求逐条自检"段（那里才有词典解析结果）──────────

push("三个外链与论坛链接齐全",
  /ExternalLink Type="github"/.test(xmlRaw) && /ExternalLink Type="x"/.test(xmlRaw) &&
  /ExternalLink Type="kofi"/.test(xmlRaw) && /<ForumLink /.test(xmlRaw));
push("缩略图文件存在", fs.existsSync(path.join(root, "Properties", "Thumbnail.png")));
const localeSrc = fs.readFileSync(path.join(root, "LocaleTable.cs"), "utf8");
const blocks = {};
{
  const re = /private static Dictionary<string, string> (\w+)\(\)/g;
  let m;
  const marks = [];
  while ((m = re.exec(localeSrc))) marks.push({ name: m[1], start: m.index });
  for (let i = 0; i < marks.length; i++) {
    const end = i + 1 < marks.length ? marks[i + 1].start : localeSrc.length;
    const body = localeSrc.slice(marks[i].start, end);
    const keys = new Set();
    const kre = /\{ "([^"]+)", "/g;
    let k;
    while ((k = kre.exec(body))) keys.add(k[1]);
    if (keys.size > 5) blocks[marks[i].name] = keys;
  }
}
const enKeys = blocks.En || new Set();
const localeBad = [];
for (const name of Object.keys(blocks)) {
  const missing = [...enKeys].filter((x) => !blocks[name].has(x));
  const extra = [...blocks[name]].filter((x) => !enKeys.has(x));
  if (missing.length || extra.length) localeBad.push(name + " 缺[" + missing + "] 多[" + extra + "]");
}
push("12 语言键集与 en-US 一致", Object.keys(blocks).length === 12 && localeBad.length === 0,
  Object.keys(blocks).length + " locales" + (localeBad.length ? " :: " + localeBad.join(" ; ") : ""));

// 前端 BuildPanelMap 用到的 slug 必须在词典里有对应键，否则 tooltip/提示显示成裸 key
const slugsLine = localeSrc.match(/string\[\] slugs = \{([^\}]*)\}/);
const keysLine = localeSrc.match(/string\[\] keys = \{([^\}]*)\}/);
const dictHas = (key) => enKeys.has(key);
let panelOk = !!(slugsLine && keysLine);
if (panelOk) {
  const wanted = keysLine[1].match(/"[^"]+"/g) || [];
  panelOk = wanted.length > 0 && wanted.every((w) => dictHas(w.replace(/"/g, "")));
}
push("面板 slug 词条齐全", panelOk, panelOk ? "" : "BuildPanelMap 引用了缺键");

// ── 需求逐条自检（v0.8.0）──────────────────────────────────────────────────────────
// 「产物比源码旧」和「某条分支从没在实机出现过」是这里最容易白跑一轮的两种失败，
// 所以每条需求都要留下一枚可对号的钉子。能在产物（DLL/mjs）里对的就在产物里对，
// 只对得了源码的（浮点常量、注册时序）在名字里写清楚"源码级"，别假装验过产物。
//
// v0.7.0 的存在理由：**代按左键这条链被实机否掉了** —— 玩家那次会话的日志是
// 34 次 `[自动落地] 代替左键` + 0 次 `已转正` + 60 次 `原版没清掉上一帧的 Apply`，
// 也就是 applyMode 注入在这个版本根本不成立。本版把这些东西全部退役，
// 所以下面一半的钉子是在证明"它们真的不在了"，一半是在证明新前提成立了。
const allCs = [];
{
  const collect = (d, prefix) => {
    for (const f of fs.readdirSync(d)) {
      const p = path.join(d, f);
      if (fs.statSync(p).isDirectory()) continue;
      if (!f.endsWith(".cs")) continue;
      allCs.push({ rel: (prefix ? prefix + "/" : "") + f, text: fs.readFileSync(p, "utf8") });
    }
  };
  collect(root, "");
  collect(path.join(root, "Systems"), "Systems");
}
const srcOf = (rel) => (allCs.find((c) => c.rel === rel) || { text: "" }).text;
const anySrc = (re) => allCs.some((c) => re.test(c.text));
/** 剥掉注释再判"代码里还有没有这东西"：文档注释里提某类型名是取证说明，不是引用。 */
const stripC = (t) => t
  .replace(new RegExp("/\\*[\\s\\S]*?\\*/", "g"), "")
  .replace(new RegExp("//[^\\r\\n]*", "g"), "");
const anyCode = (re) => allCs.some((c) => re.test(stripC(c.text)));

// 需求 2（图标不变红 / 面板不筛选的共同根因，v0.5.0 取证）：这条一直是本版最硬的验收点，
// 因为玩家这轮还在说"图标没变红"——他跑的是线上 0.5.0，修复还没到他手上。
{
  const ui = srcOf("Systems/UiCommandSystem.cs");
  push("源码级：UiCommandSystem.OnUpdate 第一件事是 base.OnUpdate()",
    /protected override void OnUpdate\(\)\s*\{\s*(?:\/\/[^\n]*\r?\n\s*)*base\.OnUpdate\(\);/.test(ui),
    (ui.match(/protected override void OnUpdate\(\)[\s\S]{0,120}/) || [""])[0].replace(/\s+/g, " "));
}

// —— 需求 1：挡位选项 + 代按链路必须"整个不在产物里" ——
push("LandMode / LandPolicy / ShouldAutoPlace 已从源码消失",
  !anySrc(/LandMode|LandPolicy|ShouldAutoPlace|NormalizeLandMode/), "");
push("ToolApplyProxy 已退役（DLL 里找不到反射痕迹）",
  !utf8Has(dll, "ToolApplyProxy") && !utf8Has(dll, "GetSetMethod") && !utf8Has(dll, "applyMode"), "");
push("AutoPlaceSystem 不再参与编译",
  !srcOf("Systems/AutoPlaceSystem.cs").length && !utf8Has(dll, "AutoPlaceSystem"), "");
push("代按日志字面量已消失", !utf16Has(dll, "[自动落地]"), "");
push("就绪提示通道已消失（C# 与 mjs 两端）",
  !anySrc(/ArmedHintLocaleKey/) && !mjs.includes("parsed.hint") && !localeSrc.includes("hint.confirmLand"), "");
push("warn.applyUnavailable 词条已清",
  !localeSrc.includes("warn.applyUnavailable") && !localeSrc.includes("WARN_APPLY_UNAVAILABLE"), "");

const modCs = fs.readFileSync(path.join(root, "BusLineAutoStopsMod.cs"), "utf8");

// —— 需求 4：懒人模式绑在创建线路工具上 ——
{
  const lmRaw = srcOf("Systems/LazyModeSystem.cs");
  // 文档注释里会提到 CreationDefinition 这类词（那是取证说明，不是代码）：
  // 判"有没有写世界"必须先把注释剥掉，否则检查会被自己的注释骗过去。
  const BLOCK_RE = new RegExp("/\\*[\\s\\S]*?\\*/", "g");
  const LINE_RE = new RegExp("//[^\\r\\n]*", "g");
  const lm = lmRaw.replace(BLOCK_RE, "").replace(LINE_RE, "");
  push("需求4：不在线路工具里就退出懒人模式",
    lm.includes("active as RouteToolSystem") && lm.includes('RequestExitLazyMode("离开线路工具")')
    && lm.includes("RequestExitLazyMode(\"本版未放开的线路类型\")"), "");
  push("退出原因进了 DLL 日志（UTF-16 字面量）", utf16Has(dll, "[退出懒人模式]"), "");
  push("需求3：进入时由系统请求切到线路工具",
    lm.includes("LazyModeState.PendingLineKind") && lm.includes("ActivatePrefabTool(line)")
    && !modCs.includes("ActivatePrefabTool"), "入口不切工具，切工具只在系统帧里");
  push("落点读的是公开 API（不碰私有字段）",
    lm.includes("route.GetControlPoints(out var deps)") && lm.includes("deps.Complete()"), "");
  push("需求2/5：预览与提交都走原版定义通道（ToolOutputBarrier + CreationDefinition + Updated）",
    lm.includes("CreationDefinition") && lm.includes("ToolOutputBarrier")
    && lm.includes("CreationFlags.Attach") && lm.includes("default(Updated)"), "");
  push("预览幽灵必须带 Optional（校验错误自动撤销，绝不挡玩家点击）",
    lm.includes("CreationFlags.Optional") && lm.includes("optional ? CreationFlags.Optional : 0"), "");
  push("转正集合照抄 ApplyObjectsSystem（:922-923）",
    lm.includes("RemoveComponent<Temp>") && lm.includes("Game.Common.Applied")
    && lm.includes("Game.Common.Created") && lm.includes("Game.Common.Updated"), "");
  push("0,0,0 假静止防御在位（校正 #22 不再复发）",
    lm.includes("math.any(cp.m_Position != float3.zero)") && lm.includes("cp.Equals(default(ControlPoint))"), "");
  push("静止阈值 0.18s（0.10.0 需求 3：人手微停常在 0.1s，误触发=闪烁）",
    /kSettleSeconds = 0\.18f;/.test(lm), "");
  push("预览/提交/撤点/完成日志字面量进了 DLL（UTF-16）",
    utf16Has(dll, "[预览站台]") && utf16Has(dll, "[落点提交]") && utf16Has(dll, "[撤点回退]")
    && utf16Has(dll, "[线路完成]"), "");

  // —— v0.8.1 根因钉子：0.8.0 实机 27 次静止事件全被拒、[预览站台] 0 次 ——
  push("落点判定不再把工具的吸附父级当否决（Route/Waypoint 否决已删）",
    !lm.includes("已有同类站台（原版依附或点它）") && !lm.includes("HasBuffer<RouteWaypoint>(parent)")
    && lm.includes("TryResolveLanding") && lm.includes("ResolveAttachParent"), "");
  push("跳过日志给的是真原因（不再写死一句骗人的话）",
    lm.includes('") 原因：" + why') && utf16Has(dll, "原因："), "");
  push("合法父级按 AttachPositionSystem 认的两种形态判（Node / Composition+EdgeGeometry+Curve+NetCompositionArea）",
    lm.includes("Game.Net.Node") && lm.includes("Game.Net.Composition")
    && lm.includes("Game.Net.EdgeGeometry") && lm.includes("Game.Prefabs.NetCompositionArea"), "");
  push("父级解析带射线兜底（工具父级被 SnapJob 改成线路时不至于全拒）",
    lm.includes("GetRaycastResult(out r)") && lm.includes("r.m_Hit.m_HitEntity") && lm.includes("r.m_Owner")
    && lm.includes("ToolRaycastSystem"), "");
  push("索引位置读物件 Transform（Game.Routes.Position 只有路径点有，站台索引曾整表为空）",
    lm.includes("Game.Objects.Transform t") && lm.includes("Position p")
    && !/if \(!EntityManager\.HasComponent<Position>\(e\)\) return false;/.test(lm), "");
  push("类别切换/会话开始会清站台索引（旧类别条目不误拒新类别）",
    /internal void InvalidateStops\(\)[\s\S]{0,240}?m_stops\.Clear\(\);/.test(srcOf("Systems/LazyModeSystem.cs")), "");
  push("预览有认领步骤：喂完去 Temp 里认回自己那枚（不认领就永远不知道自己有没有出幽灵）",
    lm.includes("MaintainPreview(hover, cursor)") && lm.includes("ClaimPendingPreview(hover, cursor)")
    && utf16Has(dll, "[预览结果]"), "窗口长度归 0.10.0 的 kClaimMaxFrames 钉子管（0.9.0 的“2 帧”已被逐帧追取代）");

  // —— v0.8.2 根因钉子（实机 2026-10-07 23:02：130 次喂送、129 次"幽灵已出"，玩家却看不见预览）——
  push("查重重量在吸附后的实际落位，不再拿路中央的悬停点量 10m",
    lm.includes("kHoverSkipDistance") && lm.includes("m_stops.Nearest(EntityManager, landed, kSnapDistance")
    && lm.includes("m_stops.Nearest(EntityManager, landedAt, kSnapDistance"), "");
  push("描述实体逐组件兜异常（整段 try 被一枚 ArgumentException 一把吞掉过 343 行日志）",
    /private void Flag<T>/.test(lm) && /private void BufFlag<T>/.test(lm)
    && !lm.includes("描述失败"), "");
  push("依附改写有结果日志（找到哪个控制点、差多少米）",
    utf16Has(dll, "[依附改写]") && lm.includes("依附=\" + Describe(stop)")
    && lm.includes("控制点里找不到这一笔"), "");
  push("面板过滤逐条判（旧写法要求同栏同时有同类与别类站台，按 tab 喂的面板永远不满足）",
    mjs.includes("!f.allow.has(key) && !f.lines.has(key)")
    && !mjs.includes("allowed === 0 || foreign === 0"), "");
  push("面板过滤取键支持多种条目形态（只认 it.entity 会静默不筛）",
    mjs.includes("function itemKey") && mjs.includes("o.entity") && mjs.includes("o.value"), "");
  push("面板过滤自检回落到模组日志（前端静默降级玩家看不见）",
    mjs.includes("FilterDiag") && utf16Has(dll, "[面板过滤·前端]")
    && /kFilterDiag = "FilterDiag"/.test(srcOf("Systems/UiCommandSystem.cs")), "");
}
{
  // —— v0.9.0 根因钉子（实机 2026-10-08 03:17–03:38 会话，1276 行日志逐条对过）——
  const lm09 = srcOf("Systems/LazyModeSystem.cs")
    .replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/[^\r\n]*/g, "");
  const lm = lm09;
  // 需求 1：一停一大排图标。0.8.2 的"每帧喂"是反的 —— 静止时原版根本不写 Clear，
  // 一秒堆 60 枚；规矩改成"世上只留一枚、丢了才补"。
  // （0.9.0 那半句"并且光标一动就自己收走"在 0.10.0 改成了"连续移动满 0.15s 才收"——
  //   一动就删正是玩家报的闪烁：幽灵活不到被认领就被我们自己删了。见 v0.10.0 需求 3 的钉子。）
  push("世上只留一枚预览幽灵：它还活着就不再喂（停回原处认同一枚）",
    /private void MaintainPreview\(ControlPoint hover, float3 cursor\)\s*\{\s*if \(IsPreviewAlive\(\)\)/.test(lm)
    && lm.includes("math.distance(m_previewSpot, cursor) <= kPreviewKeepRadius) return;"), "");
  push("移动/重置/退出都主动收回上一枚（原版静止时不写 Clear，不管它就留一世）",
    /private void RetirePreview\(\)/.test(lm) && /private void RetireTemp\(Entity e\)/.test(lm)
    && (lm.match(/RetirePreview\(\);/g) || []).length >= 2
    && lm.includes("EntityManager.AddComponent<Game.Common.Deleted>(e)"), "");
  push("待提交与退出清理不留幽灵（沿用中的那一枚也收）",
    lm.includes("RetirePendingTemps()") && (lm.match(/RetireTemp\(/g) || []).length >= 4, "");

  // 需求 2：路灯/垃圾桶占位时预览时现时不现。根因 = 原版 Optional 规则连 Warning 都 Cancel+Hidden
  // （ValidationSystem.cs:1659-1681）。预览因此改走非 Optional，只有 Error 级才自己收回。
  push("预览不带 Optional（Optional 会把警告级重叠也 Cancel，预览就“时有时无”）",
    lm.includes("FeedStopDefinition(prefab, spot, parent, optional: false)")
    && !/FeedStopDefinition\(prefab, spot, parent\)/.test(lm), "");
  push("Error 级才收幽灵、Warning 级保留（和左键能做的事一致：站台放下、被占的物件消失）",
    lm.includes("HasBlockingError") && lm.includes("Game.Tools.Error")
    && lm.includes("Game.Tools.Warning") && utf16Has(dll, "[预览收回]"), "");
  push("原版没造出幽灵时沿路挪一档（不擅自删世界物件；0.10.0 起不再横向，见 v0.10.0 需求 1）",
    lm.includes("kSlideNudges") && lm.includes("SlidePos(") && utf16Has(dll, "[预览让位]"), "");

  // 需求 3：六车道左键放不上。日志：Large Road 上“幽灵已出，离落点 8.6m”，
  // 同一秒“[落点提交] 等不到原版生成，放弃”—— 6m 半径量的是路中央的停靠点，根本没看见那颗幽灵。
  push("提交沿用玩家看见的那一枚（不再二次喂定义、不会认错幽灵）",
    lm.includes("ReusablePreviewGhost(clickCursor, prefab)") && lm.includes("FeedPos = feedPos")
    && utf16Has(dll, "[落点提交] 沿用预览幽灵"), "0.10.0：两侧都用光标真实点比（旧写法拿停靠点比，宽路上认不回同一枚）");
  push("提交查找按“喂下去的那一处”量、半径 20m（旧 6m 从路中央量，宽路必放弃）",
    /internal const float kCommitMatchTolerance = 20f;/.test(lm)
    && lm.includes("math.distance(pos, p.FeedPos)") && !lm.includes("math.distance(pos, p.Position)"), "");
  push("控制点匹配另用 8m 半径（依附改写找的是“哪一笔控制点”，不该被幽灵半径带跑）",
    /internal const float kCpMatchTolerance = 8f;/.test(lm)
    && lm.includes("bestD > kCpMatchTolerance"), "");
  push("同类站台判重收到 4m（10m 会把同侧隔 5~7 米的第二个站吞掉）",
    /internal const float kSnapDistance = 4f;/.test(lm), "");

  // 需求 4：有轨电车剥出来置灰"暂未上线"。分类能力不动，只关"能不能跑懒人模式"。
  {
    const rules = srcOf("TransportKindRules.cs");
    const settingCs = srcOf("Setting.cs");
    push("懒人模式门分成两层：分类认电车，功能按 IsLazyModeEnabled( Tram, tramEnabled=false )",
      rules.includes("IsLazyModeEnabled") && rules.includes("if (kind == TransportKind.Tram) return tramEnabled;")
      && rules.includes("kind == TransportKind.Bus || kind == TransportKind.Tram"), "");
    push("C# 两处判据都改用 Enabled（进入与系统帧），并给电车专用提示",
      modCs.includes("IsLazyModeEnabled(kind, LazyModeState.TramEnabled)")
      && lm.includes("IsLazyModeEnabled(kind, LazyModeState.TramEnabled)")
      && modCs.includes('"WARN_TRAM_OFF"'), "");
    push("电车那条选项在设置页置灰不可选（TramOptionDisabled 恒 true）",
      settingCs.includes('[SettingsUIDisableByCondition(typeof(Setting), nameof(TramOptionDisabled))]')
      && /public bool TramOptionDisabled\(\)\s*\{\s*return true;/.test(settingCs), "");
    push("本版强制关电车（老存档/手改 config 塞 true 也不放开）",
      /SyncToState\(\)[\s\S]{0,400}?LazyModeState\.TramEnabled = false;/.test(settingCs), "");
    push("电车词条 12 份齐（label/desc/暂未上线提示）",
      Object.keys(blocks).length === 12 && Object.keys(blocks).every((n) =>
        blocks[n].has("tram.label") && blocks[n].has("tram.desc") && blocks[n].has("warn.tramNotOnline")),
      Object.keys(blocks).join(","));
    push("选电车资产不再把懒人模式类别切走（IsSelectableDuringLazy 带上电车开关）",
      srcOf("StationSelectTracker.cs").includes("IsSelectableDuringLazy(stopKind, LazyModeState.TramEnabled)")
      && srcOf("Systems/UiCommandSystem.cs").includes("IsSelectableDuringLazy(kind, LazyModeState.TramEnabled)"), "");
    push("跨类资产不残留：进入懒人模式时起点类别与手上资产不一致就清空选择",
      /SelectedStationKind != kind[\s\S]{0,200}?SelectedStationKind = TransportKind.None;/.test(modCs), "");
  }

  // 需求 6：面板过滤（0.8.2 的实机自检报了 allowed=5 / removed=0，说明藏东西那步没执行）
  push("线路资产也留在过滤名单里（C# 发 lines，前端 allow∪lines 才留下）",
    mjs.includes("f.lines.has(key)") && srcOf("AssetFilterService.cs").includes("s_lines")
    && utf16Has(dll, "线路="), "");
{
  // —— v0.11.0 需求 1（玩家 2026-10-10 21:00 会话：没开懒人模式点公交站资产，
  //  同一批同类条目一起高亮）根因钉子 ——
  // 原版 ItemGrid 的选中判据是 bundle 里的 entityEquals：只比 index+version
  //（function Br(e,t){return e===t||...e.index===t.index&&e.version===t.version}，
  //  美术/主题/名字一概不看）。所以同一个 prefab 实体在面板里登记几次（分类的
  //  UIGroupElement 里有重复项，资产包/导入器都会造成），点它就会同时点亮几条 ——
  //  玩家看到的就是“点一个公交站资产、其它同类一起高亮”。
  {
    const fe = fs.readFileSync(path.join(root, "Frontend", "src", "index.tsx"), "utf8");
    push("v0.11.0：面板条目按实体键去重（同一 prefab 登记几次只留第一条，原版 selected 判据只比实体）",
      fe.includes("function normalizeItems") && fe.includes("const seen = new Set<string>();")
      && fe.includes("if (seen.has(key))") && mjs.includes("function normalizeItems"),
      "留第一条、丢后面的：同一个实体登记两次时两条条目除了位置没有任何区别");
    push("v0.11.0：去重保留原数组引用（没丢东西不能换引用，否则 ItemGrid 内部自造重渲染循环）",
      fe.includes("if (!out) out = items;") && fe.includes("return applyFilter(out, f);"), "");
    push("v0.11.0：去重自检回落到模组日志（dupes=0 也要留一行，否则又变成静默猜测）",
      mjs.includes('"kind":"dedupe"') && fe.includes("reportDedup(items, out ? out.length"),
      'payload: {"kind":"dedupe","items":N,"kept":M,"dupes":K,"first":"…","dups":"k1,k2"}');
    push("v0.11.1：去重自检能指认是哪一面板（道路栏有重复时会把诊断带沟里，first/dups 就是归属）",
      mjs.includes('"first":"') && mjs.includes('"dups":"') && fe.includes("dupKeys.slice(0, 3)"), "");
    // 懒人模式的两处高亮：线路工具那一条是原版自己点亮的（工具就在线路工具上，
    // toolbar.selectedAsset 跟着 ToolSystem.activePrefab 走 —— ToolbarUISystem.OnUpdate），
    // 站台资产这一条原版永远不点亮（懒人模式里点站台走 SelectStopAsset，工具不切）。
    push("v0.11.0：懒人模式把当前站台资产顶替 selectedItem（公交站栏点亮站台、线路栏照旧点亮线路工具）",
      fe.includes("function pickLazySelected") && fe.includes("pickLazySelected(props.selectedItem, items, lazyAsset)")
      && fe.includes("lazyAsset ? pickLazySelected(props.selectedItem, items, lazyAsset)"), "");
    push("v0.11.1：懒人高亮闸门只看 lazyAsset（C# 只在懒人模式真开着才给键；不再绑 f.on，过滤名单取不到时高亮不跟着消失）",
      fe.includes("lazyAsset ? pickLazySelected") && !fe.includes("f.on && lazyAsset"), "");
    push("v0.11.0：C# 每帧 JSON 带 lazyAsset 实体键（与前端条目主键同一种 index:version 格式）",
      /\\"lazyAsset\\":\\"" \+ Escape\(assetKey\)/.test(srcOf("Systems/UiCommandSystem.cs"))
      && utf16Has(dll, "lazyAsset"), "");
    push("v0.11.0：资产键变化时主动 bump seq（同类站台之间切换不碰 ModeKind，序号不动前端收不到新键）",
      srcOf("Systems/UiCommandSystem.cs").includes("LazyModeState.UiSeq++;")
      && srcOf("Systems/UiCommandSystem.cs").includes("private string LazyAssetKey()")
      && srcOf("Systems/UiCommandSystem.cs").includes("bool needPush = m_lastJson.Length > 0 && seq == m_lastSeq;"),
      "GetUiState 每帧被 AddUpdateBinding 轮询调用，稳态必须只有一次引用比较");
    push("v0.11.0：懒人资产键按资产引用失效（换存档后不拿着上一个世界的 PrefabBase 算键）",
      srcOf("Systems/UiCommandSystem.cs").includes("private PrefabBase m_assetKeyPrefab;")
      && srcOf("LazyModeState.cs").includes("SelectedStationPrefab = null;"),
      "新世界建新系统实例 + ResetForNewWorld 清静态选择，两头都断了");
  }
}
}
{
  // —— v0.10.0 根因钉子（实机 2026-10-08 16:10 起的 v0.9.0 会话，逐条数过：
  //   [预览让位] 814、[预览无幽灵] 240（其中 204 次"世界 Temp 数=0"）、
  //   [预览收回] 196（151 次"已有同类站台"、45 次 Error 级）、[落点提交] 已落地 82）——
  const lm = srcOf("Systems/LazyModeSystem.cs")
    .replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/[^\r\n]*/g, "");

  // 需求 1：鼠标在右、原版路径点也在右，左键却把站台放到左（四车道要很偏右才放对）。
  // 根因不是"吸附把站台放错边"，而是**我们喂给吸附的那个点在路中央**：线路工具的 SnapJob 把
  // 悬停点吸到车行道上，RouteToolSystem.cs:234-235 还把 m_HitPosition 覆写成 m_Position，
  // 横向信息只在射线结果里活着。AttachPositionSystem.cs 的侧向完全由喂点决定：
  //   :236 SnapRouteLane 用 dot(MathUtils.Right(切向), 喂点-曲线点) 选车道；
  //   :284-333 SnapSegmentAreas 用 distancesq(人行道中线点, 喂点) 选可建造区。
  // 喂路中央 = 两侧等距 = 掷硬币。
  push("预览与提交的喂点都取射线真实命中点（不再拿被吸到路中央的控制点决定左右）",
    lm.includes("float3 cursor = CursorGroundPos(hover.m_Position);")
    && lm.includes("float3 clickCursor = CursorGroundPos(cp.m_Position);")
    && lm.includes("float3 spot = SlidePos(cursor, parent, Nudge());")
    && lm.includes("float3 feedPos = SlidePos(clickCursor, clickParent, Nudge());"), "");
  push("光标真实点读 r.m_Hit.m_HitPosition，带飞点防御（离控制点 >40m 不采信、y 用悬停点的）",
    lm.includes("r.m_Hit.m_HitPosition") && /internal const float kCursorMaxDrift = 40f;/.test(lm)
    && lm.includes("if (math.distance(hit.xz, fallback.xz) > kCursorMaxDrift) return fallback;")
    && lm.includes("return new float3(hit.x, fallback.y, hit.z);"),
    "y 必须用悬停点的：射线打在灯杆/建筑上时那个 y 是物件表面高度");
  push("让位阶梯沿切向挪、永不跨中线（0.9.0 的横向阶梯是需求 1 的另一半）",
    lm.includes("float2 dir = tan / math.sqrt(len);")
    && lm.includes("return new float3(basePos.x + dir.x * nudge, basePos.y, basePos.z + dir.y * nudge);")
    && !lm.includes("new float2(-tan.y, tan.x)") && lm.includes("kSlideNudges"), "");
  push("侧向取证进日志：喂点与落位的有符号横向距离都打（同号=同侧，实机就靠这行判定修没修好）",
    utf16Has(dll, "光标横向") && utf16Has(dll, "落点横向")
    && lm.includes("MathUtils.Right(math.normalizesafe(MathUtils.Tangent(curve.m_Bezier, t).xz))"),
    "正方向必须用 MathUtils.Right（=(f.y,-f.x)，ilspycmd Colossal.Mathematics.MathUtils:2037）——"
    + "SnapRouteLane 选车道用的就是它，日志与原版判定才是同一个轴");
  push("静止/沿用/查重全部改用光标真实点（判据两侧不同坐标系就会每帧收一枚再喂一枚）",
    lm.includes("if (math.distance(cursor, m_lastPos) > kMoveEpsilon)")
    && lm.includes("math.distance(m_previewSpot, cursor) <= kPreviewKeepRadius) return;")
    && lm.includes("TryResolveLanding(cursor, hover.m_OriginalEntity, out parent, out why)"), "");
  push("每帧只读一次射线（TryRaycast 一处读，三个取用者共用）",
    (lm.match(/GetRaycastResult/g) || []).length === 1
    && lm.includes("private bool TryRaycast(out RaycastResult r)"), "");

  // 需求 2：预览落到已有站台上 → 老公交站消失，退出懒人模式移上去又瞬间出现。
  // 根因：线路工具自己会把"悬停点依附上的那枚既有站台"复制一份 Temp（RouteToolSystem.cs:549
  // →:562-564 CreateTempWaypointObject→:788-789 写 m_Original=那枚站台 + CreationFlags.Select），
  // GenerateObjectsSystem.cs:967-969 因此给**原件**挂 Game.Tools.Hidden。
  // 我们把那枚副本当自己的幽灵 AddComponent<Deleted> → 副本被我们删了、原件还被原版藏着
  // = 玩家眼里"老站凭空消失"；再移上去原版重造副本 → 又出现。
  push("只认/只删 Temp.m_Original==Null 的幽灵（我们喂的定义从不写 m_Original）",
    /private bool IsOurTemp\(Entity e\)/.test(lm)
    && lm.includes("return EntityManager.GetComponentData<Temp>(e).m_Original == Entity.Null;")
    && (lm.match(/IsOurTemp\(/g) || []).length >= 3, "");
  push("RetireTemp 拒绝删原版副本并留一行[幽灵拒删]（误删就是玩家说的“老站消失”）",
    lm.includes("Entity original = EntityManager.GetComponentData<Temp>(e).m_Original;")
    && lm.includes("if (original != Entity.Null)") && utf16Has(dll, "[幽灵拒删]"), "");
  push("认领与提交两处查找都跳过原版副本（同资产同位置，不跳就认到别人身上、还把人删了）",
    (lm.match(/m_Original != Entity\.Null/g) || []).length >= 2, "");

  // 需求 3：0.1s 太快，鼠标移动途中的自然微停就触发预览 → 玩家看到闪烁。
  push("静止 0.18s + 淡出 0.15s + 停回 2.5m 内沿用同一枚（三档一起才算迟滞）",
    /kSettleSeconds = 0\.18f;/.test(lm) && /kPreviewFadeSeconds = 0\.15f;/.test(lm)
    && /kPreviewKeepRadius = 2\.5f;/.test(lm), "");
  push("移动不立刻收幽灵：连续移动满 0.15s 才真收（0.9.0 一动就删=幽灵活不到被认领，204 次“世界 Temp 数=0”）",
    lm.includes("if (m_ghostMoveSince == 0f) m_ghostMoveSince = now;")
    && lm.includes("else if (now - m_ghostMoveSince >= kPreviewFadeSeconds)")
    // 0.9.0 的形状是「移动分支里直接 RetirePreview()」；那种写法必须不再出现。
    && !/if \(m_previewTemp != Entity\.Null \|\| m_previewFed\)\s*\{\s*RetirePreview\(\);/.test(lm), "");
  push("认领逐帧追、最多 10 帧（0.9.0 只给 2 帧，卡一帧就误判“原版没造出幽灵”）",
    /internal const int kClaimMaxFrames = 10;/.test(lm)
    && lm.includes("if (++m_previewWaitFrames > kClaimMaxFrames)")
    && !lm.includes("m_previewPendingFrames"), "");

  // 需求 4：退出时选"不保留公交站"，站台删了但路上留一枚孤零零的路径点标志。
  push("不保留站台时先撤依附在自动站台上的末尾停靠点（只限本次会话、只限当前这条线）",
    /private int RemoveAttachedControlPoints\(\)/.test(lm)
    && lm.includes("int cpRemoved = RemoveAttachedControlPoints();")
    && lm.includes("if (owner == Entity.Null || !IsSessionStop(owner)) break;")
    && utf16Has(dll, "[退出清理] 顺带撤掉"), "");
  push("撤停靠点只在 state==Create 做，越界/误删三条防御都在（其余线路与已完成的线一律不碰）",
    lm.includes("if (route.state != RouteToolSystem.State.Create) return 0;")
    && lm.includes("for (int i = cps.Length - 2; i >= 1; i--)")
    && lm.includes("if (cps.Length <= 3) break;"), "");
  push("先撤停靠点、后删站台（删实体本身置 forceUpdate，原版下一帧按新列表重建预览）",
    lm.indexOf("RemoveAttachedControlPoints();") < lm.indexOf("if (DeleteIfSafe(m_placed[i])) deleted++;"), "");
  push("GetControlPoints 是工具自己的缓冲：只 deps.Complete()，绝不 Dispose（Dispose 会把工具的点列表删了）",
    /NativeList<ControlPoint> cps = route\.GetControlPoints\(out var deps\);\s*deps\.Complete\(\);/.test(lm)
    && !/cps\.Dispose\(\)/.test(lm), "");

  // 需求 5：审查影响模拟速度/性能的机制。
  push("词条表按语言缓存（面板与设置页每被问一次就重建 30 键字典的日子过去了）",
    srcOf("LocaleTable.cs").includes("s_cache") && srcOf("LocaleTable.cs").includes("BuildFresh(locale)"), "");
  push("认领路径不再每帧解析资产（FindStopTemp 直接用喂下去的那枚 prefab）",
    lm.includes("FindStopTemp(m_previewPrefab, anchor, out dist)")
    && /private Entity FindStopTemp\(Entity prefab, float3 target/.test(lm), "");
  push("世界没有 Temp 时不分配认领/提交的 NativeArray（实机 204 次全是白干）",
    (lm.match(/m_tempQuery\.CalculateEntityCount\(\) == 0\) return Entity\.Null;/g) || []).length >= 2, "");
  push("站台索引仍按时间节流重建（0.5s / 回退 2s），鼠标移动不触发重建",
    /kRefreshSeconds = 0\.5f;/.test(lm) && /kFallbackRefreshSeconds = 2f;/.test(lm), "");
}
{
  // 闪退根因钉子（开发笔记 #63）：UpdateAt / UpdateAfter / UpdateBefore 写进的是两张互不相干的表
  // （Game/UpdateSystem.cs:271-277 进 m_Systems，:278-291 进 m_RefMap[另一个系统]），
  // Refresh/AddSystemUpdate 不去重（:425/:454 无条件 m_Updates.Add）——
  // 同一个系统注册三次 = 一帧跑三遍。v0.6.1 就是这样自己把 Apply 清掉、并且大概率把游戏打崩的。
  const regs = (modCs.match(/updateSystem\.(UpdateAt|UpdateAfter|UpdateBefore)<LazyModeSystem/g) || []);
  push("LazyModeSystem 只注册一次（三次=一帧跑三遍，闪退根因）",
    regs.length === 1 && /UpdateBefore<LazyModeSystem, ToolOutputSystem>/.test(modCs)
    && !/UpdateAt<LazyModeSystem>/.test(modCs) && !/UpdateAfter<LazyModeSystem/.test(modCs),
    "注册 " + regs.length + " 处");
  const uiRegs = (modCs.match(/updateSystem\.(UpdateAt|UpdateAfter|UpdateBefore)<UiCommandSystem/g) || []);
  push("UiCommandSystem 也只注册一次", uiRegs.length === 1, "注册 " + uiRegs.length + " 处");
}
{
  // —— v0.11.2 第三轮审查改进项（P1-1 死分支复活 / P1-2 结构改动时序）——
  // 这两枚钉子都是"检查器自己不够严"换来的：
  // · 旧的那枚「结构改动前收 job 依赖」写成 `RunExitCleanup[\s\S]{0,400}?FlushPendingJobs\(\);`，
  //   只要 400 字符内出现过 FlushPendingJobs 就绿 —— 而实际代码里 ResetSettle 排在它前面，
  //   检查照样通过（2026-10-12 审查 P1-2）。现在按**方法体内部**的先后次序判。
  // · `[预览结果]` 那枚只查 utf16Has(字面量)，分支死了、实机一条都不打，产物里字面量还在 → 假绿
  //   （待办 #1 / 2026-10-12 审查 P1-1）。现在判的是"闸门写的到底是哪个标志"。
  const lmRaw = srcOf("Systems/LazyModeSystem.cs");
  const lm = lmRaw.replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/[^\r\n]*/g, "");
  /** 取某个方法签名之后的方法体（到第一个 8 空格缩进的收尾大括号）；默认在剥过注释的 LazyModeSystem 里找。 */
  const bodyOf = (sig, src) => {
    const text = src || lm;
    const i = text.indexOf(sig);
    if (i < 0) return "";
    const m = text.slice(i).match(/[\s\S]*?\n {8}\}/);
    return m ? m[0] : "";
  };
  const exitBody = bodyOf("private void RunExitCleanup(ExitDeletePolicy policy, float now)");
  const flushAt = exitBody.indexOf("FlushPendingJobs();");
  const retireAt = exitBody.indexOf("ResetSettle(now);");
  push("v0.11.2 P1-2：RunExitCleanup 里 FlushPendingJobs 先于任何结构改动（ResetSettle→RetirePreview）",
    /private void FlushPendingJobs\(\)/.test(lmRaw) && flushAt >= 0 && retireAt >= 0 && flushAt < retireAt,
    flushAt < 0 || retireAt < 0 ? "方法体里没找到两句之一" : "flush@" + flushAt + " reset@" + retireAt);
  push("v0.11.2 P1-2：两条清理路径顺序一致（BeginSession 消费段仍是 flush→pending→preview）",
    /if \(m_beginCleanupPending\)\s*\{\s*m_beginCleanupPending = false;\s*FlushPendingJobs\(\);\s*RetirePendingTemps\(\);\s*RetirePreview\(\);/.test(lm), "");
  const feedBody = bodyOf("private void FeedPreview(ControlPoint hover, float3 cursor)");
  const claimBody = bodyOf("private void ClaimPendingPreview(ControlPoint hover, float3 cursor)");
  push("v0.11.2 P1-1：认领结果日志的闸门是 m_previewResultLogged（不再共用喂送那个标志）",
    /if \(!m_previewResultLogged\)\s*\{\s*m_previewResultLogged = true;/.test(claimBody)
    && !claimBody.includes("m_previewAnnounced"), "两条闸门各自独立，否则 [预览结果] 永远 0 条");
  push("v0.11.2 P1-1：喂送侧只置 m_previewAnnounced，不碰 result 标志",
    /if \(m_previewAnnounced\) return;\s*m_previewAnnounced = true;/.test(feedBody)
    && !feedBody.includes("m_previewResultLogged = true"), "");
  push("v0.11.2 P1-1：两个标志在每个静止事件都归零（重置点数必须相同且 ≥2：移动分支 + ResetSettleTimers）",
    (lm.match(/m_previewAnnounced = false;/g) || []).length ===
    (lm.match(/m_previewResultLogged = false;/g) || []).length
    && (lm.match(/m_previewResultLogged = false;/g) || []).length >= 2, "");
  push("v0.11.2 P1-1：两条日志字面量都进了 DLL",
    utf16Has(dll, "[预览站台]") && utf16Has(dll, "[预览结果]"), "");
  // 查重索引（P2-5）：审查给的前提被反编译否掉了——ConnectedRoute 缓冲与 BusStop 标记同一份原型给的，
  // 未连线的站台一直在索引里（research/TransportStop.cs:50-59 / ObjectPrefab.cs:43 / GenerateObjectsSystem.cs:1232）。
  // 本版把查询收到原版自己的口径（research/ResourceAvailabilitySystem.cs:1021：BusStop+!Deleted+!Temp），
  // 于是这两枚钉子钉的是"别再退回冗余判据"，而不是"补上漏掉的站台"。
  {
    const q = (lm.match(/m_stopQuery = GetEntityQuery\(new EntityQueryDesc[\s\S]*?\n {12}\}\);/) || [""])[0];
    push("v0.11.2 P2-5：站台索引查询不再要求 ConnectedRoute 缓冲（原型证据：标记与缓冲同时给）",
      q.length > 0 && !q.includes("ReadOnly<ConnectedRoute>")
      && q.includes("Game.Routes.BusStop") && q.includes("Game.Routes.TramStop"), q.slice(0, 60).replace(/\s+/g, " "));
    push("v0.11.2 P2-5：查询排除 Temp 与 Deleted（与原版 ResourceAvailabilitySystem.cs:1021 同口径）",
      /None = new ComponentType\[\][\s\S]{0,200}?ReadOnly<Temp>[\s\S]{0,200}?ReadOnly<Game\.Common\.Deleted>/.test(q), "");
    push("P2-5 取证文件在本地可复查（research/ 不入库，但结论必须能对行号）",
      fs.existsSync(path.join(root, "research", "TransportStop.cs"))
      && fs.readFileSync(path.join(root, "research", "TransportStop.cs"), "utf8").includes("components.Add(ComponentType.ReadWrite<BusStop>())"),
      "缺文件就重新 ilspycmd -t Game.Prefabs.TransportStop");
  }
  {
    // 资产过滤（P2-4）：构建标记必须**成功之后**才置位，否则极端异常路径会把旧名单当新名单返回
    const afRaw = srcOf("AssetFilterService.cs");
    const af = afRaw.replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/[^\r\n]*/g, "");
    const bj = bodyOf("public static string BuildJson()", af);
    const parts = bj.split("MarkBuilt(seq, on, kind);");
    push("v0.11.2 P2-4：BuildJson 里两处置标记之前都已经算出 s_json（标记只认证已完成的构建）",
      bj.includes("MarkBuilt") && parts.length === 3
      && parts[0].includes('s_json = "{\\"on\\":false') && parts[1].includes('s_json = "{\\"on\\":"')
      && !parts[2].includes("s_json ="),
      "方法体 " + bj.length + " 字符 / 切出 " + parts.length + " 段；旧写法在方法开头三行直接置 s_built*，" +
      "枚举抛异常时标记已置、名单还是上一份");
    push("v0.11.2 P2-4：BuildJson 体内不再直接赋值 s_built*（只有 MarkBuilt 一处写）",
      !/s_built(Seq|On|Kind)\s*=/.test(bj)
      && /private static void MarkBuilt\(int seq, bool on, TransportKind kind\)\s*\{\s*s_builtSeq = seq;\s*s_builtOn = on;\s*s_builtKind = kind;\s*\}/.test(af)
      && (af.match(/^\s*s_builtSeq = seq;/gm) || []).length === 1, "");
    push("v0.11.2 P2-4：过滤名单仍走 call()（没把几百条 JSON 塞进每帧绑定）",
      srcOf("Systems/UiCommandSystem.cs").includes("GetAssetFilter")
      && utf16Has(dll, "GetAssetFilter"), "P2-4 的前提：这条链一次都不许变成每帧序列化");
  }
}

{
  // —— 安全自查钉子（2026-10-09，逐条对照 BlueprintHub 崩溃交接 Desktop/blueprinthub-handoff.md）——
  // 那份交接的七条隐患归纳成三句话：①别在池线程/回调里调引擎 API，②缓存要有生命周期与负缓存，
  // ③跨线程读写要有编组。本模组没有网络也没有线程（第 1 条钉子就是把这件事钉成事实），
  // 但 ① 的"回调里改引擎状态"这一族我们**真的有一处**：BeginSession 被快捷键回调直接调用，
  // 而它当时会 AddComponent<Deleted> 收幽灵 —— 与本模组自己在 ExitLazyMode 里写的规矩相反。
  const lm = srcOf("Systems/LazyModeSystem.cs")
    .replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/[^\r\n]*/g, "");
  const st = srcOf("LazyModeState.cs");
  const sa = srcOf("StationAssets.cs");
  const resetBody = (st.match(/public static void ResetForNewWorld\(\)[\s\S]*?\n        \}/) || [""])[0];

  push("零线程 / 零异步 / 零阻塞等待 / 零 lock（交接第 1~3 条在本模组根本不成立，这是证明）",
    !anyCode(/ThreadPool\.|Task\.Run|Task\.Factory|Task\.Wait|new Thread\(|async Task|\.GetAwaiter\(\)|SpinWait|\block\s*\(/),
    "任何一条出现都要重新评估“它是不是在池线程上调引擎 API”");
  push("BeginSession 一行世界代码都不写：只清托管状态 + 排队（快捷键/UI 回调不是做结构改动的地方）",
    /public static void BeginSession\(\)[\s\S]{0,1400}?\n        \}/.test(lm)
    && !/public static void BeginSession\(\)[\s\S]{0,1400}?(RetirePendingTemps\(\)|RetirePreview\(\)|ResetSettle\()/.test(lm)
    && lm.includes("s.m_beginCleanupPending = true;"),
    "原写法在回调里 AddComponent<Deleted>，与本模组 ExitLazyMode「只挂请求，改动在系统帧里」的规矩相矛盾");
  push("进入清理在系统帧消费，且收依赖在前（FlushPendingJobs → RetirePendingTemps → RetirePreview）",
    /if \(m_beginCleanupPending\)\s*\{\s*m_beginCleanupPending = false;\s*FlushPendingJobs\(\);\s*RetirePendingTemps\(\);\s*RetirePreview\(\);/.test(lm), "");
  push("ResetSettle 拆出 ResetSettleTimers：回调里可安全归零计时，收幽灵留给系统帧",
    /private void ResetSettle\(float now\)\s*\{\s*ResetSettleTimers\(now\);\s*RetirePreview\(\);/.test(lm)
    && /private void ResetSettleTimers\(float now\)/.test(lm), "");
  push("静态入口拒绝已销毁系统（BeginSession / RequestAssetRefresh 判 m_destroyed，OnDestroy 置上）",
    (lm.match(/s == null \|\| s\.m_destroyed/g) || []).length >= 2
    && /protected override void OnDestroy\(\)\s*\{\s*m_destroyed = true;/.test(lm), "");
  push("兜底站台有负缓存：认定“这一类没有可用资产”后 5 秒内不再整表重扫（交接第 4/5 条的同族问题）",
    /internal const float kFallbackRetrySeconds = 5f;/.test(lm)
    && lm.includes("bool blocked = m_fallbackScanFail == kind && t - m_fallbackScanAt < kFallbackRetrySeconds;")
    && lm.includes("m_fallbackScanFail = m_fallbackStop == null ? kind : TransportKind.None;"),
    "FindFallbackStop 会把 PrefabSystem 全部资产逐条分类一遍，资产多的存档一帧上万次读取");
  push("换资产 / 开新一场都让这份负缓存立刻作废",
    /public static void RequestAssetRefresh\(\)[\s\S]{0,700}?m_fallbackScanFail = TransportKind.None;/.test(lm)
    && /public static void BeginSession\(\)[\s\S]{0,1400}?m_fallbackScanFail = TransportKind.None;/.test(lm), "");
  push("新世界建立时清掉跨世界的静态状态（SelectedStationPrefab 属于上一个已销毁世界）",
    /protected override void OnCreate\(\)\s*\{\s*base\.OnCreate\(\);[\s\S]{0,500}?LazyModeState\.ResetForNewWorld\(\);/.test(lm)
    && resetBody.includes("SelectedStationPrefab = null;")
    && resetBody.includes("PendingExitCleanup = false;")
    && resetBody.includes("ActiveLinePrefab = null;"), "");
  push("ResetForNewWorld 不清总开关 Enabled、也不清 Broken（前者来自设置文件，后者是注册失败的保险）",
    !!resetBody && !/(^|\n)\s{8}Enabled =/.test(resetBody) && !/(^|\n)\s{8}Broken =/.test(resetBody), "");
  push("资产枚举缓存认世界（换存档后 5 秒窗口里不能继续供旧世界的 PrefabBase）",
    sa.includes("private static World s_cacheWorld;")
    && sa.includes("if (s_cache.Count > 0 && !ReferenceEquals(s_cacheWorld, w))")
    && /s_cacheTime = now;\s*s_cacheWorld = w;/.test(sa), "");
}

// —— 本版仍然要守住的旧结论（跨版本红线） ——
push("DLL 认得线路工具与站台标记类型", utf8Has(dll, "RouteToolSystem") && utf8Has(dll, "TramStop"));
push("不引用 ToolSystem.ignoreErrors（本版连读都不读了）", !utf8Has(dll, "ignoreErrors"), "");
push("零自定义组件类型（卸载不脏存档）继续成立", !anySrc(/:\s*(?:IComponentData|ISingletonComponent|IBufferElementData|ICleanupComponent)/), "");
push("退出策略仍只有 2 项", (() => {
  const b = /enum ExitDeletePolicy\s*\{([\s\S]*?)\n    \}/.exec(srcOf("TransportKindRules.cs"));
  return !!b && (b[1].match(/^\s{8}[A-Za-z]\w*\s*=/gm) || []).length === 2;
})(), "");
push("PullUiState 兜底拉取仍在（图标不变红的第二通道）",
  utf16Has(dll, "PullUiState") && /kStateCall/.test(srcOf("Systems/UiCommandSystem.cs"))
  && mjs.includes("PullUiState") && mjs.includes("poll 取到更新的 seq"), "");
push("前端状态初值没被裁剪删空（TS 会静默把 state 变 undefined）",
  /useState<UiState>\(\{[^}]*enabled: false[^}]*\}\)/.test(fs.readFileSync(path.join(root, "Frontend", "src", "index.tsx"), "utf8")), "");
push("资产过滤链路仍在（需求 3 的面板筛选）",
  mjs.includes("game-ui/game/components/item-grid/item-grid.tsx") && mjs.includes("extend")
  && utf16Has(dll, "GetAssetFilter"), "");
push("12 语言键集一致、中文不含「站台/月台」继续成立",
  Object.keys(blocks).length === 12 && [...(blocks.En || [])].length >= 20, Object.keys(blocks).length + " locales / en " + [...(blocks.En || [])].length + " 键");
{
  const zhBad = [];
  for (const name of ["ZhHans", "ZhHant"]) {
    const at = localeSrc.indexOf("Dictionary<string, string> " + name + "()");
    const body = at < 0 ? "" : localeSrc.slice(at, localeSrc.indexOf("private static", at + 10));
    const kre = /\{ "([^"]+)", "([^"]*)" \}/g;
    let k;
    while ((k = kre.exec(body))) if (/站台|月台/.test(k[2])) zhBad.push(name + "." + k[1]);
    if (!body.length) zhBad.push(name + " 词典没解析到");
  }
  push("中文词条不含「站台/月台」（术语红线）", zhBad.length === 0, zhBad.join(","));
}
push("旧的四项策略名已彻底清掉",
  !anySrc(/DoNothing|DeleteStopsAndStations|DeleteStationsOnly|DeleteStopsOnly|ResolveExitDelete/), "");
push("线路对抗那套逻辑不在编译里（WaypointLink/Ledger/EnsureStopBuffer）",
  !utf8Has(dll, "WaypointLinkSystem") && !utf8Has(dll, "PlacedStopLedger") && !utf8Has(dll, "EnsureStopBuffer"), "");
push("0.8.0 起写创建定义，但禁止喂 WaypointDefinition 缓冲（不代建线路）",
  !anyCode(/AddBuffer<WaypointDefinition>/), "");
push("仍未碰 applyMode（代按链路保持退役）",
  !utf8Has(dll, "ToolApplyProxy") && !utf8Has(dll, "GetSetMethod") && !utf8Has(dll, "applyMode"), "");
push("面板选资产拦截在位（懒人模式中换资产不切工具）",
  utf16Has(dll, "SelectStopAsset") && utf16Has(dll, "选中资产·懒人") && mjs.includes("SelectStopAsset"), "");
push("零 Harmony 补丁（卸载不残留）", !/HarmonyPatch|new HarmonyInstance|\.Patch\(/.test(modCs), "");
push("中文名仍是「公交站自动放置」", /\{ "mod\.name", "公交站自动放置" \}/.test(localeSrc), "");

function utf16Has(buf, s) {
  const b = Buffer.from(s, "utf16le");
  return buf.indexOf(b) >= 0;
}

/** .NET 元数据里的类型/属性名是 UTF-8，直接用 ascii 子串查即可。 */
function utf8Has(buf, s) {
  return buf.indexOf(Buffer.from(s, "utf8")) >= 0;
}

let bad = 0;
for (const c of checks) {
  console.log((c.ok ? "  ok   " : "  FAIL ") + c.name + (c.extra ? "  [" + c.extra + "]" : ""));
  if (!c.ok) bad++;
}
console.log(bad ? "PACK FAILED (" + bad + ")" : "PACKED -> " + outDir);
process.exit(bad ? 1 : 0);
