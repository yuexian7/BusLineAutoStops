/**
 * 只做 C# 编译检查，不部署、不跑 webpack。
 * 用法：node scripts/compile-check.js
 * 引用表与 scripts/deploy-local.js 保持一致（含 Colossal.UI.Binding.dll——
 * agemo 的 csproj 里没这条引用，所以 dotnet build 会报 CS0234，别拿它当准）。
 */
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
// SDK 路径不写死版本号（v0.11.2 审查 P2-6）：本机 SDK 一升级，写死 8.0.425 的三条链会一起断。
const { cscPath } = require(path.join(__dirname, "dotnet-sdk.js"));
const csc = cscPath();
const managed = require(path.join(__dirname, "paths.js")).MANAGED_DIR;
const harmony222 = "C:\\Users\\10975\\.nuget\\packages\\lib.harmony\\2.2.2\\lib\\net48\\0Harmony.dll";
const harmony233 = "C:\\Users\\10975\\.nuget\\packages\\lib.harmony\\2.3.3\\lib\\net48\\0Harmony.dll";

const refNames = [
  "mscorlib.dll", "System.dll", "System.Core.dll", "netstandard.dll",
  "Game.dll", "Colossal.Core.dll", "Colossal.Logging.dll",
  "Colossal.IO.AssetDatabase.dll", "Colossal.Localization.dll",
  "Colossal.UI.Binding.dll", "Colossal.Collections.dll", "Colossal.Mathematics.dll",
  "Unity.Entities.dll", "Unity.Collections.dll", "Unity.Mathematics.dll",
  "Unity.Burst.dll", "UnityEngine.CoreModule.dll", "Unity.InputSystem.dll",
];

const refs = refNames.map((n) => path.join(managed, n)).filter((p) => fs.existsSync(p));
const harm = fs.existsSync(harmony222) ? harmony222 : harmony233;
if (fs.existsSync(harm)) refs.push(harm);

const srcs = [];
(function walk(d) {
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    if (["research", "tests", "Frontend", "bin", "obj", "releases", "scripts", "Properties"].includes(e.name)) continue;
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p);
    else if (e.name.endsWith(".cs")) srcs.push(p);
  }
})(root);

const outDir = path.join(root, "bin", "check");
fs.mkdirSync(outDir, { recursive: true });
const out = path.join(outDir, "BusLineAutoStops.dll");

console.log("compile-check:", srcs.length, "个源文件 →", out);
try {
  execFileSync("dotnet", [
    csc, "-nologo", "-t:library", "-langversion:9.0", "-codepage:65001",
    "-optimize+", "-nowarn:1701,1702", "-out:" + out,
    ...refs.map((r) => "-r:" + r), ...srcs,
  ], { stdio: "inherit", cwd: root });
} catch (e) {
  console.error("COMPILE FAILED");
  process.exit(1);
}
console.log("COMPILE OK  dll=" + fs.statSync(out).size + "B");
