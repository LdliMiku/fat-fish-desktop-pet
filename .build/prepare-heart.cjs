// 从手绘爱心源图生成两版粒子贴图：
//   assets/particles/pink-heart.png        小爱心：只保留心形本体，去掉四周星光
//   assets/particles/pink-heart-large.png  大爱心：完整图（心形 + 星光）
// 只做裁切、连通块筛选与缩放，不重绘。
const fs = require('fs');
const sharp = require('C:/Users/Lenovo/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');

const source = process.argv[2] || 'assets/particles/heart-crayon-source.png';
const smallOut = process.argv[3] || 'assets/particles/pink-heart.png';
const largeOut = process.argv[4] || 'assets/particles/pink-heart-large.png';
const SMALL_WIDTH = 256, LARGE_WIDTH = 512, ALPHA_FLOOR = 96;

(async () => {
  const { data, info } = await sharp(source).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const w = info.width, h = info.height;
  const seen = new Uint8Array(w * h);
  const queue = new Int32Array(w * h);
  const parts = [];
  for (let i = 0; i < w * h; i++) {
    if (seen[i] || data[i * 4 + 3] <= ALPHA_FLOOR) continue;
    let head = 0, tail = 0; queue[tail++] = i; seen[i] = 1;
    let minX = w, maxX = -1, minY = h, maxY = -1, count = 0;
    while (head < tail) {
      const n = queue[head++], x = n % w, y = (n - x) / w;
      count++;
      if (x < minX) minX = x; if (x > maxX) maxX = x;
      if (y < minY) minY = y; if (y > maxY) maxY = y;
      for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
        const px = x + dx, py = y + dy;
        if (px < 0 || px >= w || py < 0 || py >= h) continue;
        const idx = py * w + px;
        if (seen[idx] || data[idx * 4 + 3] <= ALPHA_FLOOR) continue;
        seen[idx] = 1; queue[tail++] = idx;
      }
    }
    if (count > 500) parts.push({ minX, maxX, minY, maxY, count });
  }
  if (parts.length === 0) throw new Error('源图里没有找到不透明内容。');
  parts.sort((a, b) => b.count - a.count);
  console.log('components: ' + parts.map((p) => p.count).join(', '));
  const heart = parts[0];

  // 小爱心：从心形本体里重新做一次连通块标记，只输出这一块
  const heartMask = new Uint8Array(w * h);
  let seed = -1;
  for (let y = heart.minY; y <= heart.maxY && seed < 0; y++)
    for (let x = heart.minX; x <= heart.maxX; x++)
      if (data[(y * w + x) * 4 + 3] > ALPHA_FLOOR) { seed = y * w + x; break; }
  let head = 0, tail = 0;
  queue[tail++] = seed; heartMask[seed] = 1;
  while (head < tail) {
    const n = queue[head++], x = n % w, y = (n - x) / w;
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
      const px = x + dx, py = y + dy;
      if (px < 0 || px >= w || py < 0 || py >= h) continue;
      const idx = py * w + px;
      if (heartMask[idx] || data[idx * 4 + 3] <= ALPHA_FLOOR) continue;
      heartMask[idx] = 1; queue[tail++] = idx;
    }
  }
  const only = Buffer.alloc(w * h * 4);
  for (let idx = 0; idx < w * h; idx++) {
    if (!heartMask[idx]) continue;
    const p = idx * 4;
    only[p] = data[p]; only[p + 1] = data[p + 1]; only[p + 2] = data[p + 2]; only[p + 3] = data[p + 3];
  }
  await sharp(only, { raw: { width: w, height: h, channels: 4 } })
    .extract({ left: heart.minX, top: heart.minY, width: heart.maxX - heart.minX + 1, height: heart.maxY - heart.minY + 1 })
    .resize({ width: SMALL_WIDTH, kernel: 'lanczos3' })
    .png().toFile(smallOut);

  // 大爱心：完整原图（含星光），只裁掉透明边
  await sharp(source).trim({ threshold: 10 }).resize({ width: LARGE_WIDTH, kernel: 'lanczos3' }).png().toFile(largeOut);

  const small = await sharp(smallOut).metadata();
  const large = await sharp(largeOut).metadata();
  console.log(`small ${smallOut} ${small.width}x${small.height}（只有心形）`);
  console.log(`large ${largeOut} ${large.width}x${large.height}（心形 + 星光）`);
})();
