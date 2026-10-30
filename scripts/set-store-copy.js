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

const VERSION = "0.14.2";

const NL = "&#xA;";
const BLANK = "&#xA;&#xA;";

/* --------------------------- ShortDescription --------------------------- */
const short = [
  "Preview and place bus stops while drawing a line: left click places the preview, drag and release places one where you let go. / 画公交线时顺手放站：光标停住出预览，左键落地，拖拽松手处也落站。",
].join(" ");

/* --------------------------- LongDescription ---------------------------- */
// 2026-10-30 重写：作者嫌旧文案啰嗦、AI 味重（删了旧模组重发）。精简到三段，只说
// 干什么 / 怎么开关与退出 / 安全性，去掉逐条罗列和解释性长句。
const enLong = [
  "While you draw a bus line, rest the cursor on the pavement for a moment and the game previews a stop on that side of the road. Left click places it and attaches the waypoint; drag the line and a stop lands where you let go, attached to the dragged point. Hovering one of the line's own path points shows no preview - that gesture is a drag, not a placement. Only your own clicks place anything; the mod never touches the game's input.",
  "Toggle it with the top-left icon, the hotkey, or the master switch in the options. Leaving the line tool ends the session by itself. When a session ends you choose: keep the stops, or remove only the ones this session placed (remove is the default). While a session is still open, right-clicking a waypoint also removes the stop this session placed on it; once the session has ended those stops are permanent.",
  "Cost, undo history, error messages and pavement snapping are all the game's own - the mod only feeds a preview into the vanilla pipeline. No custom components, no patches, nothing left behind: uninstalling never affects a save, and with the master switch off the mod does nothing at all. Interface text ships in the game's 12 languages. Tram stops are greyed out for now; they need a mechanism of their own.",
].join(BLANK);

const zhLong = [
  "画公交线路时，把光标在人行道上停一会儿，游戏就会在这一侧路边预览一个公交站。左键落地、路径点自动依附；拖拽线路时，松手的位置同样落一个站，接在被拖的路径点上。光标停在线路自己的路径点上不出预览 —— 那是拖拽，不是放站。模组不替你按键，只有你自己的点击会放站。",
  "左上角图标、快捷键、设置里的总开关都能随时开关；离开线路工具会自动结束。会话结束时你选：保留这些站，或只移除本次会话放的站（默认移除）。会话没结束时，右键删掉某个路径点，会把它上面本次会话放的站一起删掉；会话结束后这些站就是永久的。",
  "造价、撤销、报错、人行道吸附全是原版自己做的，模组只往原版管线里喂一枚预览。不加组件、不打补丁、不留残余：卸载不伤存档，总开关关掉时模组什么都不做。界面文字跟随游戏自带的 12 种语言。有轨电车站暂时置灰，它有自己的画法，要单独一套机制。",
].join(BLANK);

/* ------------------------------ ChangeLog ------------------------------- */
// 作者 2026-10-30 定：更新日志只留本版，内容就一句，中英双语。
const enLog = [
  "v0.14.2 Fixed shortcut settings.",
].join(NL);

const zhLog = [
  "修复快捷键设置。",
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
// ModId 不写死：2026-10-30 作者删除旧模组（160528）后重新首发，新 ID 由发布器分配、发布后回填。
// 这里断言的是「本次重写没动它」，值是多少以 XML 现状为准。
const modIdNow = (/<ModId Value="(\d+)" \/>/.exec(s) || [])[1];
if (!modIdNow) { console.error("读不到 <ModId>，文件结构变了？"); process.exit(1); }
const KEEP = [
  'ModId Value="' + modIdNow + '"',
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
