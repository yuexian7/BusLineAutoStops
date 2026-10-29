# BusLine AutoStops 架构速览（v0.14.2）

给下一个接手者的最小集：现行机制、硬不变量、构建命令、待办。
逐版迭代史与机制取证（96 条校正、每版实机日志）在 `docs/history/开发笔记-v0.1.1~v0.11.1.md`（已冻结，只查不改）。
当前版本状态：**v0.14.2 已本地部署、离线门禁全绿（194 条钉子 + 96 条 T3）、已上传线上（versionId=17，0.14.2，仍 Private）**：ModId 160528 / dll 107520B / mjs 41833B / 线上包 61020B。
0.14.2 只修一件事：「退出懒人模式时」下拉框每次重启都回到「保留公交站」；默认值按玩家要求定为「不保留」。
根因是**落盘键名**（不是 UI、也不是 SetDefaults）：老文件里那句旧默认值跟玩家真选过的值长得一样，只有换键才失效 —— 见下"v0.14.2 本轮修订"，那条规律以后每次改默认值都要用。
0.14.1 只改一条选项说明的措辞（12 语言），零代码行为改动；0.14.0 修玩家五条反馈：拖拽中要有预览、关总开关图标不消失、退出策略下拉框每次重启回默认、右键删路径点要连站台一起删、关掉后必须零影响。
其中"设置每次重启回默认"在 0.14.0 修了落盘寻址的两个 bug（类名唯一 + 自己一个 .coc），0.14.2 才修掉最后这一层：**值本身是旧默认写进去的**。
**0.12.0 / 0.12.1 只在本机验证过、从未上传**（线上一直停在 0.11.2），它们的内容（拖拽放站、松手帧闪退修复、总开关与快捷键联动、退出策略默认不保留）合并进 0.13.0 一起上线。

## 机制一页纸：懒人模式状态机

零 Harmony 补丁、零自定义 ECS 组件；放置/校验/吸附全走原版管线。核心在 `Systems/LazyModeSystem.cs`。

1. **静止判定**：线路工具悬停控制点（`RouteToolSystem.GetControlPoints` 的最后一个，公开 API）静止满 **0.18s**（`kSettleSeconds`）→ 触发预览；连续移动满 0.15s（`kPreviewFadeSeconds`）才收，停回 2.5m（`kPreviewKeepRadius`）内沿用同一枚幽灵。
2. **喂幽灵**：喂一枚 `CreationDefinition{m_Prefab=站台资产, m_Attached=解析出的合法父级, m_Flags=Attach|Optional}` + `ObjectDefinition + Updated`，走 `ToolOutputBarrier`（与原版工具同一条通道）；同帧 Modification1（GenerateObjectsSystem）就出 Temp 幽灵，Modification4 吸人行道。`Optional` 让原版校验自动撤销幽灵上的错误，预览永不挡点击。
3. **认领**：喂出后逐帧追、最多 10 帧（`kClaimMaxFrames`）把幽灵认领回来，登记成"**世上只留一枚幽灵**"——它活着就一帧不再喂，被原版 Clear 收走了才补喂；认领那一刻按吸附后的实际落位查重（已有同类站台则不放置，原版会依附到它）。
4. **转正**：玩家**左键**点击，applyMode 由原版应用（模组只读不写），站台转正为真实建筑；线路完成时停靠点自动连到站台、公交进站。转正/依附均由原版管线完成。
5. **撤点回退**：右键撤停靠点 → 同时撤掉该点由本模组建的站台（先撤点后删站，四条防御）；线路完成 → 站台全部保留。退出策略：保留 / 不保留本次会话落地的站台（**线路保存后即永远保留**）。
   v0.14.0 起再加一条（与退出策略无关）：本会话自动放置的站台，**路径点被右键删光时跟着删**；
   判据是站台自己的 `ConnectedRoute`（原版删点时会把那一枚 `RemoveValue` 掉），账在退出懒人模式时清空。

## 四条硬不变量（违反即闪退/毁世界，取证都在笔记 #63/#88/#89/#90）

| # | 不变量 | 原因（一句话） |
|---|--------|----------------|
| 63 | **一个系统只注册一次** `UpdateBefore<ToolOutputSystem>`（单槽），绝不 `UpdateAt`+`UpdateBefore` 叠用 | `UpdateSystem` 的 `m_Systems` 与 `m_RefMap` 是两张互不相干的表，`Refresh()` 无去重，叠用 = `OnUpdate` 一帧跑三次 → 2026-10-03 闪退 |
| 88 | `RouteToolSystem.GetControlPoints` 返回的是**工具自己的持久缓冲**（NativeList 视图）→ **共用缓冲绝不 Dispose**；撤点只能 `state==Create` 时从 `Length-2` 往前 `RemoveAt`，且守住四条防御（不动悬停位 / 遇非本会话依附就停 / 剩 3 点收手） | `Cancel()` 是私有的，别的系统（GuideLinesSystem）也是直接拿这份缓冲进 job |
| 89 | **`Temp.m_Original == Entity.Null` 才是本模组造的幽灵**，`!= Null` 是原版的复制/选中副本（原件正被 `Hidden` 藏着）→ 判己防误删，删副本 = 玩家看到"站台消失" | 副本要等 `SubObjectHiddenSystem` 确认没有活 Temp 指回原件才解除隐藏 |
| 90 | **落点定侧只由喂点的有符号横向偏移决定**（喂点用射线真实命中点，不用控制点位置） | `SnapRouteLane` 用 `dot(MathUtils.Right(切向), 喂点-曲线点)` 选车道，`SnapSegmentAreas` 再用它选可建造区；喂错点 = 落到对面人行道 |

## v0.14.2 本轮修订（2026-10-29，一个下拉框 + 一条落盘规律）

玩家原话："切换懒人模式的快捷键还是会被重置为保留（默认改成不保留吧），你再修复一下"。
（玩家把设置页那一栏说成"快捷键"，指的就是「退出懒人模式时」那条下拉框；`ToggleLazyBinding` 本身一直是好的，
`FallbackSettings.coc` 里 `"ToggleLazyBinding":{"m_Path":"<Keyboard>/g"}` 读得回来。）

**现象**：每次重启游戏，那条下拉框都显示「保留公交站」，哪怕上一场改成了「不保留」。
**不是** UI 渲染问题（0.14.0 已经证明推送链路通）、**也不是** `SetDefaults` 没改（代码里默认早就是
`DeleteSessionStops`）。真根因是**落盘里那个键还带着旧默认值**：

1. 设置文件存的是**属性名**：`"ExitPolicy":"KeepStops"`。
2. 而 `KeepStops` 正是 0.5.0–0.11.x 的**默认值**（0.12.0 才把默认改成不保留）。默认值也会被写进文件
   （这条属性带 `[SettingsUIForceSave]`，永远写），于是**老玩家的文件里几乎都躺着一个 KeepStops**。
3. 读回时 `AssetDatabase.LoadSettings` → `JSON.WriteInto`：键在就写进设置对象，**键在就不走默认值**
   （`Setting.SetDefaults` 只在 `SharedSettings.Reset()` / `SetDefaultsWithoutApplying()` 时被调，
   模组设置载入时根本不会调）。于是新默认永远轮不到执行，玩家看到的永远是「保留」。
4. 致命之处在于**无法区分**：由旧默认写进去的 `KeepStops`，和玩家真的点选过「保留」，在文件里一模一样。

**修法：换持久化键名**（`ExitPolicy` → `StopPolicy`，语义与字段 `m_ExitPolicy` 一律不动）。
依据是解码器对未知键的处理 —— `JSON.TryGetDecodableMember`（`research/decompiled/JSON.cs:655-683`）返回 false 时，
`DecodeObject` 的循环（同文件 `:624-637`）**静默跳过**：不抛异常、不影响别的键、更不会把整份片段丢掉。
于是老文件里的 `ExitPolicy` 变成未知键被忽略，值回落到字段初值 `TransportKindRules.DefaultExitPolicy()`
（= 不保留）；下一次保存时 `DiffWithPersistent` 按属性名回填老键，找不到同名属性，**旧键顺手被丢掉、不会复活**。

配套三处必须同时改，缺一个就出事：

| 位置 | 改动 | 不改的后果 |
|------|------|------------|
| `Setting.cs` | 属性 `ExitPolicy` → `StopPolicy` | 老值继续压着默认值，玩家重启后还是「保留」 |
| `LocaleTable.cs` | `nameof(...ExitPolicy)` → `nameof(...StopPolicy)`（label + desc 两条） | 设置页这一条**没有标题和说明**（key 是 `nameof` 现算的） |
| `Setting.cs` setter | 加一行 `[选项] 退出懒人模式时 = …` | 下次实机出问题分不清"值没进设置对象"还是"没写下去" |

**由此定下一条以后每次改默认值都要遵守的规律**（已写进 `Setting.cs` 类注释第 3 条）：
**改默认值 = 改默认值 + 换落盘键名**。只改 `SetDefaults` 对老玩家完全无效，因为键一直在。

顺带核实（同一族问题，本版不动）：`[FileLocation("BusLineAutoStops")]` 只决定**新建**片段落在哪个文件
（`AssetDatabase.CreateSettingsAsset`）；老玩家数据库里已有同名片段时，`LoadSettings` 直接复用那一份、
`ApplyAndSave` 也跟着写回它原来所在的文件（`SaveSpecificSetting` → `GetTargetSetting` 按片段找 `fragment.source`）。
所以老玩家的值仍住在 `FallbackSettings.coc` —— 0.14.0 那条注释里"从此写入落在自己的文件里"的说法**只对新装成立**，已改正。

## v0.14.1 本轮修订（2026-10-26，纯文案）

玩家要求（原话）："ExitPolicy 说明里『已经被线路路径点用上的站点永远保留』改成『线路保存后即永远保留』，后面那句『线路没画完……』删掉。"

- 改动面只有一处语义：`LocaleTable.cs` 的 `policy.desc`（12 语言）里那句"永远保留"的判据，
  从"被路径点用上"改成直接给结论"**线路保存后即永远保留**"，并整句删掉随后的"线路没画完就取消是原版行为…"。
- 为什么删得掉：那句话解释的是原版行为，与这个选项无关；而它前面那句已经说清了范围（只管本会话自动放置的站台），
  后面那条「注意……」段也说清了右键连删规则 —— 留着它只是让玩家在设置页多读一行无关信息。
- 语义没变，代码没动：`DeleteIfSafe`（`Systems/LazyModeSystem.cs:3141`）的判据仍是"站台自己的 `ConnectedRoute` 里
  还有活着路径点就不删"，也就是"线路保存后它的路径点活着 → 永远保留"。所以这条是**文案改口，不是行为改口**。
- 同一句论断还出现在两处玩家可见文案里，一并改口免得一处新一处旧：商店长描述（`scripts/set-store-copy.js`
  的 `enLong`/`zhLong`，中英各一句）、`README.md` 功能列表。
- 新增钉子一枚（`scripts/pack-release.js`）：12 语言逐个验"新说法必须在、旧说法必须没"，
  防止以后有人只改一种语言、或把旧句子又粘回去。

## v0.14.0 本轮修订（2026-10-21/22，玩家五条反馈）

玩家原话：①"拖拽前不要模拟放置公交站，但是开始拖拽后（鼠标左键一直按着了）还是要出现预览的啊"；②"关闭总开关，左上角的图标按钮不会消失"；③"退出懒人模式是否保留公交站的下拉框每次重启游戏都会变成默认设置"；④"在修改已有线路时，该次懒人模式下自动放置的公交站，会随右键删除路径点而被删除……退出懒人模式后就不再能被一起删除了，这个规则不受本选项改变"；⑤"关闭总开关或卸载模组后对存档完全没影响，必须能完全恢复原版行为。性能、帧数都不要有影响"。

### 1) 拖拽中必须预览（0.13.0 拦过头）

0.13.0 的 `SuppressPreview` 把 `Modify` 也拦了。查原版：`State.Modify` **只可能**从
`Apply()` 的 Default 分支进入，而那条分支头上的 `GetAllowApply()`（`research/ToolBaseSystem.cs:541-543`）
就要求"这一帧左键按下了" —— 所以 **Modify 态 == 左键按住、正在拖拽**，正是玩家要预览的状态。
现在闸门是三档：`Remove`（左键在确认删除）／`Default` 压在既有路径点上（**按下之前**）／
既有站台依附范围内（交原版直接依附）；`Modify` 明确放行。

顺带补上 0.13.0 没暴露的一个组合：放行拖拽后，"松手帧手上还攥着一枚幽灵、同帧又在喂新定义"真的会发生，
而 `ApplyObjectsSystem.Create()` 对 `Temp`（`m_Original == Null`）是**不过校验、不计费**直接转正的（笔记 #71）。
所以加 `bool previewOkNow = previewOk && phase != kPhaseApply;`：Apply 帧只关闸、不喂、不收。

### 2) + 3) 设置每次重启回默认 —— 落盘寻址的两个真 bug（不是 UI）

- **类名冲突**：`Setting.ApplyAndSave()`（`research/decompiled/Game.Settings/Setting.cs:151-155`）写的是
  `SaveSpecificSetting(GetType().Name)`，而 `AssetDatabase.SaveSpecificSetting → GetTargetSetting`
  （`/tmp/ad.cs:820-840`）**只按 `fragment.source.GetType().Name` 找目标**，命中第一个就 `break`。
  本类原来就叫 `Setting`，本机同时装着 `AccessAnarchy.Setting`（`ilspycmd -l c AccessAnarchy.dll` 实测），
  两个 `GetType().Name` 都是 `"Setting"` → 我们的保存请求可能整个落到别人的 `.coc` 上
  （随后 `GetSettingsInSameFile` 只写那一个文件里的片段），自己的值一次都写不进去。
  **实机取证**：玩家 2026-10-21 23:04 连按四次总开关，`FallbackSettings.coc` 的 mtime 仍停在
  2026-10-20 05:15 —— 一行都没写进去。
  → 类改成 `BusLineAutoStopsSetting`。`id` 不受影响（`ModSetting.id` 用的是 **Mod 类**），
  `.coc` 片段名不受影响（那是 `LoadSettings(nameof(BusLineAutoStops), …)` 传的字符串）；
  只有 `name` 变了，而它只进 `Options.OPTION[...]` / `OPTION_DESCRIPTION[...]` 两条 key，
  LocaleTable 一直用 `setting.GetOptionLabelLocaleID(...)` 现算，两边同步。
- **公共文件**：没有 `[FileLocation]` 时框架把片段塞进 `FallbackSettings.coc`（`LoadSettings` 里
  `assetName` 的兜底值），与几十个模组共用一个文件 —— 别人任何一次保存都会整份重写。
  → 加 `[FileLocation("BusLineAutoStops")]`。老玩家已存在的片段仍留在 FallbackSettings.coc，
  `LoadSettings` 是按**片段名**找的，读得到、不会丢。
- 顺带：`SetDefaults()` 以前除了写字段还顺手写了四条 `LazyModeState.*`，与 `SyncToState()` 两处各写一遍；
  现在 `SetDefaults()` 只决定字段，搬到运行时状态**只有 `SyncToState()` 一条路**。
  总开关补 `[SettingsUIForceSave]`（关掉的状态必须写得下去）。
- 分诊：启动时 `[设置] 载入后 启用模组=… 退出策略=… 键位=… 文件=…` 一行；
  前端控制台每次总开关变化 `master on/off → 显示图标/隐藏图标` 一行。
  0.13.0 的日志只在 `enabled` 变化时打，master 的变化完全看不见 —— 这条反馈当时无法分诊。

### 4) 右键删路径点 → 本会话自动放置的站台跟着删（新规则）

不抓右键事件，也不碰原版输入：判据用**站台自己的 `ConnectedRoute` 缓冲**。
原版删路径点走 `State.Remove` → `ApplyRoutesSystem` 删实体 → `WaypointConnectionSystem` 的
Deleted 分支把那一枚从站台的缓冲里 `RemoveValue` 掉 —— 也就是"曾经连着、现在一枚活的都不剩"
本身就是原版留下的证据。

- 新增会话账 `m_sessionStops`（`{Stop, HadWaypoint}`）：点击落地与拖拽落地**都记**
  （`m_placed` 是撤销栈，按 v0.12.0 约定不收拖拽那一笔，两者生命周期不同）。
- 判据 `PlacementDecisions.SessionStopLostWaypoint(hadWaypoint, liveWaypoints)`：
  `hadWaypoint && liveWaypoints <= 0`。`hadWaypoint` 防"刚落地、线路还没画完"被误判。
- `MaintainSessionStops()`：账空即早退；站台没了就出账；`live > 0` 记 `HadWaypoint`；
  判定成立时 `FlushPendingJobs()` → `DeleteIfSafe`（还挂着别的活路径点就**不删**）→ 出账 + 从 `m_placed` 摘掉。
  **非 Apply 帧**才跑（`if (phase != kPhaseApply) MaintainSessionStops();`），同帧互斥结构改动=空托管栈崩溃。
- 清账时机就是需求边界：`RunExitCleanup`（退出懒人模式）与 `BeginSession`（新会话）。
  清掉之后这些站台**彻底放置好**，再进懒人模式也删不到；账里只有我们自己的实体，玩家手摆的永远不碰。
- 设置页说明（12 语言）补上这句规则，并要求"不受保留/删除选项改变"。

### 5) 关掉 / 卸载后零影响

- `LazyModeSystem.OnUpdate` 第一行（`Broken` 之后）：`!Enabled && !PendingExitCleanup && !m_beginCleanupPending → return`。
  稳态下三个 bool 读 + 返回；两条收尾队列（关总开关排的退出清理、进会话排的残留回收）不被跳过。
- 仍然是**零 Harmony、零自定义组件类型**：对路径点写的 `Connected`/`VehicleTiming`/`Updated` 与站台
  `ConnectedRoute` 全是原版类型、原版语义（原版自己也这么写），卸载后存档就是一份正常存档。

## v0.13.0 本轮修订（2026-10-21，玩家三条反馈）

玩家原话：①"拖拽线路，拖拽前不需要预览显示公交站啊，我看你每次都是鼠标移到路径点上还没左键按下去拖拽就已经开始模拟生成一个公交站了"；②"拖拽结束后这次倒是生成公交站了，但是这个拖拽的路径点没有依附到这个公交站"；③"如果鼠标位置已经进入公交站依附点范围的话，就不要预览模拟放置了，按游戏原版行为就会被直接依附到公交站上去"。

三条都落在**预览闸门**与**拖拽落地后的收尾**上。判据全部照抄原版自己的决定，不新发明：

1. **需求 1：拖拽前/拖拽中不预览**。判据收敛在 `PlacementDecisions.SuppressPreview(state, hoverOnRouteElement, hoverOnExistingStop)`：
   - `state == Modify / Remove`：工具已经在拖拽 / 删点，左键的含义是"改线路"（`RouteToolSystem.cs:1908-1920` 进 Modify、`:2025-2052` 松手提交）；
   - `state == Default` 且光标压在既有线路的路径点/路段上 —— 就是原版 `Apply()` 的 Default 分支进 Modify 的那条判据
     `HasComponent<Route>(cp.m_OriginalEntity) && math.any(cp.m_ElementIndex >= 0)`（`research/RouteToolSystem.cs:1908-1912`）。
     `HoverOnRouteElement` 逐字对齐它，另外把带 `Temp` 的"正在画的那条线"排除（那是在加路径点，不是拖拽）；
   - `Create` 态不抑制：画线途中吸到线路上很常见，那时左键是加路径点。
   抑制期间幽灵要**当场收干净**（Apply 帧除外，理由见 v0.12.1 的崩溃取证）：一枚还带着 `Game.Tools.Error` 的幽灵会让
   `ToolBaseSystem.GetAllowApply()`（`research/ToolBaseSystem.cs:541-543`，查的是**全世界**的 Error）整帧为假 ——
   玩家那一笔会被原版悄悄吞掉。抑制期间另做一次**只解析、不显示**的记录（`NoteLandingHint` → `m_previewParent/m_previewSpot`），
   所以拖拽落站的首选线索（"沿用玩家眼前那枚预览的父级"）不会因为"不显示预览"而断掉。

2. **需求 2：拖拽落地的路径点接上新站台**。拖拽走 Modify 态，松手那一帧原版就把线路提交完了
   （`ApplyRoutesSystem` 把 `WaypointDefinition` 写回真实路径点），路径点是在**站台出现之前**造出来的，
   它的 `Connected` 只能是空 —— 于是"站台有了、公交不靠站"。左键画线那条路能靠改写控制点让定义带连接（笔记 #71），
   拖拽**没有控制点可改**，只能按原版自己的数据形状事后补（`AttachDraggedWaypoint`）：
   - 拖拽期间记下"哪条线、哪一枚路径点"：`TryCaptureDragRoute` 从 Modify 态的悬停点取 `m_OriginalEntity`（Route 实体）
     + `m_ElementIndex.x`（路径点下标，`RaycastJobs` 的 `m_CellIndex`）；悬停点被 `SnapJob` 改写时用 `RaycastOwner()` 兜底。
     下标只是首选，落点 `kDragAttachRadius`（12m）内最近的一枚路径点兜底（拖路段会插新点）；
   - 写三处、都是原版自己会写的那种：路径点 `Connected`（+ `VehicleTiming`，一次 `ComponentTypeSet` 补齐，对应
     `RoutePrefab.LateInitialize` 的 hashSet3 与 `TransportLinePrefab.GetArchetypeComponents`）并补 `Updated`；
     站台 `ConnectedRoute` 缓冲去重追加（`WaypointConnectionSystem` 只在 Created 那一帧 Add，事后再加它不管）。
     之后 `WaypointConnectionSystem.FindWaypointConnectionsJob` 照 `Connected` 重算 AccessLane/RouteLane、
     把路径点搬到站台位置、发 `PathTargetMoved` 并重算路段路径（`WaypointConnectionSystem.cs:1170-1410`）——
     车道与寻路全是原版自己做的，我们不接管；
   - 闸门在 `PlacementDecisions.ShouldAttachDraggedWaypoint`：找不到 / 已经连着这枚站台（幂等）/ 已连着别的活实体（不抢）都不写；
   - **这是本模组唯一一处写"别人的实体"**（线路的路径点）。约束写进代码注释：只碰本笔拖拽那条线上、落点 12m 内的**一枚**
     路径点，只加原版 archetype 里本来就有的组件，`FlushPendingJobs()` 之后才动结构，全程 try/catch + 一行 `[拖拽依附]` 日志。

3. **需求 3：站台依附范围内不模拟**。判据 `IsAttachStopHover`：① 权威 —— 工具解析出来的实体本身是同类站台
   （`FindWaypointLocation` 的 ConnectedRoute 分支，`RouteToolSystem.cs:277-280`，命中即"这一笔路径点会直接依附它"）；
   ② 兜底 —— 光标 `kStopAttachRadius`（4m）内有同类站台。三处共用：预览闸门、`TryResolveLanding`（预览/左键/拖拽补提交共用）、
   `TryLandDragStop`（松手处就在站台旁边 → 交原版依附，不新建）。
   实机 2026-10-21 01:08:18 的失败正是 **3.07m**：旧的 3m 判据差一点点没拦住，喂出去的模拟站台在偏移位置跟物件碰撞被挂
   `Game.Tools.Error`，玩家的那一笔就被 `GetAllowApply()` 顶掉（"在这个站台放路径点会失败"）。
   另外 `RetireTemp` 现在**先摘 Error 再加 Deleted**（`ClearGhostError`）：带 Deleted 的实体要到帧末 Cleanup 才销毁，
   中间那几帧的 Error 照样挡玩家。

4. **附带（需求 2 的稳定性）**：松手后"光标跑开"改成**连续 3 帧**才算数（`kDragAwayGraceFrames` + `PlacementDecisions.DragCursorLeft`）。
   松手帧原版先 Clear 控制点、再重新射线补一个（`RouteToolSystem.cs:2039-2048`），采样点会跳 1~2 帧；实机 2026-10-21 01:08:28
   就是被这一跳骗掉一整笔已经解析成功的拖拽（松手后 28ms 报"光标已离开落点"）。

5. **版本口径**：0.12.0 / 0.12.1 从未上传，线上一直是 0.11.2；所以商店 ChangeLog 从 0.11.2 直接接到 0.13.0，
   `scripts/set-store-copy.js` 里补的是 0.13.0 一段（把 0.12.x 的内容一并写进去），旧的 0.11.2 及更早段落原样保留。

## v0.12.1 本轮修订（2026-10-20 夜，修 0.12.0 的拖拽闪退）

玩家报："拖拽放站这个发生闪退了，拖拽结束的一瞬间就闪退了"。实机日志把病灶钉死了：

```
22:07:33,994 [线路工具] 态 Modify → Default 相位=1 本帧判为拖拽提交=True
22:07:33,997 [拖拽落站] @(-560.1,70.5,1008.9) 资产=Pack7-BusStop01 ← 沿用玩家眼前那枚预览的父级 425913 … QuayLarge01
（日志到此为止）
```
Player.log 收尾是 `Native Crash Reporting / Got a UNKNOWN while executing native code` + **空托管栈** ——
本项目里"空托管栈原生崩溃"的同义词就是"持有句柄期间改结构 / 同帧互斥结构改动"（笔记 #63/#89）。
落站逻辑本身已经走通（站台、父级都解析出来了），崩在**喂定义那一帧**：

1. 松手帧 `applyMode == Apply`。我们在这一帧 `RetirePreview()`（对我们的预览幽灵 `AddComponent<Deleted>`），
   紧接着 `FeedStopDefinition`（同 prefab、同位置 —— 因为沿用的就是预览的父级与落点）。
2. 同一帧 `GenerateObjectsSystem.CollectCreationDataJob` 的 `m_DeletedQuery`
   （`Object + Deleted + Temp + PrefabRef`，:1734）**正是拿"带 Deleted 的幽灵"当可复用的旧物件**，
   按 `{Prefab, SubPrefab, Original, Owner}` + 最近位置配对（:745-783），对我们的新定义走
   `CreateObject(..., oldEntity)` → 对**同一个实体** `RemoveComponent<Deleted>`（:1103-1130）。
3. 于是同一帧里两条路径对同一实体做**互斥的组件增删**（我们删、它复活）→ 原生崩溃。

修法（两拍，见 `ArmDragLanding` / `TryLandDragStop`）：

- **松手帧只记账**：`ArmDragLanding` 把落点、预览父级、站台资产存进托管字段，**只补 `TempFlags.Cancel`**
  （`SetComponentData`，非结构改动，挡住 Apply 帧的免费转正），然后 return ——
  不建实体、不改结构、不 `Complete` 依赖。
- **第一个非 Apply 帧才落地**：`TryLandDragStop` 走 `FlushPendingJobs() → RetirePreview() → FeedDragCommit()`，
  与"预览喂定义"完全同构（预览从来没崩过）；代价只有一帧（约 16ms）。
  判据收敛在 `PlacementDecisions.DecideDragRetry(..., toolApplies, ...)`：**Apply 帧一律 Wait**，
  且这条 Wait 排在 `landingResolved → Commit` **之前**（顺序不能换）。

顺带按玩家意见收敛默认值：`ExitPolicy` 的**唯一决策点只有 `Setting.SetDefaults`**；
`LazyModeState.ExitPolicy` / `PendingExitPolicy` 是热路径镜像与排队中间量（正常流程一定被覆盖），
`ResetForNewWorld` 里那行 `PendingExitPolicy = …` 是死赋值（上一行已把 `PendingExitCleanup` 清掉），已删除。
⚠️ v0.14.2 补一条：`SetDefaults` **只对"文件里没有这个键"的人生效**。老玩家的文件里有键（旧默认写进去的），
所以那一次改默认值其实一个人都没生效 —— 详见上面 v0.14.2 那节；改默认值必须连键名一起换。

## v0.12.0 本轮修订（2026-10-20，玩家三条需求）

玩家原话三件事：①"快捷键失效了，每次开启或关闭总开关后就没用了"；②"默认改成不保留公交站"；③"拖拽路线不能自动放置公交站的问题还是没解决"。

1. **需求 1：总开关与快捷键的双向联动**。根因（实机日志 2026-10-20 05:53 四行 `[总开关]` 只有关的两次后面跟着 `[快捷键] 跟随总开关 shouldBeEnabled=False`，开的两次一条都没有）：`ApplyMasterToHotkey()` 只在**关**那条路径上调过，打开时没有任何一处把 `shouldBeEnabled` 设回 true —— action 一旦停用就再没接上，玩家看到"用一次就坏"。实现：setter 里 `if (v) TurnOnHotkey(); else TurnOffIfLazy();` 两个方向都走，外加每帧自愈闸门 `KeepHotkeyInSync()`（在 `UiCommandSystem.OnUpdate` 调）。自愈的理由：`ProxyAction.shouldBeEnabled` 的 getter 读的是 `m_DefaultActivator.enabled`（`research/ProxyAction.cs:322-347`），那是 InputManager 与原版工具 `SetAction` 也会改写的共享状态，setter 只管得到我们自己这一侧。
2. **需求 1：总开关改叫「启用模组」且快捷键开不了它**。总开关管的是模组本身：关掉时左上角图标不出现（前端 `master !== false` 才渲染）、世界侧系统一行不写（`LazyModeState.Enabled` 早退）；开启后懒人模式才能用图标或快捷键进出。快捷键回调第一件事就是 `if (!LazyModeState.Enabled) return;` —— 永远不给"快捷键把模组打开"留缝。12 语种 `master.label`/`master.desc` 全改。
3. **需求 2：退出策略默认改成不保留**。**决策点只有 `Setting.SetDefaults` 一处**（玩家指出的：改那一个值就够）。
   其余引用是镜像与兜底，全部指向同一个常量 `TransportKindRules.DefaultExitPolicy()`：`Setting.m_ExitPolicy`
   字段初值（设置文件缺失/读取失败时兜底）、`LazyModeState.ExitPolicy`（热路径镜像，正常由 `SyncToState` 覆盖）、
   `PendingExitPolicy`（排队中间量，`ExitLazyMode` 一定先写它再置 `PendingExitCleanup`）。
   老存档里明确选过保留的仍是保留（值读回来就是 0）。
4. **需求 3：拖拽落站（真 bug，三轮没修好的那个）**。实机日志 2026-10-20 05:48-05:53 全场 **0 条 `[拖拽落站]`**，而那场会话里懒人模式 05:52:57 就因为"离开线路工具"自动退出了 —— 拖拽根本不是模组在处理，这是第一层。真正写进代码的病因有三条：
   - **自己删掉了唯一的线索**：`CommitDraggedStop` 开头就 `RetirePreview()`，而它会把 `m_previewParent`/`m_previewSpot`/`m_hasPreviewSpot` 一起清空。预览能显示，就说明上一帧我们在同一处**已经成功解析出过一个合法父级**（`FeedPreview` 里解析失败是不会喂的）—— 那个父级就是玩家脚下这条路的权威答案。松手帧的射线却常常正压在另一枚站台或线路飘带上（2026-10-19 23:25:30 取证：工具父级=射线父级=`预制体=Pack7-BusStop01`，全链一个可依附的都没有），怎么解析都是 Null。修法：**先存后清**，`seenParent`/`seenSpot`/`seenUsable` 取出来再收幽灵，并**优先沿用**（松手帧 ①、补提交帧都先认它）。
   - **松手帧没有悬停点就整笔丢弃**：原版 `Apply()` 的 State.Modify 分支（`research/RouteToolSystem.cs:2039-2047`）是"先 Clear 控制点、再重新射线补一个"，射线没打中时控制点是空的。0.11.2 的顺序让 `if (!TryCurrentPoint)` 在拖拽提交之前 return，这一笔连日志都不会留。修法：提交块整体上移到 `TryCurrentPoint` 早退**之前**，没有悬停点就用**上一帧记下的光标**（新增 `m_lastCursor`，每帧赋值；不能复用 `m_lastPos` —— 那个是静止判定的锚点，只在光标**动了**时才更新）。
   - **诊断缺位，分不清"没触发"与"没开"**：新增进出 Modify 态各一行日志（每笔拖拽正好两条），下一轮实机只要看到 Modify 行而没有 `[拖拽落站]`，就说明检测没触发；两条都没有而玩家确实拖了，说明懒人模式当时没开。
5. 拖拽落地的站台**不进 `m_placed`**（不可右键回退，与原版一致）—— 这条 0.11.2 定案、本版保留。
   "不做依附改写"这一半在 **v0.13.0 已改**：改写控制点（左键那条路）确实不能做，但落地后必须把那一枚已经造出来的
   路径点接到新站台上，否则公交不靠站；见上面 v0.13.0 的第 2 条。

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
node scripts/pack-release.js <版本>   # 打包 releases/<版本>/（DLL+mjs+mod.json+images）并自检，发布打包的事实标准（本版 194 枚钉子）
```

官方工具链 `dotnet build` 已修复可用（0 警告 0 错误；csproj 已补 `Colossal.UI.Binding` 引用、删死 Harmony 配置、`math.isfinite` 替代 net48 没有的 `float.IsFinite`），适合 IDE/编译校验；**发布打包仍以 `scripts/pack-release.js` 为准**。
⚠️ **但 `dotnet build` 会走官方 DeployWIP 重铺部署目录**：整个 `Mods\BusLineAutoStops\` 被清掉后只铺回 DLL+Burst 桩，deploy-local 铺的前端（mjs/mod.json/images）全没了 → 实机表现"左上角图标消失、面板选资产拦截失效"（2026-10-12 事故根因，后端仍活着，日志易误判）。**跑过 `dotnet build` 之后必须立刻 `node scripts/deploy-local.js` 重铺完整产物。**

离线门禁：

```powershell
node scripts/verify.cjs                 # 版本四处一致 + T3 纯逻辑 96 断言
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
**改默认值另有一条**（v0.14.2 定案）：默认值 + 落盘键名要一起改，否则老玩家永远看不到新默认（见 v0.14.2 那节）。

## 发布序列（本模组）

```powershell
node scripts/pull-live.js            # 只读：线上版本表停在哪一版
node scripts/pack-release.js 0.13.0  # 全量自检，红一条就别发
node scripts/verify.cjs --deployed   # 部署完整性
MSYS_NO_PATHCONV=1 DOTNET_ROLL_FORWARD=Major dotnet "<游戏>\Cities2_Data\Content/Game/.ModdingToolchain/ModPublisher/ModPublisher.dll" NewVersion Properties/PublishConfiguration.xml --contentFolder releases/0.13.0 --verbose
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
| `pack-release.js` | 打包 `releases/<版本>/` 并做版本/需求/安全自检（194 枚钉子，防发旧产物与死分支） |
| `pull-live.js` | 只读核对 Paradox 线上版本表（写 `research/live-versions.json`） |
| `set-store-copy.js` | 写 `PublishConfiguration.xml` 面向玩家的 4 个字段 + `Frontend/mod.json` |
| `verify.cjs` | 离线门禁：版本四处一致 + T3 +（`--deployed`）部署完整性 |

## 验证台账

**已验证（离线，2026-10-29 v0.14.2）**

- `node scripts/compile-check.js` 0 错误（13 源文件 → 107520B；与 0.14.1 同尺寸 —— `ExitPolicy` 与 `StopPolicy` 恰好同长 10 字符）。
- `node scripts/pack-release.js 0.14.2` 全绿：**194 枚钉子**（本版新增五枚：属性改名且旧名不许回来 / ForceSave + 置灰两条属性还在 /
  setter 仍是归一 + 写运行时状态 + 打日志 / `LocaleTable` 两条 key 跟着 `nameof(StopPolicy)` 且旧 nameof 不许留 /
  默认值仍只有一个来源）。
- `node scripts/verify.cjs` → 版本四处一致 + T3 `pass=96 fail=0`（本版零纯逻辑改动，断言一条没动）。
- `node scripts/verify.cjs --deployed` 全绿（dll 107520B / mjs 41833B / mod.json 0.14.2 / 两张图标）。
- `releases/0.14.2/` 快照已生成（dll 107520B / mjs 41833B / mod.json / images）。
- **发布已成功**：`NewVersion` 回话 `New mod version published` + `Access Control Level State: Private`；
  `pull-live.js` 回读 `versionId=17 userVersion=0.14.2 size=61020`（created 2026-10-29T09:12:11Z）。
- 本版 ChangeLog 982 字符（中英合计，远低于发布器 5000 上限）。

**已验证（离线，2026-10-26 v0.14.1）**

- `node scripts/compile-check.js` 0 错误（13 源文件 → 107520B；比 0.14.0 小 2560B，就是 12 语言被删掉的那些句子）。
- `node scripts/pack-release.js 0.14.1` 全绿：**189 枚钉子**（本版新增一枚：12 语言新文案在、旧句子没）。
- `node scripts/verify.cjs` → 版本五处一致 + T3 `pass=96 fail=0`（T3 只测纯逻辑，文案改动不影响断言）。
- `node scripts/verify.cjs --deployed` 全绿（dll 107520B / mjs 41833B / mod.json 0.14.1 / 两张图标）。
- `releases/0.14.1/` 快照已生成（dll 107520B / mjs 41833B / mod.json / images）。
- **发布已成功**：`NewVersion` 回话 `New mod version published` + `Access Control Level State: Private`；
  `pull-live.js` 回读 `versionId=16 userVersion=0.14.1 size=61017`（created 2026-10-25T17:23:44Z）。

**已验证（离线，2026-10-22 v0.14.0）**

- `node scripts/compile-check.js` 0 错误（13 源文件 → 110080B）。
- `node scripts/pack-release.js 0.14.0` 全绿：**188 枚钉子**（本版新增十四枚：拖拽预览+Apply 帧不喂 / 会话站台账 /
  删除判据与顺序 / 清账时机 / 总开关早退 / 设置类名唯一 / 自己一个 .coc / 总开关 ForceSave /
  SetDefaults→SyncToState 单路径 / 载入分诊日志 / 12 语言说明文案）。
- `node scripts/verify.cjs` → 版本四处一致 + T3 `pass=96 fail=0`（新增 6 条：`SuppressPreview` 的 Modify 放行 3 条、
  `SessionStopLostWaypoint` 4 条；同时把 0.13.0 那三条"Modify 不预览"的断言按新行为改写）。
- 版本号五处统一（`kVersion` / `Setting.kVersion` / `Frontend/mod.json` / `PublishConfiguration.xml` / mjs 横幅）。
- `releases/0.14.0/` 快照（dll 110080B / mjs 41833B / mod.json / 两张图标）；`--deployed` 全绿。
- **发布已成功**：`NewVersion` → `pull-live.js` 回读 `versionId=15 userVersion=0.14.0 size=61759`，
  ModPublisher 侧 `Access Control Level State: Private`。
- 发布器上限实测仍然只有两条（ChangeLog ≤ 5000、ShortDescription ≤ 200）；本版 ChangeLog 3084 字符。

**已验证（离线，2026-10-21 v0.13.0）**

- `node scripts/compile-check.js` 0 错误（13 源文件 → 102400B）。
- `node scripts/pack-release.js 0.13.0` 全绿：176 枚钉子（含 v0.13.0 新增十枚：预览闸门三条 / 依附范围两条 / 拖拽依附三条 / 松手宽限一条 / ChangeLog 上限一条）。
- `node scripts/verify.cjs` → 版本四处一致 + T3 `pass=90 fail=0`（新增 17 条：`SuppressPreview` 8 / `ShouldAttachDraggedWaypoint` 4 / `DragCursorLeft` 5）。
- 版本号五处统一：`BusLineAutoStopsMod.kVersion` / `Setting.kVersion` / `Frontend/mod.json` / `PublishConfiguration.xml` 的 ModVersion + mjs 横幅（pack 钉 `mjs 横幅版本一致`）。
- `releases/0.13.0/` 已生成本版快照（dll / mjs / mod.json / 两张图标）。
- 部署目录四件套与版本已核对（`--deployed`）：dll 102400B / mjs / mod.json(0.13.0) / 图标齐全；日志仍走 `ModsData`。
- **发布已成功**：`NewVersion` → `pull-live.js` 回读 `versionId=14 userVersion=0.13.0 size=59078`（created 2026-10-20T19:11:21Z），
  ModPublisher 侧 `Access Control Level State: Private`，四条外链与 Tag/GameVersion 原样。

**本轮两处工具链修正（都是发布当场被咬出来的）**

- `verify.cjs` 的"最新前端源码比部署 mjs 新"原来把 `Frontend/mod.json` 也算进**编译输入**，可它是
  `set-store-copy.js` 写、deploy 原样拷的元数据 —— 于是"改版本号/商店文案 → 重跑 set-store-copy"必然让门禁变红
  （第一批 0.13.0 出包时就是这样，而 webpack 的 compareBeforeEmit 又不会重写内容相同的 dist mjs）。
  现在只算 `Frontend/src`；mod.json 的一致性仍由"`部署 mod.json = Frontend 源`"那条内容判据管。
- `set-store-copy.js` 的 `assertSafe` 补上 **ChangeLog ≤ 5000 字符**：发布器实测
  `changelogEntry: Must be a string between 1 and 5000 length`，而且是在**传完文件之后**才报。
  为了压进 5000，商店 ChangeLog 只保留 v0.13.0 与 v0.11.2 两段（v0.11.0 / v0.11.1 段落从线上文案里退场，
  源码与 git 历史里仍在）。

**未验证（要靠玩家本地实机）**

- **v0.14.0 需求 1 拖拽中要有预览**：在路径点上按住左键开始拖拽后，预览站台应当**跟着光标出现**；
  松手仍能落地（Apply 帧不喂定义，只晚一帧）。日志看 `[拖拽落站]`。
- **v0.14.0 需求 2 总开关**：设置页关掉「启用模组」→ 左上角图标立刻消失；
  重启游戏后仍然是关的。分诊：模组日志 `[设置] 载入后 启用模组=False`，且 UI.log 出现
  `master off → 隐藏图标`。若后者不出现 ⇒ 前端没收到推送（C# 侧 JSON 里有 master=false）。
- **v0.14.2 下拉框**：升级后第一次进设置页，「退出懒人模式时」应当显示**「不保留公交站」**（老文件里那条旧键已失效）；
  再改成「保留公交站」→ 重启 → 仍应是「保留公交站」（这一次是玩家真选过的，落在新键 `StopPolicy` 上）。
  分诊：日志 `[选项] 退出懒人模式时 = …` 说明值进了设置对象，`[设置] 载入后 … 退出策略=…` 说明读回来的是什么；
  新装玩家的片段应在自己的 `BusLineAutoStops.coc`，老玩家的旧片段仍在 `FallbackSettings.coc`（`[FileLocation]` 不搬旧片段，0.14.2 已核实）。
- **v0.14.0 需求 4 右键连删**：修改既有线路 → 拖拽落站 → 右键删掉那一枚路径点 → 站台应一起消失，
  日志 `[右键撤点] 站台 … 的路径点已被删除`；退出懒人模式后再去删路径点 → 站台**不能**再被连删。
- **v0.14.0 需求 5 零影响**：总开关关掉后帧数无可测变化；卸载模组后读档无报错。
- **v0.14.1 文案**：设置页「退出懒人模式是否保留公交站」的说明应当是"线路保存后即永远保留"，
  且那一行里不再出现"线路没画完就取消"；12 种语言都要看（钉子只钉源码字符串，游戏内渲染仍要眼睛过一遍）。

- **需求 1 拖拽前不预览**（v0.13.0）：鼠标移到既有线路的路径点/路段上，应当**看不到**模拟的公交站；整个拖拽过程中也不显示。
- **需求 2 路径点依附新站台**（v0.13.0）：拖拽落站后路径点应当接到新站台上、公交真的靠站。日志看 `[拖拽依附] 路点 … → 依附=…`；
  `这一笔没记下线路实体` / `找不到落点 … 内的路径点` 是两条已知的降级路径（站台照旧落地，只差连接）。
- **需求 3 站台依附范围内不模拟**（v0.13.0）：光标压进既有站台 4m 内应当**不预览**，左键直接把路径点依附到那枚站台上。日志看
  `[预览跳过] … 光标已在既有站台的依附范围内`。
- **需求 3 拖拽落站**：左键拖拽已画好的线路，松手是否在光标处放下公交站。日志看三条：`[线路工具] 态 Default → Modify` / `→ Default`（证明检测触发了）、`[拖拽落站] … 站台已落地（拖拽新增，不可右键回退）`（成功）、或 `本帧解析不出父级，转入补提交` / `补提交放弃`（失败与原因）。只有前两条 Modify 行而没有 `[拖拽落站]`，才说明拖拽链路真的坏了。
- **需求 1 快捷键**：开/关总开关各几次之后按 G，看是否每次都能切换懒人模式；日志里 `[快捷键] 跟随总开关 shouldBeEnabled=…` 应在开关两条路径上都出现（0.11.2 只在关的那次出现）。
- **需求 1 图标**：总开关关掉时左上角图标应当消失、懒人模式当场退出；打开后图标回来。
- **需求 2 默认不保留**：退出懒人模式后本次会话自动放下的站台应当被删掉；选「保留公交站」则留下。被线路停靠点引用的永不删。

**已知限制（定案，不当 bug 追）**

- 禁用/卸载模组要求重启进程：同一场进程内选项页仍留着本模组那一栏（P3-2）。
- 有轨电车置灰"暂未上线"，分类认得电车、懒人模式不启用（见待办 2）。
- 自动站台不写 `Game.Tools.Recent` → 建不扣钱、铲不退款（净零刷不出钱），是玩家定案的设计（校正 #23）。
- 商店长描述里仍有"自动放置/Auto-place"字样（功能名与 mod 名如此），按"商店描述不动"保留。
- 第三方资产的 `BusStop` 标记能否脱离连接缓冲单独存在：**未取证**（P2-5 只证明了原版路径）。
- 商店 ChangeLog 字段受发布器 5000 字符上限约束，只保留最近两段；更早版本的历史在 git 与 `docs/history/`。
- 线上元数据（描述 / 链接 / AccessLevel）只能人工读网页核对：私有模组的 API 侧 403，`pull-live.js` 只核对版本表。

## 已知待办（按优先级）

1. **电车落点机制**：`Setting.TramEnabled` 恒 false 且选项置灰 + `TransportKindRules.IsLazyModeEnabled` 两层门——分类认得 Tram（资产识别/过滤/查重），但懒人模式不启用、不可选；要补的是"两站一线"的电车落点，不是把开关打开。
2. **`[预览结果]` 实机对账**（v0.11.2 新增能力）：确认它按静止事件各打一行、`落点横向` 与 `光标横向` 同号，之后这条就可以从待办里划掉。
3. **`m_stopQueryVerified` 的回退路径**：一旦某台机器的站台物体不带 BusStop/TramStop 标记就永久走"每 2s 全量物体扫描"，在 12.8 万 lane 的城里仍可能是周期尖峰；日志 `[站台索引]` 会明说走了哪条，实机若命中再想办法（不提前优化）。
4. **拖拽附件的实机对账**（v0.13.0 新增能力）：确认 `[拖拽依附]` 每笔拖拽一行、站台 `ConnectedRoute` 缓冲 +1，
   且路径点位置被原版搬到站台上（`WaypointConnectionSystem` 的 PathTargetMoved）。若出现"路径点连着站台但公交仍不靠站"，
   下一步查 AccessLane/RouteLane 是否被重算。
5. 经济侧已定案的偏差：见上面的已知限制，`CommitNow` 注明"以后别顺手补 Recent"。
6. 火车/地铁/机场/码头是车站体系，`TransportKind` 架构预留。

## 文件夹清单（根目录应当只有这些）

入库：`Systems/`、`Frontend/`、`Properties/`、`docs/history/`、`scripts/`、`tests/` 与根上的 12 个 `.cs` + `BusLineAutoStops.csproj` + `README.md` + `ARCHITECTURE.md` + `.gitignore` + `.zcodeignore`。
不入库（构建/调研产物，`.gitignore` 已排）：`bin/`、`obj/`、`Library/`、`releases/`、`research/`（反编译 dump 与 live-versions.json）、`Frontend/node_modules/`、`Frontend/dist/`、`.qoder-credits/`（定期删）。
一次性补丁脚本用完就删（本轮 12 语种 `master.desc` 的替换脚本跑完即删，改完结果已入库）。
