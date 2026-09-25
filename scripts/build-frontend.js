const { execFileSync } = require("child_process");
const path = require("path");
const fs = require("fs");

const cwd = path.join(__dirname, "..", "Frontend");
const npmCli = "C:\\Program Files\\nodejs\\node_modules\\npm\\bin\\npm-cli.js";

function npm(args) {
  // 直接跑 npm-cli.js，避开带空格的 npm.cmd 被 shell 拆坏
  execFileSync(process.execPath, [npmCli, ...args], {
    cwd,
    encoding: "utf8",
    stdio: "inherit",
  });
}

if (!fs.existsSync(npmCli)) {
  console.error("npm-cli.js not found at", npmCli);
  process.exit(1);
}

console.log("npm install in", cwd);
npm(["install", "--no-fund", "--no-audit"]);

console.log("webpack build...");
npm(["run", "build"]);
