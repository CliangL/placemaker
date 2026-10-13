// Small 2D geometry kit for the mock (XZ plane, metres).
export type V2 = [number, number];

export const add = (a: V2, b: V2): V2 => [a[0] + b[0], a[1] + b[1]];
export const sub = (a: V2, b: V2): V2 => [a[0] - b[0], a[1] - b[1]];
export const mul = (a: V2, k: number): V2 => [a[0] * k, a[1] * k];
export const len = (a: V2) => Math.hypot(a[0], a[1]);
export const dist = (a: V2, b: V2) => Math.hypot(a[0] - b[0], a[1] - b[1]);
export const lerp = (a: V2, b: V2, t: number): V2 => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];

export function signedArea(ring: V2[]) {
    let s = 0;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) s += ring[j][0] * ring[i][1] - ring[i][0] * ring[j][1];
    return s / 2;
}
export const area = (ring: V2[]) => Math.abs(signedArea(ring));

export function perimeter(ring: V2[]) {
    let p = 0;
    for (let i = 0; i < ring.length; i++) p += dist(ring[i], ring[(i + 1) % ring.length]);
    return p;
}

export function centroid(ring: V2[]): V2 {
    let a = 0, cx = 0, cy = 0;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
        const f = ring[j][0] * ring[i][1] - ring[i][0] * ring[j][1];
        a += f;
        cx += (ring[j][0] + ring[i][0]) * f;
        cy += (ring[j][1] + ring[i][1]) * f;
    }
    if (Math.abs(a) < 1e-9) {
        const n = ring.length || 1;
        return [ring.reduce((s, p) => s + p[0], 0) / n, ring.reduce((s, p) => s + p[1], 0) / n];
    }
    return [cx / (3 * a), cy / (3 * a)];
}

export function bbox(ring: V2[]) {
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const [x, y] of ring) {
        x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y);
    }
    return { x0, y0, x1, y1, w: x1 - x0, h: y1 - y0 };
}

export function inside(p: V2, ring: V2[]) {
    let c = false;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
        const a = ring[i], b = ring[j];
        if ((a[1] > p[1]) !== (b[1] > p[1]) && p[0] < ((b[0] - a[0]) * (p[1] - a[1])) / (b[1] - a[1]) + a[0]) c = !c;
    }
    return c;
}

export function distToSeg(p: V2, a: V2, b: V2) {
    const ab = sub(b, a);
    const l2 = ab[0] * ab[0] + ab[1] * ab[1];
    const t = l2 > 1e-9 ? Math.max(0, Math.min(1, ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1]) / l2)) : 0;
    return dist(p, [a[0] + ab[0] * t, a[1] + ab[1] * t]);
}

export function distToRing(p: V2, ring: V2[]) {
    let d = Infinity;
    for (let i = 0; i < ring.length; i++) d = Math.min(d, distToSeg(p, ring[i], ring[(i + 1) % ring.length]));
    return d;
}

export function closestOnRing(p: V2, ring: V2[]): { seg: number; t: number; d: number; q: V2 } {
    let best = { seg: -1, t: 0, d: Infinity, q: p as V2 };
    for (let i = 0; i < ring.length; i++) {
        const a = ring[i], b = ring[(i + 1) % ring.length];
        const ab = sub(b, a);
        const l2 = ab[0] * ab[0] + ab[1] * ab[1];
        const t = l2 > 1e-9 ? Math.max(0, Math.min(1, ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1]) / l2)) : 0;
        const q: V2 = [a[0] + ab[0] * t, a[1] + ab[1] * t];
        const d = dist(p, q);
        if (d < best.d) best = { seg: i, t, d, q };
    }
    return best;
}

function segIntersect(a: V2, b: V2, c: V2, d: V2) {
    const o = (p: V2, q: V2, r: V2) => (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0]);
    const d1 = o(a, b, c), d2 = o(a, b, d), d3 = o(c, d, a), d4 = o(c, d, b);
    return d1 * d2 < 0 && d3 * d4 < 0;
}

export function selfIntersects(ring: V2[]) {
    const n = ring.length;
    for (let i = 0; i < n; i++)
        for (let j = i + 1; j < n; j++) {
            if (Math.abs(i - j) <= 1 || (i === 0 && j === n - 1)) continue;
            if (segIntersect(ring[i], ring[(i + 1) % n], ring[j], ring[(j + 1) % n])) return true;
        }
    return false;
}

/** Sutherland–Hodgman: keep the part of `poly` on the left of the directed line a→b. */
export function clipHalfPlane(poly: V2[], a: V2, b: V2): V2[] {
    const side = (p: V2) => (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]);
    const out: V2[] = [];
    for (let i = 0; i < poly.length; i++) {
        const p = poly[i], q = poly[(i + 1) % poly.length];
        const sp = side(p), sq = side(q);
        if (sp >= 0) out.push(p);
        if (sp * sq < 0) out.push(lerp(p, q, sp / (sp - sq)));
    }
    return out;
}

/** Clip any polygon by a convex window (CCW). */
export function clipConvex(poly: V2[], window: V2[]): V2[] {
    let out = poly;
    const ccw = signedArea(window) > 0 ? window : window.slice().reverse();
    for (let i = 0; i < ccw.length && out.length; i++) out = clipHalfPlane(out, ccw[i], ccw[(i + 1) % ccw.length]);
    return out;
}

export function rotate(p: V2, c: V2, ang: number): V2 {
    const s = Math.sin(ang), k = Math.cos(ang);
    const d = sub(p, c);
    return [c[0] + d[0] * k - d[1] * s, c[1] + d[0] * s + d[1] * k];
}

/** Shrink toward the centroid by roughly `d` metres (good enough for convex-ish mock shapes). */
export function shrink(ring: V2[], d: number): V2[] {
    const c = centroid(ring);
    return ring.map((p) => {
        const v = sub(p, c);
        const l = len(v);
        return l > d + 0.5 ? add(c, mul(v, (l - d) / l)) : c;
    });
}

/** Ramer–Douglas–Peucker on a closed ring. */
export function simplify(ring: V2[], eps: number): V2[] {
    if (ring.length < 5) return ring;
    const rdp = (pts: V2[]): V2[] => {
        if (pts.length < 3) return pts;
        let maxD = 0, idx = 0;
        for (let i = 1; i < pts.length - 1; i++) {
            const d = distToSeg(pts[i], pts[0], pts[pts.length - 1]);
            if (d > maxD) { maxD = d; idx = i; }
        }
        if (maxD <= eps) return [pts[0], pts[pts.length - 1]];
        const l = rdp(pts.slice(0, idx + 1));
        const r = rdp(pts.slice(idx));
        return l.slice(0, -1).concat(r);
    };
    // split at the farthest point from ring[0] so the closed ring simplifies symmetrically
    let far = 0, fd = 0;
    ring.forEach((p, i) => { const d = dist(p, ring[0]); if (d > fd) { fd = d; far = i; } });
    const a = rdp(ring.slice(0, far + 1));
    const b = rdp(ring.slice(far).concat([ring[0]]));
    return a.slice(0, -1).concat(b.slice(0, -1));
}

/** Seeded PRNG (mulberry32). */
export function rng(seed: number) {
    let s = seed >>> 0 || 1;
    return () => {
        s = (s + 0x6d2b79f5) >>> 0;
        let t = s;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

export function hash(s: string) {
    let h = 2166136261 >>> 0;
    for (let i = 0; i < s.length; i++) {
        h ^= s.charCodeAt(i);
        h = Math.imul(h, 16777619) >>> 0;
    }
    return h;
}
