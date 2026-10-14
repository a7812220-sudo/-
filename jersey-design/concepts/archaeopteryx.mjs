// ARCHAEOPTERYX — "Grain-lit sculpture" (concept B).
// The Berlin specimen, worn. BACK (dorsal view): the fossil's death pose. The head is thrown back on an S-curved neck
// above the right shoulder blade and is the only full-white focal point. The wings are raised in a V along the
// shoulder line, with woven feather fans hanging down the raglan sleeves. A low-key narrow ribcage leaves room for a
// number, then come a trabecular-lattice pelvis, zig-zag legs with crisp clawed feet, and the long bony frond tail
// (22 caudals, a woven feather pair on each from the 6th) running down the spine line and dissolving before the hem.
// FRONT (ventral view): a robust boomerang wishbone hung under the V-neck as the brightest object, the same raised
// wings with three separate clawed fingers on both sleeves, and a soft herringbone of gastralia (belly ribs, no keeled
// sternum) whose midline crossings interlock, fading out by mid-torso.
// Bones are height fields lit by one key light (upper left) and a weak rim light (upper right), printed only as grain
// (ref1): fine stipple on lit faces, sparse grain on shadow faces that melts into the black fabric, and crisp tapered
// hairlines where a rim or crest catches the light. Feathers are woven strands (ref7). Steel ball joints and surgical
// pins (ref4) mark the shoulder, elbow, wrist, hip and knee. There is no contour or iso-line texture anywhere: the only
// outline extraction is a single threshold of the bone field, used to place the rim hairlines.
import { rng, noise2D, bezier } from '../lib/art.mjs';
import { BODY } from '../lib/jersey.mjs';
import { contours } from 'd3-contour';

// ------------------------------------------------------------------ math
const clamp = (v, a = 0, b = 1) => (v < a ? a : v > b ? b : v);
const lerp = (a, b, t) => a + (b - a) * t;
const sstep = (a, b, v) => { const t = clamp((v - a) / (b - a)); return t * t * (3 - 2 * t); };
const n2 = ([x, y]) => { const l = Math.hypot(x, y) || 1; return [x / l, y / l]; };
const n3 = ([x, y, z]) => { const l = Math.hypot(x, y, z) || 1; return [x / l, y / l, z / l]; };
const add = (a, b) => [a[0] + b[0], a[1] + b[1]];
const sub = (a, b) => [a[0] - b[0], a[1] - b[1]];
const mul = (a, s) => [a[0] * s, a[1] * s];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1];
const dot3 = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const rot = ([x, y], a) => [x * Math.cos(a) - y * Math.sin(a), x * Math.sin(a) + y * Math.cos(a)];
const lerp2 = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t)];
const deg = Math.PI / 180;
const f1 = v => (Math.round(v * 10) / 10).toString();
const mirrorX = p => [1000 - p[0], p[1]];

// ------------------------------------------------------------------ light (shared by both garments and both faces)
const LIGHT = n3([-0.55, -0.70, 0.55]);
const HALF = n3([LIGHT[0], LIGHT[1], LIGHT[2] + 1]);
const LXY = n2([LIGHT[0], LIGHT[1]]);
const TAN_ELEV = LIGHT[2] / Math.hypot(LIGHT[0], LIGHT[1]);
const RIM = n3([0.66, -0.5, 0.42]);
const RXY = n2([RIM[0], RIM[1]]);
const RIM_K = 0.35;                       // the rim light is at most 35% of the key
const HEM_Y = 1080;                       // all art is gone by here; the hem band starts at 1118
const hemFade = y => 1 - sstep(1035, HEM_Y, y);

// ------------------------------------------------------------------ curves
// Catmull-Rom through control points, roughly `spacing` units between samples.
function spline(ctrl, spacing = 2.5) {
  const n = ctrl.length;
  if (n === 2) {
    const [a, b] = ctrl, m = Math.max(1, Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1]) / spacing));
    return Array.from({ length: m + 1 }, (_, k) => lerp2(a, b, k / m));
  }
  const P = i => ctrl[Math.max(0, Math.min(n - 1, i))];
  const out = [];
  for (let i = 0; i < n - 1; i++) {
    const p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
    const m = Math.max(1, Math.ceil(Math.hypot(p2[0] - p1[0], p2[1] - p1[1]) / spacing));
    for (let k = 0; k < m; k++) {
      const t = k / m, t2 = t * t, t3 = t2 * t;
      out.push([0, 1].map(j => 0.5 * (2 * p1[j] + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2 +
        (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3)));
    }
  }
  out.push(ctrl[n - 1].slice());
  return out;
}
function cumLen(pts) {
  const c = [0];
  for (let i = 1; i < pts.length; i++) c.push(c[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]));
  return c;
}
// point + unit tangent at arc length s
function at(pts, cum, s) {
  const tot = cum[cum.length - 1];
  s = clamp(s, 0, tot);
  let i = 1;
  while (i < pts.length - 1 && cum[i] < s) i++;
  const a = pts[i - 1], b = pts[i], u = (s - cum[i - 1]) / ((cum[i] - cum[i - 1]) || 1);
  return [lerp2(a, b, u), n2(sub(b, a))];
}
// sub-polyline between arc lengths s0..s1
function slice(pts, cum, s0, s1, spacing = 1.5) {
  const m = Math.max(2, Math.ceil((s1 - s0) / spacing));
  return Array.from({ length: m + 1 }, (_, k) => at(pts, cum, lerp(s0, s1, k / m))[0]);
}
// arc of a curved element (claws, feathers): start, initial direction, length, total turn (radians)
function arcPts(p, dir, len, turn, n = 14) {
  const out = [p];
  let d = n2(dir), q = p;
  for (let i = 1; i <= n; i++) {
    d = rot(d, turn / n);
    q = add(q, mul(d, len / n));
    out.push(q);
  }
  return out;
}

// ------------------------------------------------------------------ polygons
function polyBox(poly) {
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (const [x, y] of poly) { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
  return [x0, y0, x1, y1];
}
function inPoly(x, y, poly) {
  let c = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const [xi, yi] = poly[i], [xj, yj] = poly[j];
    if ((yi > y) !== (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c;
  }
  return c;
}
function sd2(px, py, ax, ay, bx, by) {
  const dx = bx - ax, dy = by - ay;
  const t = clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy || 1));
  const ex = px - ax - t * dx, ey = py - ay - t * dy;
  return ex * ex + ey * ey;
}
function polyDist(x, y, poly) {
  let m = Infinity;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) m = Math.min(m, sd2(x, y, poly[j][0], poly[j][1], poly[i][0], poly[i][1]));
  return Math.sqrt(m);
}
function ellipsePoly(cx, cy, rx, ry, ang = 0, n = 36) {
  return Array.from({ length: n }, (_, i) => {
    const t = i / n * Math.PI * 2;
    const [x, y] = rot([rx * Math.cos(t), ry * Math.sin(t)], ang);
    return [cx + x, cy + y];
  });
}
// parse the jersey outline (M/C/L/Z only) into a polygon
function pathPoly(d) {
  const tok = d.match(/[MCLZ]|-?\d*\.?\d+/g);
  const out = [];
  let i = 0, cur = [0, 0], cmd = '';
  while (i < tok.length) {
    if (/[MCLZ]/.test(tok[i])) cmd = tok[i++];
    if (cmd === 'Z') continue;
    if (cmd === 'M' || cmd === 'L') { cur = [+tok[i], +tok[i + 1]]; i += 2; out.push(cur); }
    else if (cmd === 'C') {
      const b = [cur, [+tok[i], +tok[i + 1]], [+tok[i + 2], +tok[i + 3]], [+tok[i + 4], +tok[i + 5]]];
      i += 6;
      out.push(...bezier(b, 24).slice(1));
      cur = b[3];
    }
  }
  return out;
}

// ------------------------------------------------------------------ height field
const G = 1;                      // grid step (units)
const NX = 1001, NY = 1201, N = NX * NY;
const MAT = { NONE: 0, BONE: 1, SOFT: 2 };   // SOFT: tonal grain only (no hairlines, capped tone, no erosion)
const CAP = 0.45;                 // flattened joint ends (fraction of the end radius) so phalanges show real gaps
const GAP = 2;                    // visible gap between phalanges

function newField() {
  return {
    H: new Float32Array(N), I: new Float32Array(N), M: new Uint8Array(N),
    T: new Float32Array(N), TI: new Float32Array(N), TM: new Uint8Array(N),
    bb: [NX, NY, -1, -1], pend: [], crests: [], feathers: [], accents: [],
  };
}
function box(F, x0, y0, x1, y1) {
  const i0 = Math.max(0, Math.floor(x0)), j0 = Math.max(0, Math.floor(y0));
  const i1 = Math.min(NX - 1, Math.ceil(x1)), j1 = Math.min(NY - 1, Math.ceil(y1));
  const b = F.bb;
  if (i0 < b[0]) b[0] = i0; if (j0 < b[1]) b[1] = j0; if (i1 > b[2]) b[2] = i1; if (j1 > b[3]) b[3] = j1;
  return [i0, j0, i1, j1];
}
function put(F, k, h, inten, mat) {
  if (h > F.T[k]) { F.T[k] = h; F.TI[k] = inten; F.TM[k] = mat; }
}
const smax = (a, b, k) => { const h = Math.max(k - Math.abs(a - b), 0) / k; return Math.max(a, b) + h * h * k * 0.25; };
// merge the scratch group into the scene; z lifts the group above what lies under it.
function commit(F, { z = 0, k = 0 } = {}) {
  const [i0, j0, i1, j1] = F.bb;
  for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
    const q = j * NX + i, t = F.T[q];
    if (t <= 0) continue;
    const tz = t + z * sstep(0, 1.2, t);
    const h0 = F.H[q];
    if (tz >= h0) { F.I[q] = F.TI[q]; F.M[q] = F.TM[q]; }
    F.H[q] = k > 0 ? smax(h0, tz, k) : Math.max(h0, tz);
    F.T[q] = 0; F.TI[q] = 0; F.TM[q] = 0;
  }
  F.bb = [NX, NY, -1, -1];
  for (const c of F.pend) F.crests.push({ ...c, z });
  F.pend = [];
}
const val = (v, t) => (typeof v === 'function' ? v(t) : v);

// tube along a polyline; rf(t) radius, round (cylinder) or crest (blade) profile. `cap` < 1 flattens the two end caps.
// hair > 0 adds a crest hairline; its options: color, off (offset toward the lit side, in radii), tipT, hi (highlight),
// even (same weight whatever the bone's angle to the light, for tiny parts such as teeth).
function tube(F, pts, rf, { amp = 0.9, prof = 'round', inten = 1, mat = MAT.BONE, cap = 1, hair = 0, ...crest } = {}) {
  const cum = cumLen(pts), tot = cum[cum.length - 1] || 1, last = pts.length - 2;
  if (hair) F.pend.push({ pts, cum, rf, amp, prof, hair, inten, off: 0, tipT: 0.93, ...crest });
  for (let s = 0; s <= last; s++) {
    const a = pts[s], b = pts[s + 1], ta = cum[s] / tot, tb = cum[s + 1] / tot;
    let rm = 0;
    for (let k = 0; k <= 4; k++) rm = Math.max(rm, rf(lerp(ta, tb, k / 4)));
    const [i0, j0, i1, j1] = box(F, Math.min(a[0], b[0]) - rm - 1, Math.min(a[1], b[1]) - rm - 1, Math.max(a[0], b[0]) + rm + 1, Math.max(a[1], b[1]) + rm + 1);
    const dx = b[0] - a[0], dy = b[1] - a[1], ll = dx * dx + dy * dy || 1, sl = Math.sqrt(ll);
    for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
      const px = i * G, py = j * G;
      const uRaw = ((px - a[0]) * dx + (py - a[1]) * dy) / ll, u = clamp(uRaw);
      const t = lerp(ta, tb, u), r = rf(t);
      const over = s === 0 && uRaw < 0 ? -uRaw * sl : s === last && uRaw > 1 ? (uRaw - 1) * sl : 0;
      let q;
      if (over > 0 && cap < 1) q = Math.hypot(Math.abs(dx * (py - a[1]) - dy * (px - a[0])) / sl / r, over / (r * cap));
      else q = Math.hypot(px - a[0] - u * dx, py - a[1] - u * dy) / r;
      if (q >= 1) continue;
      const h = prof === 'round' ? r * amp * Math.sqrt(1 - q * q) : r * amp * (1 - Math.pow(q, 1.15));
      put(F, j * NX + i, h, val(inten, t), mat);
    }
  }
}
// rotated ellipsoid
function blob(F, c, rx, ry, ang, { amp = 1, inten = 1, mat = MAT.BONE } = {}) {
  const R = Math.max(rx, ry);
  const [i0, j0, i1, j1] = box(F, c[0] - R - 1, c[1] - R - 1, c[0] + R + 1, c[1] + R + 1);
  const ca = Math.cos(-ang), sa = Math.sin(-ang);
  for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
    const x = i * G - c[0], y = j * G - c[1];
    const u = (x * ca - y * sa) / rx, v = (x * sa + y * ca) / ry;
    const q = u * u + v * v;
    if (q >= 1) continue;
    put(F, j * NX + i, Math.min(rx, ry) * amp * Math.sqrt(1 - q), val(inten, 0), mat);
  }
}
// pillowed polygon (flat bone plate with rounded edges)
function plate(F, poly, { A = 6, R = 5, inten = 1, mat = MAT.BONE, mod } = {}) {
  const [x0, y0, x1, y1] = polyBox(poly);
  const [i0, j0, i1, j1] = box(F, x0 - 1, y0 - 1, x1 + 1, y1 + 1);
  for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
    const x = i * G, y = j * G;
    if (!inPoly(x, y, poly)) continue;
    const s = Math.min(1, polyDist(x, y, poly) / R);
    let h = A * Math.sqrt(1 - (1 - s) * (1 - s));
    if (mod) h *= mod(x, y);
    put(F, j * NX + i, h, val(inten, 0), mat);
  }
}
// cut a hole with a rounded rim into the scratch group
function carve(F, poly, R = 3) {
  const [x0, y0, x1, y1] = polyBox(poly);
  const i0 = Math.max(0, Math.floor(x0 - R - 1)), j0 = Math.max(0, Math.floor(y0 - R - 1));
  const i1 = Math.min(NX - 1, Math.ceil(x1 + R + 1)), j1 = Math.min(NY - 1, Math.ceil(y1 + R + 1));
  for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
    const q = j * NX + i;
    if (F.T[q] <= 0) continue;
    const x = i * G, y = j * G;
    if (inPoly(x, y, poly)) { F.T[q] = 0; continue; }
    const d = polyDist(x, y, poly);
    if (d < R) { const s = d / R; F.T[q] *= Math.sqrt(1 - (1 - s) * (1 - s)); }
  }
}
// trabecular lattice: voronoi cells hollowed out of a plate, leaving rounded struts (ref5)
function lattice(F, poly, seeds, { strut = 3.2, R = 2.4, border = 5 } = {}) {
  const [x0, y0, x1, y1] = polyBox(poly);
  for (let j = Math.max(0, Math.floor(y0)); j <= Math.min(NY - 1, Math.ceil(y1)); j++)
    for (let i = Math.max(0, Math.floor(x0)); i <= Math.min(NX - 1, Math.ceil(x1)); i++) {
      const q = j * NX + i;
      if (F.T[q] <= 0) continue;
      const x = i * G, y = j * G;
      if (!inPoly(x, y, poly)) continue;
      const bd = polyDist(x, y, poly);
      if (bd < border) continue;
      let d1 = Infinity, d2 = Infinity;
      for (const [sx, sy] of seeds) {
        const d = Math.hypot(x - sx, y - sy);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
      }
      const e = Math.min((d2 - d1) / 2 - strut / 2, bd - border);
      if (e <= 0) continue;
      const s = Math.min(1, e / R);
      F.T[q] *= 1 - Math.sqrt(1 - (1 - s) * (1 - s));
    }
}
// add a smooth bump to the scratch group (only where something is already drawn)
function bump(F, c, rx, ry, ang, amp) {
  const R = Math.max(rx, ry);
  const i0 = Math.max(0, Math.floor(c[0] - R)), j0 = Math.max(0, Math.floor(c[1] - R));
  const i1 = Math.min(NX - 1, Math.ceil(c[0] + R)), j1 = Math.min(NY - 1, Math.ceil(c[1] + R));
  const ca = Math.cos(-ang), sa = Math.sin(-ang);
  for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
    const q = j * NX + i;
    if (F.T[q] <= 0) continue;
    const x = i * G - c[0], y = j * G - c[1];
    const u = (x * ca - y * sa) / rx, v = (x * sa + y * ca) / ry;
    const w = 1 - (u * u + v * v);
    if (w > 0) F.T[q] += amp * Math.sqrt(w);
  }
}

// ------------------------------------------------------------------ bone vocabulary
// long-bone radius profile: shaft r0->r1 with knobby epiphyses
const longR = (r0, r1, k0 = 1.45, k1 = 1.35, w = 0.11) => t =>
  lerp(r0, r1, t) * (1 + (k0 - 1) * Math.exp(-((t / w) ** 2)) + (k1 - 1) * Math.exp(-(((1 - t) / w) ** 2)));
const taperR = (r0, r1, p = 1) => t => lerp(r0, r1, t ** p);
const hourR = (re, rw) => t => rw + (re - rw) * Math.abs(2 * t - 1) ** 2.2;
// how far a flat-capped bone end must stand back from a joint centre to leave a clear gap
const trim = rEnd => GAP / 2 + CAP * rEnd;

function longBone(F, a, b, r0, r1, { bow = 0, k0, k1, w, ...o } = {}) {
  const m = add(mul(add(a, b), 0.5), mul(n2([-(b[1] - a[1]), b[0] - a[0]]), bow));
  tube(F, spline([a, m, b], 2), longR(r0, r1, k0, k1, w), o);
}
// curved claw from base along dir, curling by `turn`; every claw carries a crisp crest hairline with a light tip
function claw(F, base, dir, len, turn, r0, o = {}) {
  const pts = arcPts(base, dir, len, turn, 16);
  tube(F, pts, t => Math.max(0.55, r0 * (1 - t) ** 0.95 * (1 + 0.45 * Math.exp(-((t / 0.2) ** 2)))),
    { cap: CAP, ...o, hair: 1.0, color: '#e2e2e0', tipT: 0.985 });
  return pts;
}
// chain of phalanges with open joint gaps, ending in an ungual claw; returns the claw's centre line
function digit(F, base, dir, lens, r, clawLen, turn, { bend = 0, ...o } = {}) {
  let p = base, d = n2(dir);
  for (let i = 0; i < lens.length; i++) {
    const ri = r * (1 - i * 0.08), q = add(p, mul(d, lens[i]));
    longBone(F, add(p, mul(d, trim(ri * 1.35))), sub(q, mul(d, trim(ri * 0.9 * 1.3))), ri, ri * 0.9, { k0: 1.35, k1: 1.3, w: 0.16, cap: CAP, ...o });
    p = q;
    d = rot(d, bend);
  }
  return claw(F, add(p, mul(d, trim(r * 1.15 * 1.45))), d, clawLen, turn, r * 1.15, o);
}

// ------------------------------------------------------------------ accents (ref4): steel ball joints and surgical pins
function ball(F, p, r) { F.accents.push({ t: 'ball', p, r }); }
function pin(F, p, dir) { F.accents.push({ t: 'pin', p, d: n2(dir) }); }

// ------------------------------------------------------------------ feathers (ref7): woven strands, drawn in render()
// a feather impression: rachis polyline plus vane half-widths left/right of the rachis direction
function feather(F, pts, wl, wr, gain) {
  const prof = w => t => w * Math.sin(Math.PI * clamp(t * 1.01)) ** 0.5 * (0.75 + 0.25 * t);
  F.feathers.push({ pts: spline(pts, 3.2), wl: prof(wl), wr: prof(wr), kl: wl > 7 ? 12 : 10, kr: wr > 7 ? 12 : 10, gain });
}

// ------------------------------------------------------------------ skull (lateral view, local u/v frame)
// u: 0 occiput -> 1 snout tip, v: up (dorsal), both in units of skull length L.
const SKULL = {
  cranium: [[0.0, -0.05], [-0.006, 0.03], [0.02, 0.11], [0.07, 0.175], [0.14, 0.215], [0.23, 0.232], [0.32, 0.218],
    [0.42, 0.178], [0.55, 0.132], [0.68, 0.092], [0.8, 0.057], [0.9, 0.027], [0.965, 0.002], [1.0, -0.028],
    [0.985, -0.055], [0.93, -0.066], [0.8, -0.076], [0.65, -0.086], [0.5, -0.096], [0.38, -0.104], [0.27, -0.112],
    [0.19, -0.13], [0.13, -0.155], [0.085, -0.165], [0.05, -0.125], [0.02, -0.09]],
  orbit: { c: [0.255, 0.066], r: 0.094 },
  temporal: { c: [0.112, -0.02], rx: 0.038, ry: 0.07 },
  antorbital: [[0.385, -0.07], [0.6, -0.066], [0.565, -0.022], [0.47, 0.072], [0.415, 0.088], [0.372, 0.03]],
  maxFen: { c: [0.645, -0.038], rx: 0.026, ry: 0.017 },
  naris: { c: [0.8, 0.012], rx: 0.085, ry: 0.021, a: -15 },
  mandible: [[0.075, -0.16], [0.13, -0.136], [0.25, -0.124], [0.4, -0.11], [0.55, -0.1], [0.7, -0.09], [0.85, -0.08],
    [0.95, -0.071], [0.988, -0.076], [0.97, -0.096], [0.9, -0.11], [0.75, -0.126], [0.58, -0.146], [0.42, -0.17],
    [0.28, -0.195], [0.17, -0.205], [0.1, -0.2], [0.065, -0.186]],
  mandFen: { c: [0.31, -0.158], rx: 0.065, ry: 0.017, a: 4 },
};
function skull(F, { condyle, dir, L, z = 10, open = 8, inten = 1 }) {
  const a = n2(dir);
  let up = [a[1], -a[0]];
  if (up[1] > 0) up = mul(up, -1);
  // origin so that the occipital condyle (u .03, v -.075) lands on `condyle`
  const O = sub(condyle, add(mul(a, 0.03 * L), mul(up, -0.075 * L)));
  const W = ([u, v]) => add(O, add(mul(a, u * L), mul(up, v * L)));
  const ell = (e, n = 30) => ellipsePoly(e.c[0], e.c[1], e.rx ?? e.r, e.ry ?? e.r, (e.a ?? 0) * deg, n).map(W);
  const hinge = [0.09, -0.16];
  const openP = p => add(hinge, rot(sub(p, hinge), -open * deg));
  const tooth = (b0, tip, o = {}) => tube(F, spline([b0, add(mul(add(b0, tip), 0.5), mul(a, -0.004 * L)), tip], 0.8),
    taperR(0.014 * L, 0.0035 * L, 0.8), { amp: 1, inten, ...o });

  // small recurved upper teeth (premaxilla + maxilla); the lower row stands in the gaps between them,
  // so the two rows interlock across the open jaw
  const upper = Array.from({ length: 12 }, (_, i) => {
    const u = lerp(0.55, 0.965, i / 11), v = lerp(-0.095, -0.06, (u - 0.55) / 0.42) + 0.006;
    return [[u, v], [u - 0.014, v - 0.056 + i * 0.0008]];
  });
  const lowerAt = u => { const v = lerp(-0.1, -0.073, (u - 0.55) / 0.4) + 0.004; return [[u, v], [u - 0.012, v + 0.042]]; };
  const lower = upper.slice(1).map(([, tip], i) => {
    const target = (upper[i][1][0] + tip[0]) / 2;
    let u = target + 0.012;
    for (let it = 0; it < 4; it++) u += target - openP(lowerAt(u)[1])[0];
    return lowerAt(u);
  });

  // mandible (behind), then its teeth just above it
  plate(F, SKULL.mandible.map(openP).map(W), { A: 5.5, R: 3.5, inten: inten * 0.9 });
  carve(F, ell({ ...SKULL.mandFen, c: openP(SKULL.mandFen.c) }), 2);
  commit(F, { z: z - 3 });
  for (const [b0, tip] of lower) tooth(W(openP(b0)), W(openP(tip)));
  commit(F, { z: z - 1 });

  // cranium + upper jaw with its windows: orbit, temporal, antorbital and maxillary fenestrae, naris
  plate(F, SKULL.cranium.map(W), {
    A: 8.5, R: 6.5, inten,
    mod: (x, y) => {
      const d = sub([x, y], O), u = (d[0] * a[0] + d[1] * a[1]) / L;
      return lerp(1, 0.62, sstep(0.25, 1, u));
    },
  });
  bump(F, W([0.12, 0.085]), 0.13 * L, 0.12 * L, Math.atan2(a[1], a[0]), 5);
  bump(F, W([0.5, 0.11]), 0.2 * L, 0.03 * L, Math.atan2(a[1], a[0]) - 0.25, 1.6);
  carve(F, ell(SKULL.orbit, 40), 3.2);
  carve(F, ell(SKULL.temporal), 2.2);
  carve(F, SKULL.antorbital.map(W), 2.8);
  carve(F, ell(SKULL.maxFen), 1.6);
  carve(F, ell(SKULL.naris), 1.8);
  commit(F, { z, k: 1.5 });
  // upper teeth stand proud of the jaw margin so the cranium does not swallow them in its cast shadow
  for (const [b0, tip] of upper) tooth(W(b0), W(tip), { hair: 1.0, even: true });
  commit(F, { z: z + 7 });

  // sclerotic ring inside the orbit: 14 separate radial plates (never a concentric ring)
  const oc = SKULL.orbit.c;
  for (let i = 0; i < 14; i++) {
    const t0 = (i / 14) * Math.PI * 2 + 0.06, t1 = ((i + 1) / 14) * Math.PI * 2 - 0.06;
    const r0 = 0.058, r1 = 0.086;
    const quad = [[r0, t0], [r1, t0], [r1, (t0 + t1) / 2], [r1, t1], [r0, t1]].map(([r, t]) => W([oc[0] + r * Math.cos(t), oc[1] + r * Math.sin(t)]));
    plate(F, quad, { A: 2.6, R: 1.6, inten: inten * 0.95 });
  }
  commit(F, { z: z - 4 });
}

// ------------------------------------------------------------------ vertebral column
// `cap` < 1 gives flat-ended centra that stand apart by `gap`; `hair` adds a crest hairline on each lit side.
function vertebraSeries(F, pts, s0, s1, count, { re, rw, gap = 2.6, inten = 1, z = 0, cap = 1, hair = 0, procs } = {}) {
  const cum = cumLen(pts);
  const seg = (s1 - s0) / count;
  for (let i = 0; i < count; i++) {
    const t = count > 1 ? i / (count - 1) : 0;
    const R = val(re, t), Rw = val(rw, t), pull = cap < 1 ? cap * R : 0;
    const a0 = s0 + i * seg + gap / 2 + pull, a1 = s0 + (i + 1) * seg - gap / 2 - pull;
    tube(F, slice(pts, cum, a0, a1, 1.2), hourR(R, Rw), { inten: val(inten, t), cap, ...(hair ? { hair, off: 0.55 } : {}) });
    if (procs) procs(i, t, at(pts, cum, (a0 + a1) / 2), at(pts, cum, a0), at(pts, cum, a1), R);
  }
  commit(F, { z });
}

// ------------------------------------------------------------------ jersey masks
const BODY_POLY = pathPoly(BODY);
const CUFFS = [[[84, 452], [214, 508]], [[916, 452], [786, 508]]];
// distance from the cuff edge, measured up the sleeve (Infinity away from the sleeves)
function cuffDist(x, y) {
  let m = Infinity;
  for (const [a, b] of CUFFS) {
    const d = n2(sub(b, a));
    let nx = d[1], ny = -d[0];
    if (ny > 0) { nx = -nx; ny = -ny; }
    const along = ((x - a[0]) * d[0] + (y - a[1]) * d[1]) / Math.hypot(b[0] - a[0], b[1] - a[1]);
    if (along < -0.1 || along > 1.1) continue;
    m = Math.min(m, (x - a[0]) * nx + (y - a[1]) * ny);
  }
  return m;
}
const V_POLY = [...bezier([[384, 92], [402, 160], [448, 226], [500, 292]], 30), ...bezier([[500, 292], [552, 226], [598, 160], [616, 92]], 30).slice(1), [616, 70], [384, 70]];
const BACKBAND_POLY = [...bezier([[384, 92], [420, 116], [462, 128], [500, 128]], 20), ...bezier([[500, 128], [538, 128], [580, 116], [616, 92]], 20).slice(1), [616, 70], [384, 70]];
// printable area: the body minus the collar, the cuff bands (+26 units) and everything below HEM_Y
function visibleMask(side) {
  const V = new Uint8Array(N);
  const [, y0, , y1] = polyBox(BODY_POLY);
  for (let j = Math.floor(y0); j <= Math.min(HEM_Y, Math.ceil(y1)); j++) {
    const xs = [];
    for (let i = 0, k = BODY_POLY.length - 1; i < BODY_POLY.length; k = i++) {
      const [xa, ya] = BODY_POLY[i], [xb, yb] = BODY_POLY[k];
      if ((ya > j) !== (yb > j)) xs.push(xa + (j - ya) * (xb - xa) / (yb - ya));
    }
    xs.sort((p, q) => p - q);
    for (let s = 0; s + 1 < xs.length; s += 2)
      for (let i = Math.max(0, Math.ceil(xs[s] - 1)); i <= Math.min(NX - 1, Math.floor(xs[s + 1] + 1)); i++) V[j * NX + i] = 1;
  }
  const hide = side === 'front' ? V_POLY : BACKBAND_POLY;
  const [hx0, hy0, hx1, hy1] = polyBox(hide);
  for (let j = Math.max(0, Math.floor(hy0)); j <= Math.ceil(hy1); j++)
    for (let i = Math.floor(hx0); i <= Math.ceil(hx1); i++) if (inPoly(i, j, hide)) V[j * NX + i] = 0;
  for (let j = 400; j < 560; j++) for (let i = 0; i < NX; i++) {
    if (!V[j * NX + i] || (i > 260 && i < 740)) continue;
    if (cuffDist(i, j) < 26) V[j * NX + i] = 0;
  }
  return V;
}

// ------------------------------------------------------------------ separable box blur
function blur(src, r, passes = 2) {
  let a = Float32Array.from(src), b = new Float32Array(N);
  const w = 2 * r + 1;
  for (let p = 0; p < passes; p++) {
    for (let j = 0; j < NY; j++) {
      let s = 0;
      const row = j * NX;
      for (let i = -r; i <= r; i++) s += a[row + clamp(i, 0, NX - 1)];
      for (let i = 0; i < NX; i++) {
        b[row + i] = s / w;
        s += a[row + Math.min(NX - 1, i + r + 1)] - a[row + Math.max(0, i - r)];
      }
    }
    for (let i = 0; i < NX; i++) {
      let s = 0;
      for (let j = -r; j <= r; j++) s += b[clamp(j, 0, NY - 1) * NX + i];
      for (let j = 0; j < NY; j++) {
        a[j * NX + i] = s / w;
        s += b[Math.min(NY - 1, j + r + 1) * NX + i] - b[Math.max(0, j - r) * NX + i];
      }
    }
  }
  return a;
}

// ------------------------------------------------------------------ feather strands
// Every vane is a weave of fine strands between the rachis and the vane edge, spaced as on a flattened cylinder
// (dense at the edges) and lit by the key light. The rachis is one hairline exactly on the ridge. Bones hide the
// strands, bone shadows darken them, and they dissolve before the cuffs and the hem.
const FLAT = 0.45;                // vane cross-section: height / half-width of the cylinder the strands wrap
const OP_STEPS = 80;              // strand opacity is quantised to 1/80 so strands group into few <path>s
function relPath(pts) {
  let px = Math.round(pts[0][0] * 10), py = Math.round(pts[0][1] * 10);
  let d = `M${px / 10} ${py / 10}l`;
  for (let i = 1; i < pts.length; i++) {
    const x = Math.round(pts[i][0] * 10), y = Math.round(pts[i][1] * 10);
    d += `${(x - px) / 10} ${(y - py) / 10} `;
    px = x; py = y;
  }
  return d.trimEnd();
}
function featherSvg(F, VIS, SH, light) {
  const SHs = blur(SH, 2, 2);   // bone shadows fall on the vanes with a soft edge
  const vane = new Map(), rach = new Map();
  // a strand goes out in short chunks, each at its own opacity (steps of 1/OP_STEPS); hidden points break it.
  // Chunk boundaries are staggered strand by strand so the opacity steps never line up into bands.
  const strand = (map, pts, ops, phase) => {
    let run = [], acc = 0;
    const flush = () => {
      const lv = Math.round(acc / run.length * OP_STEPS);
      if (run.length > 1 && lv >= 3) { if (!map.has(lv)) map.set(lv, []); map.get(lv).push(relPath(run)); }
      run = []; acc = 0;
    };
    for (let i = 0; i < pts.length; i++) {
      if (ops[i] < 0) { flush(); continue; }
      run.push(pts[i]); acc += ops[i];
      if (run.length > 1 && (i + phase) % 3 === 0) { flush(); run.push(pts[i]); acc += ops[i]; }
    }
    flush();
  };
  // light, cast shadow and cuff fade at p; -1 where a bone, the collar or the hem hides it
  const env = p => {
    const i = Math.round(p[0]), j = Math.round(p[1]);
    if (i < 0 || j < 0 || i >= NX || j >= NY) return -1;
    const q = j * NX + i;
    if (!VIS[q] || F.H[q] > 0.3) return -1;
    return light(p[0], p[1]) * (1 - 0.8 * SHs[q]) * sstep(38, 60, cuffDist(p[0], p[1]));
  };
  const tipFade = t => sstep(0, 0.08, t) * (1 - 0.4 * sstep(0.4, 1, t));
  for (const fe of F.feathers) {
    const P = fe.pts, n = P.length, cum = cumLen(P), tot = cum[n - 1];
    const NN = P.map((_, i) => { const d = n2(sub(P[Math.min(n - 1, i + 1)], P[Math.max(0, i - 1)])); return [-d[1], d[0]]; });
    for (const [sg, wf, K] of [[-1, fe.wl, fe.kl], [1, fe.wr, fe.kr]]) {
      for (let k = 0; k < K; k++) {
        const th = (Math.PI / 2) * (k + 0.5) / K, c = Math.cos(th), s = Math.sin(th);
        const pts = [], ops = [];
        for (let i = 0; i < n; i++) {
          const t = cum[i] / tot, e = mul(NN[i], sg);
          const p = add(P[i], mul(e, wf(t) * c));
          const nv = n3([e[0] * c, e[1] * c, s / FLAT]);
          const lit = clamp(dot3(nv, LIGHT) + RIM_K * Math.max(0, dot3(nv, RIM)) ** 2);
          const en = env(p);
          pts.push(p);
          ops.push(en < 0 ? -1 : (0.15 + 0.3 * lit) * fe.gain * tipFade(t) * en);
        }
        strand(vane, pts, ops, k);
      }
    }
    const rp = [], ro = [];
    for (let i = 0; i < n; i++) {
      const t = cum[i] / tot;
      if (t < 0.05 || t > 0.85) continue;
      const en = env(P[i]);
      rp.push(P[i]);
      ro.push(en < 0 ? -1 : 0.85 * fe.gain * tipFade(t) * en);
    }
    strand(rach, rp, ro, 0);
  }
  const group = (map, stroke, w) => `<g fill="none" stroke="${stroke}" stroke-width="${w}">` +
    [...map].sort((p, q) => p[0] - q[0]).map(([lv, ds]) => `<path stroke-opacity="${(lv / OP_STEPS).toFixed(3)}" d="${ds.join('')}"/>`).join('') + '</g>';
  return group(vane, '#c2c2c6', 0.5) + group(rach, '#7e7e85', 0.6);
}

// ------------------------------------------------------------------ accents
function accentSvg(list, id, light) {
  const c = (p, r, extra = '') => `<circle cx="${f1(p[0])}" cy="${f1(p[1])}" r="${f1(r)}"${extra}/>`;
  return list.map(a => {
    const o = clamp(light(a.p[0], a.p[1]) * 1.1, 0.35, 1).toFixed(2);
    if (a.t === 'ball') return `<g opacity="${o}">${c(a.p, a.r + 1.8, ' fill="none" stroke="#b9b9be" stroke-width="0.5" stroke-opacity="0.6"')}${c(a.p, a.r, ` fill="url(#${id}-ball)"`)}</g>`;
    // surgical pin through the joint: 26 long, ball ends
    const p0 = add(a.p, mul(a.d, -9)), p1 = add(a.p, mul(a.d, 17));
    return `<g opacity="${o}"><path d="M${f1(p0[0])} ${f1(p0[1])}L${f1(p1[0])} ${f1(p1[1])}" stroke="#c9c9ce" stroke-width="0.7" stroke-opacity="0.8" fill="none"/>` +
      `${c(p1, 1.7, ` fill="url(#${id}-ball)"`)}${c(p0, 1.2, ` fill="url(#${id}-ball)"`)}</g>`;
  }).join('');
}

// ------------------------------------------------------------------ rendering: light -> grain + hairlines
const TONES = ['#2e2e33', '#414146', '#56565c', '#6e6e75', '#88888f', '#a4a4aa', '#c2c2c6', '#cfcfd4'];
const SOFT_TONE_MAX = 3;          // gastralia never go brighter than #6e6e75, so a white number on top still reads
const WHITE_MIN = 0.97;           // only full-intensity bone (the back's skull) earns pure-white rim hairlines
const HAIR = { dim: '#9c9ca2', mid: '#d2d2d4', white: '#f6f6f2' };
const DENS = 2.0;                 // dots per unit² on a fully lit face (fine r≈0.5 dots, dense enough to read as light)
function render(F, side, { seed = 7, light = () => 1 } = {}) {
  const R = rng(seed), nz = noise2D(seed + 1), nz2 = noise2D(seed + 2);
  const VIS = visibleMask(side);
  const { H, I, M } = F;
  const occ = new Float32Array(N);
  for (let q = 0; q < N; q++) occ[q] = H[q] > 0.15 ? 1 : 0;
  const halo = blur(occ, 30, 3);
  const Hb = blur(H, 3, 2);
  const Hao = blur(H, 7, 2);

  // cast shadows (light rakes from the upper left, shadows fall to the lower right)
  const SH = new Float32Array(N);
  for (let j = 1; j < NY - 1; j++) for (let i = 1; i < NX - 1; i++) {
    const q = j * NX + i;
    if (!VIS[q] || halo[q] < 0.002) continue;
    const h0 = H[q];
    let sh = 0;
    for (let s = 1.5; s <= 36; s += 1.5) {
      const x = Math.round(i + LXY[0] * s), y = Math.round(j + LXY[1] * s);
      if (x < 0 || y < 0 || x >= NX || y >= NY) break;
      const e = H[y * NX + x] - (h0 + s * TAN_ELEV);
      if (e > 0) sh = Math.max(sh, clamp(e / 3));
      if (sh >= 1) break;
    }
    SH[q] = sh;
  }

  // ---- grain: fine dots on lit faces, sparse dots on shadow faces, a little dust only near bone
  const dots = TONES.map(() => []);
  const bgDots = [[], []];
  let count = 0;
  const emit = (arr, x, y, r) => { arr.push(f1(x), f1(y), f1(r)); count++; };
  for (let j = 1; j < NY - 1; j++) for (let i = 1; i < NX - 1; i++) {
    const q = j * NX + i;
    if (!VIS[q]) continue;
    const x = i * G, y = j * G;
    const h = H[q];
    const fy = light(x, y);
    if (h > 0.15) {
      const gx = (H[q + 1] - H[q - 1]) / (2 * G), gy = (H[q + NX] - H[q - NX]) / (2 * G);
      const nl = Math.hypot(gx, gy, 1);
      const nv = [-gx / nl, -gy / nl, 1 / nl];
      const lam = dot3(nv, LIGHT), lamR = dot3(nv, RIM);
      const spec = Math.max(0, dot3(nv, HALF)) ** 28;
      const ao = clamp((Hao[q] - h) * 0.06, 0, 0.6);
      const soft = M[q] === MAT.SOFT;
      let b = (0.03 + 0.97 * sstep(0.22, 0.97, lam) ** 1.25 * (1 - 0.85 * SH[q]) + RIM_K * sstep(0.45, 0.98, lamR) ** 1.6 + 0.6 * spec) * (1 - ao);
      b *= I[q] * fy;
      // erosion: only weak bone (rib tips, the tail end, toward the hem) breaks up into loose grain and emerges
      // from the black; bone at 0.75 intensity or more stays whole, soft tissue always does
      const er = nz(x / 26, y / 26) * 0.55 + nz2(x / 8, y / 8) * 0.3;
      const keep = soft ? 1 : sstep(-0.3, 0.3, er + 3 * (I[q] * fy - 0.55));
      // soft tissue gets an even tonal fill so its whole shape reads; bone is lit grain
      const lam_ = soft ? 0.5 * I[q] * fy + 1.6 * clamp(b) ** 0.8 : clamp(b) ** 1.15 * DENS * keep + 0.025 * keep;
      let k = Math.floor(lam_) + (R() < lam_ % 1 ? 1 : 0);
      while (k-- > 0) {
        const tb = clamp(b + (R() - 0.5) * 0.16);
        let tone = Math.min(TONES.length - 1, Math.floor(tb * TONES.length * 0.999));
        if (soft) tone = Math.min(tone, SOFT_TONE_MAX);
        emit(dots[tone], x + R() - 0.5, y + R() - 0.5, 0.45 + 0.07 * tb);
      }
    } else if (halo[q] > 0.02) {
      // dust only within ~60 units of bone, empty in cast shadows
      const dens = 0.06 * halo[q] ** 1.2 * (1 - 0.95 * SH[q]) * lerp(1, 0.55, sstep(650, 1130, y)) * hemFade(y);
      if (R() < dens) emit(bgDots[R() < 0.3 + halo[q] ? 1 : 0], x + R() - 0.5, y + R() - 0.5, 0.45 + R() * 0.09);
    }
  }
  const circles = (arr, fill) => {
    let s = `<g fill="${fill}">`;
    for (let k = 0; k < arr.length; k += 3) s += `<circle cx="${arr[k]}" cy="${arr[k + 1]}" r="${arr[k + 2]}"/>`;
    return s + '</g>';
  };

  // ---- hairlines, grouped by fill colour
  const lines = new Map();
  const line = (color, d) => { if (!lines.has(color)) lines.set(color, []); lines.get(color).push(d); };
  const binColor = (w, inten) => (w > 1.2 && inten >= WHITE_MIN ? HAIR.white : w > 0.85 ? HAIR.mid : HAIR.dim);
  const polyD = poly => 'M' + poly.map(p => f1(p[0]) + ' ' + f1(p[1])).join('L') + 'Z';
  const sample = (A, x, y) => {
    const i = clamp(Math.floor(x), 0, NX - 2), j = clamp(Math.floor(y), 0, NY - 2), u = x - i, v = y - j, q = j * NX + i;
    return (A[q] * (1 - u) + A[q + 1] * u) * (1 - v) + (A[q + NX] * (1 - u) + A[q + NX + 1] * u) * v;
  };

  // rim hairlines: single-threshold outline of the bone field, kept only where it faces the light
  const HB = new Float64Array(N);
  for (let q = 0; q < N; q++) HB[q] = M[q] === MAT.BONE && VIS[q] ? H[q] : 0;
  const rings = contours().size([NX, NY]).thresholds([0.8])(HB)[0].coordinates.flat();
  for (const ring of rings) {
    if (ring.length < 10) continue;
    let pts = ring.map(([gx, gy]) => [(gx - 0.5) * G, (gy - 0.5) * G]);
    pts.pop();
    for (let it = 0; it < 4; it++) {
      const n = pts.length;
      pts = pts.map((p, k) => { const a = pts[(k - 1 + n) % n], b = pts[(k + 1) % n]; return [(a[0] + 2 * p[0] + b[0]) / 4, (a[1] + 2 * p[1] + b[1]) / 4]; });
    }
    const n = pts.length;
    const W = new Float32Array(n), IN = new Float32Array(n), NO = [];
    for (let k = 0; k < n; k++) {
      const [x, y] = pts[k];
      const out = n2([-(sample(Hb, x + 1, y) - sample(Hb, x - 1, y)), -(sample(Hb, x, y + 1) - sample(Hb, x, y - 1))]);
      NO.push(out);
      const lit = Math.max(dot(out, LXY), 0.6 * dot(out, RXY));
      const xi = clamp(Math.round(x - out[0] * 2), 0, NX - 1), yi = clamp(Math.round(y - out[1] * 2), 0, NY - 1);
      IN[k] = I[yi * NX + xi] * light(x, y);
      W[k] = 1.45 * sstep(0.25, 0.97, lit) ** 1.3 * sstep(0.5, 0.95, IN[k]) * (1 - SH[yi * NX + xi]);
    }
    let k0 = 0;
    for (let k = 0; k < n; k++) if (W[k] < 0.5) { k0 = k; break; }
    let run = [];
    const flush = () => {
      if (run.length >= 5) {
        const L = run.length;
        const w = run.map((k, m) => Math.max(0.5, W[k] * Math.min(1, (m + 1) / 4, (L - m) / 4)));
        const outer = run.map(k => [pts[k][0] - NO[k][0] * 0.2, pts[k][1] - NO[k][1] * 0.2]);
        const inner = run.map((k, m) => [pts[k][0] - NO[k][0] * (0.2 + w[m]), pts[k][1] - NO[k][1] * (0.2 + w[m])]).reverse();
        line(binColor(Math.max(...run.map(k => W[k])), Math.max(...run.map(k => IN[k]))), polyD(outer.concat(inner)));
      }
      run = [];
    };
    for (let s = 1; s <= n; s++) {
      const k = (k0 + s) % n;
      if (W[k] >= 0.5) run.push(k); else flush();
    }
    flush();
  }

  // crest hairlines: the ridge of blades, claws and vertebrae, bright where light and shadow meet (ref1)
  const ribbon = (run, key, color) => {
    if (run.length < 5) return;
    const L = run.length;
    const w = run.map((o, m) => Math.max(0.5, o[key] * Math.min(1, (m + 1) / 5, (L - m) / 5)));
    const a = run.map(o => o.p), b = run.map((o, m) => add(o.p, mul(o.litDir, w[m]))).reverse();
    line(color ?? binColor(Math.max(...run.map(o => o[key])), Math.max(...run.map(o => o.inten))), polyD(a.concat(b)));
  };
  for (const c of F.crests) {
    const tot = c.cum[c.cum.length - 1], m = Math.ceil(tot / 1.2);
    const rise = c.prof === 'round' ? Math.sqrt(1 - c.off ** 2) : 1 - c.off ** 1.15;
    const S = [];
    for (let k = 0; k <= m; k++) {
      const s = (k / m) * tot, t = s / tot;
      const [p, d] = at(c.pts, c.cum, s);
      const r = c.rf(t), perp = [-d[1], d[0]];
      const csK = dot(perp, LXY), csR = 0.6 * dot(perp, RXY);
      const cs = Math.abs(csK) >= Math.abs(csR) ? csK : csR;
      const litDir = cs > 0 ? perp : mul(perp, -1);
      const pc = add(p, mul(litDir, 0.3 + c.off * r));
      const q = clamp(Math.round(pc[1]), 0, NY - 1) * NX + clamp(Math.round(pc[0]), 0, NX - 1);
      const lt = VIS[q] && H[q] <= r * c.amp * rise + c.z + 0.8 ? light(p[0], p[1]) * (1 - SH[q]) : 0;
      const inten = val(c.inten, t) * lt;
      const w = c.hair * (c.even ? 1 : sstep(0.3, 0.85, Math.abs(cs))) * inten * sstep(0, 0.07, t) * sstep(1, c.tipT, t);
      // the one highlight (front wishbone): a tapered white stroke along the ridge between hi.x0 and hi.x1
      const wHi = c.hi ? 1.15 * Math.sin(Math.PI * clamp((p[0] - c.hi.x0) / (c.hi.x1 - c.hi.x0)) ** 0.75) ** 0.8 * lt : 0;
      S.push({ p: pc, litDir, w, wHi, inten });
    }
    for (const [key, color] of [['w', c.color], ...(c.hi ? [['wHi', c.hi.color]] : [])]) {
      let run = [];
      for (const o of S) { if (o[key] >= 0.5) run.push(o); else { ribbon(run, key, color); run = []; } }
      ribbon(run, key, color);
    }
  }

  const id = `axg-${side}`;
  const defs = `<filter id="${id}-glow" x="-5%" y="-5%" width="110%" height="110%"><feGaussianBlur stdDeviation="2.2"/></filter>
  <filter id="${id}-bloom" x="-5%" y="-5%" width="110%" height="110%"><feGaussianBlur stdDeviation="7"/></filter>
  <radialGradient id="${id}-ball" cx="35%" cy="32%" r="70%"><stop offset="0" stop-color="#f4f4f6"/><stop offset="0.45" stop-color="#8d8d93"/><stop offset="1" stop-color="#1b1b1e"/></radialGradient>
  <radialGradient id="${id}-air" cx="28%" cy="12%" r="80%"><stop offset="0" stop-color="#ffffff" stop-opacity="0.045"/><stop offset="0.6" stop-color="#ffffff" stop-opacity="0.01"/><stop offset="1" stop-color="#ffffff" stop-opacity="0"/></radialGradient>`;
  const use = (filter, op) => `<use xmlns:xlink="http://www.w3.org/1999/xlink" href="#${id}-grain" xlink:href="#${id}-grain" filter="url(#${id}-${filter})" opacity="${op}"/>`;
  const feathers = featherSvg(F, VIS, SH, light);
  const art = [
    `<rect width="1000" height="1200" fill="url(#${id}-air)"/>`,
    circles(bgDots[0], '#3a3a3f'), circles(bgDots[1], '#4e4e54'),
    feathers,
    // soft light: the grain itself, blurred, under the grain (ref1 / ref6); optional, the grain reads without it
    use('bloom', 0.15), use('glow', 0.5),
    `<g id="${id}-grain">${dots.map((arr, t) => circles(arr, TONES[t])).join('')}</g>`,
    ...[HAIR.dim, HAIR.mid, '#e2e2e0', HAIR.white].filter(col => lines.has(col)).map(col => `<g fill="${col}">${lines.get(col).map(d => `<path d="${d}"/>`).join('')}</g>`),
    accentSvg(F.accents, id, light),
  ].join('\n');
  console.error(`[${side}] dots ${count}, hairlines ${[...lines.values()].flat().length}, feathers ~${Math.round(feathers.length / 1024)} KB, accents ${F.accents.length}`);
  return { art, defs };
}

const clearance = [];

// a wing: humerus, radius + ulna, wrist, three metacarpals, three separate clawed fingers, and its ball joints and pin.
// sd = -1 left of the image, +1 right (mirrored).
function wing(F, sd, { sh, elbow, wrist, handTo, z = 9, inten = 1, crest = [22, 6.5] }) {
  const o = { inten };
  const hd = n2(sub(elbow, sh)), hp = [-hd[1], hd[0]];
  const lead = hp[1] < 0 ? hp : mul(hp, -1);
  longBone(F, sh, elbow, 8.6, 7.6, { k0: 1.55, k1: 1.45, inten: t => inten * lerp(0.88, 1, t) });
  blob(F, add(lerp2(sh, elbow, 0.27), mul(lead, 7.5)), crest[0], crest[1], Math.atan2(hd[1], hd[0]), { amp: 0.95, inten: inten * 0.92 }); // deltopectoral crest
  blob(F, add(sh, mul(hd, 2)), 12.5, 10.5, Math.atan2(hd[1], hd[0]), { amp: 0.95, inten: inten * 0.88 });                                 // humeral head
  commit(F, { z });
  const fd = n2(sub(wrist, elbow)), fp = [-fd[1], fd[0]];
  const trail = fp[1] > 0 ? fp : mul(fp, -1);
  const bowSign = dot(trail, fp) > 0 ? 1 : -1;
  longBone(F, add(elbow, mul(trail, 4)), add(wrist, mul(trail, 5)), 6.2, 5.4, { bow: bowSign * 7, k0: 1.6, k1: 1.3, ...o }); // ulna
  longBone(F, add(elbow, mul(trail, -5.5)), add(wrist, mul(trail, -5.5)), 4.1, 3.8, { k0: 1.45, k1: 1.5, ...o });              // radius
  commit(F, { z: z + 0.5, k: 1.2 });
  const hd2 = n2(sub(handTo, wrist)), hp2 = [-hd2[1], hd2[0]];
  const lead2 = hp2[0] * sd > 0 ? hp2 : mul(hp2, -1);   // the hand's leading edge faces outward
  blob(F, add(wrist, mul(hd2, 4)), 10, 7.5, Math.atan2(hd2[1], hd2[0]), { amp: 0.95, inten }); // semilunate carpal
  const mcB = add(wrist, mul(hd2, 9));
  const mcIend = add(add(mcB, mul(hd2, 16)), mul(lead2, 11));
  const mcIIend = add(mcB, mul(hd2, 46));
  const mcIIIend = add(add(mcB, mul(hd2, 42)), mul(lead2, -10));
  longBone(F, add(mcB, mul(lead2, 5)), mcIend, 4.6, 4.3, { k0: 1.3, k1: 1.35, cap: CAP, ...o });             // mc I
  longBone(F, add(mcB, mul(lead2, 0.5)), mcIIend, 5.0, 4.5, { k0: 1.3, k1: 1.35, cap: CAP, ...o });           // mc II
  longBone(F, add(mcB, mul(lead2, -4.5)), mcIIIend, 3.0, 2.8, { k0: 1.25, k1: 1.4, bow: 2, cap: CAP, ...o }); // mc III
  commit(F, { z: z + 1, k: 1 });
  // three separate, well-spread fingers (formula 2-3-4), each ending in a large hooked claw curling to the palm side
  const r1 = rot(hd2, 1);
  const curl = dot(sub(r1, hd2), mul(lead2, -1)) > 0 ? 1 : -1;
  const dI = rot(hd2, -curl * 42 * deg), dII = rot(hd2, -curl * 4 * deg), dIII = rot(hd2, curl * 28 * deg);
  const claws = [
    digit(F, add(mcIend, mul(dI, trim(4.3 * 1.35))), dI, [26], 5.2, 33, curl * 105 * deg, o),
    digit(F, add(mcIIend, mul(dII, trim(4.5 * 1.35))), dII, [24, 27], 5.2, 30, curl * 100 * deg, o),
    digit(F, add(mcIIIend, mul(dIII, trim(2.8 * 1.4))), dIII, [11, 11, 14], 4.0, 24, curl * 95 * deg, o),
  ];
  commit(F, { z: z + 1.5 });
  clearance.push(Math.round(Math.min(...claws.flat().map(p => cuffDist(p[0], p[1])))));
  ball(F, sh, 3.2);
  ball(F, elbow, 2.6);
  ball(F, wrist, 2.2);
  pin(F, elbow, fp[1] < 0 ? fp : mul(fp, -1));   // across the forearm, pointing up toward the shoulder line
}
// wing feather impressions: secondaries hang from the ulna, primaries from the hand, fanning down the sleeve.
// Angles are screen angles for the left wing (90 = straight down) and are mirrored for the right wing.
function wingFeathers(F, sd, { elbow, wrist, handTo, gain = 1, sec = [62, 100], pri = [104, 142], secLen = [128, 160], priLen = [150, 178] }) {
  const hand = n2(sub(handTo, wrist));
  const dirOf = a => { const v = [Math.cos(a * deg), Math.sin(a * deg)]; return sd < 0 ? v : [-v[0], v[1]]; };
  // wide inner vane (9) on the side the fan opens toward, narrow outer vane (6)
  const vane = (pts, g) => feather(F, pts, sd < 0 ? 6 : 9, sd < 0 ? 9 : 6, g);
  for (let k = 0; k < 9; k++) {
    const t = k / 8;
    const base = add(lerp2(elbow, wrist, 0.05 + t * 0.92), [0, 5]);
    vane(arcPts(base, dirOf(lerp(sec[0], sec[1], t)), lerp(secLen[0], secLen[1], t), -sd * 8 * deg, 24), gain);
  }
  for (let k = 0; k < 8; k++) {
    const t = k / 7;
    const tr = [-hand[1], hand[0]], trail = tr[0] * sd < 0 ? tr : mul(tr, -1);
    const base = add(add(wrist, mul(hand, 8 + t * 74)), mul(trail, 9));
    vane(arcPts(base, dirOf(lerp(pri[0], pri[1], t)), lerp(priLen[0], priLen[1], t), -sd * 6 * deg, 24), gain * 0.85);
  }
}
// leg in a bird's zig-zag: femur forward (up), tibiotarsus back (down), tarsometatarsus forward, four clawed toes
function leg(F, sd, L, { z = 9, inten = 0.9, footI = 1.15 } = {}) {
  longBone(F, L.hip, L.knee, 8.6, 8.2, { bow: sd * 6, k0: 1.45, k1: 1.42, inten: inten * 0.92 });
  blob(F, add(L.hip, mul(n2(sub(L.knee, L.hip)), 3)), 12.5, 11, 0, { amp: 0.95, inten: inten * 0.92 });
  commit(F, { z });
  longBone(F, L.knee, L.ankle, 7.4, 6, { k0: 1.5, k1: 1.42, bow: -sd * 5, inten });
  const fd = n2(sub(L.ankle, L.knee));
  let fp = [-fd[1], fd[0]];
  if (fp[0] * sd < 0) fp = mul(fp, -1); // fibula on the outer side
  tube(F, spline([add(L.knee, mul(fp, 8.5)), add(lerp2(L.knee, L.ankle, 0.4), mul(fp, 10)), add(lerp2(L.knee, L.ankle, 0.75), mul(fp, 7))], 1.2), taperR(4.2, 1.2, 0.8), { inten: inten * 0.95 });
  commit(F, { z: z - 0.5 });
  const md = n2(sub(L.foot, L.ankle)), mp = [-md[1], md[0]];
  // the foot is lifted a step above the leg so it stays crisp where the light falls off toward the hem
  for (const o of [-1, 0, 1]) longBone(F, add(L.ankle, mul(mp, o * 2.4)), add(L.foot, mul(mp, o * 6.5)), 4.6, 4.1, { k0: 1.35, k1: 1.25, cap: CAP, inten: footI });
  commit(F, { z: z - 0.5, k: 1.2 });
  // toes II, III, IV fan forward from just past the metatarsal ends; the claws hook back toward the body
  const fwd = n2(L.toeDir), tp = [-fwd[1], fwd[0]], cl = -sd, o = { inten: footI };
  const toe = (off, ang) => { const d = rot(fwd, ang * deg); return [add(add(L.foot, mul(tp, off)), mul(d, trim(4.1 * 1.25))), d]; };
  digit(F, ...toe(-9, -32), [18, 14], 4.8, 22, cl * 85 * deg, { ...o, bend: -4 * deg });
  digit(F, ...toe(0, 0), [20, 15, 12], 5.0, 23, cl * 85 * deg, { ...o, bend: cl * 3 * deg });
  digit(F, ...toe(9, 32), [13, 11, 9, 8], 4.2, 19, cl * 80 * deg, { ...o, bend: 4 * deg });
  // hallux: short, reversed
  digit(F, add(lerp2(L.ankle, L.foot, 0.8), mul(mp, -sd * 6 * Math.sign(mp[0] || 1))), rot(md, Math.PI - sd * 40 * deg), [14], 4.2, 19, sd * 85 * deg, o);
  commit(F, { z });
  ball(F, L.hip, 2.8);
  ball(F, L.knee, 2.8);
}

// ================================================================== BACK: dorsal view, Berlin pose
// Wings raised in a V along the shoulder line, feather fans hanging down the sleeves, head thrown back between them.
// Elbow, wrist, hand and feathers share the front's coordinates, so each wing wraps over the shoulder seam unbroken.
// Everything in the number zone (x 380-620, y 330-760) stays at 0.8 intensity or less; only the skull is full white.
const WING_BACK = { sh: [446, 380], elbow: [336, 182], wrist: [262, 236], handTo: [228, 340] };
function backField() {
  const F = newField();
  const X = 500;

  // ---- the frond tail: 22 caudals, a woven feather pair on each from the 6th on, fading out before the hem
  const tailTop = 660;
  const tailLens = [13, 13, 13, 13.5, 14, 19, 21, 22, 23, 23.5, 24, 24, 24, 23.5, 23, 22.5, 22, 21, 20, 18.5, 16.5, 13].map(l => l * 0.9);
  const tailPts = [[X, tailTop]];
  { let y = tailTop; for (const l of tailLens) { y += l; tailPts.push([X + 3 * Math.sin((y - tailTop) / 130), y]); } }
  const tailLine = spline(tailPts, 1.5), tailCum = cumLen(tailLine), tailTot = tailCum[tailCum.length - 1];
  const cumAt = c => tailLens.slice(0, c).reduce((p, q) => p + q, 0);
  for (let c = 5; c < tailLens.length; c++) {
    const [p, d] = at(tailLine, tailCum, cumAt(c) + tailLens[c] * 0.5);
    const s = (c - 5) / (tailLens.length - 6);
    const ang = lerp(64, 6, s ** 0.8) * deg;
    const len = lerp(78, 132, Math.min(1, s * 1.5) ** 0.6) - (s > 0.8 ? (s - 0.8) * 70 : 0);
    for (const sd of [-1, 1]) {
      const dir = rot(d, -sd * ang);
      const pts = arcPts(add(p, mul(dir, 6)), dir, len, sd * 12 * deg * (1 - s * 0.6), 22);
      feather(F, pts, sd < 0 ? 6 : 9, sd < 0 ? 9 : 6, lerp(0.9, 0.7, s));
    }
  }

  // ---- wing feather impressions on the sleeves
  for (const sd of [-1, 1]) {
    const P = p => (sd < 0 ? p : mirrorX(p));
    wingFeathers(F, sd, { elbow: P(WING_BACK.elbow), wrist: P(WING_BACK.wrist), handTo: P(WING_BACK.handTo), gain: 1 });
  }

  // ---- pubes: long, pointing backward (down the tail), meeting in the pubic boot
  for (const sd of [-1, 1]) {
    const P = p => (sd < 0 ? p : mirrorX(p));
    tube(F, spline([P([456, 652]), P([468, 704]), P([482, 754]), P([494, 790])], 1.5), longR(6, 4.6, 1.2, 1.3), { inten: 0.78 });
  }
  blob(F, [X, 796], 17, 7.5, 0, { amp: 1, inten: 0.78 });
  commit(F, { z: 8 });

  // ---- a narrow bird ribcage, dim so a number can sit on it
  const dorsalTop = 376, dorsalBot = 584, nDorsal = 11;
  const dy = (dorsalBot - dorsalTop) / nDorsal;
  const ribReach = [72, 92, 104, 110, 110, 106, 98, 86, 70];
  for (let r = 0; r < ribReach.length; r++) {
    const y = dorsalTop + dy * (r + 0.8);
    const Rr = ribReach[r], sw = lerp(4, 30, r / (ribReach.length - 1)) * deg;
    for (const sd of [-1, 1]) {
      const base = [X + sd * 18, y];
      const ctrl = [[0, 0], [0.32, -0.06], [0.66, 0.08], [0.88, 0.36], [0.97, 0.72], [0.96, 1.0]]
        .map(([u, v]) => add(base, rot([sd * u * Rr, v * Rr * 0.75], sd * sw)));
      tube(F, spline(ctrl, 1.5), t => lerp(4.4, 2.2, t ** 0.8) * (1 + 0.45 * Math.exp(-((t / 0.05) ** 2))),
        { inten: t => lerp(0.72, 0.42, t ** 1.3) });
    }
  }
  commit(F, { z: 3 });

  // ---- dorsal vertebrae (neural arch + transverse processes)
  const spine = spline([[X, dorsalTop], [X, dorsalBot]], 1.5);
  vertebraSeries(F, spine, 0, dorsalBot - dorsalTop, nDorsal, {
    re: 10.2, rw: 7.4, gap: 3.4, inten: 0.78, z: 6,
    procs: (i, t, [c]) => { for (const sd of [-1, 1]) longBone(F, [c[0] + sd * 7, c[1] + 1], [c[0] + sd * 21, c[1] - 1.5], 3.6, 3.2, { k1: 1.55, inten: 0.78 }); },
  });
  // ---- synsacrum + ilia + ischia
  tube(F, spline([[X, dorsalBot + 2], [X, tailTop - 2]], 1.5), t => 10.5 * (1 + 0.1 * Math.cos(t * Math.PI * 10)), { inten: 0.8 });
  for (let k = 0; k < 5; k++) {
    const y = dorsalBot + 11 + k * 15;
    for (const sd of [-1, 1]) longBone(F, [X + sd * 8, y], [X + sd * 26, y + 2], 3.8, 4, { k1: 1.3, inten: 0.8 });
  }
  commit(F, { z: 6 });
  for (const sd of [-1, 1]) {
    const P = p => (sd < 0 ? p : mirrorX(p));
    const ilium = [[474, 560], [462, 570], [452, 594], [447, 624], [446, 652], [450, 680], [459, 700], [471, 710], [483, 702], [488, 678], [489, 640], [488, 598], [484, 570]].map(P);
    plate(F, ilium, { A: 10, R: 13, inten: 0.8 });
    bump(F, P([476, 602]), 6, 38, 0.1 * -sd, 3.2);   // iliac crest ridge
    { // pneumatic / trabecular lattice inside the iliac blade (ref5): a few large, deliberate voids
      const Rl = rng(sd < 0 ? 31 : 37), seeds = [];
      for (let row = 0, yy = 572; yy < 712; row++, yy += 22)
        for (let xx = 440; xx < 500; xx += 22) seeds.push(P([xx + (Rl() - 0.5) * 9 + (row % 2) * 11, yy + (Rl() - 0.5) * 9]));
      lattice(F, ilium, seeds, { strut: 4.6, R: 3.4, border: 8 });
    }
    carve(F, ellipsePoly(...P([446, 642]), 10, 11, 0, 30), 3.2);
    tube(F, spline([P([460, 668]), P([470, 696]), P([482, 720])], 1.2), taperR(6, 3.6), { inten: 0.75 });
  }
  commit(F, { z: 7, k: 1.5 });

  // ---- caudal vertebrae with transverse processes on the first six
  const tailI = t => lerp(0.85, 0.74, t);
  vertebraSeries(F, tailLine, 0, tailTot, tailLens.length, {
    re: t => lerp(10, 4.8, t ** 0.75), rw: t => lerp(7.4, 3.5, t ** 0.75), gap: 2.8, inten: tailI, z: 9,
    procs: (i, t, [c, d]) => {
      if (i > 5) return;
      const w = lerp(24, 9, i / 5);
      for (const sd of [-1, 1]) {
        const px = [-d[1] * sd, d[0] * sd];
        longBone(F, add(c, mul(px, 5)), add(add(c, mul(px, w)), mul(d, 2.5)), 3.3, 2.5, { k1: 1.2, inten: tailI(t) });
      }
    },
  });

  // ---- legs: bird zig-zag (femur forward, tibiotarsus back, tarsometatarsus forward), Berlin-like asymmetry
  leg(F, -1, { hip: [452, 646], knee: [382, 604], ankle: [328, 792], foot: [356, 872], toeDir: [-0.05, 1] }, { z: 10 });
  leg(F, 1, { hip: [548, 646], knee: [620, 612], ankle: [676, 802], foot: [650, 884], toeDir: [0.12, 1] }, { z: 10 });

  // ---- scapulae: blade straps on the athlete's shoulder blades
  for (const sd of [-1, 1]) {
    const P = p => (sd < 0 ? p : mirrorX(p));
    tube(F, spline([P([448, 384]), P([452, 428]), P([458, 470]), P([466, 512])], 1.5),
      t => lerp(9, 7.4, t) * (1 + 0.25 * Math.exp(-((t / 0.08) ** 2))) * (t > 0.84 ? Math.sqrt(1 - ((t - 0.84) / 0.165) ** 2 * 0.85) : 1),
      { prof: 'crest', amp: 1.0, inten: 0.85, hair: 0.8 });
  }
  commit(F, { z: 9 });

  // ---- wings raised in a V
  for (const sd of [-1, 1]) {
    const P = p => (sd < 0 ? p : mirrorX(p));
    wing(F, sd, { sh: P(WING_BACK.sh), elbow: P(WING_BACK.elbow), wrist: P(WING_BACK.wrist), handTo: P(WING_BACK.handTo), z: 11, inten: 0.92 });
  }

  // ---- neck: ten hourglass cervicals in a swan curve, brightening toward the head thrown back between the wings
  const neck = spline([[500, 374], [489, 338], [485, 300], [494, 264], [514, 234], [534, 210], [547, 194]], 1.2);
  const ncum = cumLen(neck), ntot = ncum[ncum.length - 1];
  const neckI = t => lerp(0.8, 0.95, t);
  vertebraSeries(F, neck, 0, ntot, 10, {
    re: t => lerp(10.5, 8, t), rw: t => lerp(7.6, 5.8, t), gap: 4.2, inten: neckI, z: 12, cap: 0.35, hair: 0.9,
    procs: (i, t, mid, [p0, d0]) => {
      const px = [-d0[1], d0[0]];
      for (const s of [-1, 1]) blob(F, add(add(p0, mul(px, s * lerp(9.5, 7.4, t))), mul(d0, 3.5)), 3.4, 2.6, Math.atan2(d0[1], d0[0]), { amp: 0.95, inten: neckI(t) });
    },
  });
  skull(F, { condyle: [552, 192], dir: [-1, 0.21], L: 172, z: 16, open: 9, inten: 1.1 });
  return F;
}

// ================================================================== FRONT: ventral view
const WING_FRONT = { sh: [402, 262], elbow: [336, 182], wrist: [262, 236], handTo: [228, 340] };
function frontField() {
  const F = newField();
  for (const sd of [-1, 1]) {
    const P = p => (sd < 0 ? p : mirrorX(p));
    wingFeathers(F, sd, { elbow: P(WING_FRONT.elbow), wrist: P(WING_FRONT.wrist), handTo: P(WING_FRONT.handTo), gain: 0.92 });
  }

  // gastralia: six rows of soft spindle-shaped belly ribs in a herringbone, apex toward the head (echoing the tail
  // frond on the back). The medial pieces of each row cross at the midline and the side on top alternates row by row,
  // so the crossings interlock like a zig-zag seam. Tonal only: no hairlines, never brighter than #6e6e75.
  const R = rng(5);
  const spindle = (a, b) => t2 => a + (b - a) * Math.sin(Math.PI * t2) ** 0.6;
  const bow = (a, b, k) => spline([a, add(mul(add(a, b), 0.5), [0, -k]), b], 1.2);
  for (let r = 0; r < 6; r++) {
    const t = r / 5;
    const inten = lerp(0.5, 0.28, t);
    for (const sd of [-1, 1]) {
      const y = 470 + r * 34 + (sd > 0 ? 5 : 0) + (R() - 0.5) * 2;
      const ang = (lerp(26, 34, t) + (R() - 0.5) * 2) * deg;
      const dir = [sd * Math.cos(ang), Math.sin(ang)];
      const lenM = lerp(72, 58, t) + (R() - 0.5) * 4, lenL = lerp(70, 50, t) + (R() - 0.5) * 4;
      const m0 = add([500, y], mul(dir, -13)), m1 = add(m0, mul(dir, lenM));
      tube(F, bow(m0, m1, 3), spindle(1.8, 4.2), { inten, mat: MAT.SOFT });
      commit(F, { z: (r + (sd > 0 ? 1 : 0)) % 2 ? 5.2 : 4 });
      const l0 = add(m1, add(mul(dir, -9), [0, 6])), l1 = add(l0, mul(rot(dir, sd * 9 * deg), lenL));
      tube(F, bow(l0, l1, 4), spindle(1.6, 3.8), { inten: t2 => inten * lerp(0.95, 0.6, t2), mat: MAT.SOFT });
      commit(F, { z: 4 });
    }
  }

  // wings (ventral), raised along the shoulder line like the back; dimmer, with a smaller crest, so the shoulder
  // never reads as a heavy handle
  for (const sd of [-1, 1]) {
    const P = p => (sd < 0 ? p : mirrorX(p));
    wing(F, sd, { sh: P(WING_FRONT.sh), elbow: P(WING_FRONT.elbow), wrist: P(WING_FRONT.wrist), handTo: P(WING_FRONT.handTo), z: 9, inten: 0.85, crest: [15, 5] });
  }

  // furcula: a robust boomerang wishbone repeating the V-neck 110 units under its point; the brightest object on the
  // front, with a silver crest hairline and one tapered white highlight on its upper-left arm. The arms catch the
  // light; the bottom of the U, inside the number zone, is a step softer.
  const furc = spline([[394, 250], [402, 296], [422, 336], [456, 362], [500, 372], [544, 362], [578, 336], [598, 296], [606, 250]], 1.2);
  tube(F, furc, t => { const c = Math.abs(t - 0.5) * 2; return lerp(11, 7.5, c ** 0.9) * (1 + 0.35 * Math.exp(-(((1 - c) / 0.05) ** 2))); },
    { prof: 'crest', amp: 1.0, inten: t => lerp(0.66, 0.96, sstep(0.3, 0.75, Math.abs(t - 0.5) * 2)), hair: 1.1, color: HAIR.mid, hi: { x0: 400, x1: 470, color: HAIR.white } });
  commit(F, { z: 10, k: 2 });
  return F;
}

// ------------------------------------------------------------------ exports
// key light is one global gradient per face; the back keeps the feet near 0.8, the front's detail is gone soon
// after y 640, and everything is gone by HEM_Y
const backLight = (x, y) => lerp(1, 0.72, sstep(300, 1180, y + 0.15 * Math.abs(x - 470))) * hemFade(y);
const frontLight = (x, y) => lerp(1, 0.55, sstep(380, 900, y + 0.15 * Math.abs(x - 500))) * (1 - 0.7 * sstep(600, 690, y)) * hemFade(y);
let cache = null;
function build() {
  if (cache) return cache;
  const t0 = Date.now();
  const back = render(backField(), 'back', { seed: 11, light: backLight });
  const front = render(frontField(), 'front', { seed: 23, light: frontLight });
  console.error(`built in ${Date.now() - t0} ms; claw cuff clearance (units): ${clearance.join(' ')}`);
  cache = { back, front };
  return cache;
}
export function front() { return build().front; }
export function back() { return build().back; }
