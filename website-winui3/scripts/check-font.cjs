// 解析 SEGOEICONS.TTF 的 cmap 表，列出全部可用 codepoint
const fs = require('fs');
const path = process.argv[2];
const buf = fs.readFileSync(path);
const numTables = buf.readUInt16BE(4);
let cmapOffset = -1;
for (let i = 0; i < numTables; i++) {
  const tag = buf.toString('ascii', 12 + i * 16, 12 + i * 16 + 4);
  if (tag === 'cmap') { cmapOffset = buf.readUInt32BE(12 + i * 16 + 8); break; }
}
if (cmapOffset < 0) { console.error('no cmap'); process.exit(1); }
const numSub = buf.readUInt16BE(cmapOffset + 2);
const codepoints = new Set();
let best = null;
for (let i = 0; i < numSub; i++) {
  const platformID = buf.readUInt16BE(cmapOffset + 4 + i * 8);
  const encodingID = buf.readUInt16BE(cmapOffset + 4 + i * 8 + 2);
  const off = cmapOffset + buf.readUInt32BE(cmapOffset + 4 + i * 8 + 4);
  const format = buf.readUInt16BE(off);
  if (platformID === 3 && encodingID === 10 && format === 12) best = { off, format, score: 3 };
  else if (platformID === 0 && format === 4 && (!best || best.score < 2)) best = { off, format, score: 2 };
  else if (format === 4 && (!best || best.score < 1)) best = { off, format, score: 1 };
}
if (!best) { console.error('no usable cmap subtable'); process.exit(1); }
const { off, format } = best;
if (format === 12) {
  const nGroups = buf.readUInt32BE(off + 12);
  for (let g = 0; g < nGroups; g++) {
    const start = buf.readUInt32BE(off + 16 + g * 12);
    const end = buf.readUInt32BE(off + 16 + g * 12 + 4);
    for (let cp = start; cp <= end; cp++) codepoints.add(cp);
  }
} else {
  const segCount = buf.readUInt16BE(off + 6) / 2;
  const endCode = off + 14;
  const startCode = endCode + segCount * 2 + 2;
  for (let i = 0; i < segCount; i++) {
    const start = buf.readUInt16BE(startCode + i * 2);
    const end = buf.readUInt16BE(endCode + i * 2);
    if (start === 0xFFFF) continue;
    for (let cp = start; cp <= end; cp++) codepoints.add(cp);
  }
}
const want = [0xE80F, 0xE896, 0xE946, 0xE774, 0xE7E8, 0xECDE, 0xE73D, 0xE943, 0xE8B7, 0xE7B8, 0xE9F7, 0xE790, 0xE700, 0xE8A1, 0xE721, 0xE72B, 0xE713, 0xE76F, 0xE8D2, 0xE945, 0xE8F2, 0xE15F, 0xE8FD, 0xE9D9, 0xE9D5, 0xE9F5, 0xEA37, 0xE8D6, 0xE9B0, 0xE9D2];
console.log('total glyphs:', codepoints.size);
for (const cp of want) {
  console.log('U+' + cp.toString(16).toUpperCase().padStart(4, '0'), codepoints.has(cp) ? 'OK' : 'MISSING');
}
