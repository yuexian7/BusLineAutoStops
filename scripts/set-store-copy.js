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

const VERSION = "0.14.0";

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
  "While you are still in the same lazy session, removing a path point with the right mouse button also removes the stop that this session placed on that path point. That rule only covers stops the mod put down in the current session and it ignores the keep/remove option below: once you leave lazy mode those stops are final and can no longer be removed that way.",
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
  "在同一次懒人模式会话里，用右键去掉一个路径点时，本次会话自动放置在那个路径点上的公交站也会一起删掉。这条规则只管本次会话里模组自己放下的站台，而且与下面的保留/移除选项无关：一旦退出懒人模式，这些站台就算彻底放置好了，再也不能这样一起删。",
].join(BLANK);

/* ------------------------------ ChangeLog ------------------------------- */
const enLog = [
  "v0.14.0 fixes the two settings that kept resetting, and gives the drag its preview back.",
  "- Fixed (preview while you drag): pressing the left button on one of a line's path points and dragging now shows the preview stop again, following your cursor. 0.13.0 muted it for the whole drag, so you were moving a path point with nothing on screen. Nothing is still simulated before you press: hovering a path point without holding the button remains quiet, because that click is a drag, not a placement.",
  "- Fixed (the master switch did not stick): turning the mod off came back on after restarting the game, so the top-left icon was there again. The cause was settings being written to the wrong file: two installed mods happened to give their settings class the same name, and the game picks the save target by that class name alone. This mod's settings class now has a name of its own and its own settings file, and the master switch is always written.",
  "- Fixed (the keep/remove stops dropdown reset every restart): same cause as above, same fix. The dropdown now keeps what you picked. Nothing about how the mod behaves changed with it.",
  "- New (right click removes the stop too): inside the same lazy session, right click a path point and the stop this session placed on that path point is removed with it. Only stops this mod placed in the current session are affected, stops you placed yourself never are, and the rule ignores the keep/remove option - leaving lazy mode makes those stops final, exactly as before.",
  "- Safer (turning the mod off): with the master switch off the world-side system now returns after a few flag reads, so the mod costs nothing measurable while it is off. Every change the mod makes is still ordinary game data, so removing the mod cannot damage a save.",
  "- Diagnostics: the log now prints the settings it actually read at startup, and the frontend console reports every master switch change, so a log file with a report can show exactly what happened.",
  "- Also in this version, carried over from 0.13.0: the dragged path point is attached to the stop that lands; releasing a drag no longer crashes the game; the toggle hotkey recovers after using the master switch; and ending lazy mode removes the stops this session placed by default, with Keep the bus stops as the option.",
].join(NL);

const zhLog = [
  "v0.14.0 修掉两个「每次重启都回默认」的设置项，并把拖拽中的预览还回来。",
  "- 修复（拖拽中显示预览）：在路径点上按住左键开始拖拽之后，预览公交站又回来了，跟着光标走。0.13.0 把整段拖拽都静音了，等于你在拖一个路径点、屏幕上什么都没有。按下之前仍然不模拟：鼠标只是停在路径点上、没按键时不显示 —— 那一下是拖拽，不是放站。",
  "- 修复（总开关留不住）：关掉模组、重启游戏之后它自己又开了，左上角图标又在那儿。真正原因在落盘寻址：本机装着的两个模组把设置类取了同一个名字，而游戏的保存目标只按类名找。本模组的设置类现在有自己的名字、自己的配置文件，总开关每次都会写下去。",
  "- 修复（保留/移除公交站的下拉框每次重启回默认）：与上面同因同修。现在你选什么就留什么，模组的行为一点没变。",
  "- 新增（右键删点连站台一起删）：在同一次懒人模式会话里，右键去掉一个路径点时，本次会话自动放在那个路径点上的公交站会一起删掉。只涉及本次会话里模组自己放下的站台，你自己摆的永远不碰；这条规则与保留/移除选项无关 —— 退出懒人模式之后这些站台就算彻底放置好了，与以前一样。",
  "- 更稳（关闭模组）：总开关关着时，世界侧的系统读几个标志位就返回，关着的时候模组没有任何可测开销。模组改动的仍然全是游戏自带的数据，卸载不会损坏存档。",
  "- 分诊（日志）：启动时会把真正读到的设置打一行，前端控制台会把每次总开关变化打一行 —— 带着日志反馈就能看清到底发生了什么。",
  "- 本版也包含 0.13.0 的那几项：拖拽落站时被拖的路径点会依附到新站台；拖拽松手不再闪退；用过总开关之后快捷键不再失效；退出懒人模式默认不保留本次会话落地的公交站（可选项为「保留公交站」）。",
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
  // 发布器实测（2026-10-21 NewVersion 0.13.0 被拒）：changelogEntry 上限 5000，
  // 而且是**传完文件之后**才报 —— 别留给发布器发现。
  if (where === "ChangeLog" && text.length > 5000) bad.push("长度 " + text.length + " > 5000（发布器上限）");
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
