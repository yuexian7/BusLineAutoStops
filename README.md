# BusLine AutoStops

创建公交线路时自动放置站台资产的都市天际线2 模组（本地开发中，**未发布**）。

## 功能

- 快捷键或左上角按钮进入/退出「懒人模式」
- 每设置一个停靠点，自动放置当前选中的**公交**站台（中心对齐）
- 悬停显示站台虚影；建造规则违规时红色虚影（原版行为）
- 吸附距离内已有公交站台/车站时不再放置
- 右键删除停靠点时，仅删除本模组生成的站台
- 选项：删除机制下拉框（默认不做任何删除）
- 12 官方语言；游戏内面板文案走 `Translate`，可被 Bridge the Language Gap 翻译

## 构建与本地部署

```powershell
# 推荐（本机 NuGet 异常时也能用）
node scripts/build-frontend.js
node scripts/deploy-local.js
# → F:\SteamLibrary\steamapps\common\Cities Skylines II\Cities Skylines II\Mods\BusLineAutoStops\
```

离线纯逻辑测试：

```powershell
dotnet run --project tests/t3/T3.csproj -c Release
```

## 目录

- `BusLineAutoStopsMod.cs` — 入口
- `Setting.cs` / `LocaleTable.cs` — 选项与 12 语言
- `Systems/` — 自动放置、虚影、UI 绑定
- `Patches/` — 线路工具与站台选择
- `Frontend/` — GameTopLeft 按钮
- `DESIGN.md` — 设计与 BLG 兼容分析

## 兼容性

- Harmony 2.2.2 编译 / 2.3.3 运行时（与其它模组共存策略见开发笔记）
- 关闭懒人模式后不干预原版行为
- 本期仅公交；其它交通类型架构已预留
