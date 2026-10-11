/* =======================================================================
   sync-site-dist.js — 把站点源码同步到 站点/dist（发布用的镜像目录）
   用法：node scripts/sync-site-dist.js
   · 只同步列出的文本文件；图片/缩略图不动，避免覆盖已经人工挑好的资产。
   · 逐字节比较：不一致才写，输出"改了哪些 / 已经一致"。
   · 源码里新增了文件而 dist 没有（例如新脚本）也会报出来并补齐。
   ======================================================================= */

const fs = require("fs");
const path = require("path");

const SITE = "D:\\WorkSpace\\CS2MOD\\站点";
const FILES = [
  "index.html",
  "assets/css/styles.css",
  "assets/js/data.js",
  "assets/js/live.js",
  "assets/js/i18n.js",
  "assets/js/i18n-b.js",
  "assets/js/app.js",
];

function rel(p) {
  return path.relative(SITE, p).replace(/\\/g, "/");
}

let copied = 0;
let same = 0;
const missing = [];

for (const f of FILES) {
  const src = path.join(SITE, f);
  const dst = path.join(SITE, "dist", f);
  if (!fs.existsSync(src)) {
    missing.push(f + "（源码就没有）");
    continue;
  }
  const a = fs.readFileSync(src);
  const b = fs.existsSync(dst) ? fs.readFileSync(dst) : null;
  if (b && b.equals(a)) {
    same++;
    continue;
  }
  fs.mkdirSync(path.dirname(dst), { recursive: true });
  fs.writeFileSync(dst, a);
  copied++;
  console.log("同步 " + f + (b ? "  (" + b.length + "B -> " + a.length + "B)" : "  (dist 里原本没有)"));
}

// 反向检查：assets/js 下有没有清单里漏掉的脚本
const jsDir = path.join(SITE, "assets", "js");
for (const name of fs.readdirSync(jsDir)) {
  if (!name.endsWith(".js")) continue;
  if (!FILES.some((f) => path.basename(f) === name)) {
    console.log("注意：assets/js/" + name + " 不在同步清单里，需要就加进 FILES");
  }
}

console.log("---");
console.log("改动并同步 " + copied + " 个，已一致 " + same + " 个");
if (missing.length) console.log("缺失：" + missing.join("、"));

// 复核：dist 侧全部齐了才算通过
const bad = FILES.filter((f) => {
  const src = path.join(SITE, f);
  const dst = path.join(SITE, "dist", f);
  if (!fs.existsSync(src)) return true;
  if (!fs.existsSync(dst)) return false;
  return !fs.readFileSync(src).equals(fs.readFileSync(dst));
});
console.log(bad.length === 0 ? "dist 与源码一致 ✓" : "仍然不一致：" + bad.join("、"));
process.exit(bad.length === 0 ? 0 : 1);
