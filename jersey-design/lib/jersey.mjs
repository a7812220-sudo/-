// V 領插肩短袖排球衣版型（平面正視圖）。
// 座標系統：viewBox 0 0 1000 1200，正面與背面共用同一個外輪廓。
// 圖案（art）用同一個座標系統繪製，會被裁切在衣身範圍內。

export const W = 1000;
export const H = 1200;

// 衣身外輪廓（含袖子），從左頸點順時針。
export const BODY =
  'M398 96 C330 116 252 148 196 196 C160 262 120 360 84 452 L214 508 ' +
  'C228 470 246 430 262 404 C276 600 286 860 280 1118 C420 1134 580 1134 720 1118 ' +
  'C714 860 724 600 738 404 C754 430 772 470 786 508 L916 452 C880 360 840 262 804 196 ' +
  'C748 148 670 116 602 96 C568 108 534 114 500 114 C466 114 432 108 398 96 Z';

// 正面 V 領開口（看得到後領內側）。
const V_OPEN = 'M398 96 C430 108 466 114 500 114 C534 114 570 108 602 96 C584 150 544 208 500 262 C456 208 416 150 398 96 Z';
// V 領羅紋：開口外側 22 單位寬的帶子。
const V_BAND_OUTER = 'M384 92 C402 160 448 226 500 292 C552 226 598 160 616 92';
const V_BAND_INNER = 'M398 96 C416 150 456 208 500 262 C544 208 584 150 602 96';
// 背面圓領：後領帶。
const BACK_BAND_OUTER = 'M384 92 C420 116 462 128 500 128 C538 128 580 116 616 92';
const BACK_BAND_INNER = 'M398 96 C432 108 466 114 500 114 C534 114 568 108 602 96';

// 插肩縫線。
const RAGLAN_L = 'M392 100 C350 190 300 300 262 404';
const RAGLAN_R = 'M608 100 C650 190 700 300 738 404';

// 袖口：袖口邊與往內 30 單位的平行線之間是袖口羅紋。
const CUFF_L = { a: [84, 452], b: [214, 508] };
const CUFF_R = { a: [916, 452], b: [786, 508] };

function cuffBand(c, inset) {
  // 往袖子內側（沿袖子方向往肩膀）平移。
  const dx = c.b[0] - c.a[0], dy = c.b[1] - c.a[1];
  const len = Math.hypot(dx, dy);
  let nx = dy / len, ny = -dx / len;
  if (ny > 0) { nx = -nx; ny = -ny; }
  const p = (pt, d) => `${(pt[0] + nx * d).toFixed(1)} ${(pt[1] + ny * d).toFixed(1)}`;
  return { nx, ny, p };
}

function cuff(c, piping) {
  const { p } = cuffBand(c);
  // 白色細條：在袖口羅紋的上緣。
  return `
    <path d="M${p(c.a, 0)} L${p(c.b, 0)} L${p(c.b, 30)} L${p(c.a, 30)} Z" fill="#0a0a0b"/>
    <path d="M${p(c.a, 0)} L${p(c.b, 0)} L${p(c.b, 30)} L${p(c.a, 30)} Z" fill="url(#rib)" opacity="0.5"/>
    <path d="M${p(c.a, 22)} L${p(c.b, 22)}" stroke="${piping}" stroke-width="5" fill="none"/>
    <path d="M${p(c.a, 30)} L${p(c.b, 30)}" stroke="#000" stroke-opacity="0.6" stroke-width="1.2" fill="none"/>`;
}

const DEFS = `
  <clipPath id="body"><path d="${BODY}"/></clipPath>
  <radialGradient id="studio" cx="50%" cy="42%" r="70%">
    <stop offset="0" stop-color="#2b2b2e"/><stop offset="0.55" stop-color="#141415"/><stop offset="1" stop-color="#050505"/>
  </radialGradient>
  <linearGradient id="vShade" x1="0" x2="0" y1="0" y2="1">
    <stop offset="0" stop-color="#fff" stop-opacity="0.05"/>
    <stop offset="0.35" stop-color="#000" stop-opacity="0"/>
    <stop offset="0.85" stop-color="#000" stop-opacity="0.18"/>
    <stop offset="1" stop-color="#000" stop-opacity="0.45"/>
  </linearGradient>
  <radialGradient id="chestLight" cx="50%" cy="28%" r="38%">
    <stop offset="0" stop-color="#fff" stop-opacity="0.07"/><stop offset="1" stop-color="#fff" stop-opacity="0"/>
  </radialGradient>
  <pattern id="mesh" width="5" height="5" patternUnits="userSpaceOnUse">
    <circle cx="1.25" cy="1.25" r="0.75" fill="#000" fill-opacity="0.55"/>
    <circle cx="3.75" cy="3.75" r="0.75" fill="#000" fill-opacity="0.55"/>
  </pattern>
  <pattern id="rib" width="3" height="3" patternUnits="userSpaceOnUse" patternTransform="rotate(70)">
    <rect width="1.2" height="3" fill="#fff" fill-opacity="0.08"/>
  </pattern>
  <filter id="soft" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="14"/></filter>
  <filter id="softer" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="30"/></filter>`;

// 立體感：衣身陰影、皺褶、網眼、縫線。只用在預覽圖，不進印刷檔。
const SHADING = `
  <g clip-path="url(#body)">
    <rect width="${W}" height="${H}" fill="url(#chestLight)"/>
    <path d="${BODY}" fill="none" stroke="#000" stroke-width="110" stroke-opacity="0.75" filter="url(#softer)"/>
    <path d="${BODY}" fill="none" stroke="#000" stroke-width="24" stroke-opacity="0.6" filter="url(#soft)"/>
    <rect width="${W}" height="${H}" fill="url(#vShade)"/>
    <g filter="url(#soft)" fill="none" stroke="#000">
      <path d="M262 420 C300 470 320 520 330 600" stroke-width="28" stroke-opacity="0.5"/>
      <path d="M738 420 C700 470 680 520 670 600" stroke-width="28" stroke-opacity="0.5"/>
      <path d="M150 300 C130 360 110 410 96 450" stroke-width="40" stroke-opacity="0.45"/>
      <path d="M850 300 C870 360 890 410 904 450" stroke-width="40" stroke-opacity="0.45"/>
      <path d="M300 1060 C420 1080 580 1080 700 1060" stroke-width="40" stroke-opacity="0.35"/>
      <path d="M290 780 C330 800 360 830 380 880" stroke-width="20" stroke-opacity="0.25"/>
      <path d="M710 780 C670 800 640 830 620 880" stroke-width="20" stroke-opacity="0.25"/>
    </g>
    <g filter="url(#soft)" fill="none" stroke="#fff">
      <path d="M220 230 C280 200 340 180 380 170" stroke-width="18" stroke-opacity="0.06"/>
      <path d="M780 230 C720 200 660 180 620 170" stroke-width="18" stroke-opacity="0.06"/>
      <path d="M430 330 C470 420 480 520 470 640" stroke-width="40" stroke-opacity="0.025"/>
    </g>
    <rect width="${W}" height="${H}" fill="url(#mesh)" opacity="0.35"/>
    <path d="M276 1100 C420 1116 580 1116 724 1100" fill="none" stroke="#000" stroke-opacity="0.55" stroke-width="1.4"/>
    <path d="M276 1103 C420 1119 580 1119 724 1103" fill="none" stroke="#fff" stroke-opacity="0.05" stroke-width="1"/>
  </g>`;

function seams() {
  return [RAGLAN_L, RAGLAN_R].map(d => `
    <path d="${d}" fill="none" stroke="#000" stroke-opacity="0.7" stroke-width="2"/>
    <path d="${d}" transform="translate(1.6 0)" fill="none" stroke="#fff" stroke-opacity="0.06" stroke-width="1"/>`).join('');
}

function collar(side, piping) {
  if (side === 'front') {
    return `
      <path d="${V_OPEN}" fill="#050505"/>
      <path d="${BACK_BAND_INNER} L602 96 C590 88 560 80 500 80 C440 80 410 88 398 96 Z" fill="#0b0b0c"/>
      <path d="M398 96 C432 108 466 114 500 114 C534 114 568 108 602 96" fill="none" stroke="${piping}" stroke-width="4" stroke-opacity="0.75"/>
      <path d="${V_BAND_OUTER} L602 96 C584 150 544 208 500 262 C456 208 416 150 398 96 Z" fill="#0a0a0b"/>
      <path d="${V_BAND_OUTER} L602 96 C584 150 544 208 500 262 C456 208 416 150 398 96 Z" fill="url(#rib)" opacity="0.5"/>
      <path d="${V_BAND_INNER}" fill="none" stroke="${piping}" stroke-width="6"/>
      <path d="${V_BAND_OUTER}" fill="none" stroke="#000" stroke-opacity="0.8" stroke-width="1.5"/>`;
  }
  return `
    <path d="${BACK_BAND_OUTER} L602 96 C568 108 534 114 500 114 C466 114 432 108 398 96 Z" fill="#0a0a0b"/>
    <path d="${BACK_BAND_OUTER} L602 96 C568 108 534 114 500 114 C466 114 432 108 398 96 Z" fill="url(#rib)" opacity="0.5"/>
    <path d="${BACK_BAND_INNER}" fill="none" stroke="${piping}" stroke-width="6"/>
    <path d="${BACK_BAND_OUTER}" fill="none" stroke="#000" stroke-opacity="0.8" stroke-width="1.5"/>`;
}

// 擬真預覽：攝影棚背景＋衣身＋圖案＋光影＋領口袖口。
// art：已經在 1000x1200 座標中畫好的 SVG 片段；defs：圖案自己需要的 <defs> 內容；piping：領口與袖口滾邊顏色。
export function mockup({ side, art = '', defs = '', base = '#0c0c0d', piping = '#f4f4f2' }) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <defs>${DEFS}${defs}</defs>
  <rect width="${W}" height="${H}" fill="url(#studio)"/>
  <ellipse cx="500" cy="1150" rx="300" ry="26" fill="#000" opacity="0.7" filter="url(#soft)"/>
  <path d="${BODY}" fill="${base}"/>
  <g clip-path="url(#body)">${art}</g>
  ${SHADING}
  <path d="${BODY}" fill="none" stroke="#fff" stroke-opacity="0.09" stroke-width="2"/>
  ${seams()}
  ${cuff(CUFF_L, piping)}${cuff(CUFF_R, piping)}
  ${collar(side, piping)}
</svg>`;
}

// 印刷用：不裁切、不加光影的滿版圖案（含出血），由工廠套到自己的版片上；擺放位置以預覽圖為準。
export function printFile({ art = '', defs = '', base = '#0c0c0d' }) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" width="${W}" height="${H}">
  <defs>${defs}</defs>
  <rect width="${W}" height="${H}" fill="${base}"/>
  ${art}
</svg>`;
}
