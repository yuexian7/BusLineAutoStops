/**
 * 把「右上角登录按钮 / 头像」这条需求在站点里留下的东西全部撤掉。
 * 玩家 2026-10-02 说明：那条需求是发给另一个模组的，本站点从来不需要登录控件。
 *
 * 用法：node scripts/revert-site-account.js
 *
 * 撤哪些（都是当初加进去的、可定位的整块，不碰原有内容）：
 *   index.html          —— 挂载点 div + 注释 + <script src=account.js>
 *   assets/css/styles.css —— 从「右上角账号控件」横幅到紧邻「布局」横幅之前
 *   assets/js/i18n*.js  —— 12 语言里各 4 个 acc* 键
 *   assets/js/account.js —— 整个文件删掉
 *   dist/ 里同名文件同步处理（发布用的是 dist 镜像）
 *
 * 自检：撤完再全树扫一遍，任何一处还剩 account / acct- / __qoder_auth / acc* 就非零退出，
 * 并且要求 dist 与源码逐字节一致 —— 免得"源码撤了、发出去还有"。
 */
const fs = require("fs");
const path = require("path");

const SITE = "D:\\WorkSpace\\CS2MOD\\站点";
const MIRRORED = [
  "index.html",
  "assets/css/styles.css",
  "assets/js/data.js",
  "assets/js/live.js",
  "assets/js/i18n.js",
  "assets/js/i18n-b.js",
  "assets/js/app.js",
];

const log = [];
const fail = [];

function read(p) { return fs.readFileSync(p, "utf8"); }
function write(p, s) { fs.writeFileSync(p, s); }

/** 每一处删除都记次数：次数不对就是锚点没对上，宁可不写。 */
function sub(file, name, fn, expect) {
  const before = read(file);
  const after = fn(before);
  if (before === after) { fail.push(name + "：没匹配到（锚点变了？）"); return; }
  const n = (before.match(new RegExp(expect.pattern, "g")) || []).length;
  if (n !== expect.count) { fail.push(name + "：命中 " + n + " 次，预期 " + expect.count + " 次，不写入"); return; }
  write(file, after);
  log.push(name + " ×" + n);
}

const ACCOUNT_LINE = /^([ \t]*)<script src="assets\/js\/account\.js"><\/script>\r?\n/m;
const MOUNT_BLOCK = /\r?\n[ \t]*<!-- 账号：[\s\S]*?<div class="account" id="accountBox"><\/div>/;
const CSS_START = "/* ========================= 右上角账号控件";
const CSS_END = "/* ============================== 布局";
const ACC_KEY_LINE = /^[ \t]*acc(?:SignIn|Account|SignedIn|IdentityNote):.*\r?\n/gm;

for (const base of ["", "dist/"]) {
  const dir = path.join(SITE, base.replace(/\//g, path.sep));

  // ── index.html ──
  const html = path.join(dir, "index.html");
  if (!fs.existsSync(html)) { fail.push(base + "index.html 不存在"); continue; }
  sub(html, base + "html 挂载点", (s) => s.replace(MOUNT_BLOCK, ""), { pattern: "<div class=\"account\"", count: 1 });
  sub(html, base + "html script 标签", (s) => s.replace(ACCOUNT_LINE, ""), { pattern: "assets/js/account\\.js", count: 1 });

  // ── styles.css：整块横幅到下一个原有横幅之前 ──
  const css = path.join(dir, "assets/css/styles.css");
  sub(css, base + "css 账号块", (s) => {
    const a = s.indexOf(CSS_START);
    const b = s.indexOf(CSS_END, a + 10);
    if (a < 0 || b < 0) { fail.push(base + "css 锚点不全"); return s; }
    return s.slice(0, a) + s.slice(b);
  }, { pattern: "右上角账号控件", count: 1 });

  // ── i18n：acc* 键（两份文件合计 12 语言 × 4 键） ──
  for (const f of ["assets/js/i18n.js", "assets/js/i18n-b.js"]) {
    const p = path.join(dir, f);
    if (!fs.existsSync(p)) { fail.push(base + f + " 不存在"); continue; }
    const n = (read(p).match(ACC_KEY_LINE) || []).length;
    if (n === 0) { fail.push(base + f + "：没有 acc* 键"); continue; }
    write(p, read(p).replace(ACC_KEY_LINE, ""));
    log.push(base + f + " 删 " + n + " 行词条");
  }

  // ── account.js 整个删掉 ──
  const js = path.join(dir, "assets/js/account.js");
  if (fs.existsSync(js)) { fs.unlinkSync(js); log.push(base + "account.js 已删除"); }
}

// ── 撤完扫一遍：整棵树（文本文件）里不该再有任何痕迹 ──
const BAD = /account|acct-|__qoder_auth|accSignIn|accAccount|accSignedIn|accIdentityNote|RAINBOW_ACCOUNT/i;
const hits = [];
const descriptorNotes = [];
(function walk(d) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    const p = path.join(d, e.name);
    if (e.isDirectory()) { walk(p); continue; }
    // 站点描述符（.qoder.site）是 prepare_site 生成的冻结快照，不该手改：
    // 里面若还留着旧快照只说明"下次重新 prepare 才会更新"，单独记一行，不算撤除失败。
    if (/\.qoder\.site$/i.test(e.name)) {
      const t = read(p);
      const n = (t.match(new RegExp(BAD.source, "g")) || []).length;
      descriptorNotes.push(e.name + "：" + (n ? "冻结快照里还有 " + n + " 处旧痕迹（下次 prepare_site 会重生成）" : "已无痕迹"));
      continue;
    }
    if (!/\.(html|css|js|json|md)$/i.test(e.name)) continue;
    const t = read(p);
    t.split(/\r?\n/).forEach((l, i) => { if (BAD.test(l)) hits.push(path.relative(SITE, p) + ":" + (i + 1) + " " + l.trim().slice(0, 70)); });
  }
})(SITE);

// ── dist 必须和源码一致（发布拿的是 dist） ──
for (const f of MIRRORED) {
  const a = path.join(SITE, f), b = path.join(SITE, "dist", f);
  if (!fs.existsSync(a) || !fs.existsSync(b)) { hits.push("dist 镜像缺文件：" + f); continue; }
  if (!fs.readFileSync(a).equals(fs.readFileSync(b))) hits.push("dist 与源码不一致：" + f);
}

// ── CSS 括号还得配平，撤完不能把后面内容带跑 ──
{
  const css = read(path.join(SITE, "assets/css/styles.css"));
  const open = (css.match(/\{/g) || []).length, close = (css.match(/\}/g) || []).length;
  if (open !== close) hits.push("styles.css 括号不配平 " + open + "/" + close);
  if (css.indexOf(CSS_END) < 0) hits.push("styles.css 找不到「布局」横幅（撤过头了）");
  const html = read(path.join(SITE, "index.html"));
  const scripts = (html.match(/<script src="assets\/js\/[^"]+"><\/script>/g) || []);
  if (scripts.length !== 5) hits.push("index.html script 标签数 " + scripts.length + "（应为 5：data/live/i18n/i18n-b/app）");
  if (!/<\/html>\s*$/.test(html)) hits.push("index.html 结尾异常");
}

log.forEach((l) => console.log("  done " + l));
descriptorNotes.forEach((l) => console.log("  note  " + l));
if (hits.length) {
  console.error("REVERT 未通过：\n  " + hits.slice(0, 20).join("\n  "));
  process.exit(1);
}
if (fail.length) {
  console.error("REVERT 有步骤没做成：\n  " + fail.join("\n  "));
  process.exit(1);
}
console.log("REVERT OK：站点里已无登录/头像痕迹，dist 与源码逐字节一致");
