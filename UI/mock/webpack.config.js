// Browser build of the real panel + mock backend. cs2/* modules are aliased to mock implementations.
const path = require("path");
const base = require("../webpack.config");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const { CssLintPlugin } = require("../tools/css-lint");

module.exports = {
    ...base,
    mode: "development",
    devtool: "source-map",
    entry: { mock: path.resolve(__dirname, "main.tsx") },
    externalsType: undefined,
    externals: {},
    experiments: {},
    module: {
        rules: base.module.rules.map((r) =>
            String(r.test).includes("tsx") ? { ...r, use: { loader: "ts-loader", options: { transpileOnly: true } } } : r,
        ),
    },
    resolve: {
        ...base.resolve,
        alias: {
            ...base.resolve.alias,
            "cs2/api": path.resolve(__dirname, "cs2/api.ts"),
            "cs2/ui": path.resolve(__dirname, "cs2/ui.tsx"),
            "cs2/l10n": path.resolve(__dirname, "cs2/l10n.tsx"),
            "cs2/modding": path.resolve(__dirname, "cs2/modding.ts"),
        },
    },
    output: {
        path: path.resolve(__dirname, "dist"),
        filename: "[name].js",
        publicPath: "dist/",
        clean: true,
    },
    optimization: { minimize: false },
    plugins: [new MiniCssExtractPlugin(), new CssLintPlugin()],
    stats: "errors-warnings",
};
