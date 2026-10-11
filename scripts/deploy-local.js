/**
 * 本地部署：Roslyn 直编 BusLineAutoStops.dll → 复制 0Harmony 2.3.3 → 写入 Mods\BusLineAutoStops\。
 * 目标目录 = CSII_LOCALMODSPATH（与 F:\\...\\Cities Skylines II\\Cities Skylines II\\Mods 为同一 junction）。
 */
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const csc = "C:\\Program Files\\dotnet\\sdk\\8.0.425\\Roslyn\\bincore\\csc.dll";
const managed =
  "F:\\SteamLibrary\\steamapps\\common\\Cities Skylines II\\Cities2_Data\\Managed";
const modsDir =
  "F:\\SteamLibrary\\steamapps\\common\\Cities Skylines II\\Cities Skylines II\\Mods";
const deployDir = path.join(modsDir, "BusLineAutoStops");
const harmony233 =
  "C:\\Users\\10975\\.nuget\\packages\\lib.harmony\\2.3.3\\lib\\net48\\0Harmony.dll";
const harmony222 =
  "C:\\Users\\10975\\.nuget\\packages\\lib.harmony\\2.2.2\\lib\\net48\\0Harmony.dll";

const refNames = [
  "mscorlib.dll",
  "System.dll",
  "System.Core.dll",
  "netstandard.dll",
  "Game.dll",
  "Colossal.Core.dll",
  "Colossal.Logging.dll",
  "Colossal.IO.AssetDatabase.dll",
  "Colossal.Localization.dll",
  "Colossal.UI.Binding.dll",
  "Colossal.Collections.dll",
  "Colossal.Mathematics.dll",
  "Unity.Entities.dll",
  "Unity.Collections.dll",
  "Unity.Mathematics.dll",
  "Unity.Burst.dll",
  "UnityEngine.CoreModule.dll",
  "Unity.InputSystem.dll",
];

const refs = refNames.map((n) => path.join(managed, n)).filter((p) => fs.existsSync(p));
// 编译期用 2.2.2（与 ZoneSnapper/BLG 策略一致），运行时部署 2.3.3
const harmCompile = fs.existsSync(harmony222) ? harmony222 : harmony233;
if (fs.existsSync(harmCompile)) refs.push(harmCompile);

const srcs = [];
function walk(d) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    if (
      ["research", "tests", "Frontend", "bin", "obj", "releases", "scripts", "Properties"].includes(
        e.name
      )
    )
      continue;
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p);
    else if (e.name.endsWith(".cs")) srcs.push(p);
  }
}
walk(root);

const outDir = path.join(root, "bin", "deploy");
fs.mkdirSync(outDir, { recursive: true });
const out = path.join(outDir, "BusLineAutoStops.dll");

console.log("compile", srcs.length, "sources ->", out);
execFileSync(
  "dotnet",
  [
    csc,
    "-nologo",
    "-t:library",
    "-langversion:9.0",
    "-codepage:65001",
    "-optimize+",
    "-nowarn:1701,1702",
    "-out:" + out,
    ...refs.map((r) => "-r:" + r),
    ...srcs,
  ],
  { stdio: "inherit", cwd: root }
);

fs.mkdirSync(deployDir, { recursive: true });
const harmRuntime = fs.existsSync(harmony233) ? harmony233 : harmCompile;
for (const f of [out, out.replace(/\.dll$/, ".pdb")]) {
  if (fs.existsSync(f)) fs.copyFileSync(f, path.join(deployDir, path.basename(f)));
}
// v0.2.1：本版不再打任何 Harmony 补丁（全部走 public 工具面），不再随包发 0Harmony.dll
const pdb = path.join(outDir, "BusLineAutoStops.pdb");
void pdb;

// TTE 形态：*.mjs + mod.json 与 DLL 同目录（ModManager 托管 coui://ui-mods/）
//
// 关键：这里必须先跑 webpack。上一版就是因为只拷 Frontend/dist，
// 把改过的 src/index.tsx 留在未编译状态，实机测的还是旧按钮（绝对定位那版），
// 白跑一轮。现在默认强制重建，--no-fe 才跳过。
const feDir = path.join(root, "Frontend");
if (!process.argv.includes("--no-fe")) {
  const webpackCli = path.join(feDir, "node_modules", "webpack-cli", "bin", "webpack.js");
  if (fs.existsSync(webpackCli)) {
    try {
      execFileSync(process.execPath, [webpackCli], { cwd: feDir, encoding: "utf8", stdio: "pipe" });
      console.log("webpack ok");
    } catch (e) {
      const out = (e.stdout || "") + (e.stderr || "");
      console.error("WEBPACK FAILED -> 部署的是旧 dist，先修前端！\n" + out.slice(0, 4000));
      process.exit(1);
    }
  } else {
    console.warn("webpack-cli not found under Frontend/node_modules — running npm run build");
    const npmCli = "C:\\Program Files\\nodejs\\node_modules\\npm\\bin\\npm-cli.js";
    execFileSync(process.execPath, [npmCli, "run", "build"], { cwd: feDir, encoding: "utf8", stdio: "inherit" });
  }
}

const feCandidates = [
  path.join(root, "Frontend", "dist", "BusLineAutoStops.mjs"),
  path.join(root, "Frontend", "dist", "BusLineAutoStops.js"),
];
const feDist0 = feCandidates.find((p) => fs.existsSync(p));
if (feDist0) {
  fs.copyFileSync(feDist0, path.join(deployDir, "BusLineAutoStops.mjs"));
  fs.copyFileSync(path.join(root, "Frontend", "mod.json"), path.join(deployDir, "mod.json"));
  console.log("frontend bundled -> BusLineAutoStops.mjs + mod.json");
} else {
  console.log("frontend not built yet (Frontend/dist missing) — button UI skipped");
}

// 图标文件：ModManager.cs:471 把每个模组的 UI 目录挂到 coui://ui-mods/，
// 所以 Frontend/images/*.svg 必须原样跟到模组目录里（Road Builder 同一套做法）。
const imgSrc = path.join(root, "Frontend", "images");
if (fs.existsSync(imgSrc)) {
  const imgOut = path.join(deployDir, "images");
  fs.mkdirSync(imgOut, { recursive: true });
  for (const f of fs.readdirSync(imgOut)) {
    // 清掉已经不在源目录里的旧图标，避免部署目录越攒越脏
    if (!fs.existsSync(path.join(imgSrc, f))) fs.rmSync(path.join(imgOut, f));
  }
  for (const f of fs.readdirSync(imgSrc)) {
    if (/\.(svg|png)$/i.test(f)) fs.copyFileSync(path.join(imgSrc, f), path.join(imgOut, f));
  }
  console.log("images -> " + fs.readdirSync(imgOut).join(", "));
}

console.log("DEPLOYED ->", deployDir);

// 部署目录白名单清理：以前发过的 0Harmony.dll / *.pdb / 改名前的 mjs 如果留在这儿，
// 实机测的就不是当前这份产物（v0.2.x 真的被这个坑过一次）。只留这 4 项。
const keep = new Set(["BusLineAutoStops.dll", "BusLineAutoStops.mjs", "mod.json", "images"]);
let pruned = 0;
for (const e of fs.readdirSync(deployDir, { withFileTypes: true })) {
  if (keep.has(e.name)) continue;
  fs.rmSync(path.join(deployDir, e.name), { recursive: true, force: true });
  pruned++;
}
if (pruned) console.log("pruned stale entries:", pruned);

console.log(fs.readdirSync(deployDir).join(", "));
