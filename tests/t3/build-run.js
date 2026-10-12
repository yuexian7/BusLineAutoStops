const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

// csc 与 net8 引用集都不写死版本号（v0.11.2 审查 P2-6）：SDK/pack 一升级这里就会断，
// 而且断的表现是"回归壳跑不起来"，很容易被当成代码问题。解析器在 scripts/dotnet-sdk.js。
const sdk = require(path.join(__dirname, "..", "..", "scripts", "dotnet-sdk.js"));
const csc = sdk.cscPath();
const refDir = sdk.netCoreRefDir();
const out = path.join(__dirname, "t3.dll");
const src1 = path.join(__dirname, "Program.cs");
const src2 = path.join(__dirname, "..", "..", "TransportKindRules.cs");
const src3 = path.join(__dirname, "..", "..", "BlgKeys.cs");

const refs = fs.readdirSync(refDir)
  .filter((f) => f.endsWith(".dll"))
  .map((f) => path.join(refDir, f));

const args = [
  csc,
  "-nologo",
  "-t:exe",
  "-codepage:65001",
  "-out:" + out,
  ...refs.map((r) => "-r:" + r),
  src1,
  src2,
  src3,
];

console.log("compiling... csc=" + csc + " ref=" + refDir);
execFileSync("dotnet", args, { stdio: "inherit" });

// runtimeconfig for net8
fs.writeFileSync(
  path.join(__dirname, "t3.runtimeconfig.json"),
  JSON.stringify(
    {
      runtimeOptions: {
        tfm: "net8.0",
        framework: { name: "Microsoft.NETCore.App", version: "8.0.0" },
      },
    },
    null,
    2
  )
);

console.log("run...");
const run = execFileSync("dotnet", [out], { encoding: "utf8" });
console.log(run);
