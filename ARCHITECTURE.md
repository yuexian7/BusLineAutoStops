# BusLine AutoStops 架构速览（v0.11.1）

给下一个接手者的最小集：现行机制、硬不变量、构建命令、待办。
逐版迭代史与机制取证（96 条校正、每版实机日志）在 `docs/history/开发笔记-v0.1.1~v0.11.1.md`（已冻结，只查不改）。
当前版本线上状态：Paradox Mods ModId 160528 / Private / versionId=12 / 0.11.1（2026-10-11 上传）。

## 机制一页纸：懒人模式状态机

零 Harmony 补丁、零自定义 ECS 组件；放置/校验/吸附全走原版管线。核心在 `Systems/LazyModeSystem.cs`。

1. **静止判定**：线路工具悬停控制点（`RouteToolSystem.GetControlPoints` 的最后一个，公开 API）静止满 **0.18s**（`kSettleSeconds`）→ 触发预览；连续移动满 0.15s（`kPreviewFadeSeconds`）才收，停回 2.5m（`kPreviewKeepRadius`）内沿用同一枚幽灵。
2. **喂幽灵**：喂一枚 `CreationDefinition{m_Prefab=站台资产, m_Attached=解析出的合法父级, m_Flags=Attach|Optional}` + `ObjectDefinition + Updated`，走 `ToolOutputBarrier`（与原版工具同一条通道）；同帧 Modification1（GenerateObjectsSystem）就出 Temp 幽灵，Modification4 吸人行道。`Optional` 让原版校验自动撤销幽灵上的错误，预览永不挡点击。
3. **认领**：喂出后逐帧追、最多 10 帧（`kClaimMaxFrames`）把幽灵认领回来，登记成"**世上只留一枚幽灵**"——它活着就一帧不再喂，被原版 Clear 收走了才补喂；认领那一刻按吸附后的实际落位查重（已有同类站台则不放置，原版会依附到它）。
4. **转正**：玩家**左键**点击，applyMode 由原版应用（模组只读不写），站台转正为真实建筑；线路完成时停靠点自动连到站台、公交进站。转正/依附均由原版管线完成。
5. **撤点回退**：右键撤停靠点 → 同时撤掉该点由本模组建的站台（先撤点后删站，四条防御）；线路完成 → 站台全部保留。退出策略：保留 / 不保留本次会话落地的站台（仍被线路引用的永不误删）。

## 四条硬不变量（违反即闪退/毁世界，取证都在笔记 #63/#88/#89/#90）

| # | 不变量 | 原因（一句话） |
|---|--------|----------------|
| 63 | **一个系统只注册一次** `UpdateBefore<ToolOutputSystem>`（单槽），绝不 `UpdateAt`+`UpdateBefore` 叠用 | `UpdateSystem` 的 `m_Systems` 与 `m_RefMap` 是两张互不相干的表，`Refresh()` 无去重，叠用 = `OnUpdate` 一帧跑三次 → 2026-10-03 闪退 |
| 88 | `RouteToolSystem.GetControlPoints` 返回的是**工具自己的持久缓冲**（NativeList 视图）→ **共用缓冲绝不 Dispose**；撤点只能 `state==Create` 时从 `Length-2` 往前 `RemoveAt`，且守住四条防御（不动悬停位 / 遇非本会话依附就停 / 剩 3 点收手） | `Cancel()` 是私有的，别的系统（GuideLinesSystem）也是直接拿这份缓冲进 job |
| 89 | **`Temp.m_Original == Entity.Null` 才是本模组造的幽灵**，`!= Null` 是原版的复制/选中副本（原件正被 `Hidden` 藏着）→ 判己防误删，删副本 = 玩家看到"站台消失" | 副本要等 `SubObjectHiddenSystem` 确认没有活 Temp 指回原件才解除隐藏 |
| 90 | **落点定侧只由喂点的有符号横向偏移决定**（喂点用射线真实命中点，不用控制点位置） | `SnapRouteLane` 用 `dot(MathUtils.Right(切向), 喂点-曲线点)` 选车道，`SnapSegmentAreas` 再用它选可建造区；喂错点 = 落到对面人行道 |

## 构建三命令

```powershell
node scripts/compile-check.js        # 只做 C# 编译检查，不部署不跑 webpack
node scripts/deploy-local.js         # Roslyn 直编 DLL → 部署到本机 Mods\BusLineAutoStops\
node scripts/pack-release.js <版本>   # 打包 releases/<版本>/（DLL+mjs+mod.json）并自检，发布打包的事实标准
```

官方工具链 `dotnet build` 已修复可用（0 警告 0 错误；csproj 已补 `Colossal.UI.Binding` 引用、删死 Harmony 配置、`math.isfinite` 替代 net48 没有的 `float.IsFinite`），适合 IDE/编译校验；**发布打包仍以 `scripts/pack-release.js` 为准**。
⚠️ **但 `dotnet build` 会走官方 DeployWIP 重铺部署目录**：整个 `Mods\BusLineAutoStops\` 被清掉后只铺回 DLL+Burst 桩，deploy-local 铺的前端（mjs/mod.json/images）全没了 → 实机表现"左上角图标消失、面板选资产拦截失效"（2026-10-12 事故根因，后端仍活着，日志易误判）。**跑过 `dotnet build` 之后必须立刻 `node scripts/deploy-local.js` 重铺完整产物。**
离线门禁：`node scripts/verify.cjs`（T3 纯逻辑 42 断言 + 版本一致性）；前端构建：`node scripts/build-frontend.js`。

## 版本五处统一（改版本必须五处一起动，verify.cjs 把关）

| 位置 | 字段 |
|------|------|
| `BusLineAutoStopsMod.cs` | `kVersion` |
| `Setting.cs` | `kVersion` |
| `Frontend/mod.json` | `version` |
| `Properties/PublishConfiguration.xml` | `ModVersion` |
| `releases/<版本>/` | 打包快照 |

## 现役脚本清单（scripts/，别删）

| 脚本 | 用途 |
|------|------|
| `build-frontend.js` | webpack 打包前端 dist（npm-cli 直调，绕开带空格的 npm.cmd） |
| `compile-check.js` | 只做 C# 编译检查（引用表与 deploy-local 一致） |
| `deploy-local.js` | Roslyn 直编 + 部署到本机 `Mods\BusLineAutoStops\` |
| `pack-release.js` | 打包 `releases/<版本>/` 并做版本/钉子自检（防发旧产物） |
| `pull-live.js` | 只读核对 Paradox 线上版本表（写 live-versions.json） |
| `set-store-copy.js` | 写 `PublishConfiguration.xml` 面向玩家的 4 个字段 + Frontend mod.json |
| `verify.cjs` | 离线门禁：T3 纯逻辑 + 版本一致性 |

## 已知待办（按优先级）

1. **`m_previewResultLogged` 预览结果日志闸门是下一版第一件事**：认领成功那条 `[预览结果]` 现在一条都不打（闸门与 `[预览站台]` 共用 `m_previewAnnounced`，喂送先置 true）。修法已写好并验证过，0.10.0 时为保线上产物一致整段还原了（见笔记 v0.10.0 节）。
2. **电车机制未上线**：`Setting.TramEnabled` 恒 false 且选项置灰 + `TransportKindRules.IsLazyModeEnabled` 两层门——分类认得 Tram（资产识别/过滤/查重），但懒人模式不启用、不可选。
3. **经济侧已知偏差（已定案的设计，不是 bug）**：自动站台不写 `Game.Tools.Recent` → 建不扣钱、铲不退款、净零刷不出钱（校正 #23；`CommitNow` 注明"以后别顺手补 Recent"）。
4. 火车/地铁/机场/码头是车站体系，`TransportKind` 架构预留。
