/**
 * v0.7.0 需求 1「去掉光标停住以后的设置项」的机器删除。
 * 只做**整行/整块唯一锚点**的删除，每一处都核对命中次数，对不上就一行都不写。
 * 改完必须跑 compile-check（编译器会抓出漏网的引用）。
 */
const fs = require("fs");
const path = require("path");
const root = path.join(__dirname, "..");

const jobs = [];
const J = (file, name, re, expect, to) => jobs.push({ file, name, re, expect, to: to === undefined ? "" : to });

// ── Setting.cs：挡位属性、枚举 key、字段、默认值、同步 ──
J("Setting.cs", "LandPolicy 属性块（含注释）",
  /\r?\n {8}\/\/\/ <summary>\r?\n {8}\/\/\/ 停留 0\.1 秒以后怎么落地[\s\S]*?\r?\n {8}public LandMode LandPolicy\r?\n {8}\{\r?\n[\s\S]*?\r?\n {8}\}\r?\n/, 1);
J("Setting.cs", "LandPolicyEnumKey",
  /\r?\n {8}public static string LandPolicyEnumKey\(LandMode m\)\r?\n {8}\{\r?\n[\s\S]*?\r?\n {8}\}\r?\n/, 1);
J("Setting.cs", "m_LandPolicy 字段", /^ {8}private LandMode m_LandPolicy = LandMode\.AutoPlace;\r?\n/m, 1);
J("Setting.cs", "SetDefaults 里 m_LandPolicy", /^ {12}m_LandPolicy = LandMode\.AutoPlace;\r?\n/m, 1);
J("Setting.cs", "SetDefaults 里 LandPolicy", /^ {12}LandPolicy = LandMode\.AutoPlace;\r?\n/m, 1);
J("Setting.cs", "SetDefaults 里 LazyModeState.Land", /^ {12}LazyModeState\.Land = LandMode\.AutoPlace;\r?\n/m, 1);
J("Setting.cs", "SyncToState 里 Land", /^ {12}LazyModeState\.Land = TransportKindRules\.NormalizeLandMode\(m_LandPolicy\);\r?\n/m, 1);
J("Setting.cs", "版本号", /kVersion = "0\.6\.1"/, 1, 'kVersion = "0.7.0"');

// ── TransportKindRules.cs：枚举与两个纯函数已用 Edit 删过，这里只核对"确实没了" ──
J("TransportKindRules.cs", "LandMode 枚举块（应为 0）", /public enum LandMode/, 0);
J("TransportKindRules.cs", "ShouldAutoPlace（应为 0）", /ShouldAutoPlace/, 0);
J("TransportKindRules.cs", "NormalizeLandMode（应为 0）", /NormalizeLandMode/, 0);

// ── LocaleTable.cs：设置页注册 + 面板 slug + 12 份词典里的 5 个键 ──
J("LocaleTable.cs", "land.label/desc 注册",
  /[ \t]*Put\(map, setting\.GetOptionLabelLocaleID\(nameof\(Setting\.LandPolicy\)\)[\s\S]*?GetOptionDescLocaleID\(nameof\(Setting\.LandPolicy\)\), Get\(d, en, "land\.desc"\)\);\r?\n/, 1);
J("LocaleTable.cs", "PutLandEnum 调用",
  /[ \t]*\/\/ v0\.6\.1：落地方式两项[\s\S]*?PutLandEnum\(map, setting, LandMode\.ConfirmByClick, Get\(d, en, "land\.confirm"\)\);\r?\n/, 1);
J("LocaleTable.cs", "PutLandEnum 方法",
  /\r?\n {8}private static void PutLandEnum\(Dictionary<string, string> map, ModSetting setting, LandMode m, string value\)\r?\n {8}\{\r?\n[\s\S]*?\r?\n {8}\}\r?\n/, 1);
J("LocaleTable.cs", "词典里的 land.*/hint 行",
  /^ {16}\{ "(land\.(label|desc|auto|confirm)|hint\.confirmLand)", .* \},\r?\n/gm, 60);

// ── UiCommandSystem.cs：hint 通道改成 dwell（本版不显示提示条，只留状态） ──
J("Systems/UiCommandSystem.cs", "hintKey 计算",
  /[ \t]*\/\/ 需求（v0\.6\.1）：「由玩家按左键放置」挡位下的就绪提示。[\s\S]*?warn \? "" : LazyModeState\.ArmedHintLocaleKey;\r?\n/, 1);
J("Systems/UiCommandSystem.cs", "JSON 里的 hint 字段",
  /[ \t]*\+ ",\\"hint\\":\\"" \+ Escape\(hintKey\) \+ "\\""\r?\n/, 1);

// ── LazyModeState.cs：会话复位里对 ArmedHintLocaleKey 的引用 ──
J("LazyModeState.cs", "复位里的 ArmedHint 分支",
  /\r?\n {12}if \(ArmedHintLocaleKey\.Length > 0\)\r?\n {12}\{\r?\n {16}ArmedHintLocaleKey = "";\r?\n {16}changed = true;\r?\n {12}\}/, 1);
J("LazyModeState.cs", "复位注释里的等你点击",
  /\/\/ 进入\/退出懒人模式都不该留着一个"等你点击"的提示。/, 1);

// ── tests/t3：LandMode 断言整块 ──
J("tests/t3/Program.cs", "v0.6.1 挡位断言块",
  /[ \t]*\/\/ v0\.6\.1 需求：落地方式只剩两项[\s\S]*?Check\(System\.Enum\.GetValues\(typeof\(LandMode\)\)\.Length == 2, "落地方式下拉只有 2 项"\);\r?\n/, 1);

// ── 入口：代按左键相关日志尾巴 ──
J("BusLineAutoStopsMod.cs", "开启日志里的落地方式",
  / \+ " 落地方式=" \+ LazyModeState\.Land/, 1);

const changed = new Map();
const errors = [];

for (const j of jobs) {
  const p = path.join(root, j.file);
  if (!fs.existsSync(p)) { errors.push(j.file + " 不存在"); continue; }
  let s = changed.get(j.file);
  if (s === undefined) s = fs.readFileSync(p, "utf8");
  const m = s.match(j.re);
  const n = m === null ? 0 : (j.re.global ? (m.length || 0) : 1);
  if (n !== j.expect) {
    errors.push(j.file + " :: " + j.name + " 命中 " + n + " 次，预期 " + j.expect + " 次");
    continue;
  }
  changed.set(j.file, s.replace(j.re, j.to));
}

if (errors.length) {
  console.error("未写入，先修锚点：\n  " + errors.join("\n  "));
  process.exit(1);
}
for (const entry of changed) {
  const f = entry[0];
  const s = entry[1];
  const before = fs.readFileSync(path.join(root, f), "utf8");
  fs.writeFileSync(path.join(root, f), s);
  console.log("  wrote " + f + "  " + before.length + "B → " + s.length + "B");
}

// 复核：这些标识符不该再出现在我们自己的源码里
const banned = ["LandMode", "ShouldAutoPlace", "NormalizeLandMode", "LandPolicy", "ArmedHintLocaleKey", "hint.confirmLand", "PutLandEnum"];
const dirs = [root, path.join(root, "Systems"), path.join(root, "Frontend", "src"), path.join(root, "tests", "t3")];
const leftovers = [];
for (const d of dirs) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    if (!e.isFile() || !/\.(cs|tsx)$/.test(e.name)) continue;
    if (/^(revert|patch)/.test(e.name)) continue;
    const t = fs.readFileSync(path.join(d, e.name), "utf8");
    for (const b of banned) {
      const idx = t.indexOf(b);
      if (idx >= 0) leftovers.push(path.relative(root, path.join(d, e.name)) + " 还有 " + b
        + " → " + t.slice(Math.max(0, idx - 40), idx + 40).replace(/\r?\n/g, " ⏎ "));
    }
  }
}
if (leftovers.length) {
  console.error("删除后仍有引用：\n  " + leftovers.slice(0, 12).join("\n  "));
  process.exit(1);
}
console.log("0.7.0 代码裁剪 OK：挡位选项与就绪提示通道已全部移除");
