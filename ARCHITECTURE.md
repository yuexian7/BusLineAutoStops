# BusLine AutoStops 架构速览（v0.13.0）

给下一个接手者的最小集：现行机制、硬不变量、构建命令、待办。
逐版迭代史与机制取证（96 条校正、每版实机日志）在 `docs/history/开发笔记-v0.1.1~v0.11.1.md`（已冻结，只查不改）。
当前版本状态：**v0.13.0 已本地部署、离线门禁全绿、已上传线上（Private）**：ModId 160528 / 线上从 0.11.2 直接到 0.13.0 / dll 102400B / **仍 Private**。
0.13.0 修玩家三条反馈：拖拽前不再模拟出公交站、拖拽落地的路径点依附到新站台、光标进入既有站台依附范围时不模拟（交原版直接依附）。
**0.12.0 / 0.12.1 只在本机验证过、从未上传**（线上一直停在 0.11.2），它们的内容（拖拽放站、松手帧闪退修复、总开关与快捷键联动、退出策略默认不保留）合并进 0.13.0 一起上线。

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
| `pack-release.js` | 打包 `releases/<版本>/` 并做版本/需求/安全自检（175 枚钉子，防发旧产物与死分支） |
| `pull-live.js` | 只读核对 Paradox 线上版本表（写 `research/live-versions.json`） |
| `set-store-copy.js` | 写 `PublishConfiguration.xml` 面向玩家的 4 个字段 + `Frontend/mod.json` |
| `verify.cjs` | 离线门禁：版本四处一致 + T3 +（`--deployed`）部署完整性 |

## 验证台账

**已验证（离线，2026-10-21 v0.13.0）**

- `node scripts/compile-check.js` 0 错误（13 源文件 → 102400B）。
- `node scripts/pack-release.js 0.13.0` 全绿：175 枚钉子（含 v0.13.0 新增九枚：预览闸门三条 / 依附范围两条 / 拖拽依附三条 / 松手宽限一条）。
- `node scripts/verify.cjs` → 版本四处一致 + T3 `pass=90 fail=0`（新增 17 条：`SuppressPreview` 8 / `ShouldAttachDraggedWaypoint` 4 / `DragCursorLeft` 5）。
- 版本号五处统一：`BusLineAutoStopsMod.kVersion` / `Setting.kVersion` / `Frontend/mod.json` / `PublishConfiguration.xml` 的 ModVersion + mjs 横幅（pack 钉 `mjs 横幅版本一致`）。
- `releases/0.13.0/` 已生成本版快照（dll / mjs / mod.json / 两张图标）。
- 部署目录四件套与版本已核对（`--deployed`）：dll 102400B / mjs / mod.json(0.13.0) / 图标齐全；日志仍走 `ModsData`。

**未验证（要靠玩家本地实机）**

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
