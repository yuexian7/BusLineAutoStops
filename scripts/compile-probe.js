/**
 * 用 Roslyn csc + 游戏 Managed 引用对本模组做编译探针（不依赖 NuGet）。
 * 仅验证 C# 能否通过类型检查；不产出可部署模组（仍需官方 Mod.props 工具链）。
 */
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const csc = "C:\\Program Files\\dotnet\\sdk\\8.0.425\\Roslyn\\bincore\\csc.dll";
const managed =
  "F:\\SteamLibrary\\steamapps\\common\\Cities Skylines II\\Cities2_Data\\Managed";
const harmony =
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

const refs = refNames
  .map((n) => path.join(managed, n))
  .filter((p) => fs.existsSync(p));
if (fs.existsSync(harmony)) refs.push(harmony);

const srcs = [];
function walk(d) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    if (e.name === "research" || e.name === "tests" || e.name === "Frontend" || e.name === "bin" || e.name === "obj" || e.name === "releases")
      continue;
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p);
    else if (e.name.endsWith(".cs")) srcs.push(p);
  }
}
walk(root);

const outDir = path.join(root, "obj", "probe");
fs.mkdirSync(outDir, { recursive: true });
const out = path.join(outDir, "BusLineAutoStops.probe.dll");

const args = [
  csc,
  "-nologo",
  "-t:library",
  "-langversion:9.0",
  "-codepage:65001",
  "-nowarn:1701,1702",
  "-out:" + out,
  ...refs.map((r) => "-r:" + r),
  ...srcs,
];

console.log("refs", refs.length, "srcs", srcs.length);
try {
  execFileSync("dotnet", args, { stdio: "inherit", cwd: root });
  console.log("PROBE COMPILE OK ->", out);
  process.exit(0);
} catch (e) {
  console.error("PROBE COMPILE FAILED");
  process.exit(1);
}
