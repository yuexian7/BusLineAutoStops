/** 把 nails-070.snippet.js 拼进 pack-release.js 的自检段（v0.7.0 边界：横幅注释 → function utf16Has 之前）。 */
const fs = require("fs");
const path = require("path");
const here = __dirname;
const p = path.join(here, "pack-release.js");
const snip = fs.readFileSync(path.join(here, "nails-070.snippet.js"), "utf8");
let s = fs.readFileSync(p, "utf8");
const a = s.indexOf("// ── 需求逐条自检（v0.7.0）");
const a0 = s.indexOf("// ── 需求逐条自检（v0.6.0）");
const start = a >= 0 ? a : a0;
const b = s.indexOf("\nfunction utf16Has(");
if (start < 0 || b < 0 || b < start) { console.error("边界不对 start=" + start + " b=" + b); process.exit(1); }
s = s.slice(0, start) + snip + "\n" + s.slice(b + 1);
fs.writeFileSync(p, s);
try {
  require("child_process").execFileSync(process.execPath, ["--check", p], { stdio: "inherit" });
} catch (e) { console.error("语法检查没过"); process.exit(1); }
console.log("拼好并通过语法检查");
