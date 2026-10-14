// 圖案共用工具：亂數、雜訊、平滑曲線、線條編織、等高線、顆粒點描。
import { contours } from 'd3-contour';
import { createNoise2D } from 'simplex-noise';

// 可重現的亂數（mulberry32）。
export function rng(seed = 1) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export const noise2D = (seed = 1) => createNoise2D(rng(seed));

const f = n => +n.toFixed(1);

// 點列 -> 折線 path。
export function polyline(pts, closed = false) {
  return 'M' + pts.map(p => `${f(p[0])} ${f(p[1])}`).join('L') + (closed ? 'Z' : '');
}

// 點列 -> Catmull-Rom 平滑曲線（轉成三次貝茲）。
export function smooth(pts, closed = false) {
  const n = pts.length;
  if (n < 3) return polyline(pts, closed);
  const at = i => closed ? pts[(i + n) % n] : pts[Math.max(0, Math.min(n - 1, i))];
  let d = `M${f(pts[0][0])} ${f(pts[0][1])}`;
  const last = closed ? n : n - 1;
  for (let i = 0; i < last; i++) {
    const p0 = at(i - 1), p1 = at(i), p2 = at(i + 1), p3 = at(i + 2);
    d += `C${f(p1[0] + (p2[0] - p0[0]) / 6)} ${f(p1[1] + (p2[1] - p0[1]) / 6)} ` +
      `${f(p2[0] - (p3[0] - p1[0]) / 6)} ${f(p2[1] - (p3[1] - p1[1]) / 6)} ${f(p2[0])} ${f(p2[1])}`;
  }
  return d + (closed ? 'Z' : '');
}

// 三次貝茲取樣：b = [[x0,y0],[x1,y1],[x2,y2],[x3,y3]]。
export function bezier(b, n = 48) {
  const out = [];
  for (let i = 0; i <= n; i++) {
    const t = i / n, u = 1 - t;
    out.push([0, 1].map(k => u * u * u * b[0][k] + 3 * u * u * t * b[1][k] + 3 * u * t * t * b[2][k] + t * t * t * b[3][k]));
  }
  return out;
}

// 線條編織（參考圖的蜂鳥）：在兩條同樣點數的曲線之間內插出 count 條線。
// ease(t) 可以讓線條在一側變密。回傳每條線的點列。
export function weave(a, b, count, ease = t => t) {
  const lines = [];
  for (let i = 0; i < count; i++) {
    const t = ease(count === 1 ? 0 : i / (count - 1));
    lines.push(a.map((p, j) => [p[0] + (b[j][0] - p[0]) * t, p[1] + (b[j][1] - p[1]) * t]));
  }
  return lines;
}

// 純量場的等高線（地形線）。field(x, y) 在衣身座標中取值。
// 回傳 [{ value, d }]，d 是 SVG path。
export function contourPaths(field, thresholds, { x0 = -20, y0 = -20, w = 1040, h = 1240, step = 4 } = {}) {
  const nx = Math.ceil(w / step) + 1, ny = Math.ceil(h / step) + 1;
  const values = new Float64Array(nx * ny);
  for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) values[j * nx + i] = field(x0 + i * step, y0 + j * step);
  return contours().size([nx, ny]).thresholds(thresholds)(values).map(c => ({
    value: c.value,
    d: c.coordinates.map(poly => poly.map(ring => polyline(ring.map(([gx, gy]) => [x0 + gx * step, y0 + gy * step]), true)).join('')).join(''),
  }));
}

// 顆粒點描（參考圖的噴砂光影）：density(x, y) 回傳 0..1，用拒絕取樣撒點。
export function stipple(density, count, rand, { r = [0.5, 1.1], fill = '#fff', opacity = 1, box = [0, 0, 1000, 1200] } = {}) {
  const [bx, by, bw, bh] = box;
  let out = '', placed = 0, tries = 0;
  while (placed < count && tries < count * 40) {
    tries++;
    const x = bx + rand() * bw, y = by + rand() * bh;
    if (rand() > density(x, y)) continue;
    out += `<circle cx="${f(x)}" cy="${f(y)}" r="${f(r[0] + rand() * (r[1] - r[0]))}"/>`;
    placed++;
  }
  return `<g fill="${fill}" fill-opacity="${opacity}">${out}</g>`;
}

// 點到線段距離，常用來做骨頭的距離場。
export function segDist(px, py, ax, ay, bx, by) {
  const dx = bx - ax, dy = by - ay;
  const t = Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy || 1)));
  return Math.hypot(px - ax - t * dx, py - ay - t * dy);
}
