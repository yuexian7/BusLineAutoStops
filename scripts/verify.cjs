#!/usr/bin/env node
/**
 * 离线门禁：T3 纯逻辑 + 版本一致性。
 * 用法: node scripts/verify.mjs
 */
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
let failed = 0;

function pass(msg) { console.log("PASS " + msg); }
function fail(msg) { console.log("FAIL " + msg); failed++; }

// 版本一致性
const modCs = fs.readFileSync(path.join(root, "BusLineAutoStopsMod.cs"), "utf8");
const xml = fs.readFileSync(path.join(root, "Properties", "PublishConfiguration.xml"), "utf8");
const banner = (modCs.match(/kVersion\s*=\s*"([^"]+)"/) || [])[1];
const xmlVer = (xml.match(/<ModVersion Value="([^"]+)"/) || [])[1];
if (banner && xmlVer && banner === xmlVer) pass(`version ${banner}`);
else fail(`version mismatch banner=${banner} xml=${xmlVer}`);

// T3 — 本机 NuGet 还原异常，走 tests/t3/build-run.js（Roslyn 直编）
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

console.log(failed === 0 ? "VERIFY OK" : "VERIFY FAILED " + failed);
process.exit(failed === 0 ? 0 : 1);
