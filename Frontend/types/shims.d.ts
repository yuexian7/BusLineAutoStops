declare module "cs2/modding" {
  // 原版 loader 的注册表（bundle 里的 Q）：
  //   add(path, exports) / override(path, exportName, value) / extend(path, exportName, cb)
  //   extend 的实现是 `n(s[t])` —— 回调拿到**当前值**（可能是别的模组已经包过一层的），
  //   返回新值写回导出对象。关键是这些导出是访问器对：
  //     Q.add("game-ui/game/components/item-grid/item-grid.tsx",
  //           {get ItemGrid(){return hoe}, set ItemGrid(e){hoe=e}})
  //   所以赋值会直接改掉模块内那个 `var hoe`，AssetGrid 里 `jsx(hoe, …)` 下次渲染就用我们的包装。
  export type ModRegistrar = (registry: {
    append: (slot: string, component: React.ComponentType) => void;
    extend: (modulePath: string, exportName: string, cb: (current: unknown) => unknown) => void;
    override: (modulePath: string, exportName: string, value: unknown) => void;
  }) => void;
}

declare module "cs2/api" {
  export function bindValue<T>(group: string, name: string, initial: T): unknown;
  export function useValue<T>(binding: unknown): T;
  export function trigger(group: string, name: string, arg?: unknown): void;
  export function call<T>(group: string, name: string, arg?: unknown): Promise<T>;
}

declare module "cs2/l10n" {
  export function useLocalization(): { [key: string]: string };
  export const Localized: React.ComponentType<{ id: string }>;
}

// 游戏自带的 React 组件库，运行时由 window["cs2/ui"] 提供（webpack externals）。
// Button variant="floating" 就是原版左上角那排小方按钮：theme 给 40rem / 6rem 圆角 /
// --accentColorNormal 蓝底，以及 hover / active / .selected 状态。
//
// 图标两条路（index.js 里 Button=Tj 的分支）：
//   src      → IconButton($b) 渲染 <img class=icon_be5 src=…>；再加 tinted 才走 TintedIcon(mask)
//   children → Ab 直接把子节点放进按钮里
// prop 转发（**已用 bundle 原文确认**，不是猜的）：
//   Tj(Button) → src 时 jsx($b,{theme:n,...e,src}) → $b(IconButton) = forwardRef(function({src,tinted,theme,className,children,...a},o){ … jsxs(Ab,{ref:o,...a,…}) })
//   → Ab(Button 本体, index.js@629394) 把 focusKey/selected/disabled/theme/className/onClick/onSelect/children/as/…
//     解构掉，剩下的 `...T`（含 style / data-*）原样落到 <button>。
//   所以 style 与 data-* 在两条分支上都能到 DOM 节点。
// Ab 的点击处理器：L=(e)=>{ (h||u) && (s||Sp(O?.select), h&&h(), u&&u(e), e.stopPropagation() ) }
//   → **onSelect 与 onClick 会在同一次 DOM 点击里都被调用**，所以只能传一个，
//     传两个会让"开关"类回调翻转两次（看起来就像没反应）。onSelect 额外覆盖游戏 UI 的 Select 动作（键盘/手柄）。
//   .icon_be5{width:100%;height:100%;--iconColor:rgb(250,250,250)} —— 没有 pointer-events:none。
// Tooltip 的 tooltip 必须是 string，否则报 "[tooltip] must be of type string"。
//
// 实机证明 children 放内联 <svg> 会让点击发不到按钮上（Cohtml 的 SVG 命中/冒泡不完整），
// 所以本模组用 src + 真 .svg 文件（同 Write Everywhere / BlueprintHub）。
//
// variant 合法值照抄 index.js 的映射表 bj={flat,primary,round,menu,default,icon,floating,text}
// （"editor" / "destructive" / "square" 是 BlueprintHub 早先自己编的，已从它那份声明里删掉）。
declare module "cs2/ui" {
  export const Button: React.ComponentType<{
    variant?: "default" | "flat" | "primary" | "round" | "menu" | "icon" | "floating" | "text";
    src?: string;
    tinted?: boolean;
    theme?: Record<string, unknown>;
    selectable?: boolean;
    selected?: boolean;
    disabled?: boolean;
    focusKey?: unknown;
    onSelect?: (e: unknown) => void;
    onClick?: (e: unknown) => void;
    className?: string;
    style?: React.CSSProperties;
    children?: React.ReactNode;
    [key: string]: unknown;
  }>;
  export const Tooltip: React.ComponentType<{
    tooltip: string;
    direction?: string;
    children?: React.ReactNode;
  }>;
  export function useTooltip(): unknown;
}

declare module "cohtml/cohtml" {
  const engine: {
    call: (name: string, ...args: unknown[]) => Promise<unknown>;
    on: (name: string, cb: (...args: unknown[]) => void) => void;
    trigger: (name: string, ...args: unknown[]) => void;
  };
  export default engine;
}

declare module "*.svg" {
  const content: string;
  export default content;
}
