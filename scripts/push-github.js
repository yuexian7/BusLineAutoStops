const { execFileSync, execSync } = require("child_process");
const fs = require("fs");
const path = require("path");
const os = require("os");

const cwd = path.join(__dirname, "..");

function git(args, opts) {
  return execFileSync("git", args, Object.assign({ cwd, encoding: "utf8", stdio: "pipe" }, opts || {}));
}

const credOut = execFileSync("cmd", ["/c", "git", "credential", "fill"], {
  input: "protocol=https\nhost=github.com\n\n",
  encoding: "utf8",
});
const user = (credOut.match(/username=(.+)/) || [])[1].trim();
const pass = (credOut.match(/password=(.+)/) || [])[1].trim();
console.log("push as", user, "token_len", pass.length);

try {
  git(["remote", "remove", "origin"]);
} catch (e) {}
console.log(git(["remote", "add", "origin", "https://github.com/yuexian7/BusLineAutoStops.git"]));

// credential helper via command line (does not write git config)
const helper = `!f() { printf 'username=%s\\npassword=%s\\n' '${user}' '${pass}'; }; f`;

try {
  execFileSync("git", ["-c", "credential.helper=" + helper, "push", "-u", "origin", "main"], {
    cwd,
    encoding: "utf8",
    stdio: "inherit",
    timeout: 120000,
  });
  console.log("PUSH OK");
} catch (e) {
  console.error("PUSH FAIL", e.status, e.message);
  process.exit(1);
}

// 回读远端
try {
  const ls = git(["ls-remote", "--heads", "origin"]);
  console.log("remote heads:\n" + ls);
} catch (e) {
  console.log("ls-remote fail", e.message);
}
