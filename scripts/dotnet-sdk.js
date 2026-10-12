/**
 * 本机 .NET SDK 工具链路径解析（v0.11.2 审查 P2-6）。
 *
 * 为什么要单独一个模块：`compile-check.js`、`deploy-local.js`、`tests/t3/build-run.js` 三条链
 * 以前都写死 `sdk\8.0.425\Roslyn\bincore\csc.dll`（t3 还写死 `Microsoft.NETCore.App.Ref\8.0.31`）。
 * SDK 一升级（本机现在就同时躺着 8.0.424 与 8.0.425）三条链一起报"找不到文件"，
 * 而这三种失败都会伪装成"构建脚本坏了"，白查一轮。
 *
 * 取值规矩（不是"越新越好"）：
 * 1) 优先取**与项目验证过的 8.x 同主版本**里最新的那个 —— 换主版本会顺带换 Roslyn 与
 *    BCL 引用集，那是另一件事，不该由一个 glob 悄悄决定；
 * 2) 没有 8.x 才退而取全局最新，并在 stderr 打一行提示（产物编码口径可能变，要人看一眼）；
 * 3) 一个都没有就抛错，绝不静默返回旧路径。
 */
const fs = require("fs");
const path = require("path");

const DOTNET_ROOT =
  process.env.DOTNET_ROOT ||
  path.join(process.env.ProgramFiles || "C:\\Program Files", "dotnet");

/** "8.0.425" < "8.0.424"？按数字段比，短的那段补 0，剩下的后缀按字典序。 */
function compareVersion(a, b) {
  const pa = a.split(/[.\-+]/);
  const pb = b.split(/[.\-+]/);
  const n = Math.max(pa.length, pb.length);
  for (let i = 0; i < n; i++) {
    const sa = pa[i] === undefined ? "0" : pa[i];
    const sb = pb[i] === undefined ? "0" : pb[i];
    const na = parseInt(sa, 10);
    const nb = parseInt(sb, 10);
    if (Number.isFinite(na) && Number.isFinite(nb)) {
      if (na !== nb) return na - nb;
    } else if (sa !== sb) {
      return sa < sb ? -1 : 1;
    }
  }
  return 0;
}

function subDirs(root) {
  try {
    return fs
      .readdirSync(root, { withFileTypes: true })
      .filter((e) => e.isDirectory())
      .map((e) => e.name);
  } catch (e) {
    return [];
  }
}

/** 从候选目录里挑：先要主版本 == preferredMajor 的最高者，没有就取最高者。 */
function pickVersioned(names, isUsable, preferredMajor, label) {
  const usable = names.filter(isUsable).sort(compareVersion);
  if (usable.length === 0) {
    throw new Error(
      label + "：在 " + DOTNET_ROOT + " 下找不到可用的版本。" +
      "装好 .NET SDK（dotnet --list-sdks）再来跑构建。"
    );
  }
  const sameMajor = usable.filter((v) => parseInt(v, 10) === preferredMajor);
  const chosen = (sameMajor.length > 0 ? sameMajor : usable)[ (sameMajor.length > 0 ? sameMajor : usable).length - 1 ];
  if (parseInt(chosen, 10) !== preferredMajor) {
    console.error(
      "WARN " + label + " 没有 " + preferredMajor + ".x（项目验证过的主版本），改用 " + chosen +
      " —— Roslyn/引用集会跟着变，产物请重新对一遍关键标识符。"
    );
  }
  return chosen;
}

/** csc.dll 全路径（本机最新 8.x SDK 优先）。 */
function cscPath() {
  const sdkRoot = path.join(DOTNET_ROOT, "sdk");
  const hasCsc = (v) => fs.existsSync(path.join(sdkRoot, v, "Roslyn", "bincore", "csc.dll"));
  const ver = pickVersioned(subDirs(sdkRoot), hasCsc, 8, "dotnet SDK");
  return path.join(sdkRoot, ver, "Roslyn", "bincore", "csc.dll");
}

/** Microsoft.NETCore.App.Ref 的 ref/net8.0 目录（T3 回归壳用它编译 net8 可执行文件）。 */
function netCoreRefDir() {
  const packRoot = path.join(DOTNET_ROOT, "packs", "Microsoft.NETCore.App.Ref");
  const hasRef = (v) => fs.existsSync(path.join(packRoot, v, "ref", "net8.0"));
  const ver = pickVersioned(subDirs(packRoot), hasRef, 8, "NETCore.App.Ref");
  return path.join(packRoot, ver, "ref", "net8.0");
}

module.exports = { cscPath, netCoreRefDir, compareVersion, dotnetRoot: DOTNET_ROOT };
