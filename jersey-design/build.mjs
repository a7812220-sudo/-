// 用法：node build.mjs [concepts/xxx.mjs ...] [--out 目錄] [--print-png]
// 每個設計模組要匯出 front() 與 back()，各自回傳 { art, defs?, base? }（1000x1200 座標的 SVG 片段）。
// 輸出：<out>/<名稱>-mockup.png（正背並排）、-front.png、-back.png，以及 <out>/print/<名稱>-front.svg、-back.svg。
import { mkdirSync, readdirSync, writeFileSync } from 'node:fs';
import { basename, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { mockup, printFile } from './lib/jersey.mjs';
import { renderPng, closeRenderer } from './lib/render.mjs';

const args = process.argv.slice(2);
const flag = name => { const i = args.indexOf(name); return i < 0 ? undefined : args.splice(i, 2)[1]; };
const printPng = args.includes('--print-png') && args.splice(args.indexOf('--print-png'), 1);
const out = resolve(flag('--out') ?? 'out');
const files = args.length ? args : readdirSync('concepts').filter(f => f.endsWith('.mjs')).map(f => `concepts/${f}`);
mkdirSync(`${out}/print`, { recursive: true });

for (const file of files) {
  const name = basename(file, '.mjs');
  const mod = await import(pathToFileURL(resolve(file)).href + `?t=${process.hrtime.bigint()}`);
  const sides = { front: await mod.front(), back: await mod.back() };
  const views = {};
  for (const side of ['front', 'back']) {
    views[side] = mockup({ side, ...sides[side] });
    const print = printFile(sides[side]);
    writeFileSync(`${out}/print/${name}-${side}.svg`, print);
    await renderPng(views[side], `${out}/${name}-${side}.png`);
    if (printPng) await renderPng(print, `${out}/print/${name}-${side}.png`, { scale: 5 });
  }
  await renderPng([views.front, views.back], `${out}/${name}-mockup.png`, { scale: 1 });
  console.log(`${name}: ${out}/${name}-mockup.png`);
}
await closeRenderer();
