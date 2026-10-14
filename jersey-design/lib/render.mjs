// 用 Chromium 把 SVG 輸出成 PNG。多張 SVG 會左右並排（各自當成 <img>，id 不會互相衝突）。
import { chromium } from 'playwright';
import { writeFileSync } from 'node:fs';

let browser;

export async function renderPng(svgs, outPath, { width = 1000, height = 1200, scale = 1.6 } = {}) {
  svgs = [].concat(svgs);
  browser ??= await chromium.launch(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {});
  const total = width * svgs.length;
  const page = await browser.newPage({ viewport: { width: total, height }, deviceScaleFactor: scale });
  const imgs = svgs.map(s => `<img width="${width}" height="${height}" src="data:image/svg+xml;base64,${Buffer.from(s).toString('base64')}">`).join('');
  await page.setContent(`<html><body style="margin:0;background:#000;display:flex">${imgs}</body></html>`);
  await page.waitForFunction(() => [...document.images].every(i => i.complete && i.naturalWidth > 0));
  writeFileSync(outPath, await page.screenshot({ clip: { x: 0, y: 0, width: total, height } }));
  await page.close();
}

export async function closeRenderer() {
  await browser?.close();
  browser = undefined;
}
