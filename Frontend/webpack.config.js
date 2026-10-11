const path = require("path");
const webpack = require("webpack");
const MOD = require("./mod.json");

// 游戏侧 Colossal.IO.AssetDatabase 会读 .mjs 顶部的这段横幅注释来解析 UIModuleAsset.moduleInfo
// （Id/Author/Version/Dependencies）。缺它 → Modding.log 里 m_ModuleId:null。
// 对照 MoveIt / TTE 的产物：`/*! Cities: Skylines II UI Module ... */`。
const banner = `/*! 
 * Cities: Skylines II UI Module
 *
 * Id: ${MOD.id}
 * Author: ${MOD.author}
 * Version: ${MOD.version}
 * Dependencies: ${MOD.dependencies.join(",")}
 */`;

// 与 Anarchy / FillItMod / TTE 一致：
// externalsType=window ⇒ 打进 window.React / window["cs2/api"]，禁止裸 ESM import（游戏 loader 解析不了）。
module.exports = {
  mode: "production",
  stats: "errors-warnings",
  entry: { [MOD.id]: "./src/index.tsx" },
  plugins: [
    new webpack.BannerPlugin({ banner, raw: true, entryOnly: true, test: /\.m?js$/ }),
  ],
  externalsType: "window",
  externals: {
    react: "React",
    "react-dom": "ReactDOM",
    "cs2/modding": "cs2/modding",
    "cs2/api": "cs2/api",
    "cs2/bindings": "cs2/bindings",
    "cs2/l10n": "cs2/l10n",
    "cs2/ui": "cs2/ui",
    "cohtml/cohtml": "cohtml/cohtml",
  },
  module: {
    rules: [
      { test: /\.tsx?$/, use: "ts-loader", exclude: /node_modules/ },
      { test: /\.svg$/i, type: "asset/inline" },
    ],
  },
  resolve: { extensions: [".tsx", ".ts", ".js"] },
  output: {
    path: path.resolve(__dirname, "dist"),
    filename: "[name].mjs",
    library: { type: "module" },
    publicPath: "coui://ui-mods/",
  },
  experiments: { outputModule: true },
  // 不压缩：Terser 的 extractComments 会把 /*! 横幅抽到 *.LICENSE.txt，
  // 游戏解析 UIModuleAsset.moduleInfo 就看不到 Id/Author/Version 了（实机 m_ModuleId:null）。
  // 本模组 bundle 只有几 KB，关掉压缩没有代价。
  optimization: { minimize: false },
  performance: { hints: false },
};
