# BusLine AutoStops

画公交线路时自动配上站台资产的都市天际线2 模组：路径点停住 0.18 秒出站台预览，左键点击才落地，线路的停靠点自动依附到站台。

- **Paradox Mods**: ModId **165510**（Private，当前线上 0.14.2；旧 ModId 160528 已于 2026-10-30 删除重发）
- **GitHub**: https://github.com/yuexian7/BusLineAutoStops

## 功能（v0.14.2）

- **总开关「启用模组」**（设置页第一条）：关掉 → 左上角图标不出现、快捷键无效、游戏完全恢复原版行为；开启后图标出现，懒人模式才能用**快捷键（默认 G）或图标按钮**进入/退出。快捷键只切懒人模式，永远不会把模组打开
- 进入懒人模式时自动切到对应类型的**创建线路工具**
- 画线时悬停路径点停满 0.18s → 落点**预览显示公交站**（依附到道路，落在光标这一侧人行道）；光标移开 0.15s 预览淡出，停回原处沿用同一枚
- **左键点击**该停靠点 → 站台转正为真实建筑，线路完成时停靠点自动连到站台、公交进站
- 光标只是停在**线路自己的路径点/路段**上时**不预览**（还没按左键，下一次左键是拖拽，不是放站）；**一旦按住左键开始拖拽，预览立刻回来**并跟着光标走。踩在**既有站台的依附范围**里同样不预览 —— 原版会把这一笔路径点直接依附到那枚站台上
- **左键拖拽**已完成的线路（拖拽线路=新增、拖拽路径点=修改）→ 松手即在光标处放下公交站，**被拖的那个路径点会接到这枚站台上**；每次拖拽新增都算完成，**右键不回退它**
- 右键撤停靠点 → 同时撤掉该点由模组建的站台；线路完成 → 站台全部保留
- **资产面板**：条目按实体键去重（同一个资产在分类里登记几次也只显示一条）—— 点任何资产只点亮它一个，与开不开懒人模式、开不开模组无关
- 懒人模式中面板两处同时点亮：**线路工具**（原版自己点亮）+ **当前站台资产**；换一枚站台资产，点亮跟着换
- 懒人模式中资产面板只显示同类站台资产；点站台资产只换预览用资产，**不会切工具退出懒人模式**
- 已有同类站台的位置不重复放置（原版会依附到它）；原版校验不放的位置一律不放
- 退出策略：默认**不保留**本次会话落地的站台（可选「保留公交站」；线路保存后即永远保留；不保留时顺带撤掉依附其上的停靠点）。该选项跨重启保持（v0.14.0 修好落盘寻址，v0.14.2 换掉持久化键名 —— 老版本默认值写进文件里的那条「保留」从此失效，玩家真正选过的值照旧保存）
- 零 Harmony 补丁、零自定义组件；放置/校验/吸附全走原版管线
- 12 官方语言；游戏内文案走 `Translate`，可被 Bridge the Language Gap 翻译

## 构建与本地部署

```powershell
# 推荐（本机 NuGet 异常时也能用，Roslyn 直编）；先退出游戏，游戏在跑时脚本会拒绝部署
node scripts/build-frontend.js
node scripts/deploy-local.js
# → F:\SteamLibrary\steamapps\common\Cities Skylines II\Cities Skylines II\Mods\BusLineAutoStops\
```

部署到 `Mods\BusLineAutoStops\` 只代表磁盘上是新版，**游戏必须重启进程才会载入**（内存里一直是启动时那份程序集）。所以实机验证的顺序固定为：退出游戏 → 部署 → 归档旧日志 → 从 Steam 启动。`deploy-local.js` 会查 `Cities2.exe`，在跑就 `exit 1`（只重铺产物/打包时用 `--allow-running`）。

官方工具链 `dotnet build` 已修复可用（0 警告 0 错误），适合 IDE 打开与编译校验；**但它会重铺部署目录、清掉前端产物，跑完必须再执行 `node scripts/deploy-local.js`**。发布打包仍以 `scripts/pack-release.js` 为准。构建命令、版本五处统一表与现役脚本清单见 `ARCHITECTURE.md`。

离线门禁与部署完整性：

```powershell
node scripts/verify.cjs              # 版本号四处一致 + 纯逻辑回归
node scripts/verify.cjs --deployed   # 再加：部署目录四件套齐全、产物就是本次构建（字面量 + mtime + 尺寸）
```

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
