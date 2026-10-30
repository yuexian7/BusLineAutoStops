#!/usr/bin/env node
/**
 * 线上状态核对（只读，不发任何写请求）。
 *
 * 为什么只能核对"版本列表"：模组是 PRIVATE，元数据（描述 / 链接 / Tag / 缩略图 / AccessLevel）
 * 只有网页 `mods.paradoxplaza.com/mods/165510/windows` 读得到。API 侧
 * `mods?modId=`、`mods/uploaded` 用游戏 `.pdxsdk` 里的 SessionToken 是 403，
 * `mods/versions?modId=` 就算加 `includeMetadata=true` 也不返回元数据（都是实测，见开发笔记的发布记录）。
 * 所以：
 *   · 本脚本用来确认「线上最新版本号 / versionId / 大小 / 时间」—— 也就是上一次 NewVersion 到底成没成功；
 *   · "别覆盖玩家在线上改过的地方"那一步仍然必须人工读网页，脚本替不了。
 *
 * 用法：node scripts/pull-live.js     → 打印版本表，并写 research/live-versions.json
 */
const fs = require("fs");
const path = require("path");

// 2026-10-30：作者删除旧模组（160528）重新首发，新 ID = 165510（发布器输出 "Mod published with Id=165510"）。
const MOD_ID = "165510";
const PDXSDK =
  "C:\\Users\\10975\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\.pdxsdk";

function findTokens() {
  const out = [];
  let entries = [];
  try {
    entries = fs.readdirSync(PDXSDK, { withFileTypes: true });
  } catch (e) {
    console.error("读不到 .pdxsdk（游戏没登录过？）:", e.message);
    return out;
  }
  for (const e of entries) {
    if (!e.isDirectory()) continue;
    const f = path.join(PDXSDK, e.name, "database.json");
    if (!fs.existsSync(f)) continue;
    const m = (fs.readFileSync(f, "utf8").match(/"SessionToken"\s*:\s*"([^"]+)"/) || [])[1];
    if (m) out.push({ who: e.name, token: m });
  }
  return out;
}

(async () => {
  const tokens = findTokens();
  if (!tokens.length) {
    console.error("没有找到 SessionToken");
    process.exitCode = 2;
    return;
  }
  const url = `https://api.paradox-interactive.com/mods/versions?modId=${MOD_ID}`;
  for (const t of tokens) {
    let r;
    try {
      r = await fetch(url, {
        headers: {
          Authorization: JSON.stringify({ session: t.token, type: "Session" }),
          Accept: "application/json",
        },
      });
    } catch (e) {
      console.error(t.who, "请求失败:", e.message);
      continue;
    }
    const text = await r.text();
    console.log(t.who, r.status, url);
    if (r.status !== 200 || !text || text.indexOf("forbidden") >= 0) {
      console.log("  ->", text.slice(0, 160).replace(/\n/g, " "));
      continue;
    }
    let j;
    try {
      j = JSON.parse(text);
    } catch {
      console.log("  返回不是 JSON:", text.slice(0, 160));
      continue;
    }
    const vs = j.modVersions || [];
    if (!vs.length) console.log("  (没有版本记录)", text.slice(0, 200));
    for (const v of vs) {
      console.log(`  versionId=${v.id} userVersion=${v.userVersion} size=${v.size} created=${v.created}`);
    }
    const out = path.join(__dirname, "..", "research", "live-versions.json");
    fs.mkdirSync(path.dirname(out), { recursive: true });
    fs.writeFileSync(out, JSON.stringify(j));
    console.log("  SAVED", out);
    return;
  }
  process.exitCode = 1;
})();
