// RAPTOR — LINE-WEAVE X-RAY
// A bird-of-prey skeleton built from bundles of fine parallel threads (ref7 string art): every bone is a ribbon
// woven between its two edge curves and lit by one key light from the upper left (weak rim from the upper right).
// Lit faces also carry a light sand-blasted grain dusting (ref1); shadow faces dissolve into the black fabric.
// Front = ventral view: the furcula repeats the V-neck and is the brightest object; coracoids, a shield sternum whose
//   keel steps back below y 430 to keep the number zone calm, ribs, and legs ending in open talons.
// Back  = dorsal view: an eagle skull (hooked beak, beaded sclerotic ring) is the focal point upper right of the spine,
//   an S-neck runs into the beaded spine wire, sabre scapulae, a kite-shaped pelvis with foramina, and the tail fan.
// Both faces share the same wing on each sleeve, so the bird wraps the body over the shoulders.
import { rng, stipple, segDist } from '../lib/art.mjs';

// ---------------------------------------------------------------- math
const clamp = (v, a = 0, b = 1) => Math.max(a, Math.min(b, v));
const lerp = (a, b, t) => a + (b - a) * t;
const smoothstep = (a, b, x) => { const t = clamp((x - a) / (b - a)); return t * t * (3 - 2 * t); };
const smin = (a, b, k) => { const h = clamp(0.5 + 0.5 * (b - a) / k); return lerp(b, a, h) - k * h * (1 - h); };
const add = (p, q, k = 1) => [p[0] + q[0] * k, p[1] + q[1] * k];
const sub = (p, q) => [p[0] - q[0], p[1] - q[1]];
const dot = (p, q) => p[0] * q[0] + p[1] * q[1];
const len = v => Math.hypot(v[0], v[1]);
const unit = v => { const l = len(v) || 1; return [v[0] / l, v[1] / l]; };
const perp = v => [-v[1], v[0]];
const rot = (v, a) => [v[0] * Math.cos(a) - v[1] * Math.sin(a), v[0] * Math.sin(a) + v[1] * Math.cos(a)];
const mixP = (p, q, t) => [lerp(p[0], q[0], t), lerp(p[1], q[1], t)];
const frac = x => x - Math.floor(x);
const f1 = n => Math.round(n * 10) / 10;
const id = p => p;
const mirX = p => [1000 - p[0], p[1]];
const mirAll = pts => pts.map(mirX);
const norm3 = v => { const l = Math.hypot(...v); return v.map(x => x / l); };
const dirA = a => [Math.cos(a), Math.sin(a)];
const meanY = pts => pts.reduce((s, p) => s + p[1], 0) / pts.length;

// one key light from the upper left, slightly toward the viewer; a weak rim light from the upper right
const LIGHT = norm3([-0.55, -0.70, 0.55]);
const RIM = norm3([0.66, -0.5, 0.42]);
const HALF = norm3([LIGHT[0], LIGHT[1], LIGHT[2] + 1]);

// ---------------------------------------------------------------- curves
function crSample(pts, per = 18) {
  const n = pts.length, out = [];
  const at = i => pts[Math.max(0, Math.min(n - 1, i))];
  for (let i = 0; i < n - 1; i++) {
    const p0 = at(i - 1), p1 = at(i), p2 = at(i + 1), p3 = at(i + 2);
    for (let k = 0; k < per; k++) {
      const t = k / per, t2 = t * t, t3 = t2 * t;
      out.push([0, 1].map(d => 0.5 * (2 * p1[d] + (-p0[d] + p2[d]) * t + (2 * p0[d] - 5 * p1[d] + 4 * p2[d] - p3[d]) * t2 + (-p0[d] + 3 * p1[d] - 3 * p2[d] + p3[d]) * t3)));
    }
  }
  out.push(pts[n - 1].slice());
  return out;
}
function arcLen(pts) { let L = 0; for (let i = 1; i < pts.length; i++) L += len(sub(pts[i], pts[i - 1])); return L; }
function resample(pts, N) {
  const L = [0];
  for (let i = 1; i < pts.length; i++) L.push(L[i - 1] + len(sub(pts[i], pts[i - 1])));
  const tot = L[L.length - 1], out = [];
  let j = 0;
  for (let k = 0; k < N; k++) {
    const s = tot * k / (N - 1);
    while (j < L.length - 2 && L[j + 1] < s) j++;
    const seg = L[j + 1] - L[j] || 1;
    out.push(mixP(pts[j], pts[j + 1], clamp((s - L[j]) / seg)));
  }
  return out;
}
// smooth curve through control points, resampled evenly (step units) or to N points
function curve(ctrl, step = 3, N) {
  const raw = crSample(ctrl);
  return resample(raw, N ?? Math.max(8, Math.round(arcLen(raw) / step)));
}
function normals(pts) {
  return pts.map((p, i) => {
    const a = pts[Math.max(0, i - 1)], b = pts[Math.min(pts.length - 1, i + 1)];
    return unit(perp(sub(b, a)));
  });
}
// curve made by walking with a turning heading (talons, hooked shapes)
function arcWalk(p, dir, length, turn, N = 24, pw = 1.3) {
  const pts = [p.slice()];
  const a = Math.atan2(dir[1], dir[0]);
  const st = length / (N - 1);
  let q = p.slice();
  for (let i = 1; i < N; i++) {
    const s = i / (N - 1);
    q = add(q, dirA(a + turn * Math.pow(s, pw)), st);
    pts.push(q.slice());
  }
  return pts;
}
// a feather shaft: from P at heading ang, length L, sideways bend (fraction of L), N points
function shaft(P, ang, L, bend = 0, N = 64) {
  const d = dirA(ang), n = perp(d);
  return curve([P, add(add(P, d, L * 0.34), n, L * bend * 0.55), add(add(P, d, L * 0.68), n, L * bend), add(add(P, d, L), n, L * bend * 0.75)], 2, N);
}

// ---------------------------------------------------------------- scene
function scene(side) {
  return { side, chunks: [], hi: [], glowPts: [], accents: [], lit: [], fade: makeFade() };
}
// global light falloff: brighter toward the upper left, dimming toward the hem and gone by y 1080
function makeFade() {
  return (x, y) => {
    const d = Math.hypot(x - 320, y - 140);
    const light = 0.6 + 0.4 * clamp(1 - d / 950);
    const hem = (1 - 0.78 * smoothstep(700, 1130, y)) * (1 - smoothstep(1040, 1080, y));
    return light * hem;
  };
}

// Render a family of strands. Each strand u in [0,1] is lit as a slice of a cylinder (curv≈1) or of a gently
// curved blade (curv small). A and B are the first/last strand (full length) used for the cross direction.
// o.across(s) instead tilts the normal along each strand (fibres that cross a dished plate);
// o.dome {c, R} gives every chunk the normal of a hemisphere centred at c (a rounded braincase).
// o.grain (number or fn(x,y)) marks the bone for the lit-face grain dusting where the key light is strong.
function strands(S, lines, us, A, B, o = {}) {
  const N = A.length;
  const curv = o.curv ?? 1, tilt = o.tilt ?? 0, tone = o.tone ?? 1, chunk = o.chunk ?? 5;
  const amb = o.amb ?? 0.08, kd = (o.kd ?? 0.42) * 2.0, ks = (o.ks ?? 0.55) * 1.2, kr = (o.kr ?? 0.16) * 1.5;
  const fade = S.fade;
  // cross vectors (A -> B), carried across pinch points
  const side = new Array(N);
  for (let j = 0; j < N; j++) { const v = sub(B[j], A[j]); side[j] = len(v) > 0.6 ? unit(v) : null; }
  let carry = side.find(Boolean) || [1, 0];
  for (let j = 0; j < N; j++) { if (side[j]) carry = side[j]; else side[j] = carry; }
  lines.forEach((full, i) => {
    const u = us[i];
    const thU = (u - 0.5) * Math.PI * curv + tilt;
    const keep = o.trim ? Math.max(2, Math.round(full.length * o.trim(u))) : full.length;
    const line = full.slice(0, keep);
    const ut = o.uTone ? o.uTone(u) : 1;
    const runs = [];
    let cur = [];
    for (let j = 0; j < line.length; j++) {
      const p = line[j];
      if (o.avoid && o.avoid(p[0], p[1])) { if (cur.length > 1) runs.push(cur); cur = []; continue; }
      cur.push(j);
    }
    if (cur.length > 1) runs.push(cur);
    for (const run of runs) {
      for (let k0 = 0; k0 < run.length - 1; k0 += chunk) {
        const k1 = Math.min(run.length - 1, k0 + chunk);
        const jm = run[(k0 + k1) >> 1];
        const pm = line[jm];
        // chunk normal: a slice across the bundle (default), a fibre crossing a dish (across), or a hemisphere (dome)
        let sv = side[jm], th = thU;
        if (o.dome) { const d = sub(pm, o.dome.c); sv = unit(d); th = Math.asin(clamp(len(d) / o.dome.R, 0, 0.97)); }
        else if (o.across) { sv = unit(sub(line[run[k1]], line[run[k0]])); th = o.across(jm / (N - 1)); }
        const sn = Math.sin(th), cs = Math.cos(th);
        const Nx = sv[0] * sn, Ny = sv[1] * sn, Nz = cs;
        const diff = Math.max(0, Nx * LIGHT[0] + Ny * LIGHT[1] + Nz * LIGHT[2]);
        const rim = Math.max(0, Nx * RIM[0] + Ny * RIM[1] + Nz * RIM[2]);
        const spec = Math.pow(Math.max(0, Nx * HALF[0] + Ny * HALF[1] + Nz * HALF[2]), o.shine ?? 22);
        const edge = Math.pow(Math.abs(Math.sin((u - 0.5) * Math.PI)), 8);
        const along = o.alongTone ? o.alongTone(jm / (N - 1)) : 1;
        const fd = fade(pm[0], pm[1]);
        // rim term capped at 35% of the key: kd * 0.35 * max(0, N·RIM)^2
        let op = tone * ut * along * fd * (amb + kd * (diff + 0.35 * rim * rim) + ks * spec + kr * edge);
        op = Math.min(0.96, op);
        if (op < 0.03) continue;
        const pts = [];
        for (let k = k0; k <= k1; k++) pts.push(line[run[k]]);
        S.chunks.push({ pts, op, w: o.w ?? 0.55 });
        if (o.grain && diff > 0.55) {
          const g = typeof o.grain === 'function' ? o.grain(pm[0], pm[1]) : o.grain;
          if (g > 0) S.lit.push({ pts, wt: g * (diff - 0.55) * fd * arcLen(pts) });
        }
      }
    }
  });
  // glow samples along the middle of the bundle
  const g = o.glow ?? 1;
  if (g > 0) for (let j = 0; j < N; j += 2) {
    const c = mixP(A[j], B[j], 0.5);
    S.glowPts.push([c[0], c[1], Math.min(9, Math.max(1.5, len(sub(A[j], B[j])) / 2)) * g * tone]);
  }
}
// line-weave between two edge curves (equal point counts)
function weave(S, A, B, o = {}) {
  const count = o.count ?? 14;
  const ease = o.ease ?? ((o.curv ?? 1) >= 0.8 ? 'cyl' : 'lin');
  const lines = [], us = [];
  for (let i = 0; i < count; i++) {
    const u = count === 1 ? 0.5 : i / (count - 1);
    const t = ease === 'cyl' ? (1 + Math.sin((u - 0.5) * Math.PI)) / 2 : ease === 'lin' ? u : ease(u);
    lines.push(A.map((p, j) => mixP(p, B[j], t)));
    us.push(u);
  }
  strands(S, lines, us, A, B, o);
}
// fan of strands from a generator gen(u) -> points (feather fans, tail)
function fan(S, gen, o = {}) {
  const count = o.count ?? 60;
  const lines = [], us = [];
  for (let i = 0; i < count; i++) { const u = i / (count - 1); lines.push(gen(u)); us.push(u); }
  strands(S, lines, us, lines[0], lines[count - 1], { curv: 0.5, glow: 0.18, ...o });
  return lines;
}
// scalloped trailing edge + per-feather light ripple for nF feathers in a fan
function featherFanOpts(nF, scallop = 0.12, ripple = 0.4) {
  return {
    trim: u => { const ph = frac(u * nF * 0.999); return 1 - scallop * (1 - Math.pow(Math.sin(Math.PI * ph), 0.7)); },
    uTone: u => { const ph = frac(u * nF * 0.999); return 1 - ripple + ripple * (0.55 + 0.45 * Math.cos(2 * Math.PI * (ph - 0.32))) - 0.25 * ripple * smoothstep(0.85, 1, ph); },
  };
}

// ribbon around a centreline: width fn w(s) (half-width), optional twist phi(s), offset fn
function ribbon(c, wfn, { twist, offset } = {}) {
  const n = normals(c), N = c.length;
  const A = [], B = [];
  for (let j = 0; j < N; j++) {
    const s = j / (N - 1);
    const w = wfn(s) * (twist ? Math.cos(twist(s)) : 1);
    const off = offset ? offset(s) : 0;
    A.push(add(c[j], n[j], off + w));
    B.push(add(c[j], n[j], off - w));
  }
  return [A, B];
}
// the upper (lit) edge of a ribbon, pulled in by `inset`, between arc fractions s0..s1
function topEdge(c, wfn, { offset, inset = 0.4, s0 = 0, s1 = 1 } = {}) {
  const [A, B] = ribbon(c, s => Math.max(0, wfn(s) - inset), { offset });
  const e = meanY(A) < meanY(B) ? A : B;
  return e.slice(Math.round(s0 * (e.length - 1)), Math.round(s1 * (e.length - 1)) + 1);
}
// long-bone width profile with epiphyses and rounded caps
function boneW({ head = 7, shaft = 3.5, tail = 6, hs = 0.15, ts = 0.15, cap = 0.07 } = {}) {
  return s => {
    let w = shaft + (head - shaft) * Math.exp(-((s / hs) ** 2)) + (tail - shaft) * Math.exp(-(((1 - s) / ts) ** 2));
    const c = Math.min(s, 1 - s) / cap;
    if (c < 1) w *= Math.sqrt(Math.max(0, 1 - (1 - c) ** 2));
    return w;
  };
}
function bone(S, ctrl, wopt, o = {}) {
  const c = o.raw ? ctrl : curve(ctrl, o.step ?? 2.5);
  const wfn = typeof wopt === 'function' ? wopt : boneW(wopt);
  const [A, B] = ribbon(c, wfn, o);
  let maxW = 0;
  for (let j = 0; j < c.length; j++) maxW = Math.max(maxW, len(sub(A[j], B[j])));
  const count = o.count ?? Math.max(5, Math.min(70, Math.round(maxW / (o.spacing ?? 1.25)) + 1));
  weave(S, A, B, { ...o, count });
  return c;
}

// ---------------------------------------------------------------- bio-mech accents
function ball(S, p, r = 3, ring = true) { S.accents.push({ t: 'ball', p, r, ring }); }
function pin(S, p, dir, L) { S.accents.push({ t: 'pin', p, d: unit(dir), L }); }
function wire(S, pts, beads = true) { S.accents.push({ t: 'wire', pts, beads }); }

// ---------------------------------------------------------------- anatomy: wing (shared front/back)
// Left wing joints (viewer's left); the right wing is mirrored. The shoulder ball sits on the raglan seam, humerus and
// forearm run along the top of the sleeve (kept ~35 units inside its outer edge, clear of the garment's edge shadow),
// and the hand stops well above the cuff band.
const WING = { SH: [338, 228], EL: [214, 222], WR: [168, 334], HD: [150, 374], TIP: [140, 398] };
// Left cuff edge (84,452)-(214,508); CUFF_IN points from it toward the shoulder. Feather tips keep 38+ units of
// clearance (44 here leaves room for the shaft's sideways bend).
const CUFF_A = [84, 452], CUFF_IN = unit([56, -130]);
function featherLen(P, ang, L) {
  const s = dot(dirA(ang), CUFF_IN), h = dot(sub(P, CUFF_A), CUFF_IN);
  return s < -1e-3 ? smin(L, Math.max(4, (h - 44) / -s), 24) : L;
}
const feather = (P, ang, L, bend, N) => shaft(P, ang, featherLen(P, ang, L), bend, N);
// grain masks: 0 within 38 units of either cuff edge (1 from 46), and on the back 0 within 15 units of the collar band
const cuffClear = (x, y) => smoothstep(38, 46, Math.min(segDist(x, y, 84, 452, 214, 508), segDist(x, y, 916, 452, 786, 508)));
const COLLAR = curve([[384, 92], [440, 121], [500, 128], [560, 121], [616, 92]], 2);
const collarY = x => { const k = COLLAR.findIndex(p => p[0] >= x); return k <= 0 ? -1e9 : lerp(COLLAR[k - 1][1], COLLAR[k][1], (x - COLLAR[k - 1][0]) / (COLLAR[k][0] - COLLAR[k - 1][0] || 1)); };
const collarClear = (x, y) => smoothstep(9, 15, y - collarY(x));

function wing(S, mirror) {
  const M = mirror ? mirX : id;
  const MA = pts => pts.map(M);
  const { SH, EL, WR, HD, TIP } = WING;
  const ulDir = unit(sub(WR, EL));
  const lead = [ulDir[1], -ulDir[0]];        // toward the armpit side of the forearm (radius)
  const trail = [-lead[0], -lead[1]];        // toward the outer sleeve edge (ulna, wing silhouette)
  const tone = mirror ? 0.88 : 1;
  const NF = 64;

  // ---- feather fans (behind the bones), strands run along the feathers like ref7's wings
  // primaries from the hand toward the cuff
  fan(S, u => MA(feather(mixP(add(WR, trail, 2), TIP, u), lerp(1.4, 1.86, u), lerp(150, 120, u), -0.05, NF)),
    { count: 46, tone: 0.85 * tone, tilt: (mirror ? 1 : -1) * 0.35, ...featherFanOpts(5, 0.12, 0.45) });
  // tertials from the distal humerus
  fan(S, u => MA(feather(add(mixP(SH, EL, lerp(0.42, 0.95, u)), [0.2, 1], 6), lerp(0.48, 0.66, u), lerp(128, 168, u), 0.05, NF)),
    { count: 28, tone: 0.7 * tone, tilt: (mirror ? -1 : 1) * 0.3, ...featherFanOpts(3, 0.1, 0.45) });
  // secondaries hanging from the ulna across the sleeve; deeper scallops separate the feathers
  fan(S, u => MA(feather(add(mixP(EL, WR, lerp(0.03, 1, u)), trail, 5), lerp(0.68, 1.36, u), lerp(178, 150, u), 0.06, NF)),
    { count: 96, tone: 0.9 * tone, tilt: (mirror ? -1 : 1) * 0.15, ...featherFanOpts(9, 0.18, 0.42) });
  // greater coverts: a short fan over the base of the secondaries (layering, like ref7's double wing)
  fan(S, u => MA(feather(add(mixP(EL, WR, lerp(0.02, 0.98, u)), trail, 4), lerp(0.78, 1.32, u), lerp(70, 54, u), 0.08, 40)),
    { count: 36, tone: 1.0 * tone, tilt: (mirror ? -1 : 1) * 0.45, ...featherFanOpts(12, 0.18, 0.5) });

  // ---- bones
  const humC = curve(MA([SH, [300, 210], [256, 206], EL]), 2.5);
  const humW = boneW({ head: 9.5, shaft: 4.6, tail: 7.5, hs: 0.18, ts: 0.13 });
  const humOff = s => (mirror ? 1 : -1) * 3.2 * Math.exp(-(((s - 0.22) / 0.13) ** 2));   // deltopectoral crest
  bone(S, humC, humW, { tone: 1.7 * tone, raw: true, offset: humOff });
  const ulC = curve(MA([add(EL, trail, 1), add(mixP(EL, WR, 0.5), trail, 5.5), add(WR, trail, 1)]), 2.5);
  const ulW = boneW({ head: 6.4, shaft: 3.8, tail: 4.8, hs: 0.12, ts: 0.12 });
  bone(S, ulC, ulW, { tone: 1.8 * tone, raw: true });
  bone(S, MA([add(EL, lead, 9), add(mixP(EL, WR, 0.5), lead, 10), add(WR, lead, 7)]), { head: 4, shaft: 2.3, tail: 3.8 }, { tone: 1.6 * tone });
  // hand: carpometacarpus, minor metacarpal, alula, major digit
  bone(S, MA([WR, mixP(WR, HD, 0.5), HD]), { head: 5.5, shaft: 3, tail: 3.6 }, { tone: 1.6 * tone });
  bone(S, MA([add(WR, trail, 2), add(mixP(WR, HD, 0.5), trail, 7), add(HD, trail, 1)]), { head: 2, shaft: 1.4, tail: 1.8 }, { tone: 1.0 * tone });
  bone(S, MA([add(WR, lead, 4), add(add(WR, lead, 12), ulDir, 6), add(add(WR, lead, 18), ulDir, 4)]), { head: 2.6, shaft: 1.6, tail: 0.6, cap: 0.2 }, { tone: 1.1 * tone });
  bone(S, MA([HD, mixP(HD, TIP, 0.55), TIP]), { head: 3, shaft: 2.2, tail: 0.8, cap: 0.2 }, { tone: 1.1 * tone });
  // leading-edge hairline along humerus + ulna: the wing silhouette
  const ulOut = ribbon(ulC, s => Math.max(0, ulW(s) - 0.4))[mirror ? 1 : 0];
  const lineCtrl = [...topEdge(humC, humW, { offset: humOff, s0: 0.1, s1: 0.86 }), ...ulOut.slice(Math.round(ulOut.length * 0.1), Math.round(ulOut.length * 0.93))];
  S.hi.push({ pts: curve(lineCtrl.filter((_, k) => k % 4 === 0), 2), op: 0.6, w: 0.6, fadeIt: true });
  // quill knobs along the ulna
  for (let k = 0; k < 10; k++) {
    const p = ulC[Math.round((0.1 + 0.8 * k / 9) * (ulC.length - 1))];
    const q = add(p, mirror ? [-trail[0], trail[1]] : trail, 4.4);
    S.accents.push({ t: 'dot', p: q, r: 0.8, op: 0.6 * S.fade(q[0], q[1]) });
  }
  ball(S, M(SH), 3.6);
  ball(S, M(EL), 3.0);
  ball(S, M(WR), 2.4, false);
  pin(S, M(EL), mirror ? [-0.42, -0.9] : [0.42, -0.9], 26);
}

// ---------------------------------------------------------------- FRONT (ventral)
function furcula(S) {
  const c = curve([[350, 200], [372, 234], [404, 276], [446, 312], [478, 330], [500, 334], [522, 330], [554, 312], [596, 276], [628, 234], [650, 200]], 2.2);
  bone(S, c, s => {
    const e = Math.min(s, 1 - s);
    let w = 3.4 + 2.6 * Math.exp(-((e / 0.06) ** 2)) + 1.6 * Math.exp(-(((s - 0.5) / 0.05) ** 2));
    if (e < 0.03) w *= Math.sqrt(Math.max(0, 1 - (1 - e / 0.03) ** 2));
    return w;
  }, { tone: 1.55, spacing: 1.0, ks: 0.85, raw: true, glow: 1.5, grain: 1, twist: s => 1.15 * Math.pow(Math.sin(2 * Math.PI * s), 2) });
  bone(S, [[500, 334], [501, 346], [502, 360]], { head: 4.6, shaft: 3, tail: 0.6, cap: 0.25 }, { tone: 1.2 });
}
function coracoids(S) {
  for (const m of [false, true]) {
    const M = m ? mirX : id;
    bone(S, [[344, 236], [372, 266], [406, 300], [432, 330], [446, 354]].map(M), s => {
      // acrocoracoid head, slender shaft, then a flat squared flare onto the sternum
      let w = 3.8 + 2.6 * Math.exp(-((s / 0.09) ** 2)) + 8.5 * smoothstep(0.6, 1, s) ** 1.6;
      if (s < 0.05) w *= Math.sqrt(s / 0.05);
      if (s > 0.985) w *= 0.6;
      return w;
    }, { tone: m ? 1.0 : 1.2, twist: s => 0.5 * smoothstep(0.55, 1, s) });
    // acrocoracoid process hooking toward the furcula
    bone(S, [[348, 232], [356, 222], [366, 216]].map(M), { head: 3, shaft: 2, tail: 0.6, cap: 0.3 }, { tone: 0.95 });
    bone(S, [[372, 266], [384, 256], [394, 252]].map(M), { head: 2.2, shaft: 1.6, tail: 0.5, cap: 0.3 }, { tone: 0.9 });
  }
}
function sternum(S) {
  // concave shield plate lit from the upper left: the right half falls away into shadow
  const L = curve([[448, 350], [428, 362], [418, 396], [418, 450], [424, 520], [436, 580], [456, 626], [482, 652]], 2.6, 110);
  const R = mirAll(L);
  const fen = (x, y) => ((x - 462) / 7) ** 2 + ((y - 590) / 16) ** 2 < 1 || ((x - 538) / 7) ** 2 + ((y - 590) / 16) ** 2 < 1;
  weave(S, L, R, { count: 34, curv: 1, tilt: -0.45, tone: 0.2, kd: 0.5, ks: 0.25, kr: 0.0, ease: 'cyl', avoid: fen, glow: 0.4,
    uTone: u => 1 - 0.55 * smoothstep(0.55, 0.8, u), alongTone: s => 0.5 + 0.5 * smoothstep(0, 0.25, s) * (1 - 0.6 * smoothstep(0.7, 1, s)) });
  // lateral margins (costal processes) as fine ribbons
  for (const m of [false, true]) {
    const M = m ? mirX : id;
    bone(S, L.slice(4, 104).map(M), s => 1.6 + 0.5 * Math.sin(s * 30), { tone: m ? 0.32 : 0.42, raw: true, count: 4, alongTone: s => 1 - 0.6 * s });
    bone(S, [[438, 584], [428, 624], [420, 662]].map(M), { head: 3.6, shaft: 2.4, tail: 3 }, { tone: 0.5 });
  }
  // keel (carina) seen slightly from the left: a crescent blade whose crisp lit margin (ref1's S-ridge) is reserved
  // for the top; below y 430 the blade and its margin step back so a chest number stays readable
  const base = curve([[500, 352], [500, 450], [500, 550], [500, 650]], 2.6, 100);
  const marg = curve([[500, 347], [510, 356], [520, 380], [524, 420], [522, 482], [515, 552], [507, 612], [500, 652]], 2.6, 100);
  const low = y => lerp(1, 0.55, smoothstep(418, 470, y));
  weave(S, base, marg, { count: 26, curv: 0.55, tilt: -0.85, tone: 0.92, kd: 0.6, ks: 0.6, ease: 'lin', glow: 1.2,
    alongTone: s => (0.55 + 0.45 * Math.exp(-(((s - 0.25) / 0.35) ** 2))) * low(marg[Math.round(s * 99)][1]),
    grain: (x, y) => (y < 430 ? 6 : 0) });
  const split = marg.findIndex(p => p[1] > 430);
  S.hi.push({ pts: marg.slice(0, split + 1), op: 0.85, w: 0.75, fadeIt: true });
  S.hi.push({ pts: marg.slice(split), op: 0.85 * 0.4, w: 0.75, fadeIt: true });
}
function ribsFront(S) {
  for (let k = 0; k < 5; k++) {
    for (const m of [false, true]) {
      const M = m ? mirX : id;
      const y0 = 392 + 44 * k;
      const c = curve([[425, y0], [404, y0 + 22], [372, y0 + 40], [334, y0 + 46 + 2 * k], [298, y0 + 38 + 4 * k], [266, y0 + 22 + 6 * k]].map(M), 2.4);
      bone(S, c, s => {
        let w = lerp(2.0, 3.0, Math.min(1, s / 0.3)) - 0.9 * smoothstep(0.5, 1, s);
        w += 1.0 * Math.exp(-(((s - 0.24) / 0.03) ** 2));          // sternocostal joint
        const e = Math.min(s, 1 - s) / 0.04;
        return e < 1 ? w * Math.sqrt(Math.max(0, 1 - (1 - e) ** 2)) : w;
      }, { tone: (m ? 0.55 : 0.68) * (1 - k * 0.07), raw: true, alongTone: s => 1 - 0.72 * smoothstep(0.2, 1, s) });
      // uncinate process: hook pointing caudally over the next rib
      const q = c[Math.round(c.length * 0.62)];
      const sx = m ? -1 : 1;
      bone(S, [q, add(q, [sx * 6, 13]), add(q, [sx * 16, 27])], { head: 2.8, shaft: 1.6, tail: 0.5, cap: 0.3 }, { tone: 0.45 });
    }
  }
}
function leg(S, mirror) {
  const M = mirror ? mirX : id;
  const MA = a => a.map(M);
  const t = mirror ? 0.9 : 1;
  // strike pose: short powerful shanks swing forward from behind the sternum, feet thrown open toward the viewer
  const HP = [462, 596], AN = [414, 714], FT = [404, 764];
  bone(S, MA([[440, 548], [452, 572], HP]), { head: 7, shaft: 6, tail: 8.2 }, { tone: 0.5 * t, alongTone: s => 0.3 + 0.7 * s });
  ball(S, M(HP), 2.8);
  bone(S, MA([HP, [446, 640], [428, 680], AN]), { head: 9.5, shaft: 7.2, tail: 9, hs: 0.12 }, { tone: 0.8 * t, alongTone: s => 0.45 + 0.55 * smoothstep(0.1, 0.6, s) });
  bone(S, MA([add(HP, [-12, 18]), [438, 650], [432, 676]]), { head: 2.6, shaft: 1.3, tail: 0.4, cap: 0.3 }, { tone: 0.55 * t });
  // tarsometatarsus, foreshortened, ending in three trochleae
  bone(S, MA([AN, [408, 740], FT]), { head: 9, shaft: 8, tail: 11, hs: 0.2, ts: 0.25 }, { tone: 0.95 * t });
  // toes radiate like an open hand; each talon curls back toward the palm
  const toes = [
    { a: -1.2, ph: [15, 12, 11, 9], tal: 34 },      // IV outer
    { a: -0.5, ph: [19, 16, 13], tal: 38 },         // III middle
    { a: 0.2, ph: [18, 14], tal: 42 },              // II inner
    { a: 0.95, ph: [21], tal: 50 },                 // I hallux, the killing talon
  ];
  for (const toe of toes) {
    let p = add(FT, [Math.sin(toe.a), Math.cos(toe.a)], 5);
    const d0 = [Math.sin(toe.a), Math.cos(toe.a)];
    let dir = d0;
    toe.ph.forEach((L, i) => {
      dir = rot(d0, (toe.a < -0.15 ? -1 : 1) * 0.07 * (i + 1));
      const q = add(p, dir, L * 1.2);
      const sz = 4.9 - i * 0.4;
      bone(S, MA([p, mixP(p, q, 0.5), q]), { head: sz, shaft: sz * 0.62, tail: sz * 0.9, cap: 0.2, hs: 0.2, ts: 0.2 }, { tone: 0.95 * t, step: 1.4, count: 11 });
      p = add(q, dir, 1.2);
    });
    const tc = arcWalk(p, dir, toe.tal * 1.2, toe.a < -0.15 ? -1.6 : 1.6, 34, 1.0);
    bone(S, MA(tc), s => 5.6 * Math.pow(1 - s, 0.75) * (s < 0.05 ? Math.sqrt(s / 0.05) : 1), { tone: 1.45 * t, ks: 0.9, raw: true, glow: 1.2, count: 15, grain: 1 });
  }
  ball(S, M(AN), 3.0);
}
export function front() {
  const S = scene('front');
  wing(S, false);
  wing(S, true);
  ribsFront(S);
  leg(S, false);
  leg(S, true);
  sternum(S);
  coracoids(S);
  furcula(S);
  wire(S, [[500, 336], [500, 352]], false);   // furcula–keel ligament
  ball(S, [500, 336], 2.2, false);
  return render(S, 11, 3000);
}

// ---------------------------------------------------------------- BACK (dorsal)
function skull(S) {
  // golden-eagle proportions in a unit frame (t: 0 = hook tip .. 1 = occiput, v: down), facing left
  const L = 240, X = ([t, v]) => [392 + L * t, 152 + L * v];
  const XA = a => a.map(X);
  const oc = X([0.64, 0.17]), orR = 0.11 * L;
  const nc = X([0.3, 0.16]);
  const inNaris = (x, y) => ((x - nc[0]) / 10) ** 2 + ((y - nc[1]) / 4.8) ** 2 < 1;
  const NP = 140;
  // roof + upper beak: the raised culmen arches into a deep hook that curls back ~0.14L below the gape;
  // the inner edge runs orbit roof -> lacrimal -> gape -> tomium and meets the outline at the hook tip
  const T = curve(XA([[1.0, 0.2], [0.99, 0.12], [0.95, 0.04], [0.88, -0.01], [0.78, -0.03], [0.68, -0.035], [0.58, -0.02], [0.48, 0.01], [0.38, 0.04], [0.28, 0.07], [0.19, 0.12], [0.12, 0.19], [0.07, 0.27], [0.045, 0.35], [0.05, 0.41], [0.07, 0.445]]), 2, NP);
  const U = curve(XA([[0.98, 0.22], [0.9, 0.12], [0.8, 0.085], [0.68, 0.08], [0.56, 0.09], [0.48, 0.13], [0.44, 0.2], [0.41, 0.26], [0.32, 0.29], [0.22, 0.30], [0.15, 0.32], [0.11, 0.36], [0.085, 0.41], [0.07, 0.445]]), 2, NP);
  // (the occiput end faces up-right, away from the key light, so it dims into the braincase)
  weave(S, T, U, { count: 56, curv: 1, tone: 1.4, avoid: inNaris, ks: 0.75, glow: 1.6, grain: 1, alongTone: s => 0.5 + 0.5 * smoothstep(0.04, 0.3, s) });
  // braincase: a dome woven from the occiput outline to the orbit's back rim, shaded as a hemisphere so its
  // upper-left quarter faces the key light and the lower right sinks into the fabric
  const bo = curve(XA([[0.78, 0.082], [0.87, 0.1], [0.935, 0.15], [0.975, 0.215], [0.985, 0.265], [0.95, 0.322], [0.88, 0.362]]), 2, 70);
  const rimIn = [];
  for (let j = 0; j < 70; j++) rimIn.push(add(oc, dirA((-80 + 160 * j / 69) * Math.PI / 180), 1.08 * orR));
  const fossa = X([0.86, 0.25]);
  const inFossa = (x, y) => ((x - fossa[0]) / 7) ** 2 + ((y - fossa[1]) / 12) ** 2 < 1;
  weave(S, bo, rimIn, { count: 40, dome: { c: X([0.80, 0.16]), R: 0.16 * L }, tone: 1.35, ks: 0.6, kr: 0, glow: 0.5, avoid: inFossa, grain: 1, ease: 'lin',
    alongTone: s => 1 - 0.35 * smoothstep(0.45, 1, s) });
  // brow: supraorbital shelf overhanging the orbit; its upper edge is the brightest line on the back
  const browC = curve(XA([[0.5, 0.02], [0.57, 0.02], [0.64, 0.035], [0.69, 0.06]]), 2);
  const browW = boneW({ head: 7.5, shaft: 5, tail: 0.6, cap: 0.18, ts: 0.3 });
  bone(S, browC, browW, { tone: 1.6, ks: 1.0, raw: true, grain: 1 });
  S.hi.push({ pts: topEdge(browC, browW, { s0: 0.04, s1: 0.85 }), op: 0.9, w: 0.75 });
  bone(S, XA([[0.49, 0.1], [0.475, 0.17], [0.455, 0.235]]), { head: 5.5, shaft: 3.6, tail: 3.2, cap: 0.16 }, { tone: 1.0 });
  // postorbital process
  bone(S, XA([[0.79, 0.1], [0.785, 0.15], [0.77, 0.2]]), { head: 3.6, shaft: 2.2, tail: 0.5, cap: 0.25 }, { tone: 0.85 });
  // jugal bar, quadrate, mandible (its tip tucked behind the hook)
  bone(S, XA([[0.85, 0.35], [0.7, 0.34], [0.55, 0.315], [0.42, 0.27]]), { head: 3.4, shaft: 2.1, tail: 3 }, { tone: 0.9 });
  bone(S, XA([[0.86, 0.33], [0.858, 0.37], [0.866, 0.405]]), { head: 3.8, shaft: 3, tail: 3.8, cap: 0.2 }, { tone: 0.85 });
  bone(S, XA([[0.9, 0.405], [0.76, 0.418], [0.6, 0.414], [0.45, 0.40], [0.3, 0.385], [0.19, 0.38], [0.13, 0.395], [0.11, 0.40]]), { head: 7, shaft: 5, tail: 0.9, hs: 0.08, ts: 0.3, cap: 0.04 }, { tone: 0.95 });
  // sclerotic ring: 14 ossicles overlapping like roof tiles, each a fan of radial threads with a bead on its rim
  for (let k = 0; k < 14; k++) {
    const a0 = (k / 14) * Math.PI * 2 - 0.18, a1 = a0 + (Math.PI * 2 / 14) * 1.3;
    const lines = [], us = [];
    for (let i = 0; i <= 5; i++) {
      const v = i / 5, a = lerp(a0, a1, v);
      const rOut = 0.9 - 0.1 * (2 * v - 1) ** 2;       // rounded outer edge of the plate
      // threads lean slightly with the overlap, like the blades of an iris diaphragm
      lines.push([0, 0.25, 0.5, 0.75, 1].map(q => add(oc, dirA(a + 0.1 * q), lerp(0.55, rOut, q) * orR)));
      us.push(v);
    }
    // the overlapping (leading) edge of each tile catches the light, its tucked edge falls into shadow
    strands(S, lines, us, lines[0], lines[5], { curv: 0.6, tilt: 0.2, tone: 1.25, ks: 0.9, glow: 0.6, uTone: u => 1 - 0.7 * u });
    S.accents.push({ t: 'dot', p: add(oc, dirA((a0 + a1) / 2), 0.9 * orR), r: 0.9, op: 0.7 });
  }
}
function neckAndSpine(S) {
  const neck = curve([[602, 246], [610, 276], [604, 298], [584, 334], [552, 370], [522, 404], [504, 440], [500, 448]], 1.2);
  const nV = 12, Ln = neck.length;
  for (let k = 0; k < nV; k++) {
    const a = Math.round((k / nV) * (Ln - 1)) + 1, b = Math.round(((k + 1) / nV) * (Ln - 1)) - 1;
    const seg = neck.slice(a, b + 1);
    if (seg.length < 4) continue;
    const sz = lerp(6.5, 9.5, k / nV);
    bone(S, seg, { head: sz, shaft: sz * 0.6, tail: sz * 0.95, hs: 0.24, ts: 0.24, cap: 0.14 }, { tone: lerp(1.2, 1.0, k / nV), raw: true, count: 14 });
    const mid = seg[seg.length >> 1], tg = unit(sub(seg[seg.length - 1], seg[0])), nn = perp(tg);
    if (k > 0) for (const sgn of [1, -1]) {
      bone(S, [add(mid, nn, sgn * sz * 0.7), add(add(mid, nn, sgn * sz * 1.6), tg, 4)], { head: 1.8, shaft: 1.2, tail: 0.4, cap: 0.3 }, { tone: sgn > 0 ? 0.75 : 0.6, step: 1 });
    }
  }
  for (let k = 0; k < 6; k++) {
    const y = 448 + 22 * k;
    bone(S, [[500, y], [500, y + 10], [500, y + 20]], { head: 8.5, shaft: 5.4, tail: 8, cap: 0.2 }, { tone: 1.0 - 0.05 * k, step: 1, count: 15 });
    for (const m of [false, true]) {
      const M = m ? mirX : id;
      bone(S, [[494, y + 8], [480, y + 9], [466, y + 12]].map(M), { head: 2.6, shaft: 1.6, tail: 2.2 }, { tone: 0.65 });
    }
  }
  S.hi.push({ pts: curve([[500, 446], [500, 520], [500, 584]], 2), op: 0.45, w: 0.6, fadeIt: true });
}
function scapulae(S) {
  for (const m of [false, true]) {
    const M = m ? mirX : id;
    const c = curve([[346, 238], [356, 280], [370, 326], [390, 370], [414, 408]].map(M), 2.2);
    bone(S, c, s => {
      let w = lerp(5, 11.5, Math.min(1, s / 0.45));
      w *= s > 0.45 ? Math.pow(1 - (s - 0.45) / 0.55, 0.8) : 1;
      return Math.max(0, w) * (s < 0.04 ? Math.sqrt(s / 0.04) : 1);
    }, { tone: m ? 0.8 : 0.95, curv: 0.45, tilt: m ? 0.2 : -0.35, ease: 'lin', count: 24, twist: s => s * 1.6, ks: 0.5, raw: true });
  }
}
function ribsBack(S) {
  // short ribs that stay inside x 300..700, dim enough to sit under a back number
  for (let k = 0; k < 5; k++) {
    for (const m of [false, true]) {
      const M = m ? mirX : id;
      const y0 = 452 + 24 * k;
      const c = curve([[488, y0], [458, y0 + 8], [420, y0 + 30], [380, y0 + 64 + 4 * k], [340, y0 + 100 + 6 * k], [306, y0 + 132 + 8 * k]].map(M), 2.4);
      bone(S, c, { head: 3.6, shaft: 2.8, tail: 2.2, hs: 0.1, cap: 0.04 }, { tone: (m ? 0.45 : 0.55) * (1 - k * 0.07), raw: true, alongTone: s => 1 - 0.5 * s });
      const q = c[Math.round(c.length * 0.46)];
      const hk = m ? [[0, 0], [4, 14], [14, 28]] : [[0, 0], [-4, 14], [-14, 28]];
      bone(S, hk.map(h => add(q, h)), { head: 2.8, shaft: 1.6, tail: 0.5, cap: 0.3 }, { tone: 0.45 });
    }
  }
}
function pelvis(S) {
  // synsacrum: fused vertebral column with its transverse ridges
  const L = curve([[490, 590], [486, 640], [484, 690], [486, 740], [489, 772]], 2.5, 80);
  weave(S, L, mirAll(L), { count: 18, tone: 0.85, ks: 0.6 });
  for (let k = 0; k < 7; k++) {
    const y = 604 + k * 25;
    for (const m of [false, true]) {
      const M = m ? mirX : id;
      bone(S, [[486, y], [476, y + 2], [469, y + 4]].map(M), { head: 2.1, shaft: 1.3, tail: 1.6 }, { tone: 0.5, step: 1.2 });
    }
  }
  // kite-shaped ilia: narrow in front, widest behind the acetabulum, closing back to the midline;
  // the dished shield is pierced by the acetabulum and a row of synsacral foramina
  for (const m of [false, true]) {
    const M = m ? mirX : id;
    const sx = m ? -1 : 1;
    const ac = M([452, 690]);
    const inAc = (x, y) => (x - ac[0]) ** 2 + (y - ac[1]) ** 2 < 49;
    const inForamen = (x, y) => {
      const k = Math.round((y - 712) / 13);
      return k >= 0 && k < 7 && ((x - (500 - sx * 26)) / 3) ** 2 + ((y - 712 - 13 * k) / 2) ** 2 < 1;
    };
    const outer = curve([[482, 590], [472, 598], [466, 620], [460, 648], [450, 676], [438, 700], [428, 722], [424, 748], [426, 772], [434, 790], [446, 800], [466, 794], [484, 790], [500, 796]].map(M), 2.5, 110);
    const inner = curve([[487, 592], [485, 630], [483, 670], [482, 710], [482, 748], [485, 774], [491, 789], [500, 796]].map(M), 2.5, 110);
    // shield fibres run straight across the dish (synsacral crest -> lateral rim at the same height), never parallel
    // to the outline: the crest side falls away toward the rim, the rim curls back up
    const innerAtY = y => {
      const k = inner.findIndex(p => p[1] >= y);
      if (k <= 0) return inner[k < 0 ? inner.length - 1 : 0];
      return mixP(inner[k - 1], inner[k], (y - inner[k - 1][1]) / (inner[k][1] - inner[k - 1][1] || 1));
    };
    const fibres = [], us = [];
    for (let i = 0; i < 84; i++) {
      const o = outer[Math.round(i / 83 * (outer.length - 1))], p = innerAtY(o[1]);
      fibres.push(Array.from({ length: 9 }, (_, k) => mixP(p, o, k / 8)));
      us.push(i / 83);
      S.glowPts.push([...mixP(p, o, 0.5), Math.min(9, len(sub(o, p)) / 2) * 0.12]);
    }
    strands(S, fibres, us, fibres[0], fibres[83], { across: s => 0.5 - 1.4 * smoothstep(0.2, 1, s), tone: 0.3, chunk: 2, glow: 0,
      avoid: (x, y) => inAc(x, y) || inForamen(x, y), ks: 0.4, kr: 0, grain: 0.7 });
    // each foramen shows a lit crescent on its lower-right wall, the side facing the key light
    for (let k = 0; k < 7; k++) {
      const c = [500 - sx * 26, 712 + 13 * k];
      const arc = Array.from({ length: 9 }, (_, q) => { const a = lerp(-0.6, 2.2, q / 8); return [c[0] + 3.4 * Math.cos(a), c[1] + 2.4 * Math.sin(a)]; });
      S.hi.push({ pts: arc, op: 0.4, w: 0.5, fadeIt: true });
    }
    // iliac crest along the kite outline, plus the dorsal crest beside the synsacrum
    bone(S, outer, s => (3 + 2.6 * Math.exp(-(((s - 0.15) / 0.18) ** 2)) + 0.8 * Math.exp(-(((s - 0.72) / 0.12) ** 2))) * Math.sqrt(Math.min(1, Math.min(s, 1 - s) / 0.04)),
      { tone: 0.85, raw: true, avoid: inAc, spacing: 1.0 });
    bone(S, inner.slice(0, 92), s => 2 * Math.sqrt(Math.min(1, Math.min(s, 1 - s) / 0.05)), { tone: 0.55, raw: true });
    // acetabulum rim
    const rim = [];
    for (let j = 0; j <= 40; j++) { const a = j / 40 * Math.PI * 2; rim.push([ac[0] + 8.6 * Math.cos(a), ac[1] + 8.6 * Math.sin(a)]); }
    bone(S, rim, () => 1.0, { tone: 0.32, raw: true, count: 3 });
    // pubis rod running straight back along the ischium margin
    bone(S, [[446, 700], [440, 740], [436, 780], [434, 812]].map(M), { head: 2.2, shaft: 1.4, tail: 1.0 }, { tone: 0.5 });
  }
  // free caudal vertebrae with broad transverse processes
  for (let k = 0; k < 3; k++) {
    const y = 774 + 11 * k;
    bone(S, [[500, y], [500, y + 5], [500, y + 10]], { head: 4.6, shaft: 3.3, tail: 4.2, cap: 0.22 }, { tone: 0.85, step: 0.8, count: 9 });
    const wv = 16 - k * 2;
    bone(S, [[500 - wv, y + 5], [500, y + 4], [500 + wv, y + 5]], { head: 1.4, shaft: 2.2, tail: 1.4, cap: 0.25 }, { tone: 0.6, step: 0.8, count: 6 });
  }
  // pygostyle: ploughshare blade
  bone(S, [[500, 808], [500, 826], [501, 848]], s => 9 * Math.pow(1 - s, 0.6) * (s < 0.12 ? Math.sqrt(s / 0.12) : 1), { tone: 1.1, curv: 0.5, tilt: -0.4, ease: 'lin', count: 18, step: 1.2 });
}
function tail(S) {
  // twelve rectrices as one continuous rounded fan from the pygostyle toward the hem
  const nF = 12;
  fan(S, u => {
    const a = Math.PI / 2 + (0.5 - u) * 2.15;
    const L = 236 - 60 * Math.pow(Math.abs(u - 0.5) * 2, 2);
    return shaft([500 + (u - 0.5) * 70, 800 - 14 * Math.pow(Math.abs(u - 0.5) * 2, 2)], a, L, (u - 0.5) * 0.1, 70);
  }, { count: 150, tone: 2.1, curv: 0.6, tilt: -0.1, alongTone: s => 0.15 + 0.85 * smoothstep(0.05, 0.45, s), ...featherFanOpts(nF, 0.1, 0.5) });
}
export function back() {
  const S = scene('back');
  wing(S, false);
  wing(S, true);
  tail(S);
  ribsBack(S);
  pelvis(S);
  scapulae(S);
  neckAndSpine(S);
  skull(S);
  wire(S, [[500, 446], [500, 806]]);
  return render(S, 23, 6000);
}

// ---------------------------------------------------------------- rendering
const ptsD = pts => 'M' + pts.map(p => `${f1(p[0])} ${f1(p[1])}`).join('L');

function glowField(S) {
  const cs = 5, nx = 201, ny = 241;
  const g = new Float32Array(nx * ny);
  for (const [x, y, w] of S.glowPts) {
    const i = Math.round(x / cs), j = Math.round(y / cs);
    if (i < 0 || j < 0 || i >= nx || j >= ny) continue;
    g[j * nx + i] += w;
  }
  const tmp = new Float32Array(nx * ny);
  const r = 3;
  for (let pass = 0; pass < 3; pass++) {
    for (let j = 0; j < ny; j++) {
      let acc = 0;
      for (let i = -r; i < nx + r; i++) {
        if (i + r < nx) acc += g[j * nx + i + r];
        if (i - r - 1 >= 0) acc -= g[j * nx + i - r - 1];
        if (i >= 0 && i < nx) tmp[j * nx + i] = acc / (2 * r + 1);
      }
    }
    for (let i = 0; i < nx; i++) {
      let acc = 0;
      for (let j = -r; j < ny + r; j++) {
        if (j + r < ny) acc += tmp[(j + r) * nx + i];
        if (j - r - 1 >= 0) acc -= tmp[(j - r - 1) * nx + i];
        if (j >= 0 && j < ny) g[j * nx + i] = acc / (2 * r + 1);
      }
    }
  }
  const sorted = Array.from(g).filter(v => v > 0).sort((a, b) => a - b);
  const ref = sorted[Math.floor(sorted.length * 0.97)] || 1;
  return (x, y) => {
    const i = clamp(Math.round(x / cs), 0, nx - 1), j = clamp(Math.round(y / cs), 0, ny - 1);
    return clamp(g[j * nx + i] / ref);
  };
}

// lit-face grain (ref1): fine dots scattered along the strand chunks that face the key light
function litGrain(S, count, rand) {
  const cum = [];
  let tot = 0;
  for (const c of S.lit) { tot += c.wt; cum.push(tot); }
  let out = '';
  for (let i = 0; i < count && tot > 0; i++) {
    const r = rand() * tot;
    let lo = 0, hi = cum.length - 1;
    while (lo < hi) { const mid = (lo + hi) >> 1; if (cum[mid] < r) lo = mid + 1; else hi = mid; }
    const pts = S.lit[lo].pts;
    const k = Math.min(pts.length - 2, Math.floor(rand() * (pts.length - 1)));
    const p = add(mixP(pts[k], pts[k + 1], rand()), unit(perp(sub(pts[k + 1], pts[k]))), (rand() - 0.5) * 1.6);
    out += `<circle cx="${f1(p[0])}" cy="${f1(p[1])}" r="${(0.45 + rand() * 0.05).toFixed(2)}"/>`;
  }
  return `<g fill="#cfcfd4" fill-opacity="0.35">${out}</g>`;
}

function render(S, seed, litCount) {
  const rand = rng(seed);
  const P = S.side === 'front' ? 'rwf' : 'rwb';
  const field = glowField(S);
  // grain halo hugging the bones (within ~45 units): a charcoal layer and a closer, lighter one
  const clear = S.side === 'back' ? (x, y) => cuffClear(x, y) * collarClear(x, y) : cuffClear;
  const haloA = stipple((x, y) => Math.pow(field(x, y), 1.1) * 0.85 * S.fade(x, y) * clear(x, y), 36000, rand, { r: [0.45, 0.55], fill: '#3a3a3f' });
  const haloB = stipple((x, y) => Math.pow(field(x, y), 2.2) * S.fade(x, y) * clear(x, y), 6000, rand, { r: [0.45, 0.55], fill: '#4e4e54' });
  const lit = litGrain(S, litCount, rand);
  let glow = '';
  for (let i = 0; i < S.glowPts.length; i += 3) {
    const [x, y, w] = S.glowPts[i];
    glow += `<circle cx="${f1(x)}" cy="${f1(y)}" r="${f1(Math.min(12, w * 1.4 + 2))}"/>`;
  }
  // line chunks: one element each so overlapping threads accumulate light like real string art
  const buckets = new Map();
  for (const c of S.chunks) {
    const key = `${Math.round(c.op * 50) / 50}|${c.w}`;
    if (!buckets.has(key)) buckets.set(key, []);
    buckets.get(key).push(ptsD(c.pts));
  }
  let lines = '';
  for (const [key, ds] of buckets) {
    const [op, w] = key.split('|');
    lines += `<g stroke-opacity="${op}" stroke-width="${w}">` + ds.map(d => `<path d="${d}"/>`).join('') + '</g>';
  }
  let hi = '';
  for (const h of S.hi) {
    const m = h.pts[h.pts.length >> 1];
    const op = h.fadeIt ? h.op * S.fade(m[0], m[1]) : h.op;
    hi += `<path d="${ptsD(h.pts)}" stroke-opacity="${Math.round(op * 100) / 100}" stroke-width="${h.w}"/>`;
  }
  let acc = '';
  for (const a of S.accents) {
    const fo = p => Math.round(clamp(S.fade(p[0], p[1]) * 1.1, 0.35, 1) * 100) / 100;
    if (a.t === 'ball') {
      acc += `<g opacity="${fo(a.p)}">`;
      if (a.ring) acc += `<circle cx="${f1(a.p[0])}" cy="${f1(a.p[1])}" r="${f1(a.r + 1.8)}" fill="none" stroke="#b9b9be" stroke-width="0.5" stroke-opacity="0.6"/>`;
      acc += `<circle cx="${f1(a.p[0])}" cy="${f1(a.p[1])}" r="${f1(a.r)}" fill="url(#${P}-ball)"/></g>`;
    } else if (a.t === 'pin') {
      const p0 = add(a.p, a.d, -a.L * 0.35), p1 = add(a.p, a.d, a.L);
      acc += `<g opacity="${fo(a.p)}"><path d="${ptsD([p0, p1])}" stroke="#c9c9ce" stroke-width="0.7" stroke-opacity="0.8" fill="none"/>`;
      acc += `<circle cx="${f1(p1[0])}" cy="${f1(p1[1])}" r="1.7" fill="url(#${P}-ball)"/>`;
      acc += `<circle cx="${f1(p0[0])}" cy="${f1(p0[1])}" r="1.2" fill="url(#${P}-ball)"/></g>`;
    } else if (a.t === 'wire') {
      const [p, q] = a.pts;
      acc += `<path d="${ptsD(a.pts)}" stroke="#bfbfc4" stroke-width="0.5" stroke-opacity="${fo(mixP(p, q, 0.5)) * 0.5}" fill="none"/>`;
      if (a.beads) {
        const nb = Math.max(2, Math.round(len(sub(q, p)) / 46));
        for (let k = 0; k <= nb; k++) {
          const b = mixP(p, q, k / nb);
          acc += `<circle cx="${f1(b[0])}" cy="${f1(b[1])}" r="1.2" fill="url(#${P}-ball)" opacity="${fo(b)}"/>`;
        }
      }
    } else if (a.t === 'dot') {
      acc += `<circle cx="${f1(a.p[0])}" cy="${f1(a.p[1])}" r="${a.r}" fill="#e8e8ec" fill-opacity="${Math.round(a.op * 100) / 100}"/>`;
    }
  }
  const defs = `
    <radialGradient id="${P}-ball" cx="35%" cy="32%" r="70%">
      <stop offset="0" stop-color="#f4f4f6"/><stop offset="0.45" stop-color="#8d8d93"/><stop offset="1" stop-color="#1b1b1e"/>
    </radialGradient>
    <filter id="${P}-blur" x="-10%" y="-10%" width="120%" height="120%"><feGaussianBlur stdDeviation="14"/></filter>
    <radialGradient id="${P}-air" cx="28%" cy="12%" r="80%">
      <stop offset="0" stop-color="#ffffff" stop-opacity="0.06"/><stop offset="0.6" stop-color="#ffffff" stop-opacity="0.012"/><stop offset="1" stop-color="#ffffff" stop-opacity="0"/>
    </radialGradient>`;
  const art = `
  <rect width="1000" height="1200" fill="url(#${P}-air)"/>
  <g fill="#ffffff" fill-opacity="0.045" filter="url(#${P}-blur)">${glow}</g>
  ${haloA}${haloB}
  <g fill="none" stroke="#efeff2" stroke-linecap="butt" stroke-linejoin="round">${lines}</g>
  ${lit}
  <g fill="none" stroke="#ffffff" stroke-linecap="round">${hi}</g>
  ${acc}`;
  return { art, defs };
}
