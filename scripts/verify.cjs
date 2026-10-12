#!/usr/bin/env node
/**
 * 离线门禁：版本一致性（四处）+ T3 纯逻辑 +（--deployed）本机部署完整性。
 * 发布装订由 pack-release.js 负责，二者互补：这里管"源码是不是同一版、部署目录能不能实机"。
 *
 * 用法：
 *   node scripts/verify.cjs              改完代码随手跑（不碰部署目录）
 *   node scripts/verify.cjs --deployed   实机前 / 上传前：再对一遍 Mods\BusLineAutoStops\
 *
 * --deployed 是 2026-10-12 事故换来的：`dotnet build` 走官方 DeployWIP 会**清空**部署目录、
 * 只铺回 DLL + Burst 桩，deploy-local 铺的 mjs/mod.json/images 全没了。后端还活着、日志照常打，
 * 实机表现却是"左上角图标消失、面板选资产拦截失效"——那次没有任何脚本报警。
 * 现在这条链由本文件钉死：四件套齐全 + 每项都是本次构建产物（**字面量 + mtime**，
 * 不用文件哈希：本项目这套 csc 调用不是确定性构建，同一份源码重复打包 md5 会变）。
 */
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const P = require(path.join(__dirname, "paths.js"));
const wantDeployed = process.argv.includes("--deployed");
let failed = 0;

function pass(msg) { console.log("PASS " + msg); }
function fail(msg) { console.log("FAIL " + msg); failed++; }

// ── 1) 版本一致性：四处一起对（v0.11.2 审查 P3-1：以前只查 2 处，漏的要等到 pack-release 才暴露） ──
const read = (...p) => fs.readFileSync(path.join(root, ...p), "utf8");
const stripBom = (t) => (t.charCodeAt(0) === 0xfeff ? t.slice(1) : t);
const where = [];
const verOf = (label, src, re) => {
  const m = re.exec(src);
  if (!m) { fail(label + " 里没读到版本号"); return null; }
  where.push({ label, v: m[1] });
  return m[1];
};

verOf("BusLineAutoStopsMod.cs kVersion", read("BusLineAutoStopsMod.cs"), /kVersion\s*=\s*"([^"]+)"/);
verOf("Setting.cs kVersion", read("Setting.cs"), /kVersion\s*=\s*"([^"]+)"/);
verOf("Frontend/mod.json version", stripBom(read("Frontend", "mod.json")), /"version"\s*:\s*"([^"]+)"/);
verOf("PublishConfiguration.xml ModVersion", read("Properties", "PublishConfiguration.xml"),
  /<ModVersion Value="([^"]+)"/);

const uniq = [...new Set(where.map((w) => w.v))];
if (where.length === 4 && uniq.length === 1) pass("version " + uniq[0] + " 四处一致（" + where.map((w) => w.label).join(" / ") + "）");
else {
  fail("版本四处不一致：读到 " + where.length + "/4 处 " +
    where.map((w) => w.label + "=" + w.v).join(" / "));
}

// ── 2) T3 — 本机 NuGet 还原异常，走 tests/t3/build-run.js（Roslyn 直编） ──
try {
  const out = execFileSync(
    "node",
    [path.join(root, "tests", "t3", "build-run.js")],
    { cwd: root, encoding: "utf8", stdio: "pipe" }
  );
  console.log(out);
  if (/fail=0/.test(out) && /TOTAL/.test(out)) pass("T3 pure logic");
  else fail("T3 had failures");
} catch (e) {
  fail("T3 run error: " + (e.stdout || e.message || e));
}

// ── 3) --deployed：部署目录完整性 + 产物是不是本次构建的 ──
if (wantDeployed) deployed();

function deployed() {
  console.log("\n── --deployed " + P.DEPLOY_DIR + " ──");
  const cur = uniq.length === 1 ? uniq[0] : null;

  if (!fs.existsSync(P.DEPLOY_DIR)) {
    fail("部署目录不存在 → node scripts/deploy-local.js");
    return;
  }

  // 3a) 四件套齐全（事故当场就是少了 mjs / mod.json / images）
  const present = fs.readdirSync(P.DEPLOY_DIR);
  for (const e of P.DEPLOY_ENTRIES) {
    const p = path.join(P.DEPLOY_DIR, e);
    if (!fs.existsSync(p)) { fail("部署缺 " + e + "（跑过 dotnet build 会清掉前端产物 → node scripts/deploy-local.js 重铺）"); continue; }
    if (e === "images") {
      const n = fs.readdirSync(p).filter((f) => /\.(svg|png)$/i.test(f));
      if (n.length === 0) fail("部署的 images/ 是空的（图标没铺上，实机就是空按钮）");
      else pass("images/ " + n.length + " 个图标");
    } else pass("部署含 " + e);
  }
  // 白名单：多一项就说明 deploy-local 的清理没跑过（0Harmony.dll / *.pdb / 旧 mjs 会静默改变实机行为）
  const extra = present.filter((e) => !P.DEPLOY_ENTRIES.includes(e));
  if (extra.length) fail("部署目录有白名单外的东西：" + extra.join(", ") + " → 实机测的可能不是当前产物");
  else pass("部署目录只有白名单四项");

  const dllP = path.join(P.DEPLOY_DIR, P.MOD_NAME + ".dll");
  const mjsP = path.join(P.DEPLOY_DIR, P.MOD_NAME + ".mjs");
  const mjP = path.join(P.DEPLOY_DIR, "mod.json");

  // 3b) DLL：字面量 + mtime 证明是本次构建（不用哈希：非确定性构建，md5 每次都变）
  if (fs.existsSync(dllP)) {
    const dll = fs.readFileSync(dllP);
    if (!cur) fail("版本号四处不一致，无法判定部署产物属于哪一版");
    else {
      const inUtf16 = dll.indexOf(Buffer.from(cur, "utf16le")) >= 0;
      const inUtf8 = dll.indexOf(Buffer.from(cur, "utf8")) >= 0;
      if (inUtf16 || inUtf8) pass("DLL 内含本版字面量 " + cur + "（" + (inUtf16 ? "#US UTF-16" : "#Strings UTF-8") + "）");
      else fail("DLL 里没有 " + cur + " → 部署的是旧产物（kVersion 改了没重编？node scripts/deploy-local.js）");
      // 旧版残留：releases/ 里存过哪些版本号，部署 DLL 里就不该再有哪一个
      const relDir = path.join(root, "releases");
      const stale = fs.existsSync(relDir)
        ? fs.readdirSync(relDir).filter((d) => /^\d+\.\d+\.\d+$/.test(d) && d !== cur)
        : [];
      const hit = stale.filter((v) => dll.indexOf(Buffer.from(v, "utf16le")) >= 0 || dll.indexOf(Buffer.from(v, "utf8")) >= 0);
      if (hit.length) fail("DLL 里还留着旧版本字面量：" + hit.join(", ") + "（源码没改干净或产物没重编）");
      else pass("DLL 里没有其它已发布版本的字面量（对过 " + stale.length + " 个 releases/ 快照）");
    }
    const builtP = path.join(root, "bin", "deploy", P.MOD_NAME + ".dll");
    if (!fs.existsSync(builtP)) fail("bin/deploy 里没有本次编译产物 → 先 node scripts/deploy-local.js");
    else {
      const built = fs.statSync(builtP), dep = fs.statSync(dllP);
      if (dep.size !== built.size) fail("部署 DLL " + dep.size + "B ≠ 本次编译 " + built.size + "B → 部署的是别处的文件");
      else if (dep.mtimeMs + 1000 < built.mtimeMs) fail("部署 DLL 比 bin/deploy 旧（拷完又重编过，没重新铺）");
      else pass("部署 DLL 与 bin/deploy 同一颗（" + dep.size + "B）");
    }
    // 源码比产物新 = 改动根本没进 DLL
    const newestSrc = newestOf([root], (f) => f.endsWith(".cs"), ["research", "tests", "Frontend", "bin", "obj", "releases", "scripts", "Properties", "Library", "docs", "node_modules"]);
    const srcStat = fs.statSync(newestSrc.path);
    if (srcStat.mtimeMs - 2000 > fs.statSync(dllP).mtimeMs) {
      fail("最新的 " + path.relative(root, newestSrc.path) + " 比部署 DLL 新 → 源码改动没编译进去");
    } else pass("部署 DLL 不早于最新源码（" + path.relative(root, newestSrc.path) + "）");
  }

  // 3c) 前端：mjs / mod.json / images 三样都要跟住源产物
  const distMjs = path.join(root, "Frontend", "dist", P.MOD_NAME + ".mjs");
  if (fs.existsSync(mjsP) && fs.existsSync(distMjs)) {
    const a = fs.statSync(mjsP), b = fs.statSync(distMjs);
    if (a.size !== b.size) fail("部署 mjs " + a.size + "B ≠ Frontend/dist " + b.size + "B → webpack 产物没铺过去");
    else if (a.mtimeMs + 1000 < b.mtimeMs) fail("部署 mjs 比 Frontend/dist 旧（重打过包没重新铺）");
    else pass("部署 mjs 与 Frontend/dist 同一颗（" + a.size + "B）");
    const banner = /^\/\*![\s\S]{0,400}/.exec(fs.readFileSync(mjsP, "utf8"));
    if (!banner || !cur || !new RegExp("Version:\\s*" + cur.replace(/\./g, "\\.")).test(banner[0])) {
      fail("部署 mjs 的横幅里没有本版版本号（横幅缺了游戏还会把 .mjs 当无效模块，见 Modding.log m_ModuleId=null）");
    } else pass("部署 mjs 横幅版本 = " + cur);
    const feNewest = newestOf([path.join(root, "Frontend", "src"), path.join(root, "Frontend", "mod.json")], () => true, ["node_modules"]);
    if (feNewest && feNewest.mtime - 2000 > a.mtimeMs) {
      fail("最新的 " + path.relative(root, feNewest.path) + " 比部署 mjs 新 → 前端改动没编译进去");
    } else pass("部署 mjs 不早于最新前端源码");
  }
  // mod.json：部署的那颗必须就是 Frontend/mod.json（版本号、name、url 全在里面）
  {
    const srcP = path.join(root, "Frontend", "mod.json");
    if (!fs.existsSync(mjP)) fail("部署缺 mod.json");
    else if (!fs.existsSync(srcP)) fail("Frontend/mod.json 不存在，没法比对部署的那颗");
    else {
      const a = fs.statSync(mjP), b = fs.statSync(srcP);
      if (a.size !== b.size) fail("部署 mod.json 与 Frontend 源不一致（" + a.size + "B vs " + b.size + "B）");
      else if (a.mtimeMs + 1000 < b.mtimeMs) fail("部署 mod.json 比源文件旧（改完没重新铺）");
      else pass("部署 mod.json = Frontend 源（" + a.size + "B）");
    }
  }
  const imgSrc = path.join(root, "Frontend", "images");
  const imgDep = path.join(P.DEPLOY_DIR, "images");
  if (fs.existsSync(imgSrc) && fs.existsSync(imgDep)) {
    const want = fs.readdirSync(imgSrc).filter((f) => /\.(svg|png)$/i.test(f));
    const bad = want.filter((f) => {
      const d = path.join(imgDep, f);
      return !fs.existsSync(d) || fs.statSync(d).size !== fs.statSync(path.join(imgSrc, f)).size;
    });
    if (bad.length) fail("图标缺失或不同源：" + bad.join(", ") + "（漏了就是线上玩家的空按钮）");
    else pass("图标 " + want.length + " 个全部与源同尺寸");
  }

  // 3d) 唯一该被这个目录写进来的东西：ModsData 是运行时数据，绝不落在 Mods\ 里
  const dataDir = path.join(P.GAME_DIR, "Cities Skylines II", "ModsData", P.MOD_NAME);
  if (fs.existsSync(path.join(P.DEPLOY_DIR, P.MOD_NAME + ".log")) || fs.existsSync(path.join(P.DEPLOY_DIR, "Logs"))) {
    fail("部署目录里有运行日志 → 构建会 RemoveDir 整个目录，日志必须写 " + dataDir);
  } else pass("部署目录里没有运行数据（日志走 ModsData）");
}

/** 目录集合里最新的文件（skip 为跳过的子目录名）。 */
function newestOf(paths, accept, skip) {
  let best = null;
  const walk = (d) => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      if (e.isDirectory()) {
        if ((skip || []).includes(e.name) || e.name.startsWith(".")) continue;
        walk(path.join(d, e.name));
      } else if (accept(e.name)) {
        const st = fs.statSync(path.join(d, e.name));
        if (!best || st.mtimeMs > best.mtime) best = { path: path.join(d, e.name), mtime: st.mtimeMs };
      }
    }
  };
  for (const p of paths) {
    if (!fs.existsSync(p)) continue;
    if (fs.statSync(p).isFile()) {
      const st = fs.statSync(p);
      if (!best || st.mtimeMs > best.mtime) best = { path: p, mtime: st.mtimeMs };
    } else walk(p);
  }
  return best;
}

console.log(failed === 0 ? (wantDeployed ? "VERIFY OK (deployed)" : "VERIFY OK") : "VERIFY FAILED " + failed);
process.exit(failed === 0 ? 0 : 1);
