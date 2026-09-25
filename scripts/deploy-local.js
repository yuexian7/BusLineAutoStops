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
// 尽量带 pdb
const pdb = path.join(outDir, "BusLineAutoStops.pdb");
fs.writeFileSync(path.join(deployDir, "0Harmony.dll"), fs.readFileSync(harmRuntime));

// TTE 形态：*.mjs + mod.json 与 DLL 同目录（ModManager 托管 coui://ui-mods/）
const feCandidates = [
  path.join(root, "Frontend", "dist", "BusLineAutoStops.mjs"),
  path.join(root, "Frontend", "dist", "BusLineAutoStops.js"),
];
const feDist = feCandidates.find((p) => fs.existsSync(p));
if (feDist) {
  fs.copyFileSync(feDist, path.join(deployDir, "BusLineAutoStops.mjs"));
  fs.copyFileSync(path.join(root, "Frontend", "mod.json"), path.join(deployDir, "mod.json"));
  console.log("frontend bundled -> BusLineAutoStops.mjs + mod.json");
} else {
  console.log("frontend not built yet (Frontend/dist missing) — button UI skipped");
}

console.log("DEPLOYED ->", deployDir);
console.log(fs.readdirSync(deployDir).join(", "));
