// Gameface CSS lint (Coherent Gameface 2.2.1 as shipped with Cities: Skylines II 1.6.x).
// Fails the build when the emitted CSS uses constructs the game silently rejects (they show up only
// in Logs/UI.log, after a game restart). See docs/RESEARCH-NOTES.md rule 23.
//   node tools/css-lint.js <file-or-dir>...      (CLI; exit code 1 on problems)
//   new CssLintPlugin()                           (webpack; errors fail the compilation)
const fs = require("fs");
const path = require("path");

const SHORTHANDS = new Set([
    "margin", "padding", "border", "border-top", "border-right", "border-bottom", "border-left",
    "border-color", "border-width", "border-style", "border-radius", "background", "font", "transition",
    "animation", "flex", "flex-flow", "outline", "inset", "gap", "place-items", "place-content", "list-style",
    "text-decoration", "columns", "overflow", "grid", "mask", "border-image",
]);

const RULES = [
    { re: /display\s*:\s*(inline-)?grid\b/i, msg: "CSS grid is not supported (use flexbox)" },
    { re: /\bgrid-(template|area|column|row|auto)[\w-]*\s*:/i, msg: "CSS grid properties are not supported" },
    { re: /\bhsla?\s*\(/i, msg: "hsl()/hsla() colours are not supported (use hex/rgba)" },
    { re: /:not\s*\(/i, msg: ":not() is not supported" },
    { re: /:disabled\b/i, msg: ":disabled is not supported (use a class)" },
    { re: /:focus-within\b/i, msg: ":focus-within is not supported" },
    { re: /:(is|where|has)\s*\(/i, msg: ":is()/:where()/:has() are not supported" },
    { re: /(^|[^\w-])(clamp|min|max)\s*\(/i, msg: "clamp()/min()/max() are not supported" },
    { re: /(^|[;{\s])(row-|column-)?gap\s*:/i, msg: "gap is not supported in Gameface flexbox (use margins)" },
    { re: /transition(-property)?\s*:\s*all\b/i, msg: "transitions must name their properties (not 'all')" },
    { re: /(^|[;{\s])transition\s*:/i, msg: "use transition-property/-duration longhands (shorthand is unreliable)" },
    { re: /@supports|@container/i, msg: "@supports/@container are not supported" },
    { re: /aspect-ratio\s*:/i, msg: "aspect-ratio is not supported" },
    { re: /position\s*:\s*sticky/i, msg: "position: sticky is not supported" },
    { re: /\d(\.\d+)?px\b/i, msg: "use rem units (1rem ≈ 1px at 1080p, scales with resolution)" },
];

function lineOf(text, index) {
    let n = 1;
    for (let i = 0; i < index && i < text.length; i++) if (text.charCodeAt(i) === 10) n++;
    return n;
}

/** @returns {{line:number, msg:string, snippet:string}[]} */
function lintCss(css) {
    const problems = [];
    // Blank out comments, keeping offsets (line numbers) intact.
    const text = css.replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, " "));
    for (const rule of RULES) {
        const re = new RegExp(rule.re.source, rule.re.flags.includes("g") ? rule.re.flags : rule.re.flags + "g");
        let m;
        while ((m = re.exec(text))) {
            problems.push({ line: lineOf(text, m.index), msg: rule.msg, snippet: m[0].trim() });
        }
    }
    const decl = /([a-zA-Z-]+)\s*:\s*([^;{}]*var\([^;{}]*)/g;
    let m;
    while ((m = decl.exec(text))) {
        const prop = m[1].toLowerCase();
        if (prop.startsWith("--")) continue;
        if (SHORTHANDS.has(prop)) {
            problems.push({ line: lineOf(text, m.index), msg: `var() inside shorthand '${prop}' is not supported (use longhands)`, snippet: m[0].trim() });
        }
    }
    return problems.sort((a, b) => a.line - b.line);
}

function format(file, problems) {
    return problems.map((p) => `${file}:${p.line}  ${p.msg}  →  ${p.snippet}`).join("\n");
}

class CssLintPlugin {
    apply(compiler) {
        compiler.hooks.thisCompilation.tap("GamefaceCssLint", (compilation) => {
            compilation.hooks.processAssets.tap(
                { name: "GamefaceCssLint", stage: compilation.constructor.PROCESS_ASSETS_STAGE_REPORT || 5000 },
                (assets) => {
                    for (const name of Object.keys(assets)) {
                        if (!name.endsWith(".css")) continue;
                        const src = assets[name].source();
                        const problems = lintCss(typeof src === "string" ? src : src.toString("utf8"));
                        if (problems.length) {
                            const { WebpackError } = compiler.webpack;
                            compilation.errors.push(new WebpackError(`Gameface CSS lint: ${problems.length} problem(s)\n` + format(name, problems)));
                        }
                    }
                },
            );
        });
    }
}

function collect(target, out) {
    const st = fs.statSync(target);
    if (st.isDirectory()) {
        for (const f of fs.readdirSync(target)) collect(path.join(target, f), out);
    } else if (target.endsWith(".css")) out.push(target);
    return out;
}

if (require.main === module) {
    const targets = process.argv.slice(2);
    if (!targets.length) {
        console.error("usage: node tools/css-lint.js <file-or-dir>...");
        process.exit(2);
    }
    let total = 0;
    const files = targets.flatMap((t) => collect(t, []));
    for (const f of files) {
        const problems = lintCss(fs.readFileSync(f, "utf8"));
        total += problems.length;
        if (problems.length) console.error(format(f, problems));
    }
    console.log(`css-lint: ${files.length} file(s), ${total} problem(s)`);
    process.exit(total ? 1 : 0);
}

module.exports = { lintCss, CssLintPlugin };
