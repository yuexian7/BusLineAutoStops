import { ModRegistrar } from "cs2/modding";
import { Button, Tooltip } from "cs2/ui";
import { bindValue, call, trigger, useValue } from "cs2/api";
import { Localized, useLocalization } from "cs2/l10n";
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";

// ─────────────────────────────────────────────────────────────────────────────
// 左上角按钮：图标走原版 IconButton 的 src 分支（v0.4.0），点击链路在 v0.5.0 修完
//
// 「点了没反应」其实有两层，都用日志分诊出来了：
//  · 0.3.2（内联 <svg> 当 children）：UI.log 里 `button clicked` **0 次**
//    → 点击根本没进 React 处理器。游戏的 Button(Ab) 把 DOM 点击合成
//    `(h||u)&&(h&&h(),u&&u(e),e.stopPropagation())`（index.js@629394），
//    铺满 40rem 的内联 SVG 子树把命中吞掉了（Cohtml 对 SVG 子树的命中/冒泡不完整）。
//    → 改成 `<Button variant="floating" src=…>`：游戏自己渲染 `<img class="icon_be5">`。
//  · 0.4.1（已经用 src）：UI.log 里 `button clicked` **4 次**，但模组日志里
//    `[入口] UI 按钮点击` **0 次** → 点击进来了，断在 JS→C# 那一跳：
//    C# 注册的是 CallBinding，前端却调 `trigger()`；`trigger` 只对 TriggerBinding 生效
//    （TriggerBinding.Attach → view.RegisterForEvent(path)）。
//    → 现在两边都用 TriggerBinding<string> + trigger(GROUP, "ToggleLazy", "")，
//      与同一台机器上玩家天天点的 BlueprintHub 完全一致。
//  · 顺带「图标不会变红」：状态绑定当年用 AddBinding 注册，而 GetterValueBinding 要靠
//    AddUpdateBinding 才会被轮询（Game.UI/UISystemBase.cs:48-58）→ 前端一直停在挂载那一刻的 JSON。
//    C# 那边改了；这里再给 Button 一个随状态变化的 key，换图时强制重挂载，不赌 Cohtml 会不会重绘 src。
//
// props 转发链（bundle 原文核对，开发笔记校正 #30）：Tj → $b → Ab 一路 spread，
// 所以 style / data-* 能落到 <button> 上（探针的 data-blas-toggle 就是靠这个找节点的）。
// 只给 onSelect 不给 onClick：Ab 的合成处理器会把两个都调一遍，两个都给＝一次点击翻两下。
//
// 图标文件（需求 5：开启时图案变红、蓝底不变）：
//   coui://ui-mods/images/BusLineAutoStopsIcon.svg      白 —— 未开启
//   coui://ui-mods/images/BusLineAutoStopsIconOn.svg    红 —— 懒人模式开着
//   `coui://ui-mods/` 由 ModManager.cs:471 把**每个模组的 UI 目录**挂到同一个虚拟根，
//   所以文件名必须全局唯一；万一文件没到位：img 加载失败只会剩下一个空的蓝按钮
//   （原版 IconButton 的 onError 占位），比 0.3.1 那种"整块白盖住"要好，而且照样能点。
// ─────────────────────────────────────────────────────────────────────────────

const GROUP = "BusLineAutoStops";

const ICON_OFF = "coui://ui-mods/images/BusLineAutoStopsIcon.svg";
const ICON_ON = "coui://ui-mods/images/BusLineAutoStopsIconOn.svg";

/** 探针用：图标文件名里唯一的部分（两张图都含它）。 */
const IMG_HINT = "BusLineAutoStopsIcon";

function safeBindValue<T>(group: string, name: string, initial: T): unknown | null {
  try {
    return bindValue<T>(group, name, initial);
  } catch (e) {
    console.warn("[BusLineAutoStops] bindValue failed", group, name, e);
    return null;
  }
}

const uiState$ = safeBindValue<string>(
  GROUP,
  "GetUiState",
  '{"enabled":false,"master":true,"seq":0,"warningKey":""}'
);

function useUiState(): string | null {
  if (!uiState$) return null;
  try {
    return useValue<string>(uiState$);
  } catch (e) {
    console.warn("[BusLineAutoStops] useValue failed", e);
    return null;
  }
}

type UiState = {
  enabled: boolean;
  master?: boolean;
  seq: number;
  warningKey: string;
  modeKind?: number;
  filterSeq?: number;
  /** v0.11.0 需求 1：懒人模式当前站台资产的实体键（"index:version"，空=不在懒人模式）。 */
  lazyAsset?: string;
};

/** 与 LocaleTable.PanelKey / BlgKeys.PanelKey 保持一致（前端只引用，不自建词典） */
const panelKey = (slug: string) =>
  `Options.BUSLINEAUTOSTOPS.${slug}[BusLineAutoStops.BusLineAutoStops.BusLineAutoStopsMod.Panel.${slug}]`;

const TOOLTIP_KEY = panelKey("BTN_TOGGLE");

/** C# 侧 UiCommandSystem.kToggleTrigger 的名字，两边必须一致。 */
const TOGGLE_TRIGGER = "ToggleLazy";

/**
 * 给探针用的定位标记。`Tj → $b → Ab` 一路 spread（bundle 原文核对过，校正 #30），
 * 所以这个 data-* 应当原样落到 <button> 上。找不到时探针退到"按图标 <img src> 往上找"，
 * 日志里的 `via=` 会说是哪条路 —— 这一步不靠猜。
 */
const MARK = "data-blas-toggle";

/** 避让用的水平间距（px，与实测矩形同一坐标系）。 */
const GAP_PX = 8;

// ─────────────────────────────────────────────────────────────────────────────
// 需求 5：懒人模式进行中，资产面板只显示同类站台资产
//
// 挂点（bundle 原文核对，不是猜的）：
//   Q.add("game-ui/game/components/item-grid/item-grid.tsx",
//         {get ItemGrid(){return hoe}, set ItemGrid(e){hoe=e}})
//   AssetGrid 里是 `jsx(hoe, {items:n, selectedItem:o, onSelectItem:l, ...e})`
//   —— 导出是**访问器对**，extend 的赋值会走 setter 直接改掉模块内那个 `var hoe`，
//   所以包装对 AssetGrid 内部的引用同样生效；而且 ItemGrid 元素是
//   `useMemo(..., [n,l,e,o])`（items 变了就重建），资产栏是玩家打开时才挂载，
//   我们的 extend 发生在 UI 模块注册阶段（更早），不存在"缓存了旧组件"的窗口。
//
// 为什么不在每帧 JSON 里塞名单：C# 的 RawValueBinding.Update() 每帧都序列化并推送
// （Colossal.UI.Binding 里没有 diff），几百条 entity 字符串 × 每帧 = 需求 6 要避免的那种白干。
// 所以每帧 JSON 只多一个 filterSeq（整数），名单用 call() 按需取一次。
//
// 只在一个栏里"既有同类站台、又有别的站台"时才动手；否则原样返回**同一个数组引用**。
// 这条很重要：ItemGrid 内部 `useMemo(() => { if (l.current === i) ... }), [c, i]` 里会 setState，
// 每次渲染都喂它一个新数组就是自造的重渲染循环。
// ─────────────────────────────────────────────────────────────────────────────

/** C# 侧 UiCommandSystem.kAssetFilterCall，两边必须一致。 */
const FILTER_CALL = "GetAssetFilter";

/** C# 侧 UiCommandSystem.kSelectStopAsset：懒人模式中点站台资产 → 换资产不切工具（v0.8.0）。 */
const SELECT_STOP_TRIGGER = "SelectStopAsset";

/** C# 侧 UiCommandSystem.kStateCall：推送通道的兜底拉取（详见 C# 那段注释）。 */
const STATE_CALL = "PullUiState";

/** C# 侧 UiCommandSystem.kFilterDiag：把过滤自检写进 BusLineAutoStops.log（详见那段注释）。 */
const FILTER_DIAG_TRIGGER = "FilterDiag";

const ITEM_GRID_MODULE = "game-ui/game/components/item-grid/item-grid.tsx";
const ITEM_GRID_EXPORT = "ItemGrid";

type StopFilter = {
  on: boolean;
  kind: number;
  seq: number;
  allow: Set<string>;
  lines: Set<string>;
  universe: Set<string>;
};

const FILTER_OFF: StopFilter = {
  on: false,
  kind: 0,
  seq: -1,
  allow: new Set<string>(),
  lines: new Set<string>(),
  universe: new Set<string>(),
};

/** 多个 ItemGrid 实例共用一份名单：模块级状态 + 订阅，只 call() 一次。 */
let stopFilter: StopFilter = FILTER_OFF;
let fetchedSeq = -2;
const filterListeners: Set<() => void> = new Set();

function emitFilter(): void {
  filterListeners.forEach((fn) => {
    try {
      fn();
    } catch (e) {
      console.warn("[BusLineAutoStops] filter listener failed", e);
    }
  });
}

/** 原版 Fr()：`${index}:${version}`；C# AssetFilterService 用同一种键。 */
function entityKey(e: unknown): string {
  const it = e as { index?: number; version?: number } | null | undefined;
  if (!it || typeof it.index !== "number") return "";
  return `${it.index}:${it.version}`;
}

/**
 * 面板条目的实体键。
 *
 * 为什么不是只读 `it.entity`：`onSelectItem` 收到的第二个入参**就是实体本身**
 * （`Fr(t.entity)`，bundle@1505224），而 `props.items` 的元素结构没有实证过——
 * ItemGrid 内部自己往行对象上塞 `entity`，`bIsLocked` 那条又读 `it.value.entity`。
 * 只认一种字段名，过滤就会静默变成"一个都没匹配上 → 原样返回"，
 * 玩家看到的正是"面板没筛选"（实机 2026-10-07 就是这个现象，且一行诊断都没有）。
 */
function itemKey(it: unknown): string {
  if (!it) return "";
  const o = it as Record<string, unknown>;
  const cands: unknown[] = [o.entity, o.value, o.id, o.asset, o.prefab, o];
  for (let i = 0; i < cands.length; i++) {
    const k = entityKey(cands[i]);
    if (k) return k;
  }
  const nested = o.value as { entity?: unknown } | undefined;
  if (nested) {
    const k2 = entityKey(nested.entity);
    if (k2) return k2;
  }
  return "";
}

function toSet(v: unknown): Set<string> {
  const s = new Set<string>();
  if (Array.isArray(v)) {
    for (let i = 0; i < v.length; i++) {
      if (typeof v[i] === "string") s.add(v[i]);
    }
  }
  return s;
}

function parseFilter(raw: unknown, seq: number): StopFilter {
  let obj: Record<string, unknown> | null = null;
  try {
    obj = typeof raw === "string" ? (JSON.parse(raw) as Record<string, unknown>) : (raw as Record<string, unknown>);
  } catch {
    obj = null;
  }
  if (!obj || typeof obj !== "object") return FILTER_OFF;
  const on = obj.on === true;
  if (!on) return FILTER_OFF;
  const allow = toSet(obj.allow);
  if (allow.size === 0) return FILTER_OFF; // 宁可什么都不藏，也不能把整栏站台藏没了
  return {
    on: true,
    kind: typeof obj.kind === "number" ? obj.kind : 0,
    seq: seq,
    allow: allow,
    lines: toSet(obj.lines),
    universe: toSet(obj.universe),
  };
}

/**
 * 过滤效果的自检上报（每个 filterSeq 一次）。
 *
 * 为什么要它：0.8.1 实机 `[资产过滤] … 生效=Y`（C# 名单算对了）但玩家看不出任何筛选，
 * 而前端是"匹配不到就原样返回"的静默降级，console 又没人看得见。
 * 走 trigger 回到 C# 日志里，玩家把 BusLineAutoStops.log 发我就够定位。
 * sample 里放头几条条目的字段名：一旦 keyed=0，字段名直接告诉我们该读哪个属性。
 */
/**
 * 过滤效果的自检上报（内容真变了才报，一帧一条会把日志刷爆）。
 *
 * 为什么要它：0.8.1/0.8.2 两次都是"C# 名单算对了、玩家看不出任何筛选"，
 * 而前端是静默降级（匹配不到就原样返回），console 里的日志玩家不会给我看。
 * 走 trigger 回到 BusLineAutoStops.log，下一次一眼就能分清是"名单没送到"
 * (keyed=0) / "这一栏本来就没有可藏的"(removed=0) / "藏错东西了"(removed 很大)。
 */
let filterReported = "";

function reportFilter(f: StopFilter, items: unknown[], keyed: number, allowed: number, foreign: number, lines: number, removed: number): void {
  const sample: string[] = [];
  for (let i = 0; i < items.length && sample.length < 2; i++) {
    const it = items[i] as Record<string, unknown> | null;
    if (!it || typeof it !== "object") {
      sample.push(typeof it);
      continue;
    }
    sample.push(Object.keys(it).slice(0, 10).join(","));
  }

  const payload =
    '{"seq":' + f.seq + ',"items":' + items.length + ',"keyed":' + keyed +
    ',"allow":' + allowed + ',"line":' + lines + ',"foreign":' + foreign +
    ',"removed":' + removed + ',"sample":"' + sample.join(" | ").replace(/\\/g, "/") + '"}';
  if (filterReported === payload) return;
  filterReported = payload;

  console.log("[BusLineAutoStops] filter diag " + payload);
  try {
    trigger(GROUP, FILTER_DIAG_TRIGGER, payload);
  } catch (e) {
    console.warn("[BusLineAutoStops] FilterDiag trigger failed", e);
  }
}

/**
 * v0.11.0 需求 1：去重自检（同一份内容只报一次，复用 FilterDiag 通道）。
 *
 * 为什么必须留在日志里：原版 ItemGrid 的选中判据是 entityEquals —— 只比实体的
 * index+version（bundle：`function Br(e,t){return e===t||...e.index===t.index&&e.version===t.version}`）。
 * 所以同一个 prefab 实体在面板里出现几次，点它就会同时点亮几条。玩家报的
 * "点一个公交站资产、其它同类一起高亮"只有这一种成因（分类的 UIGroupElement 里
 * 登记了重复 prefab，资产包/导入器都会造成）。
 * dupes=0 的那几行同样有用：它证明这一栏没有重复实体，"还是一片高亮"就得往别处查
 * （否则又会变成 0.8.1 那种"名单全对、屏幕没反应"的静默猜测）。
 */
let dedupReported = "";

function reportDedup(items: unknown, kept: number, dupes: number, dupKeys: string[]): void {
  // 这一行必须能指认"是哪一面板"：否则道路栏有重复、交通栏没有的时候，
  // 一条 dupes>0 会把下一轮诊断带到沟里（0.8.1 的教训：静默/不可归属的日志等于没有）。
  // 条目里现成能用的身份只有 name（bundle 的 toolbar.Asset 字段），取第一条的名字 + 前几个重复键。
  let first = "";
  if (Array.isArray(items) && items.length > 0) {
    const o = items[0] as Record<string, unknown> | null;
    if (o && typeof o.name === "string") first = o.name.slice(0, 32);
  }
  const payload =
    '{"kind":"dedupe","items":' + (Array.isArray(items) ? items.length : 0)
    + ',"kept":' + kept + ',"dupes":' + dupes
    + ',"first":"' + first.replace(/\\/g, "/").replace(/"/g, "'") + '"'
    + ',"dups":"' + dupKeys.slice(0, 3).join(",") + '"}';
  if (dedupReported === payload) return;
  dedupReported = payload;

  console.log("[BusLineAutoStops] dedupe diag " + payload);
  try {
    trigger(GROUP, FILTER_DIAG_TRIGGER, payload);
  } catch (e) {
    console.warn("[BusLineAutoStops] dedupe diag failed", e);
  }
}

/**
 * v0.11.0 需求 1：交给原版 ItemGrid 之前的最后一道归一化 —— 先去重，再（懒人模式下）过滤。
 *
 * 去重为什么是安全的：条目身份就是实体（React key 用的是 `Fr(entity)` 即
 * `${index}:${version}`），同一个实体登记两次时两条条目除了位置没有任何区别，
 * 留第一条、丢后面的，顺序与分页语义都保持原版样子；认不出实体键的条目一律保留
 * （宁可少去重，也不能把玩家的东西藏没了）。
 *
 * 引用纪律（0.8.2 定下的，别打破）：什么都没丢的时候必须返回**原数组本身**。
 * ItemGrid 内部 `l.current===i` 靠引用比较决定要不要重建分页状态，每次渲染喂
 * 一个新数组就是自造的重渲染循环。
 */
function normalizeItems(items: unknown, f: StopFilter): unknown {
  if (!Array.isArray(items) || items.length === 0) return items;

  let out: unknown[] | null = null;
  let dupes = 0;
  const dupKeys: string[] = [];
  const seen = new Set<string>();
  for (let i = 0; i < items.length; i++) {
    const key = itemKey(items[i]);
    if (!key) {
      // 认不出键：不参与去重，原样带着走
      if (out) out.push(items[i]);
      continue;
    }
    if (seen.has(key)) {
      dupes++;
      if (dupKeys.length < 3) dupKeys.push(key);
      if (!out) out = items.slice(0, i);
      continue;
    }
    seen.add(key);
    if (out) out.push(items[i]);
  }
  // 每份内容只报一次（dedupReported 去重）：dupes=0 的行走完一个面板就那一行，
  // 不会因为每帧渲染刷屏。
  reportDedup(items, out ? out.length : items.length, dupes, dupKeys);
  if (!out) out = items;

  // 懒人模式过滤：applyFilter 自己会在不过滤时返回同一个引用
  return applyFilter(out, f);
}

/**
 * v0.11.0 需求 1：懒人模式的面板高亮。
 *
 * 线路工具那一条由原版自己点亮 —— 懒人模式里工具就在线路工具上，原版的
 *  selectedAsset 绑定跟着 activePrefab 走，线路栏里的"公交线"条目照旧高亮。
 * 站台资产这一条原版永远不点亮：懒人模式里点站台资产走我们的 SelectStopAsset，
 * 工具不切、原版选择不换（这正是 0.8.0 的设计），于是面板里看不出"现在放的是哪一枚"。
 *
 * 所以：当前这一栏里如果有"懒人模式正在用的那枚站台资产"，就用它的实体顶替
 * selectedItem。公交站栏点亮当前站台资产、线路栏照旧点亮线路工具，两个都在；
 * 换一枚站台资产就点亮新的那一枚（C# 在键变化时主动 bump seq 推新 JSON）。
 * 不在懒人模式、或这一栏里没有那枚资产时，原样返回原版的选择。
 */
function pickLazySelected(orig: unknown, items: unknown, key: string): unknown {
  if (!key || !Array.isArray(items)) return orig;
  for (let i = 0; i < items.length; i++) {
    const o = items[i] as Record<string, unknown> | null;
    if (!o || itemKey(o) !== key) continue;
    const e = o.entity as unknown;
    if (e) return e;
  }
  return orig;
}

function applyFilter(items: unknown, f: StopFilter): unknown {
  if (!f.on || !Array.isArray(items) || items.length === 0) return items;

  let allowed = 0;
  let lineCount = 0;
  let foreign = 0;
  let keyed = 0;
  for (let i = 0; i < items.length; i++) {
    const key = itemKey(items[i]);
    if (!key) continue;
    keyed++;
    if (f.allow.has(key)) allowed++;
    else if (f.lines.has(key)) lineCount++;
    else if (f.universe.has(key)) foreign++;
  }

  // 只清"和交通有关"的那几栏：一栏里既没有同类站台也没有线路条目，说明这是别的
  // 面板（公园、道路、分区…），不去动它。
  if (allowed === 0 && lineCount === 0) {
    reportFilter(f, items, keyed, allowed, foreign, lineCount, 0);
    return items;
  }

  const out: unknown[] = [];
  let removed = 0;
  for (let i = 0; i < items.length; i++) {
    const key = itemKey(items[i]);
    // 认不出键的条目一律留着（宁可少藏，不能把玩家的东西藏没了）。
    if (key && !f.allow.has(key) && !f.lines.has(key)) {
      removed++;
      continue;
    }
    out.push(items[i]);
  }
  reportFilter(f, items, keyed, allowed, foreign, lineCount, removed);
  if (removed === 0) return items;
  return out;
}

/** 每个 ItemGrid 实例都调它：共享模块级名单，seq 相同不重复取。 */
type StopFilterView = { f: StopFilter; lazyAsset: string };

function useStopFilter(): StopFilterView {
  const raw = useUiState();
  const [, force] = React.useState(0);
  const [lazyAsset, setLazyAsset] = React.useState("");

  React.useEffect(() => {
    const notify = () => force((x) => x + 1);
    filterListeners.add(notify);
    return () => {
      filterListeners.delete(notify);
    };
  }, []);

  React.useEffect(() => {
    let parsed: UiState | null = null;
    try {
      parsed = JSON.parse(String(raw)) as UiState;
    } catch {
      return;
    }
    if (!parsed) return;
    const wantOn = !!parsed.enabled && parsed.master !== false;
    const seq = typeof parsed.filterSeq === "number" ? parsed.filterSeq : parsed.seq;
    // v0.11.0 需求 1：懒人模式当前站台资产的实体键。C# 在它变化时会主动 bump seq，
    // 所以这个字符串一变就是一次真变化；用 updater 形式，值没变不会触发重渲染。
    const asset = typeof parsed.lazyAsset === "string" ? parsed.lazyAsset : "";
    setLazyAsset((prev) => (prev === asset ? prev : asset));
    if (!wantOn) {
      if (stopFilter !== FILTER_OFF) {
        stopFilter = FILTER_OFF;
        fetchedSeq = -2;
        filterReported = "";
        emitFilter();
      }
      return;
    }
    if (fetchedSeq === seq) return;
    fetchedSeq = seq;

    let settled = false;
    const fail = (e: unknown) => {
      settled = true;
      fetchedSeq = -2; // 让下一次 seq 变化还能重试
      console.warn("[BusLineAutoStops] GetAssetFilter failed", e);
    };
    const ok = (v: unknown) => {
      if (settled) return;
      settled = true;
      stopFilter = parseFilter(v, seq);
      emitFilter();
    };
    try {
      const r = call<string>(GROUP, FILTER_CALL, "");
      if (r && typeof (r as Promise<string>).then === "function") {
        (r as Promise<string>).then(ok, fail);
      } else {
        ok(r);
      }
    } catch (e) {
      fail(e);
    }
  }, [raw]);

  return { f: stopFilter, lazyAsset };
}

type GridComponent = React.ComponentType<Record<string, unknown>>;

/**
 * extend 回调：拿到当前 ItemGrid（可能是别的模组包过的），返回我们的包装。
 *
 * v0.8.0 起除了过滤 items，还拦 onSelectItem：懒人模式开着时点站台资产（universe 里的键）
 * 不再交给原版 selectAsset —— 那会把工具切到物件工具，按需求 4 立刻退出懒人模式，
 * 玩家永远没法在画线中途换站台资产。改为 trigger("BusLineAutoStops","SelectStopAsset",key)，
 * C# 只换预览/提交用的资产，工具保持线路工具。其它条目（线路、道路…）原样放行。
 *
 * v0.11.0 需求 1 起多两件事（都不改原版语义，只在我们这一层归一化）：
 *  1) items 先按实体键去重：原版选中判据只比 index+version，同一 prefab 在面板里
 *     登记几次就同时点亮几条 —— 玩家看到的"点一个公交站资产、其它同类一起高亮"。
 *     这条与懒人模式、与总开关都无关，任何时候都生效。
 *  2) 懒人模式里把"当前站台资产"那一枚也点亮：线路工具那一条原版自己会点亮
 *     （工具就在线路工具上），站台资产这一条原版永远不会点亮（走 SelectStopAsset
 *     不切工具），所以这里在当前栏里找到那枚资产时用它的实体顶替 selectedItem。
 */
const wrapItemGrid = (current: unknown): unknown => {
  const Orig = current as GridComponent;
  return function BlasItemGrid(props: Record<string, unknown>) {
    const { f, lazyAsset } = useStopFilter();
    const items = React.useMemo(() => normalizeItems(props.items, f), [props.items, f]);
    // 闸门只用 lazyAsset（C# 只在懒人模式真开着时才给这个键），不看 f.on：
    // f.on 还依赖"过滤名单取到且非空"，那条链断了（call 失败/同类资产为空）时
    // 只是不过滤而已，高亮不该跟着消失。当前栏里没有那枚资产时原样返回原版选择，
    // 所以线路栏的"公交线"照旧由原版点亮，两个闸门的效果一致、依赖更少。
    const selectedItem = React.useMemo(
      () => (lazyAsset ? pickLazySelected(props.selectedItem, items, lazyAsset) : props.selectedItem),
      [lazyAsset, props.selectedItem, items]
    );
    const origSelect = props.onSelectItem;
    const onSelectItem = useCallback(
      (entity: unknown) => {
        const key = entityKey(entity);
        if (f.on && key && f.universe.has(key)) {
          console.log("[BusLineAutoStops] asset picked (lazy): " + key);
          try {
            trigger(GROUP, SELECT_STOP_TRIGGER, key);
          } catch (e) {
            console.warn("[BusLineAutoStops] SelectStopAsset failed", e);
          }
          return;
        }
        if (typeof origSelect === "function") (origSelect as (e: unknown) => void)(entity);
      },
      [origSelect, f]
    );
    // 什么都没变就一个 prop 都不多给：多给同名 props 会让 React 认为元素每次都是新的，
    // 把 ItemGrid 内部的引用比较（l.current===i）整个绕开。
    if (items === props.items && selectedItem === props.selectedItem) {
      return <Orig {...props} onSelectItem={onSelectItem} />;
    }
    return (
      <Orig {...props} items={items} selectedItem={selectedItem} onSelectItem={onSelectItem} />
    );
  };
};

const warnStyle: React.CSSProperties = {
  position: "fixed",
  top: "64rem",
  left: "8rem",
  maxWidth: "300rem",
  padding: "6rem 10rem",
  background: "rgba(120, 20, 20, 0.92)",
  color: "#fff",
  fontSize: "12rem",
  borderRadius: "4rem",
  pointerEvents: "none",
  zIndex: 30,
};

type Rect = { left: number; top: number; right: number; bottom: number; width: number; height: number };

function fmt(r: { left: number; top: number; width: number; height: number }): string {
  return `${Math.round(r.left)},${Math.round(r.top)} ${Math.round(r.width)}x${Math.round(r.height)}`;
}

/**
 * 找到我们那个按钮的 DOM 节点。
 *
 * 两条路，按可靠性排序：
 *  1) data-blas-toggle —— props 一路 spread 到 <button>（已核对 bundle），正常就该命中；
 *  2) 图标 <img> 往上找 —— 万一原版的按钮实现改了（版本更新）或被别的模组套了壳，
 *     这条退路仍能量到矩形。
 * 两条都拿不到时返回 null，调用方打一行日志就算了，不猜。
 */
function findButtonNode(marker: string): { node: HTMLElement | null; via: string } {
  const byAttr = document.querySelector(`[${marker}]`) as HTMLElement | null;
  if (byAttr) return { node: byAttr, via: "attr" };
  const img = document.querySelector(`img[src*="${IMG_HINT}"]`) as HTMLElement | null;
  if (!img) return { node: null, via: "none" };
  let n: HTMLElement | null = img.parentElement;
  for (let i = 0; i < 3 && n; i++) {
    if (/button/.test(String(n.tagName)) || /button_ke4/.test(String(n.className))) {
      return { node: n, via: "img" };
    }
    n = n.parentElement;
  }
  // 再退一步：flex 行里的那个条目
  return { node: img.parentElement as HTMLElement | null, via: "img-parent" };
}

/**
 * 位置自检 + 自动避让。
 *
 * 原版这条排是 flex（.info-menu-layout{position:absolute;top:10rem;left:10rem;display:flex}，
 * 子元素 margin:0 6rem 6rem 0），理论上谁都不会重叠；0.3.1 实机却看到我们的按钮压在
 * Road Builder / Write Everywhere 上面。静态推不出原因，所以不猜：挂载后实测矩形，
 * 与兄弟重叠就平移到"重叠兄弟最右边界 + 8px"，并把父链和所有兄弟矩形 console.log 到 UI.log。
 * 偏移量存在 React state 里（style.position:relative + left），因为 Button 每次渲染都会重写 style。
 */
function probe(marker: string, appliedShift: number, onShift: (px: number) => void): void {
  try {
    const found = findButtonNode(marker);
    const btn = found.node;
    if (!btn) {
      console.log("[BusLineAutoStops] layout probe: 找不到按钮节点（data-blas-toggle 与图标 <img> 都没命中）");
      return;
    }

    let host: HTMLElement | null = btn.parentElement;
    while (host && !/info-menu-layout/.test(String(host.className))) host = host.parentElement;

    let chain = String(btn.tagName) + "." + String(btn.className).split(" ")[0];
    let p: HTMLElement | null = btn.parentElement;
    for (let i = 0; i < 4 && p; i++) {
      chain = String(p.tagName) + "." + String(p.className).split(" ")[0] + ">" + chain;
      p = p.parentElement;
    }
    if (!host) host = btn.parentElement;

    let item: HTMLElement = btn;
    if (host) {
      while (item.parentElement && item.parentElement !== host) item = item.parentElement;
    }

    // 量的是**真正被平移的那个节点**（btn，style.left 在它身上）。
    // 如果错量 item（Tooltip 的包裹层，它不跟着平移），减掉 shift 就会把自然位置算歪，
    // 导致每轮重新叠加偏移 → 按钮一路往右跑出屏幕。
    const r = btn.getBoundingClientRect();
    const natural: Rect = {
      left: r.left - appliedShift,
      top: r.top,
      right: r.right - appliedShift,
      bottom: r.bottom,
      width: r.width,
      height: r.height,
    };

    const sibs: string[] = [];
    let overlap = 0;
    let maxRight = -1e9;
    if (host) {
      for (let i = 0; i < host.children.length; i++) {
        const c = host.children[i] as HTMLElement;
        if (c === item || c === btn || item.contains(c) || c.contains(item) || c.contains(btn)) continue;
        const cr = c.getBoundingClientRect();
        if (cr.width < 1 || cr.height < 1) continue;
        sibs.push(`${String(c.className).split(" ")[0]}(${fmt(cr)})`);
        const hit =
          cr.left < natural.right && natural.left < cr.right && cr.top < natural.bottom && natural.top < cr.bottom;
        if (hit) {
          overlap++;
          if (cr.right > maxRight) maxRight = cr.right;
        }
      }
    }

    console.log(
      "[BusLineAutoStops] layout probe via=" + found.via +
        " chain=" + chain +
        " host=" + (host ? String(host.className).split(" ")[0] : "none") +
        " self=" + fmt(natural) +
        " shift=" + Math.round(appliedShift) +
        " sibs=[" + sibs.join(" | ") + "]" +
        " overlap=" + overlap
    );

    if (natural.width < 4 || natural.height < 4) {
      console.log("[BusLineAutoStops] layout probe: 按钮尺寸为 0 —— .button_ke4 没生效");
    }

    if (overlap > 0) {
      const dx = Math.round(maxRight + GAP_PX - natural.left);
      if (Math.abs(dx - appliedShift) > 1) {
        onShift(dx);
        console.log("[BusLineAutoStops] 自动避让 left+" + dx + "px");
      }
    } else if (appliedShift !== 0) {
      onShift(0);
      console.log("[BusLineAutoStops] 已无重叠，撤销避让偏移");
    }
  } catch (e) {
    console.warn("[BusLineAutoStops] layout probe failed", e);
  }
}

const App = () => {
  const raw = useUiState();
  const strings = useLocalization();
  const [state, setState] = useState<UiState>({ enabled: false, master: true, seq: 0, warningKey: "" });
  const [warningKey, setWarningKey] = useState("");
  const [shift, setShift] = useState(0);
  const shiftRef = useRef(0);
  const lastEnabled = useRef<boolean | null>(null);
  /** 最近一次看到的 master。0.13.0 的日志只在 enabled 变的时候打，master 的变化完全看不见 ——
   *  玩家反馈"关总开关图标不消失"时，第一轮日志无法分辨是 C# 没推、还是前端没重绘。分开记。 */
  const lastMaster = useRef<boolean | null>(null);
  /** 最近一次从 GetterValueBinding 推送里看到的 seq；兜底拉取靠它判断推送有没有送到。 */
  const pushedSeq = useRef<number>(-1);

  useEffect(() => {
    try {
      if (!raw) return;
      const parsed = JSON.parse(String(raw)) as UiState;
      // 状态真的换了才打日志：这条能一眼看出"图标不变红"是 C# 没推新值，还是前端没重绘。
      if (lastEnabled.current !== parsed.enabled) {
        console.log(
          "[BusLineAutoStops] state " + (lastEnabled.current === null ? "init" : "change") +
            " enabled=" + parsed.enabled + " master=" + parsed.master + " seq=" + parsed.seq
        );
        lastEnabled.current = parsed.enabled;
      }
      // 需求 2（v0.14.0）：总开关的变化单独一条 —— 它决定左上角按钮在不在。
      const masterNow = parsed.master !== false;
      if (lastMaster.current !== null && lastMaster.current !== masterNow) {
        console.log(
          "[BusLineAutoStops] master " + (masterNow ? "on" : "off") +
            " → " + (masterNow ? "显示图标" : "隐藏图标") + " seq=" + parsed.seq
        );
      }
      lastMaster.current = masterNow;
      setState(parsed);
      setWarningKey(parsed.warningKey || "");
      if (typeof parsed.seq === "number") pushedSeq.current = parsed.seq;
    } catch {
      /* 保留上一帧状态 */
    }
  }, [raw]);

  // 兜底拉取：推送通道（AddUpdateBinding 的轮询）归游戏管，0.5.0 就是栽在它不轮询上。
  // 每秒 pull 一次，只有 seq 比推送里见到的还新才用它 —— 推送正常时这段一次都不会触发 setState，
  // 而 "poll 取到更新的 seq" 这行日志出现，就等于当场证明推送通道坏了（分诊用）。
  useEffect(() => {
    let dead = false;
    const apply = (v: unknown) => {
      if (dead) return;
      try {
        const p = JSON.parse(String(v)) as UiState;
        if (!p || typeof p.seq !== "number") return;
        if (p.seq === pushedSeq.current) return;
        console.log(
          "[BusLineAutoStops] poll 取到更新的 seq=" + p.seq + "（push 里最后见到 " + pushedSeq.current +
            "）→ 推送通道没送到，改用 poll：enabled=" + p.enabled
        );
        pushedSeq.current = p.seq;
        setState(p);
        setWarningKey(p.warningKey || "");
      } catch {
        /* 解析失败就等下一次 */
      }
    };
    const tick = () => {
      try {
        const r = call<string>(GROUP, STATE_CALL, "");
        if (r && typeof (r as Promise<string>).then === "function") {
          (r as Promise<string>).then(apply, (e: unknown) => {
            if (!dead) console.warn("[BusLineAutoStops] PullUiState 失败", e);
          });
        } else {
          apply(r);
        }
      } catch (e) {
        console.warn("[BusLineAutoStops] PullUiState call 失败", e);
      }
    };
    const t = window.setInterval(tick, 1000);
    return () => {
      dead = true;
      window.clearInterval(t);
    };
  }, []);

  useEffect(() => {
    if (!warningKey) return;
    const t = window.setTimeout(() => setWarningKey(""), 3000);
    return () => window.clearTimeout(t);
  }, [warningKey]);


  // 别的 mod 的按钮可能比我们晚挂载，所以量四次；每次都基于实时矩形，幂等。
  useEffect(() => {
    const rounds = [16, 300, 1200, 3000];
    const timers: number[] = [];
    for (let i = 0; i < rounds.length; i++) {
      timers.push(
        window.setTimeout(() => {
          probe(MARK, shiftRef.current, (px: number) => {
            shiftRef.current = px;
            setShift(px);
          });
        }, rounds[i])
      );
    }
    return () => {
      for (let i = 0; i < timers.length; i++) window.clearTimeout(timers[i]);
    };
  }, []);

  const active = !!state.enabled;
  const masterOn = state.master !== false;

  // Tooltip 只接受 string（传元素会报 "[tooltip] must be of type string"）
  const title = useMemo(() => {
    const bag = strings as Record<string, unknown> | undefined | null;
    const v = bag ? bag[TOOLTIP_KEY] : undefined;
    return typeof v === "string" && v && v !== TOOLTIP_KEY ? v : "BusLine AutoStops";
  }, [strings]);

  const onClick = () => {
    // 这行是需求 2 的分诊点：UI.log 里有它、模组日志里没有 [入口] ⇒ 绑定类型或名字对不上；
    // 两边都没有 ⇒ 点击根本没落到按钮上。
    console.log("[BusLineAutoStops] button clicked");
    try {
      // 必须是 trigger()：C# 那边注册的是 TriggerBinding<string>（UiCommandSystem.kToggleTrigger）。
      // 0.4.1 用的是 CallBinding 却在这里调 trigger()，于是点击静默丢失。
      trigger(GROUP, TOGGLE_TRIGGER, "");
    } catch (e) {
      console.warn("[BusLineAutoStops] toggle failed", e);
    }
  };

  const btnStyle: React.CSSProperties = {};
  // 需求 4：总开关关掉 → 左上角不出现按钮（提示条还是留着，否则玩家不知道为什么按了没反应）
  if (!masterOn && !warningKey) return null;

  // 需求 5：开启时**图案**变红、蓝底不动 —— 直接换一张红色图标，不去覆盖 background-color。
  if (shift !== 0) {
    btnStyle.position = "relative";
    btnStyle.left = `${shift}px`;
  }

  return (
    <>
      {masterOn ? (
        <Tooltip tooltip={title}>
          <Button
            // key 随状态变：强制重挂载，让 <img src> 换成另一张图（白/红）。
            // 需求 2 的另一半 —— 0.4.1 里状态其实是变的，但 GetterValueBinding 用 AddBinding
            // 注册（不轮询），前端拿到的一直是挂载那一刻的 JSON，所以图标从不变红。
            key={"blas-" + (active ? "on" : "off")}
            variant="floating"
            src={active ? ICON_ON : ICON_OFF}
            onSelect={onClick}
            style={btnStyle}
            {...{ [MARK]: "1" }}
          />
        </Tooltip>
      ) : null}
      {warningKey ? (
        <div style={warnStyle}>
          {/* 关键：Localized 触发 Translate，BridgeTheLanguageGap 才能拦到并机翻；
              样式放在外层 div 上，文字交给 Localized。 */}
          <Localized id={warningKey} />
        </div>
      ) : null}
    </>
  );
};

const register: ModRegistrar = (moduleRegistry) => {
  try {
    moduleRegistry.append("GameTopLeft", () => <App />);
    console.log("[BusLineAutoStops] UI module registered");
  } catch (e) {
    console.warn("[BusLineAutoStops] register failed", e);
  }

  // 需求 5：资产面板过滤（详见文件里"需求 5"那段挂点说明）。
  // extend 只在目标模块已注册时可用；万一原版改了路径，这里失败也只是"不过滤"，
  // 按钮和懒人模式本身不受影响。
  try {
    moduleRegistry.extend(ITEM_GRID_MODULE, ITEM_GRID_EXPORT, wrapItemGrid);
    console.log("[BusLineAutoStops] ItemGrid extended (asset filter ready)");
  } catch (e) {
    console.warn("[BusLineAutoStops] extend ItemGrid failed", e);
  }
};

/** 本模组没有独立 css 文件；loader 靠这个导出决定是否再拉同名 .css。 */
export const hasCSS = false;

export default register;
