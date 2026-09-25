const path = require("path");
const MOD = require("./mod.json");

module.exports = {
  mode: "production",
  stats: "errors-warnings",
  entry: { [MOD.id]: "./src/index.tsx" },
  externalsType: "module",
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
  performance: { hints: false },
};
