/**
 * v0.7.0 前端裁剪：撤掉 v0.6.1 的"等你点击"就绪提示条 + 闪烁定时器。
 * 本版没有任何自动动作可提示，留着就是一条会撒谎的死代码。
 * 保留：图标红/绿状态、资产面板过滤、状态兜底 pull（那个是修"图标不变红"的）。
 *
 * 每一处都核对"确实删掉了"，没命中就非零退出（改 UI 源码最怕静默失配）。
 */
const fs = require("fs");
const path = require("path");
const p = path.join(__dirname, "..", "Frontend", "src", "index.tsx");
let s = fs.readFileSync(p, "utf8");
const before = s.length;
const miss = [];

function cut(name, re) {
  const m = s.match(re);
  if (!m) { miss.push(name); return; }
  s = s.replace(re, "");
}

cut("hintStyle 定义块", /\r?\n\/\*\*\r?\n \* 「等你点击」就绪提示[\s\S]*?\r?\n\};\r?\n/);
cut("hintKey state", / {2}const \[hintKey, setHintKey\] = useState\(""\);\r?\n/);
cut("blink state", / {2}const \[blink, setBlink\] = useState\(true\);\r?\n/);
cut("setHintKey 调用", / {6}\/\/ 就绪提示不自己定时消失[^\r\n]*\r?\n {6}setHintKey\(parsed\.hint \|\| ""\);\r?\n/);
cut("闪烁定时器", /\r?\n {2}\/\/ 就绪提示的"闪烁"：[\s\S]*?\}, \[hintKey\]\);/);
cut("提示条渲染", /\r?\n {6}\{!warningKey && hintKey && masterOn && active \? \([\s\S]*?\r?\n {6}\) : null\}/);
cut("UiState.hint 字段", / {2}\/\*\* v0\.6\.1：「等我点击」挡位的就绪提示词条 key（空=不显示） \*\/\r?\n {2}hint\?: string;\r?\n/);
cut("初值里的 hint", /\{ enabled: false, master: true, seq: 0, warningKey: "", hint: "" \}/);
s = s.replace(/'\{\\?"enabled\\?".*?\}'/, (m) => (m.includes("hint") ? m.replace(/,\\?"hint\\?":\\?"\\?"/, "") : m));
cut("setState 里 hint 残留", /setHintKey/g);

if (miss.length) {
  console.error("这些锚点没命中（源码结构变了？先看清楚再改）：\n  " + miss.join("\n  "));
  process.exit(1);
}
for (const t of ["hintKey", "setBlink", "blink", "hintStyle", "parsed.hint", '"hint"', "ArmedHint"]) {
  if (s.includes(t)) { console.error("删完还剩引用：" + t); process.exit(1); }
}
fs.writeFileSync(p, s);
console.log("前端裁剪 OK：" + before + "B → " + s.length + "B（就绪提示通道已移除，兜底 pull 保留）");
