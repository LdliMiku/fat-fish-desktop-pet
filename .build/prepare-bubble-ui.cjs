// 把参考图拆成气泡运行素材并量出九宫格几何：
//   assets/ui/bubble-body.png   本体（去掉尾巴和右下角装饰，缺口用邻域补齐）
//   assets/ui/bubble-tail.png   尾巴贴图（固定贴在靠角色那一侧的底角）
//   assets/ui/bubble-whale.png  右下角装饰贴图（小鲸鱼那一块；素材里没有就跳过）
//   assets/ui/bubble-meta.json  缩放、本体矩形、四角尺寸、尾巴与装饰的位置、内边距、文字样式
// 用法：node .build/prepare-bubble-ui.cjs [源图] [--scale=0.13] [--corner=左,右,上,下]
// 只复制与修补像素，不重绘；换素材时重跑本脚本即可，不需要改 C# 代码。
const fs = require('fs');
const path = require('path');
const sharp = require('C:/Users/Lenovo/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');

const argv = process.argv.slice(2);
const source = argv.find((a) => !a.startsWith('--')) || 'assets/ui/bubble-source.png';
const scaleArg = Number(((argv.find((a) => a.startsWith('--scale=')) || '').split('=')[1] || '0'));
const cornerArg = ((argv.find((a) => a.startsWith('--corner=')) || '').split('=')[1] || '').split(',').map(Number);
const OUT = 'assets/ui';

(async () => {
  const { data, info } = await sharp(source).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const w = info.width, h = info.height;
  const alphaAt = (x, y) => data[(y * w + x) * 4 + 3];
  const solid = (x, y) => alphaAt(x, y) > 16;
  const strong = (x, y) => alphaAt(x, y) > 120;

  // ---- 1. 内容范围 ----
  let minX = w, maxX = -1, minY = h, maxY = -1;
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    if (!solid(x, y)) continue;
    if (x < minX) minX = x; if (x > maxX) maxX = x;
    if (y < minY) minY = y; if (y > maxY) maxY = y;
  }

  const rowLeft = (y) => { for (let x = 0; x < w; x++) if (solid(x, y)) return x; return -1; };
  const rowRight = (y) => { for (let x = w - 1; x >= 0; x--) if (solid(x, y)) return x; return -1; };
  const columnTop = (x) => { for (let y = 0; y < h; y++) if (solid(x, y)) return y; return -1; };
  const bottomOf = (x) => { for (let y = maxY; y >= minY; y--) if (solid(x, y)) return y; return -1; };

  // ---- 2. 本体左右边界（跳过上方圆角） ----
  const bodyTop = minY;
  let bodyLeft = w, bodyRight = -1;
  for (let y = Math.round(minY + (maxY - minY) * 0.30); y <= Math.round(minY + (maxY - minY) * 0.36); y++) {
    const l = rowLeft(y), r = rowRight(y);
    if (l < 0) continue;
    bodyLeft = Math.min(bodyLeft, l); bodyRight = Math.max(bodyRight, r);
  }

  // ---- 3. 底边线：手绘抖动大，按 8 像素分箱取最多的一箱的中位数 ----
  const bins = new Map();
  for (let x = bodyLeft + 150; x <= bodyRight - 150; x++) {
    const b = bottomOf(x);
    if (b < 0) continue;
    const key = Math.round(b / 8);
    if (!bins.has(key)) bins.set(key, []);
    bins.get(key).push(b);
  }
  const bestBin = [...bins.entries()].sort((a, b) => b[1].length - a[1].length)[0][1].slice().sort((a, b) => a - b);
  const bottomLine = bestBin[Math.floor(bestBin.length / 2)];

  // ---- 4. 尾巴：底边下方连续下凹 >12 像素、成片 ≥24 列 ----
  const runs = [];
  let run = null;
  for (let x = bodyLeft + 20; x <= bodyRight - 20; x++) {
    const b = bottomOf(x);
    const deep = b > bottomLine + 12;
    if (deep) {
      if (!run) run = { from: x, to: x, tip: b };
      else { run.to = x; run.tip = Math.max(run.tip, b); }
    } else if (run) { runs.push(run); run = null; }
  }
  if (run) runs.push(run);
  const wide = runs.filter((r) => r.to - r.from >= 24);
  const centre = (bodyLeft + bodyRight) / 2, span = bodyRight - bodyLeft;
  const middle = wide.filter((r) => (r.from + r.to) / 2 > bodyLeft + span * 0.2 && (r.from + r.to) / 2 < bodyLeft + span * 0.8);
  const tailRun = middle.length
    ? middle.reduce((a, b) => (Math.abs((a.from + a.to) / 2 - centre) <= Math.abs((b.from + b.to) / 2 - centre) ? a : b))
    : (wide.length ? wide[wide.length - 1] : null);
  if (!tailRun) throw new Error('没有找到尾巴：底边下方没有连续下凹的区段。');
  const tailL = tailRun.from, tailR = tailRun.to, tailTip = tailRun.tip;
  console.log('tail runs ' + JSON.stringify(wide) + ' chosen ' + JSON.stringify(tailRun));

  // ---- 5. 右下角装饰（小鲸鱼那一块）----
  const rowFill = (y) => {
    const acc = [0, 0, 0];
    let n = 0;
    for (let x = bodyLeft + Math.round(span * 0.30); x <= bodyLeft + Math.round(span * 0.45); x++) {
      const p = (y * w + x) * 4;
      if (data[p + 3] < 200) continue;
      acc[0] += data[p]; acc[1] += data[p + 1]; acc[2] += data[p + 2]; n++;
    }
    return n ? [acc[0] / n, acc[1] / n, acc[2] / n] : null;
  };
  let ox0 = w, ox1 = -1, oy0 = h, oy1 = -1, outside = 0;
  for (let y = minY; y <= maxY; y++) for (let x = minX; x <= maxX; x++) {
    if (!strong(x, y)) continue;
    if (x <= bodyRight + 4 && y <= bottomLine + 4) continue;
    if (x >= tailL - 24 && x <= tailR + 24) continue;            // 尾巴不算装饰
    outside++;
    if (x < ox0) ox0 = x; if (x > ox1) ox1 = x;
    if (y < oy0) oy0 = y; if (y > oy1) oy1 = y;
  }
  const outsideBox = (outside > 1500 && ox1 - ox0 > 60 && oy1 - oy0 > 60) ? { left: ox0, top: oy0, right: ox1 + 1, bottom: oy1 + 1 } : null;

  // 画在气泡内部的装饰（把手绘素材的鲸鱼、爱心、水花当作一块贴图抠出来，避免被九宫格拉变形）
  let ix0 = w, ix1 = -1, iy0 = h, iy1 = -1, inside = 0;
  const winLeft = Math.round(bodyRight - span * 0.45), winRight = bodyRight - 30;
  const winTop = Math.round(bottomLine - (bottomLine - bodyTop) * 0.5), winBottom = bottomLine - 8;
  for (let y = winTop; y <= winBottom; y++) {
    const fill = rowFill(y);
    if (!fill) continue;
    for (let x = winLeft; x <= winRight; x++) {
      if (!strong(x, y)) continue;
      const p = (y * w + x) * 4;
      const diff = Math.max(Math.abs(data[p] - fill[0]), Math.abs(data[p + 1] - fill[1]), Math.abs(data[p + 2] - fill[2]));
      if (diff <= 28) continue;
      inside++;
      if (x < ix0) ix0 = x; if (x > ix1) ix1 = x;
      if (y < iy0) iy0 = y; if (y > iy1) iy1 = y;
    }
  }
  const insideBox = inside > 1200 ? { left: Math.max(minX, ix0 - 8), top: Math.max(minY, iy0 - 8), right: Math.min(bodyRight, ix1 + 10), bottom: Math.min(bottomLine, iy1 + 10) } : null;
  const over = outsideBox || insideBox;
  console.log('decoration: outside ' + outside + ' px, inside ' + inside + ' px -> ' + (over ? JSON.stringify(over) + (outsideBox ? ' (outside)' : ' (inside)') : 'none'));

  // ---- 6. 本体照原图输出：尾巴和右下角装饰都留在右下角切片里，避免切开后描边断开 ----
  const sliceBottom = maxY;
  // 九宫格会把中间和边缘拉伸、四角保持原样，纸纹密度在切片边界对不上就会显出一条线。
  // 因此把"纯底色区域"压成同一个底色；描边、鲸鱼、爱心等墨色像素先按膨胀掩膜保护起来，
  // 否则爱心内部的浅色也会被一起抹掉（那是上一版的 bug）。
  const fillForFlat = (() => {
    const acc = [0, 0, 0];
    let n = 0;
    for (let y = Math.round(bodyTop + (bottomLine - bodyTop) * 0.3); y <= Math.round(bodyTop + (bottomLine - bodyTop) * 0.7); y++) {
      for (let x = Math.round(bodyLeft + (bodyRight - bodyLeft) * 0.3); x <= Math.round(bodyLeft + (bodyRight - bodyLeft) * 0.5); x++) {
        const p = (y * w + x) * 4;
        if (data[p + 3] < 240) continue;
        acc[0] += data[p]; acc[1] += data[p + 1]; acc[2] += data[p + 2]; n++;
      }
    }
    return n ? [Math.round(acc[0] / n), Math.round(acc[1] / n), Math.round(acc[2] / n)] : [238, 236, 230];
  })();
  const bodyW = bodyRight - bodyLeft + 1, bodyH = sliceBottom - bodyTop + 1;
  const protectedMask = new Uint8Array(bodyW * bodyH);
  const inkTest = (x, y) => {
    const p = (y * w + x) * 4;
    if (data[p + 3] < 250) return true;                      // 柔和边缘也算墨色
    const diff = Math.max(Math.abs(data[p] - fillForFlat[0]), Math.abs(data[p + 1] - fillForFlat[1]), Math.abs(data[p + 2] - fillForFlat[2]));
    return diff > 14;
  };
  // 保护范围只留 3 像素：太大就会把描边附近的纸纹一起留下，拉伸后又在切片边界显出来。
  const reach = 3;
  for (let y = bodyTop; y <= sliceBottom; y++) for (let x = bodyLeft; x <= bodyRight; x++) {
    if (!inkTest(x, y)) continue;
    for (let dy = -reach; dy <= reach; dy++) for (let dx = -reach; dx <= reach; dx++) {
      const px = x + dx, py = y + dy;
      if (px < bodyLeft || px > bodyRight || py < bodyTop || py > sliceBottom) continue;
      protectedMask[(py - bodyTop) * bodyW + (px - bodyLeft)] = 1;
    }
  }
  const flat = Buffer.from(data);
  let flattened = 0;
  for (let y = bodyTop; y <= sliceBottom; y++) for (let x = bodyLeft; x <= bodyRight; x++) {
    const p = (y * w + x) * 4;
    if (data[p + 3] < 250) continue;
    if (protectedMask[(y - bodyTop) * bodyW + (x - bodyLeft)]) continue;   // 描边与装饰附近不动
    flat[p] = fillForFlat[0]; flat[p + 1] = fillForFlat[1]; flat[p + 2] = fillForFlat[2];
    flattened++;
  }
  console.log('fill flattened: ' + flattened + ' px -> #' + fillForFlat.map((v) => v.toString(16).padStart(2, '0')).join(''));
  await sharp(flat, { raw: { width: w, height: h, channels: 4 } }).png().toFile(path.join(OUT, 'bubble-body.png'));

  // ---- 7. 贴图：尾巴与装饰都从原始图里抠 ----
  const tailCrop = { left: Math.max(0, tailL - 10), top: bottomLine, width: Math.min(w - (tailL - 10), (tailR - tailL) + 21), height: Math.min(h - bottomLine, (tailTip - bottomLine) + 9) };
  await sharp(source).extract(tailCrop).png().toFile(path.join(OUT, 'bubble-tail.png'));
  if (over) await sharp(source).extract({ left: over.left, top: over.top, width: over.right - over.left, height: over.bottom - over.top }).png().toFile(path.join(OUT, 'bubble-whale.png'));

  // ---- 8. 推导缩放、圆角与内边距 ----
  const middleY = Math.round((bodyTop + bottomLine) / 2);
  const fillAtMiddle = rowFill(middleY) || [210, 220, 235];
  const edgeX = rowLeft(middleY);
  const borderSample = (() => { const p = (middleY * w + (edgeX + 3)) * 4; return [data[p], data[p + 1], data[p + 2]]; })();
  let band = 24;
  for (let x = edgeX; x <= edgeX + 120; x++) {
    const p = (middleY * w + x) * 4;
    if (data[p + 3] < 200) continue;
    const near = Math.max(Math.abs(data[p] - fillAtMiddle[0]), Math.abs(data[p + 1] - fillAtMiddle[1]), Math.abs(data[p + 2] - fillAtMiddle[2])) < 14;
    if (near) { band = x - edgeX; break; }
  }
  const scale = scaleArg > 0 ? scaleArg : Math.round((3 / Math.max(8, band)) * 1000) / 1000;
  // 圆角半径：手绘轮廓有抖动，用"连续 24 行/列都贴住边缘"来判断弧线结束，
  // 取早了会让四角切片小于素材圆角，角就被裁成直角（远离角色那一端会像被切一刀）。
  const sustained = 24;
  let radiusV = 40, radiusH = 40;
  for (let y = bodyTop; y <= bodyTop + 600; y++) {
    let ok = true;
    for (let k = 0; k < sustained && ok; k++) if (rowLeft(y + k) > bodyLeft + 3) ok = false;
    if (ok) { radiusV = y - bodyTop; break; }
  }
  for (let x = bodyLeft; x <= bodyLeft + 600; x++) {
    let ok = true;
    for (let k = 0; k < sustained && ok; k++) if (columnTop(x + k) > bodyTop + 3) ok = false;
    if (ok) { radiusH = x - bodyLeft; break; }
  }
  const radius = Math.max(radiusV, radiusH);
  const cornerPx = Math.round(Math.min(Math.max(radius * 1.15, 150), (bottomLine - bodyTop) * 0.45));
  // 四边拉伸条与内边距只跟"描边 + 白圈"的厚度有关，跟圆角大小无关；
  // 混用会让四角一变大就把气泡整体撑高。
  const edgePx = Math.round(Math.min(Math.max(radius * 0.55, 90), 170));
  const corner = cornerArg.length === 4 && cornerArg.every((v) => v > 0)
    ? { left: cornerArg[0], right: cornerArg[1], top: cornerArg[2], bottom: cornerArg[3] }
    : { left: cornerPx, right: cornerPx, top: cornerPx, bottom: cornerPx };
  // 四个角各自独立尺寸：可见圆角都来自素材本身，所以角落大小不同不会让泡泡一边宽一边窄；
  // 只有"角落里有装饰"的那个角需要放大，把装饰完整放进不缩放的切片里。
  const margin = 18;
  const inkBox = (x0, y0, x1, y1) => {
    let minX = 1e9, minY = 1e9, maxX = -1, maxY = -1;
    for (let y = Math.max(bodyTop, y0); y <= Math.min(sliceBottom, y1); y++) {
      for (let x = Math.max(bodyLeft, x0); x <= Math.min(bodyRight, x1); x++) {
        const p = (y * w + x) * 4;
        if (data[p + 3] < 200) continue;
        const diff = Math.max(Math.abs(data[p] - fillForFlat[0]), Math.abs(data[p + 1] - fillForFlat[1]), Math.abs(data[p + 2] - fillForFlat[2]));
        if (diff <= 45) continue;                 // 只认明显的墨色（纸纹暗点是十几）
        if (x < minX) minX = x; if (x > maxX) maxX = x;
        if (y < minY) minY = y; if (y > maxY) maxY = y;
      }
    }
    return maxX < 0 ? null : { minX, minY, maxX, maxY };
  };
  const quadNeed = (box, anchorX, anchorY, dirX, dirY) => box == null ? 0 : {
    w: dirX > 0 ? box.maxX - anchorX + margin : anchorX - box.minX + margin,
    h: dirY > 0 ? box.maxY - anchorY + margin : anchorY - box.minY + margin
  };
  const tlBox = inkBox(bodyLeft, bodyTop, bodyLeft + span * 0.22, bodyTop + (bottomLine - bodyTop) * 0.28);
  const trBox = inkBox(bodyRight - span * 0.22, bodyTop, bodyRight, bodyTop + (bottomLine - bodyTop) * 0.32);
  const base = { w: cornerPx, h: cornerPx };
  const keepLeft = over ? Math.min(over.left, tailL - 12) : tailL - 12;
  const keepTop = over ? Math.min(over.top, bottomLine) : bottomLine;
  // 关键：九宫格的拉伸只能落在"素材上真正平直"的那一段。手绘气泡的两侧通常是长弧线，
  // 如果把边界切在弧线中间，那一小段弧会被拉伸变形，看起来就像被切了一刀。
  // 这里对四条边分别求"轮廓位置在 ±3 像素内不变"的最长连续区间，把它当作可拉伸段。
  const outlineInk = (x, y) => { const p = (y * w + x) * 4; return data[p + 3] > 120 && data[p + 2] - data[p] > 15; };
  const leftAt = (y) => { for (let x = bodyLeft; x < bodyLeft + 500; x++) if (outlineInk(x, y)) return x; return -1; };
  const rightAt = (y) => { for (let x = bodyRight; x > bodyRight - 500; x--) if (outlineInk(x, y)) return x; return -1; };
  const topAt = (x) => { for (let y = bodyTop; y < bodyTop + 500; y++) if (outlineInk(x, y)) return y; return -1; };
  const bottomAt = (x) => { for (let y = sliceBottom; y > sliceBottom - 500; y--) if (outlineInk(x, y)) return y; return -1; };
  const longestFlat = (profile, from, to) => {
    let best = null, run = null;
    for (let t = from; t <= to; t++) {
      const v = profile(t);
      if (v < 0) { run = null; continue; }
      if (run && Math.abs(v - run.avg) <= 3) { run.to = t; run.avg = (run.avg * (run.to - run.from) + v) / (run.to - run.from + 1); }
      else run = { from: t, to: t, avg: v };
      if (!best || run.to - run.from > best.to - best.from) best = { from: run.from, to: run.to };
    }
    return best;
  };
  const leftFlat = longestFlat(leftAt, bodyTop + 40, bottomLine - 40);
  const rightFlat = longestFlat(rightAt, bodyTop + 40, bottomLine - 40);
  const topFlat = longestFlat(topAt, bodyLeft + 40, bodyRight - 40);
  const bottomFlat = longestFlat(bottomAt, bodyLeft + 40, bodyRight - 40);
  console.log('flat spans: left ' + JSON.stringify(leftFlat) + ' right ' + JSON.stringify(rightFlat) + ' top ' + JSON.stringify(topFlat) + ' bottom ' + JSON.stringify(bottomFlat));
  const flatMargin = 8;
  const sideNeedH = leftFlat ? { top: leftFlat.from - bodyTop + flatMargin, bottom: sliceBottom - leftFlat.to + flatMargin } : null;
  const sideNeedHR = rightFlat ? { top: rightFlat.from - bodyTop + flatMargin, bottom: sliceBottom - rightFlat.to + flatMargin } : null;
  const edgeNeedW = topFlat ? { left: topFlat.from - bodyLeft + flatMargin, right: bodyRight - topFlat.to + flatMargin } : null;
  const edgeNeedWB = bottomFlat ? { left: bottomFlat.from - bodyLeft + flatMargin, right: bodyRight - bottomFlat.to + flatMargin } : null;
  const tlNeed = quadNeed(tlBox, bodyLeft, bodyTop, 1, 1);
  const trNeed = quadNeed(trBox, bodyRight, bodyTop, -1, 1);
  const brNeed = { w: bodyRight - keepLeft + 30, h: sliceBottom - keepTop + 20 };
  const bodyHeightPx = sliceBottom - bodyTop;
  const corners = {
    tl: { w: Math.min(Math.round(span * 0.45), Math.max(base.w, tlNeed.w || 0, edgeNeedW ? edgeNeedW.left : 0)),
          h: Math.min(Math.round(bodyHeightPx * 0.45), Math.max(base.h, tlNeed.h || 0, sideNeedH ? sideNeedH.top : 0)) },
    tr: { w: Math.min(Math.round(span * 0.45), Math.max(base.w, trNeed.w || 0, edgeNeedW ? edgeNeedW.right : 0)),
          h: Math.min(Math.round(bodyHeightPx * 0.45), Math.max(base.h, trNeed.h || 0, sideNeedHR ? sideNeedHR.top : 0)) },
    bl: { w: Math.min(Math.round(span * 0.45), Math.max(base.w, edgeNeedWB ? edgeNeedWB.left : 0)),
          h: Math.min(Math.round(bodyHeightPx * 0.45), Math.max(base.h, sideNeedH ? sideNeedH.bottom : 0)) },
    br: { w: Math.min(Math.round(span * 0.62), Math.max(base.w, brNeed.w, edgeNeedWB ? edgeNeedWB.right : 0)),
          h: Math.min(Math.round(bodyHeightPx * 0.7), Math.max(base.h, brNeed.h, sideNeedHR ? sideNeedHR.bottom : 0)) }
  };
  console.log('decoration boxes: TL ' + JSON.stringify(tlBox) + ' TR ' + JSON.stringify(trBox) + ' -> corners ' + JSON.stringify(corners));
  const shellW = over ? over.right - over.left : 0, shellH = over ? over.bottom - over.top : 0;
  const meta = {
    source: path.basename(source),
    image: { width: w, height: h },
    scale: scale,
    body: { left: bodyLeft, top: bodyTop, right: bodyRight, bottom: sliceBottom },
    corner: corners,
    // 四边拉伸条的厚度（取素材自身圆角大小），四个方向一致。
    edge: { w: edgePx, h: edgePx },
    tail: {
      w: tailCrop.width, h: tailCrop.height,
      tipX: (tailL + tailR) / 2 - tailCrop.left, tipY: tailTip - tailCrop.top,
      side: ((tailL + tailR) / 2) > (bodyLeft + bodyRight) / 2 ? 'right' : 'left'
    },
    shell: over ? { w: shellW, h: shellH, leftOfBodyRight: bodyRight - over.left, topAboveBottom: bottomLine - over.top } : null,
    padding: {
      // 尽量贴合文字：右下角切片虽然很宽（要包住鲸鱼），但文字不必退那么多，按上限计算。
      left: Math.round(edgePx * scale * 1.2 + 6),
      right: Math.round(Math.min(corners.br.w, 260) * scale * 0.9 + 8),
      top: Math.round(edgePx * scale * 0.9 + 4),
      bottom: Math.round(edgePx * scale * 0.7 + 5)
    },
    text: { maxWidth: 280, ink: '#' + borderSample.map((v) => Math.round(Math.max(0, Math.min(255, v * 0.5))).toString(16).padStart(2, '0')).join('') }
  };
  fs.writeFileSync(path.join(OUT, 'bubble-meta.json'), JSON.stringify(meta, null, 2));
  console.log(JSON.stringify(meta, null, 1));
})().catch((e) => { console.error(e); process.exitCode = 1; });
