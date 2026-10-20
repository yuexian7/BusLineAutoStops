/**
 * set-store-copy.js — 写发布配置里"面向玩家的全部文字"：ModVersion / ShortDescription /
 * LongDescription / ChangeLog，外加 Frontend/mod.json 的版本与描述。
 * 用法：node scripts/set-store-copy.js
 *
 * 规矩（都是踩过的）：
 *  · 属性值里不许出现裸 ASCII 双引号（会截断 XML 属性）；面向玩家的引号一律用中文“ ”。
 *  · 不许出现裸 & < >；换行用 &#xA;。
 *  · ChangeLog 必须以 "v<本版> " 开头（pack-release 就是按这条查有没有忘了改版本）。
 *  · 只改这 4 个字段，DisplayName / AccessLevel / 链接 / Tag / Thumbnail 一律不碰：
 *    线上那些地方是玩家自己调过的。
 */
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const XML = path.join(root, "Properties", "PublishConfiguration.xml");
const MODJSON = path.join(root, "Frontend", "mod.json");

const VERSION = "0.13.0";

const NL = "&#xA;";
const BLANK = "&#xA;&#xA;";

/* --------------------------- ShortDescription --------------------------- */
const short = [
  "Hold the cursor on the pavement to preview a bus stop; left click places it, path point attached. Dragging a line places one where you let go. / 光标停在人行道上即预览公交站，左键落地并依附路径点；拖拽线路松手处也落站。",
].join(" ");

/* --------------------------- LongDescription ---------------------------- */
const enLong = [
  "Lazy mode lives inside the line tool. While you draw a bus line, hold the cursor still for about 0.2 seconds at a spot and the game shows a preview bus stop on the pavement on the same side of the road your cursor is on - the same ghost the object tool draws. Move the cursor away and that preview fades out with it; the left click that adds your waypoint turns the preview you were looking at into a real stop, and the waypoint attaches to it. Dragging a line works too: let go and a stop is placed at the spot you released, with the dragged path point connected to it. Hovering one of the line's own path points shows no preview at all - that is a drag, not a placement. Finish the line and the stops are connected, so buses actually stop there.",
  "Enter it at any moment: press the top-left icon or the hotkey with a stop asset in hand, or with nothing selected at all - with nothing in hand the mod switches you into the bus line tool so lazy mode has somewhere to work. While it is on, the transport panel lists only bus stop assets; picking a different one changes what gets placed without switching your tool away from drawing the line.",
  "The transport panel behaves exactly like the game's own: clicking an asset lights up only that asset, never a group of similar-looking entries. While lazy mode is on the panel keeps two entries lit - the line tool, which the game lights by itself because the tool is the line tool, and the stop asset you are about to place, which the game never lights on its own because picking a stop in lazy mode does not switch your tool. Switch to another stop asset and the light moves with it, so you can always see which one is live.",
  "Tram stops are split out into their own option and are greyed with “not available yet” in this version. A tram line is drawn stop to stop, not along the road edge like a bus line, so the pavement rule that makes bus previews work does not apply to it; that half needs its own mechanism and comes in a later update. Choosing a tram stop while a bus line is being drawn no longer drags lazy mode into a state where nothing appears.",
  "Nothing about placing is automatic any more. Earlier versions tried to press the place button for you; that mode is gone. Only your left click commits, the mod never writes into the game's input, and a stop that belongs to another line is never touched.",
  "Cost, undo history, error messages and the pavement snapping are all the game's own work: the mod feeds one preview into the game's creation pipeline and the game decides. A pavement that merely has a lamp post or a bin on it still previews, because the game allows a stop there and clears that prop when it lands; a spot the game truly refuses shows nothing.",
  "Ending it: the icon, the hotkey or the master switch stop the feature. Leaving the line tool - switching to another tool or pressing ESC - ends lazy mode by itself and the icon returns to its normal colour. When lazy mode ends you choose either to keep the stops or to remove only the stops this session placed (removing them is the default in this version); choosing to remove them also removes the waypoints that were attached to those stops, walking back to the first waypoint you placed yourself, so no lonely waypoint marker is left on the road. A stop another line already uses is always kept.",
  "Removing the mod cannot corrupt a save: no custom component types, no patched game methods, no global tool switches flipped. While the mode is off the systems do nothing at all.",
  "Localized in the 12 official languages with the game's own terminology: bus stop and tram stop are the assets, platform is the station platform, waypoint is the path point. UI text goes through game localization, so Bridge the Language Gap can translate it.",
  "This feature is still being calibrated road by road: on some very wide roads or odd pavements a case may still behave differently. The mod writes a plain-text log (Cities2 Logs folder, BusLineAutoStops.log) that says exactly what it decided and why, so a report with that file gets a precise answer.",
].join(BLANK);

const zhLong = [
  "懒人模式挂在创建线路工具上。画公交线路时，光标在某个位置停住约 0.2 秒，原版就会在人行道上显示一枚公交站的预览，而且就在你光标这一侧的路边 —— 和拿资产时那个幽灵一模一样；光标移开，那枚预览才跟着收走。你按左键加路径点的那一下，才把你正在看的那一枚变成真的公交站，路径点同时依附上去。拖拽线路也一样：松手就在你松开的那一处落一枚站台，被拖的路径点接在它上面。光标停在路径点上则完全不预览 —— 那一下是拖拽，不是放站。线路画完，公交站和路径点是连着的，公交真的会停。",
  "任何时候都能进入：手里拿着公交站资产时点左上角图标或按快捷键；什么都没选也能进 —— 手里空着时模组会把你切进公交线路的创建工具，懒人模式才有地方可用。开着的时候交通面板只列公交站资产，换一个要用的资产也不会把你从画线里踢出去。",
  "交通面板和游戏原生表现一致：点一个资产只点亮这一个，绝不会把一批看着差不多的条目一起点亮。懒人模式开着时，面板同时点亮两处 —— 线路工具（这一处原版自己就会亮：工具就在线路工具上）和你正要用的公交站资产（这一处原版永远不亮：懒人模式里点公交站资产不切工具）。换一枚公交站资产，点亮就跟着换过去，随时看得出当前放的是哪一枚。",
  "有轨电车在这一版被拆成单独一条，并且置灰注明“暂未上线”。电车线路是一个电车站连一个电车站画出来的，不像公交线路那样沿道路边缘落点，所以让公交预览成立的那套人行道规则在它身上不成立；这半边需要自己的一套机制，放到后面的版本。顺带一条：画公交线时误选电车站资产，也不会再把懒人模式拖进“什么都不显示”的状态。",
  "放置这件事不再有任何自动成分。早期版本试过“模组替你按放置键”，那条链路已经整个撤掉：只有你的左键会落地，模组不碰游戏的输入，也绝不碰属于别的线路的站台。",
  "造价、撤销记录、错误提示、人行道吸附都由游戏自己做：模组只往原版的产出管线里喂一枚预览，认不认这个位置由原版判。人行道上有路灯、垃圾桶这类东西时预览照样在 —— 原版允许在这个位置放站台，站台落地时那些物件自己消失；只有原版真的不给放的位置才什么都不显示。",
  "退出：图标、快捷键或总开关都只是停止这个功能。切去别的工具或按 ESC 离开线路工具，懒人模式自己退出、图标恢复原色。退出时你选：保留公交站，或只移除本次会话里模组落地的公交站（这一版默认就是移除）；选移除时，依附在这些公交站上的路径点也一并撤掉，一路撤到你自己放的那一个为止，路上不会剩一枚孤零零的路径点标志。已经被别的线路用上的公交站永远保留。",
  "卸载本模组不会让存档报错：不写任何自定义组件类型、不打任何补丁、不残留全局开关。模式关着的时候，模组的系统一行世界代码都不写。",
  "12 种官方语言，用词跟随原版：公交站 / 有轨电车站是资产，站台指的是车站月台，两者不是一回事；waypoint 是路径点。界面文字走游戏本地化，Bridge the Language Gap 可以翻译。",
  "这个功能还在逐条道路校准：特别宽的路、形状古怪的人行道上可能仍有表现不一样的情况。模组会把它的判断和原因逐条写进纯文本日志（游戏 Logs 目录下的 BusLineAutoStops.log），带上这个文件的反馈能得到确切答复。",
].join(BLANK);

/* ------------------------------ ChangeLog ------------------------------- */
const enLog = [
  "v0.13.0 dragging a line is consistent now: no preview while you are about to drag, and the path point you dragged is attached to the stop that lands.",
  "- Fixed (no preview while you are about to drag): moving the mouse onto one of a line's own path points no longer simulates a bus stop. A left click there starts dragging that point, so the mod now stays quiet - before the press and during the whole drag - and only the game's own preview remains on screen. The road under the drag is still remembered silently, so the stop that lands when you let go goes to the right road.",
  "- Fixed (the path point attaches to the new stop): when a drag places a stop, the path point you dragged is connected to it, so buses really call there. Before, the stop appeared but the point stayed loose on the road, because the game had already committed the line before the stop existed.",
  "- Fixed (a stop right next to where you point): if the cursor is already inside an existing bus stop's attach range, the mod no longer simulates a stop of its own - the game snaps the path point onto that stop by itself. The simulated one used to collide with something at the slightly offset spot, and that could get your click refused.",
  "- Fixed (crash when a drag ended, in the test build after 0.11.2): the placement itself was reached, but on that single frame the game both removed a ghost and re-created one at the same spot while the mod fed a new definition into that same frame. Placement now waits one frame: releasing only records the spot, and the stop is fed on the next frame. The result is unchanged, the stop just appears a frame later.",
  "- Fixed (a drag that cannot resolve the road no longer dies on one bad frame): the spot stays frozen where you let go, but one frame of cursor jitter right after the release no longer throws the drag away. A drag that still cannot be resolved retries at the release spot for a few frames instead of chasing the cursor.",
  "- Fixed (hotkey after using the master switch): turning the mod off and back on used to disable the toggle hotkey for good. Both directions are handled now and the state is re-checked every frame, so it also recovers if the game resets it.",
  "- Changed (master switch): it is now called Enable the mod, and it is the mod itself rather than lazy mode. Off means the top-left icon disappears and the game behaves exactly as it would without the mod.",
  "- Changed (default): when lazy mode ends, the stops it placed this session are removed by default. Choose Keep the bus stops to go on using them; a stop another line already uses is always kept.",
  "v0.11.2 hardening release: nothing about how a stop gets placed changed, and you should feel no difference while playing.",
  "- Fixed (diagnostics): the log line that reports the outcome of a preview - how far the game snapped it from the point we fed it, which side of the road it landed on, whether the game accepted the spot - was gated on the same switch as the earlier line, so it never appeared in BusLineAutoStops.log at all. It is output again now, so a log file you send with a report can say exactly where a stop would have landed.",
  "- Safer (leaving lazy mode): when the mode ends, the mod now waits for the game's in-flight work to finish before it takes back the preview ghost - the same order it already used when the mode starts. That closes one more route to the rare crash that happens when structure changes are made while handles are held.",
  "- Safer (asset panel): the filtered asset list is now remembered only after it has been built successfully, so a failure half way through can no longer serve the previous list back as if it were the new one.",
  "- Duplicate check: deciding whether a bus stop is already there no longer depends on whether a line has connected to it. The game gives every stop its connection buffer the moment it is created, so the check now follows the game's own definition of a stop and stays correct for assets that carry the label without the buffer.",
  "- Author side, no effect on players: before a build is considered ready, the tooling checks the deployed folder is complete (dll, frontend, mod.json, icons), that the version number matches in all four places it is written, and that the compiler is found by asking the SDK rather than by a pinned path.",
  "v0.11.1 the lazy-mode highlight no longer depends on the panel filter, and the duplicate diagnostic names the panel it came from.",
  "- Changed (lazy mode): the lit stop asset stays lit even when the panel filter list fails to load - the highlight now follows lazy mode itself, one link less to break. - Improved (diagnostics): the duplicate-count line also names the panel it came from and lists up to three duplicated assets, so a log report can be tied to one category instead of a guess.",
  "v0.11.0 the transport panel lights up only the asset you clicked, and lazy mode keeps both the line tool and the stop asset you are placing lit.",
  "- Fixed (panel highlight): clicking a bus stop asset in the transport panel used to light up a whole group of similar-looking entries, as if several had been clicked at once. The game decides which entry is selected by the asset's entity alone - same entity, same highlight - so an asset that is registered in a category more than once shows up in the panel several times, and all of those entries lit up together. The panel is now normalised on our side before it reaches the game's grid: entries are keyed by their entity and duplicates are dropped, so exactly one entry can ever light up, whichever asset you pick and whether lazy mode is on or off. The log gains a line that counts the duplicates it found, so a report still tells them apart if anything is left.",
  "- New (lazy mode highlight): while lazy mode is on the panel now keeps two entries lit - the line tool, which the game lights by itself because the tool is the line tool, and the stop asset you are currently placing, which the game never lights on its own because picking a stop in lazy mode does not switch your tool. Switch to another stop asset and the light moves with it, so you can always see which one is live.",
  "- Unchanged: tram stops remain a separate, greyed option marked “not available yet”; nothing is placed without your own left click; no custom component types and no patched game methods, so removing the mod cannot corrupt a save.",
].join(NL);

const zhLog = [
  "v0.13.0 拖拽线路这条链路收拾干净了：拖拽前不再模拟出公交站，拖拽落站时被拖的路径点会依附到新站台上。",
  "- 修复（拖拽前不再模拟出公交站）：鼠标移到线路自己的路径点上时，不会再冒出一枚模拟的公交站。在那里按左键是拖动这个路径点，所以模组现在全程安静 —— 按下之前与整个拖拽过程中都不显示，屏幕上只有游戏自己的预览。拖拽脚下的那条路仍然会被静默记住，松手落站依旧落在正确的路上。",
  "- 修复（路径点会依附到新站台）：拖拽落站之后，被拖动的那个路径点会接到这枚站台上，公交真的会靠站。以前站台出现了、路径点却留在路上，因为原版先把线路提交完了，站台那时还不存在。",
  "- 修复（光标旁边正好有站台时）：光标已经落进既有公交站的依附范围时，模组不再自己模拟一枚站台 —— 原版会把这一笔路径点直接吸附到那枚站台上。模拟的那一枚在稍微偏移的位置上会跟物件碰撞，反而可能让你这一笔被原版拒绝。",
  "- 修复（拖拽结束时的闪退，0.11.2 之后那个测试版）：落站其实已经走到了，但那一帧游戏正在同一处删掉一个幽灵、又重建一个，而模组把新定义喂进了同一帧。现在落站推迟一帧：松手只记下落点，真正的喂定义放在下一帧。结果完全一样，只是站台晚一帧出现。",
  "- 修复（解析不出道路的拖拽不再因为一帧抖动就作废）：落点仍旧定格在松手处，但松手后单帧的光标跳变不会再丢掉整笔拖拽；仍解析不出时，就在松手处继续试几帧，不再跟着鼠标跑。",
  "- 修复（用过总开关之后快捷键失效）：以前关掉模组再打开，切换懒人模式的快捷键会一直停用。现在两个方向都处理，并且每帧复查一次，游戏把它重置掉也能自己恢复。",
  "- 调整（总开关）：改名「启用模组」，它管的是模组本身，不是懒人模式。关闭时左上角图标不出现，游戏完全恢复原版行为。",
  "- 调整（默认值）：退出懒人模式时，本次会话里由模组放下的公交站默认就不保留。想接着用这些站台就选「保留公交站」；已经被线路停靠点用上的站台永远保留。",
  "v0.11.2 加固版：放公交站这件事的玩法一点没变，玩的时候应当感觉不到差别。",
  "- 修复（日志）：报告预览结果的那一行——原版把幽灵吸附到了哪儿、离我们喂下去的位置多远、落在路的哪一侧、判定是通过还是只是警告——以前被前一行日志的开关吃掉，模组日志里一条都不会出现。现在恢复输出，于是随反馈一起发来的日志能确切说明“这一枚为什么会放在这里 / 为什么没放”。",
  "- 更稳（退出懒人模式）：模式结束时，模组先等游戏在跑的活儿收干净，再收回那枚预览幽灵，与进入模式时用的是同一套顺序；又少掉一类“持有句柄期间改结构”的罕见崩溃路径。",
  "- 更稳（资产面板）：过滤名单改成算成功之后才记账，中途出问题时不会把上一份名单当成新名单发回面板。",
  "- 查重判据：判断“这里是不是已经有公交站”不再取决于有没有线路连上它。原版在建公交站的那一刻就已经把连接缓冲一起给了它，所以现在的判据跟原版自己认公交站的方式一致，遇到只带公交站标记、没有连接缓冲的资产也不会漏判。",
  "- 开发侧（不影响玩家）：出包前会先检查部署目录四件套齐全（本体、前端、mod.json、图标）、版本号四处一致，编译工具的路径也不再写死某一个 SDK 版本。",
  "v0.11.1 懒人模式高亮不再依赖面板过滤名单；去重自检日志会写明来自哪个面板、重复的是哪几枚资产。",
  "- 调整（懒人模式）：当前公交站资产的高亮在过滤名单取不到时照样亮 —— 高亮现在只跟随懒人模式本身，少了一条会断的链。 - 改进（诊断）：去重计数那一行现在会写明来自哪个面板，并列出最多三枚重复资产，于是日志报告能精确对应到某一个分类，而不用猜。",
  "v0.11.0 点击资产只点亮这一个；懒人模式开着时，线路工具和你正在用的公交站资产两处同时点亮。",
  "- 修复（面板高亮）：在交通面板里点一个公交站资产，以前会把一批看着差不多的条目一起点亮，像同时点了好几个。原版判断“哪一条被选中”只看资产实体本身 —— 实体相同就一起点亮；而同一个资产在分类里登记了几次，面板里就会出现几条，点它就同时亮一片。现在面板在送到原版网格之前先按实体键归一化：重复条目只留第一条，于是无论点哪个资产、开不开懒人模式，都只可能亮一个。日志里另留一行去重计数，万一还有别的原因也能分清。",
  "- 新增（懒人模式高亮）：懒人模式开着时，面板同时点亮两处 —— 线路工具（这一处原版自己就会亮，因为工具就在线路工具上）和你正在使用的公交站资产（这一处原版永远不亮：懒人模式里点公交站资产不切工具）。换一枚公交站资产，点亮跟着换，随时看得出当前放的是哪一枚。",
  "- 保持不变：有轨电车仍是设置页里单独一条、置灰并注明“暂未上线”；不按左键什么都不会落地；不写自定义组件类型、不打任何补丁，卸载模组不会让存档报错。",
].join(NL);

/* ------------------------------- 写入 ----------------------------------- */
let s = fs.readFileSync(XML, "utf8");

function assertSafe(text, where) {
  const bad = [];
  if (/"/.test(text)) bad.push("裸 ASCII 双引号");
  if (/&(?!#)/.test(text)) bad.push("裸 &");
  if (/</.test(text) || />/.test(text)) bad.push("< 或 >");
  // 短描述上限 200（发布器实测：shortDescription: Must be a string between 1 and 200 length，
  // 超了整次 NewVersion 直接失败，而且它是在传完文件之后才报的 —— 别留给发布器发现）。
  if (where === "ShortDescription" && text.length > 200) bad.push("长度 " + text.length + " > 200");
  if (/\*\*|__|^#/.test(text)) bad.push("混进了 Markdown 记号");
  if (bad.length) {
    console.error(where + " 含非法字符：" + bad.join("、"));
    process.exit(1);
  }
}

function setAttr(tag, value, where) {
  assertSafe(value, where);
  const re = new RegExp("<" + tag + " Value=\"[\\s\\S]*?\" \\/>");
  if (!re.test(s)) { console.error("找不到元素 <" + tag + ">"); process.exit(1); }
  s = s.replace(re, "<" + tag + ' Value="' + value + '" />');
}

setAttr("ModVersion", VERSION, "ModVersion");
setAttr("ShortDescription", short, "ShortDescription");
setAttr("LongDescription", enLong + BLANK + zhLong, "LongDescription");
setAttr("ChangeLog", enLog + BLANK + zhLog, "ChangeLog");

const counts = ["ModVersion", "ShortDescription", "LongDescription", "ChangeLog"]
  .map((t) => [t, (s.match(new RegExp("<" + t + " ", "g")) || []).length]);
for (const [t, n] of counts) if (n !== 1) { console.error(t + " 元素数量异常：" + n); process.exit(1); }

// 不该被动的字段（线上那些位置是玩家自己调过的）
const KEEP = [
  'ModId Value="160528"',
  'DisplayName Value="BusLine AutoStops"',
  'AccessLevel Value="Private"',
  'Tag Value="Code Mod"',
  'Thumbnail Value="Properties/Thumbnail.png"',
  'GameVersion Value="1.6.*"',
];
for (const keep of KEEP) {
  if (!s.includes(keep)) { console.error("固定字段被改掉了：" + keep); process.exit(1); }
}

fs.writeFileSync(XML, s, "utf8");

/* ----------------------------- mod.json -------------------------------- */
// mod.json 带 UTF-8 BOM（上一版特意保留的），JSON.parse 不吃 BOM，先剥再写回。
const rawMj = fs.readFileSync(MODJSON, "utf8");
const bom = rawMj.charCodeAt(0) === 0xfeff ? "\ufeff" : "";
const mj = JSON.parse(bom ? rawMj.slice(1) : rawMj);
mj.version = VERSION;
mj.description = "Auto-place bus stops and tram stops with the game's own placement / 用原版放置流程自动落公交站、有轨电车站";
fs.writeFileSync(MODJSON, bom + JSON.stringify(mj, null, 2) + "\n", "utf8");

console.log("已写：" + VERSION + "  xml=" + s.length + "B  short=" + short.length
  + "  long=" + (enLong.length + zhLong.length + BLANK.length) + "  log=" + (enLog.length + zhLog.length + BLANK.length));
