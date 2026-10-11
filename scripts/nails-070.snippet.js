// ── 需求逐条自检（v0.7.0）──────────────────────────────────────────────────────────
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
    && lm.includes("RequestExitLazyMode(\"不支持的线路类型\")"), "");
  push("退出原因进了 DLL 日志（UTF-16 字面量）", utf16Has(dll, "[退出懒人模式]"), "");
  push("需求3：进入时由系统请求切到线路工具",
    lm.includes("LazyModeState.PendingLineKind") && lm.includes("ActivatePrefabTool(line)")
    && !modCs.includes("ActivatePrefabTool"), "入口不切工具，切工具只在系统帧里");
  push("落点读的是公开 API（不碰私有字段）",
    lm.includes("route.GetControlPoints(out var deps)") && lm.includes("deps.Complete()"), "");
  push("本版零世界写入（除了给我们自己站台的 Deleted 标记）",
    !lm.includes("SetComponentData") && !lm.includes("AddBuffer<") && !lm.includes("CreationDefinition")
    && lm.includes("AddComponent<Game.Common.Deleted>"), "");
  push("静止阈值仍是 0.1s（源码级）", /kSettleSeconds = 0\.10f;/.test(lm), "");
  push("取证探针有预算上限（不会刷屏）",
    /kProbeBudget = (\d);/.test(lm) && lm.includes("m_probeLeft--") && utf16Has(dll, "[取证·射线]"), "");
}
{
  // 闪退根因钉子（开发笔记 #63）：UpdateAt / UpdateAfter / UpdateBefore 写进的是两张互不相干的表
  // （Game/UpdateSystem.cs:271-277 进 m_Systems，:278-291 进 m_RefMap[另一个系统]），
  // Refresh/AddSystemUpdate 不去重（:425/:454 无条件 m_Updates.Add）——
  // 同一个系统注册三次 = 一帧跑三遍。v0.6.1 就是这样自己把 Apply 清掉、并且大概率把游戏打崩的。
  const lmFile = srcOf("Systems/LazyModeSystem.cs");
  const regs = (modCs.match(/updateSystem\.(UpdateAt|UpdateAfter|UpdateBefore)<LazyModeSystem/g) || []);
  push("LazyModeSystem 只注册一次（三次=一帧跑三遍，闪退根因）",
    regs.length === 1 && /UpdateBefore<LazyModeSystem, ToolOutputSystem>/.test(modCs)
    && !/UpdateAt<LazyModeSystem>/.test(modCs) && !/UpdateAfter<LazyModeSystem/.test(modCs),
    "注册 " + regs.length + " 处");
  const uiRegs = (modCs.match(/updateSystem\.(UpdateAt|UpdateAfter|UpdateBefore)<UiCommandSystem/g) || []);
  push("UiCommandSystem 也只注册一次", uiRegs.length === 1, "注册 " + uiRegs.length + " 处");
  push("结构改动前收 job 依赖（FlushPendingJobs 存在且在清理最前面）",
    /private void FlushPendingJobs\(\)/.test(lmFile) &&
    /RunExitCleanup[\s\S]{0,400}?FlushPendingJobs\(\);/.test(lmFile), "");
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
push("不再自己写创建定义/补缓冲（剥注释后的源码 + DLL 两头对）",
  !anyCode(/CreationDefinition|ToolOutputBarrier|AddBuffer<WaypointDefinition>/)
  && !utf8Has(dll, "CreationDefinition"), "");
push("零 Harmony 补丁（卸载不残留）", !/HarmonyPatch|new HarmonyInstance|\.Patch\(/.test(modCs), "");
push("中文名仍是「公交站自动放置」", /\{ "mod\.name", "公交站自动放置" \}/.test(localeSrc), "");
