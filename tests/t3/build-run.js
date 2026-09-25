const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const csc = "C:\\Program Files\\dotnet\\sdk\\8.0.425\\Roslyn\\bincore\\csc.dll";
const refDir = "C:\\Program Files\\dotnet\\packs\\Microsoft.NETCore.App.Ref\\8.0.31\\ref\\net8.0";
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

console.log("compiling...");
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
