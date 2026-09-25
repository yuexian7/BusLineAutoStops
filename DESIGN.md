# BusLine AutoStops 设计文档

版本：v0.1.0（本地开发，未发布）  
日期：2026-09-24  
游戏：都市天际线2 1.6.*（工具链 net48 + 官方 Mod.props）

## 1. 目标

创建公交线路时，每设置一个停靠点就自动放置当前选中的站台资产；右键删除该停靠点时一并删除本模组放置的站台。用快捷键或左上角按钮进入/退出「懒人模式」。

本期只做**公交**；架构按「线路类型 ↔ 站台类型」成对扩展。

## 2. 风格锚点 / 视觉

- 锚点：原版线路创建工具 + TrafficToolEssentials 的 GameTopLeft 浮动按钮。
- 色板：原版 UI 面板底 `rgb(42,55,83)`，文字近白，激活态用原版强调青/黄；警告用原版错误红。
- 图标：线路创建语义（站台 + 路径节点），单色 SVG。
- 字型：跟随游戏 Cohtml 默认；设置页走官方 ModSetting。

## 3. 功能规格

### 3.1 懒人模式开关

| 入口 | 行为 |
|------|------|
| 快捷键「开启/关闭懒人模式」 | 默认空（`BindingKeyboard.None`），玩家自绑 |
| 左上角按钮（信息视图开关右侧） | 同上；激活时按钮高亮 |

进入条件：当前已选择**同类型**（公交）站台资产。  
不满足时：按钮下方警告「请选择站台资产!」，3 秒后消失。

进入后：

1. 切换到创建线路工具（公交）。
2. 底部面板仍显示站台资产选择（可中途换同类型站台）。
3. 资产面板中非公交站台置灰/不可选。
4. 游戏视图为线路创建视图。

完成一条线路后**保持**懒人模式，直到玩家手动关闭。

### 3.2 停靠点 → 自动站台

- 悬停预览：在路边显示当前站台虚影，中心对齐预览停靠点。
- 虚影颜色遵循原版建造规则（可建=正常，碰撞/坡度/淹水=红虚影）。
- 若吸附距离内已有**同类型**站台（含车站/客运站/大型/独特建筑内的公交站台）→ 不显示虚影、不放置。
- 其它类型站台（电车/火车等）不算占用。
- 点击设置停靠点后：若预览可建，自动放置站台（中心对齐停靠点），由游戏吸附。
- 右键删除停靠点：仅当站台是本模组随该停靠点生成时，才删除站台。

### 3.3 中途退出删除机制（选项下拉框）

| 值 | 行为 |
|----|------|
| 不做任何删除（默认） | 保留已放站台与停靠点 |
| 删除新建线路全部站台和停靠点 | 回滚本会话本条新建内容 |
| 仅删除新建线路站台 | |
| 仅删除新建线路停靠点 | |

关闭懒人模式后完全恢复原版行为。

### 3.4 选项界面

1. 快捷键：开启/关闭懒人模式（默认空）
2. 下拉框：中途退出懒人模式的删除机制

## 4. 架构

```
BusLineAutoStops/
├── BusLineAutoStops.csproj
├── BusLineAutoStopsMod.cs      IMod：设置/快捷键/Harmony/UI 绑定
├── Setting.cs                  ModSetting：快捷键 + 删除机制下拉
├── LocaleTable.cs              12 官方语言词条（设置页 + 游戏内面板）
├── LazyModeState.cs            volatile 状态镜像（热路径不读属性链）
├── TransportKind.cs            线路/站台类型枚举（Bus 先实现）
├── PlacedStopRegistry.cs       停靠点 ↔ 本模组站台 的关联账本
├── Patches/
│   ├── RouteToolPatches.cs     捕获停靠点创建/删除
│   └── ObjectSelectPatches.cs  捕获当前选中站台资产
├── Systems/
│   ├── AutoPlaceSystem.cs      停靠点落点后放置站台 / 删除联动
│   ├── StopGhostSystem.cs      悬停虚影（Temp 实体，走原版校验）
│   └── UiCommandSystem.cs      向前端推送按钮状态/警告
└── Frontend/                   Cohtml UI（GameTopLeft 按钮 + 警告）
    ├── mod.json
    └── src/...
```

### 4.1 类型映射（可扩展）

| TransportKind | 线路 RouteType / TransportType | 站台特征 |
|---------------|--------------------------------|----------|
| Bus（本期） | Bus / Bus | `BusStop` + `TransportStopData` |
| Tram（预留） | Tram | `TramStop` |
| Train（预留） | Train | `TrainStop` |
| … | … | … |

选择非 Bus 站台资产时**不可**进入懒人模式；以后启用对应 Kind 即可。

### 4.2 预览与原版建造规则

不自写碰撞/坡度/水面检测。做法：

1. 根据 RouteTool 悬停控制点推算停靠点位置；
2. 在该位置创建/更新 **Temp** 物体实体（prefab=当前站台，flags 含 Create）；
3. 原版 `ValidationSystem` / 网格渲染会给出正常或红色虚影；
4. 已有同类型站台时直接不建 Temp。

放置成功后删除 Temp，走正常对象落档（与手动放站台同一套规则）。

### 4.3 关联账本

`PlacedStopRegistry` 记录：

- 停靠点 Entity（或稳定身份：Index+Version+坐标哈希，Playbook 硬规则 21）
- 站台 Entity
- 是否本模组生成

右键删除停靠点时：查账本，仅当 `ByThisMod` 才删站台。  
中途退出按删除策略批量回滚。

## 5. BridgeTheLanguageGap 兼容（已按「不改 BLG」落地）

### 5.1 为何 BLG 翻不动 TrafficToolEssentials 面板文字

| 路径 | TTE 实际做法 | 是否经 `UILocalizationManager.Translate` |
|------|----------------|------------------------------------------|
| 游戏内面板 | `TLEFrontend/src/localisations/*.ts` + `getString()` 直出 React 文本 | **否** |
| 模组选项页 | `LocalizationDictionary.Add` + `GetOptionLabelLocaleID` | **是**（BLG 可翻） |

BLG 只 Hook `UILocalizationManager.Translate`。TTE 面板字符串从不经过该出口 ⇒ BLG 看不见。

### 5.2 本模组如何保证「能被 BLG 翻译」（不改 BLG）

| 文案 | 出口 | BLG Scope 归类 | 机制 |
|------|------|----------------|------|
| 警告/按钮提示等面板字 | `AddSource` + 前端 `cs2/l10n` 的 `<Localized id>` / `useLocalization` | **ModOptions** | key=`Options.…[setting.id.Panel.slug]` 或 `Common.ACTION[setting.id.Panel.slug]`，identifier 以 `ModSetting.instances` 的 key（≈`setting.id`）为前缀 |
| 选项行标题/说明 | 框架 `GetOptionLabel/DescLocaleID` | **ModOptions** | 同上，id 在 `Options.OPTION[...]` 的 path 里 |
| 下拉枚举成员名 | 框架 `GetEnumValueLocaleID` + 自动 `Options.<id>.EXITDELETEPOLICY[成员]` | **GameCore** | `[]` 内只有成员名，拿不到 setting.id（T3 已钉住）；经 Translate，BLG 勾「游戏本体」可翻 |
| 前端硬编码/TS 词典 | **禁止** | 不可见 | 这是 TTE 翻不动的根因 |

判定逻辑见 `BlgKeys.cs`（与 BLG `Scope.ExtractIdentifier` / `IsModIdentifier` 同形），T3 覆盖。

### 5.3 不改 BLG、也不改 TTE

TrafficToolEssentials 面板翻译属其上游问题。改 BLG 去抓第三方前端词典脆弱且易误翻，**不执行**。

## 6. 本地化

- 12 官方语言：`de-DE en-US es-ES fr-FR it-IT ja-JP ko-KR pl-PL pt-BR ru-RU zh-HANS zh-HANT`
- 设置页：`LocaleTable.Build(setting, locale)` 每语言独立字典（禁用全局 activeLocale）
- 游戏内：同一套词表挂到 `Options.…[BusLineAutoStops]` 等 key
- 下拉枚举：`Options.<id>.DELETEPOLICY[成员]`（带 id 前缀，ZoneSnapper 教训）

## 7. 快捷键（照 AccessAnarchy / ZoneSnapper）

```csharp
[SettingsUIKeyboardAction(kActionToggleLazy)]
[SettingsUIKeyboardBinding(BindingKeyboard.None, kActionToggleLazy)]
public ProxyBinding ToggleLazyBinding { get; set; }
// LoadSettings → RegisterKeyBindings → GetAction
// 仅 IsBindablePath 时 shouldBeEnabled = true
// onInteraction 只认 InputActionPhase.Started
```

## 8. 删除机制枚举

```csharp
public enum ExitDeletePolicy
{
    DoNothing = 0,          // 默认
    DeleteStopsAndStations = 1,
    DeleteStationsOnly = 2,
    DeleteStopsOnly = 3,
}
```

## 9. 验证

- T1：`dotnet build -c Release` 零错误
- T2：部署 DLL 含版本字面量与关键类型名
- T3：纯逻辑回归（账本、类型匹配、删除策略、词条键）
- T4：本地实机（部署 `Mods\BusLineAutoStops\`，只启用本模组）
- **不上传** Paradox / GitHub（本轮明确禁止）

## 10. 约束

- 不修改 `BridgeTheLanguageGap` 目录内任何文件（仅只读分析）
- 不删除/修改非本模组创建的文件
- 关闭懒人模式后恢复原版行为
