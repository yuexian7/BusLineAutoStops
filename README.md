# BusLine AutoStops

画公交线路时自动配上站台资产的都市天际线2 模组：路径点停住 0.18 秒出站台预览，左键点击才落地，线路的停靠点自动依附到站台。

- **Paradox Mods**: ModId **160528**（Private）
- **GitHub**: https://github.com/yuexian7/BusLineAutoStops

## 功能（v0.11.1）

- 快捷键（默认 G）或左上角按钮进入/退出「懒人模式」，进入时自动切到对应类型的**创建线路工具**
- 画线时悬停路径点停满 0.18s → 落点**预览显示公交站**（依附到道路，落在光标这一侧人行道）；光标移开 0.15s 预览淡出，停回原处沿用同一枚
- **左键点击**该停靠点 → 站台转正为真实建筑，线路完成时停靠点自动连到站台、公交进站
- 右键撤停靠点 → 同时撤掉该点由模组建的站台；线路完成 → 站台全部保留
- **资产面板**：条目按实体键去重（同一个资产在分类里登记几次也只显示一条）—— 点任何资产只点亮它一个，与开不开懒人模式、开不开模组无关
- 懒人模式中面板两处同时点亮：**线路工具**（原版自己点亮）+ **当前站台资产**；换一枚站台资产，点亮跟着换
- 懒人模式中资产面板只显示同类站台资产；点站台资产只换预览用资产，**不会切工具退出懒人模式**
- 已有同类站台的位置不重复放置（原版会依附到它）；原版校验不放的位置一律不放
- 退出策略：保留 / 不保留本次会话落地的站台（仍被线路引用的永不误删；不保留时顺带撤掉依附其上的停靠点）
- 零 Harmony 补丁、零自定义组件；放置/校验/吸附全走原版管线
- 12 官方语言；游戏内文案走 `Translate`，可被 Bridge the Language Gap 翻译

## 构建与本地部署

```powershell
# 推荐（本机 NuGet 异常时也能用，Roslyn 直编）
node scripts/build-frontend.js
node scripts/deploy-local.js
# → F:\SteamLibrary\steamapps\common\Cities Skylines II\Cities Skylines II\Mods\BusLineAutoStops\
```

官方工具链 `dotnet build` 已修复可用（0 警告 0 错误），适合 IDE 打开与编译校验；**但它会重铺部署目录、清掉前端产物，跑完必须再执行 `node scripts/deploy-local.js`**。发布打包仍以 `scripts/pack-release.js` 为准。构建命令、版本五处统一表与现役脚本清单见 `ARCHITECTURE.md`。

离线纯逻辑测试：

```powershell
node tests/t3/build-run.js
```

## 目录

- `BusLineAutoStopsMod.cs` — 入口（键位 / 语言 / 系统注册）
- `Setting.cs` / `LocaleTable.cs` — 选项与 12 语言
- `Systems/LazyModeSystem.cs` — 预览 + 点击落地 + 撤点回退
- `Systems/UiCommandSystem.cs` — UI 绑定与资产选择拦截
- `AssetFilterService.cs` / `StationAssets.cs` / `Systems/StopIndex.cs` — 面板过滤与站台索引
- `Frontend/` — GameTopLeft 按钮 + ItemGrid 过滤/拦截
- `ARCHITECTURE.md` — 现行机制速览、四条硬不变量、构建命令与已知待办
- `docs/history/开发笔记-v0.1.1~v0.11.1.md` — 已冻结的逐版需求、机制取证（file:line）与实机验证清单

## 兼容性

- 零 Harmony 补丁、零自定义 ECS 组件；卸载后读档不脏存档
- 关闭懒人模式后不干预原版行为
- 本期支持公交与有轨电车；火车/地铁/机场/码头是车站体系，架构预留

## Bridge the Language Gap（BLG）兼容

- BLG 只 Hook `UILocalizationManager.Translate`。TrafficToolEssentials 的游戏内面板用 TS 词典直出、不经过该出口，所以 BLG 翻不动它——那是 TTE 的上游问题，本模组不去改 BLG 也不改 TTE。
- 本模组全部文案都走 `Translate`，保证 BLG 能翻：面板字挂 `ModOptions` scope（key 以 setting.id 为前缀），选项行标题/说明走框架 `GetOptionLabel/DescLocaleID`，下拉枚举成员走 `GetEnumValueLocaleID`（`Options.<id>.EXITDELETEPOLICY[成员]`，归 `GameCore` scope）。前端硬编码 TS 词典是被禁止的路径——那正是 TTE 翻不动的根因。
- 判定逻辑与 BLG 的 `Scope.ExtractIdentifier` / `IsModIdentifier` 同形，见 `BlgKeys.cs`。
