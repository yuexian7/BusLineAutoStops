/**
 * 构建与部署的本机绝对路径 —— 只在这一处写（v0.11.2 审查 P1-3）。
 *
 * 为什么要抽出来：`deploy-local.js` 往哪儿铺、`verify.cjs --deployed` 查哪儿，必须是同一个目录。
 * 两处各写一遍绝对路径，改一次就要赌没人忘；2026-10-12 那次"部署目录被 dotnet build 重铺、
 * 前端产物全没"如果当时有第二处真值可查，事故当场就会被脚本发现。
 *
 * 关于 CSII_LOCALMODSPATH：它指向 `LocalLow\Colossal Order\Cities Skylines II\Mods`，
 * 而那是下面这条 F: 路径的 junction（两个路径 stat 出来同一 inode）。这里**故意不读环境变量**：
 * deploy-local.js 从来都是直接铺 F: 这条，检查脚本跟着它走，不随某个进程的环境差异改判据。
 */
const path = require("path");

const MOD_NAME = "BusLineAutoStops";

/** 游戏安装根目录（1.6.0f1）。 */
const GAME_DIR = "F:\\SteamLibrary\\steamapps\\common\\Cities Skylines II";

/** 游戏托管 DLL（反编译/引用都来自这里）。 */
const MANAGED_DIR = path.join(GAME_DIR, "Cities2_Data", "Managed");

/** 模组部署根目录。 */
const MODS_DIR = path.join(GAME_DIR, "Cities Skylines II", "Mods");

/** 本模组的部署目录 —— 上传内容 = 这个目录原样（不含 ModsData 运行数据）。 */
const DEPLOY_DIR = path.join(MODS_DIR, MOD_NAME);

/**
 * 部署目录应当**只有**这几项：多一项就是发过不该发的东西（0Harmony.dll / *.pdb / 旧 mjs），
 * 少一项就是前端或图标没铺上。deploy-local.js 用它做白名单清理，verify.cjs --deployed 用它做完整性断言。
 */
const DEPLOY_ENTRIES = [MOD_NAME + ".dll", MOD_NAME + ".mjs", "mod.json", "images"];

module.exports = { MOD_NAME, GAME_DIR, MANAGED_DIR, MODS_DIR, DEPLOY_DIR, DEPLOY_ENTRIES };
