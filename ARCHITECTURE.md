# BusLine AutoStops 架构速览（v0.11.2）

给下一个接手者的最小集：现行机制、硬不变量、构建命令、待办。
逐版迭代史与机制取证（96 条校正、每版实机日志）在 `docs/history/开发笔记-v0.1.1~v0.11.1.md`（已冻结，只查不改）。
当前版本状态：**v0.11.2 已上传线上**：ModId 160528 / versionId=**13** / userVersion=0.11.2 / size=54185B / **仍 Private**（2026-10-13 发布；发前用 `pull-live.js` 核对线上停在 versionId=12，发后回读 versions 表 + 网页可访问性树确认 MOD VER.、描述、四个外链、Code Mod、1.6.* 全部仍在）。玩家本地实机验收，重点看日志里 `[预览结果]` 是否恢复输出。

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

## v0.11.2 本轮修订（2026-10-12 第三轮审查 → 2026-10-13 实施）

本版**不改任何玩法**：八条里六条是加固与工具链，一条是日志，一条是索引判据。

1. **P1-1 `[预览结果]` 日志闸门复活**。结论：这条从 0.10.0 起一条都没打过。根因：喂送侧 `FeedPreview` 成功时就置 `m_previewAnnounced=true`（旧 `:1064`），认领侧 `ClaimPendingPreview` 的判据是 `if (!m_previewAnnounced)`（旧 `:1219`）→ 永远为假。实现：新增 `m_previewResultLogged`，与 `m_previewAnnounced` 各自独立，两处归零点（移动分支 + `ResetSettleTimers`）同步加一行；**纯日志，零世界改动**。坑在检查器：pack-release 那枚旧钉子只查 `utf16Has(dll,"[预览结果]")`，分支死了字面量还在 → 长期假绿。
2. **P1-2 退出清理先收 job 再改结构**。结论：`RunExitCleanup` 里 `ResetSettle(now)`（内含 `RetirePreview` → `AddComponent<Deleted>`）排在 `FlushPendingJobs()` 之前，与 `FlushPendingJobs` 自己的注释（"结构改动之前必须先收干净 job"，旧 `:1866-1870`）矛盾；`BeginSession` 的消费路径（`OnUpdate` 的 `m_beginCleanupPending`）本来就是 flush→pending→preview。实现：`SettleDwell(false); FlushPendingJobs(); ResetSettle(now);`（`SettleDwell` 只写托管状态，位置无所谓）。旧钉子写成 `RunExitCleanup[\s\S]{0,400}?FlushPendingJobs\(\);`，顺序反了也照样绿 → 换成**方法体内**取 `FlushPendingJobs` 与 `ResetSettle` 的字符次序判。
3. **P1-3 部署完整性守卫**。结论：2026-10-12 那次 `dotnet build` 走 DeployWIP 清掉部署目录、前端产物全没的事故，没有任何脚本报警。实现：`verify.cjs --deployed` 断言四件套齐全、目录里没有白名单外的东西、DLL 含本版字面量且不含 `releases/` 里其它版本的字面量、部署 DLL/mjs/mod.json 与 `bin/deploy`+`Frontend/dist`+`Frontend/mod.json` 同尺寸且不更早、**最新 .cs / 前端源码的 mtime 不得晚于产物**、图标逐个同尺寸、运行数据不落 `Mods\`。判据全用**字面量 + mtime + 尺寸**，不用文件哈希（这套 csc 调用非确定性构建）。目录真值抽到 `scripts/paths.js`，deploy 与 verify 读同一处。
4. **P2-4 资产过滤的构建标记后置**。结论：`AssetFilterService.BuildJson` 在枚举资产**之前**就置 `s_builtSeq/s_builtOn/s_builtKind`（`:57-59`），中途抛异常时标记已置、`s_json` 还是上一份 → 同一 seq 的后续调用把旧名单当新名单返回（换存档后 `index:version` 键失效那段时间尤其难看）。现网各环节都有 catch，风险低，但方向是"认证失败的构建不许被复用"。实现：三行赋值收进 `MarkBuilt(seq, on, kind)`，只在两条 `s_json` 算出之后各调一次。
5. **P2-5 查重索引：审查的前提被反编译否掉了**。结论：`All=ConnectedRoute` 不是"漏掉未连线站台"的原因——**连接缓冲与 BusStop/TramStop 标记是同一份实例原型给的**，与有没有线路连上无关。证据：`Game.Prefabs.TransportStop.GetArchetypeComponents()`（`research/TransportStop.cs:50-59`，ilspycmd 单类型 dump）对 Bus 型一次加 `:57 ConnectedRoute` + `:59 BusStop`（Tram 型 `:85`/`:87`）；`ObjectPrefab.RefreshArchetype` 把整套原型记进 `ObjectData.m_Archetype`（`research/ObjectPrefab.cs:43`，物件实例必带 `Game.Objects.Transform`：同文件 `:22`），`GenerateObjectsSystem.cs:1232 CreateEntity(m_Archetype)` 建实体时一次铺齐，`ApplyObjectsSystem.cs:669-685` 转正只摘 Temp 动画类/加 Applied。反向：原版写 `ConnectedRoute` 的三处（`WaypointConnectionSystem.cs:111-113`、`ApplyRoutesSystem.cs:66-72`、`ConnectedRouteSystem.cs:34-36`）都是 `HasBuffer` 判过之后往已有缓冲里塞元素，从不新建缓冲。实现：照原版口径收查询——去掉冗余的 `All`，`None` 补 `Deleted`（`research/ResourceAvailabilitySystem.cs:1021`：`BusStop + Exclude<Deleted> + Exclude<Temp>`）。**未取证**：第三方资产能否只带 `BusStop` 不带缓冲（只找到经 `Game.Prefabs.TransportStop` 授权这一条路）；改宽的方向是"多认一枚已有公交站 → 更少放置"，安全侧。
6. **P2-6 编译器路径不再写死**。结论：`compile-check.js:12`、`deploy-local.js:10`、`tests/t3/build-run.js:5-6` 三处写死 `sdk\8.0.425`（t3 还写死 `Microsoft.NETCore.App.Ref\8.0.31`），SDK 一升级三条链一起断。实现：`scripts/dotnet-sdk.js` 的 `cscPath()` / `netCoreRefDir()`——**优先本机最高 8.x**（与项目验证过的主版本同 band），没有 8.x 才取最新并在 stderr 提示"Roslyn/引用集会跟着变"，一个都没有就抛错。
7. **P3-1 版本一致性扩到四处**：`verify.cjs` 原来只对 `Mod.cs kVersion` 与 XML `ModVersion`，现在加上 `Setting.cs kVersion` 与 `Frontend/mod.json version`（第五处 `releases/<版本>/` 由 pack-release 装订），不一致就在改完当场红，不用等出包。
8. **P3-2 OnDispose 不注销语言源/选项页 → 记为已知限制，不动代码**。注销 API 其实是有的：`LocalizationManager.RemoveSource`（`Colossal.Localization/LocalizationManager.cs:357`）、`ModSetting.UnregisterInOptionsUI`（`Game.Modding/ModSetting.cs:51`），但 `RemoveSource` 命中当前语言时会 `activeDictionary.Clear()` + 重载 + 广播（同文件 `:368-372`），属于拆除期做重活；游戏禁用模组要重启进程才生效，注册项寿命=进程寿命。已在 `BusLineAutoStopsMod.OnDispose` 的文档注释里写明理由与限制。

顺带一处文案定案（玩家 2026-10-13 选）：设置页总开关说明 `master.desc` 12 语种改为"**停住约 0.2 秒出预览，真正放下的是你自己的左键**"，不再写"自动放置"（与上面机制第 4 条一致，也和商店长描述不互相矛盾）；商店页 Short/Long Description 本版不动，只推版本号与 ChangeLog。

## 构建三命令

```powershell
node scripts/compile-check.js        # 只做 C# 编译检查，不部署不跑 webpack
node scripts/deploy-local.js         # Roslyn 直编 DLL → 铺完整产物到本机 Mods\BusLineAutoStops\
node scripts/pack-release.js <版本>   # 打包 releases/<版本>/（DLL+mjs+mod.json+images）并自检，发布打包的事实标准（本版 144 枚钉子）
```

官方工具链 `dotnet build` 已修复可用（0 警告 0 错误；csproj 已补 `Colossal.UI.Binding` 引用、删死 Harmony 配置、`math.isfinite` 替代 net48 没有的 `float.IsFinite`），适合 IDE/编译校验；**发布打包仍以 `scripts/pack-release.js` 为准**。
⚠️ **但 `dotnet build` 会走官方 DeployWIP 重铺部署目录**：整个 `Mods\BusLineAutoStops\` 被清掉后只铺回 DLL+Burst 桩，deploy-local 铺的前端（mjs/mod.json/images）全没了 → 实机表现"左上角图标消失、面板选资产拦截失效"（2026-10-12 事故根因，后端仍活着，日志易误判）。**跑过 `dotnet build` 之后必须立刻 `node scripts/deploy-local.js` 重铺完整产物。**

离线门禁：

```powershell
node scripts/verify.cjs                 # 版本四处一致 + T3 纯逻辑 42 断言
node scripts/verify.cjs --deployed      # 再加部署完整性 15 项（实机前、上传前必跑）
node scripts/build-frontend.js          # 前端构建（deploy-local 默认已强制重跑 webpack）
node scripts/set-store-copy.js          # 写面向玩家的 4 个字段 + mod.json 版本（改版本后跑）
```

## 版本五处统一（改版本必须五处一起动）

| 位置 | 字段 | 谁把关 |
|------|------|--------|
| `BusLineAutoStopsMod.cs` | `kVersion` | verify.cjs（四处一致）+ pack-release |
| `Setting.cs` | `kVersion` | verify.cjs + pack-release |
| `Frontend/mod.json` | `version`（webpack 横幅从这里取） | verify.cjs + pack-release |
| `Properties/PublishConfiguration.xml` | `ModVersion` | verify.cjs + pack-release |
| `releases/<版本>/` | 打包快照 | pack-release |

流程：改两处 `kVersion` → 改 `scripts/set-store-copy.js` 的 `VERSION` 与 ChangeLog → `node scripts/set-store-copy.js` → `node scripts/pack-release.js <版本>` → `node scripts/verify.cjs --deployed`。

## 发布序列（本模组）

```powershell
node scripts/pull-live.js            # 只读：线上版本表停在哪一版
node scripts/pack-release.js 0.11.2  # 全量自检，红一条就别发
node scripts/verify.cjs --deployed   # 部署完整性
MSYS_NO_PATHCONV=1 DOTNET_ROLL_FORWARD=Major dotnet "<游戏>\Cities2_Data\Content/Game/.ModdingToolchain/ModPublisher/ModPublisher.dll" NewVersion Properties/PublishConfiguration.xml --contentFolder releases/0.11.2 --verbose
```

`PublishConfiguration.xml` 是**线上商店页的镜像**，NewVersion 会无条件重推全部元数据：`ModId 160528`、`DisplayName`、`AccessLevel=Private`、`Tag`、`GameVersion`、`Thumbnail`、论坛 + github + x + kofi 四条链接都不许被本地手改带歪（`set-store-copy.js` 里有 `KEEP` 断言）。

## 现役脚本清单（scripts/，别删）

| 脚本 | 用途 |
|------|------|
| `build-frontend.js` | webpack 打包前端 dist（npm-cli 直调，绕开带空格的 npm.cmd） |
| `compile-check.js` | 只做 C# 编译检查（引用表与 deploy-local 一致） |
| `deploy-local.js` | Roslyn 直编 + 部署到本机 `Mods\BusLineAutoStops\`（默认强制重跑 webpack） |
| `dotnet-sdk.js` | **v0.11.2 新增**：解析本机最新 8.x SDK 的 `csc.dll` 与 `NETCore.App.Ref` 引用目录（三条构建链共用，别再写死版本号） |
| `paths.js` | **v0.11.2 新增**：游戏目录 / 部署目录 / 部署白名单四项的唯一真值（deploy-local 与 verify --deployed 读同一处） |
| `pack-release.js` | 打包 `releases/<版本>/` 并做版本/需求/安全自检（144 枚钉子，防发旧产物与死分支） |
| `pull-live.js` | 只读核对 Paradox 线上版本表（写 `research/live-versions.json`） |
| `set-store-copy.js` | 写 `PublishConfiguration.xml` 面向玩家的 4 个字段 + `Frontend/mod.json` |
| `verify.cjs` | 离线门禁：版本四处一致 + T3 +（`--deployed`）部署完整性 |

## 验证台账

**已验证（离线，2026-10-13）**

- `node scripts/compile-check.js` 0 错误；`dotnet build` 不在本版跑（跑完必须 deploy-local 重铺）。
- `node scripts/pack-release.js 0.11.2` → 144 枚钉子全绿（含本版新增 12 枚：P1-1 四枚 / P1-2 两枚 / P2-4 三枚 / P2-5 三枚）。
- `node scripts/verify.cjs` → 版本四处一致 + T3 `pass=42 fail=0`。
- `node scripts/verify.cjs --deployed` → 15 项全绿；守卫有效性做过一次真实反证：源码改动后、重编之前跑，它准确报出"最新的 AssetFilterService.cs 比部署 DLL 新"。
- P2-5 的原型结论有 `file:line` 反编译证据（上面第 5 条，dump 存在 `research/`，不入库）。
- 发布回读（2026-10-13）：`/mods/versions` 登记 versionId=13 / 0.11.2；模组页 `MOD VER.=0.11.2`、`Private mod`、`Code Mod`、`SUGGESTED GAME VER. 1.6.*`、GitHub/X/Ko-fi/论坛四条链接与中英描述逐段仍在 —— NewVersion 重推元数据没有把玩家线上手调过的东西盖掉。

**未验证（要靠玩家本地实机）**

- `[预览结果]` 是否真的开始输出，且每枚预览只一行（本版最该看的一条：日志里该出现 `幽灵已出，离喂点 X.Xm … 落点横向=… 判定=…`）。
- 退出懒人模式（尤其选"不保留"）时不再出现"持句柄改结构"类异常；顺带看 `[退出清理] 顺带撤掉 N 个…` 依旧正常。
- P2-5 换宽后的查重表现：在它旁边叠放应当被拒；`[索引] … 站台标签查询为空 → 改用全物体路径` 这行**不该**出现（出现了说明这台机器的站台连 BusStop 标记都没有）。
- 资产面板过滤在换存档后的第一份名单是否正确（P2-4 的异常路径只做了静态推理）。

**已知限制（定案，不当 bug 追）**

- 禁用/卸载模组要求重启进程：同一场进程内选项页仍留着本模组那一栏（P3-2）。
- 有轨电车置灰"暂未上线"，分类认得电车、懒人模式不启用（见待办 2）。
- 自动站台不写 `Game.Tools.Recent` → 建不扣钱、铲不退款（净零刷不出钱），是玩家定案的设计（校正 #23）。
- 设置页 `master.label` 与商店长描述里仍有"自动放置/Auto-place"字样（功能名与 mod 名如此），本版只按玩家决定改了 `master.desc`；长描述里 3 处"站台"未清理（同样按"商店描述不动"）。
- 第三方资产的 `BusStop` 标记能否脱离连接缓冲单独存在：**未取证**（P2-5 只证明了原版路径）。

## 已知待办（按优先级）

1. **电车落点机制**：`Setting.TramEnabled` 恒 false 且选项置灰 + `TransportKindRules.IsLazyModeEnabled` 两层门——分类认得 Tram（资产识别/过滤/查重），但懒人模式不启用、不可选；要补的是"两站一线"的电车落点，不是把开关打开。
2. **`[预览结果]` 实机对账**（v0.11.2 新增能力）：确认它按静止事件各打一行、`落点横向` 与 `光标横向` 同号，之后这条就可以从待办里划掉。
3. **`m_stopQueryVerified` 的回退路径**：一旦某台机器的站台物体不带 BusStop/TramStop 标记就永久走"每 2s 全量物体扫描"，在 12.8 万 lane 的城里仍可能是周期尖峰；日志 `[站台索引]` 会明说走了哪条，实机若命中再想办法（不提前优化）。
4. 经济侧已定案的偏差：见上面的已知限制，`CommitNow` 注明"以后别顺手补 Recent"。
5. 火车/地铁/机场/码头是车站体系，`TransportKind` 架构预留。

## 文件夹清单（根目录应当只有这些）

入库：`Systems/`、`Frontend/`、`Properties/`、`docs/history/`、`scripts/`、`tests/` 与根上的 12 个 `.cs` + `BusLineAutoStops.csproj` + `README.md` + `ARCHITECTURE.md` + `.gitignore` + `.zcodeignore`。
不入库（构建/调研产物，`.gitignore` 已排）：`bin/`、`obj/`、`Library/`、`releases/`、`research/`（反编译 dump 与 live-versions.json）、`Frontend/node_modules/`、`Frontend/dist/`、`.qoder-credits/`（定期删）。
一次性补丁脚本用完就删（本轮 12 语种 `master.desc` 的替换脚本跑完即删，改完结果已入库）。
