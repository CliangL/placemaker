// Bakes a colour into our monochrome line icons at build time: "icon.svg?tint=dark" → every #ffffff
// stroke/fill becomes the tint's colour. Gameface renders <img src="*.svg"> reliably; CSS mask-image with
// currentColor (what we used before) showed nothing in game, so colour variants are separate files.
const TINTS = require("./icon-tints.json");

module.exports = function svgTintLoader(source) {
    const q = new URLSearchParams((this.resourceQuery || "").replace(/^\?/, ""));
    const tint = q.get("tint") || "light";
    const color = TINTS[tint];
    if (!color) {
        this.emitError(new Error(`unknown icon tint '${tint}' (tools/icon-tints.json)`));
        return source;
    }
    return String(source).replace(/#fff(fff)?\b/gi, color);
};
