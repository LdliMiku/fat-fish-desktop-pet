const sharp = require('C:/Users/Lenovo/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');

const path = require('path');
const root = path.resolve(__dirname, '..');
const order = [34, 35, 36, 37, 0, 38, 39, 40, 41];

(async () => {
  const layers = order.map((frame, index) => ({
    input: `${root}/.build/frame-${frame}.png`,
    left: (index % 3) * 340,
    top: Math.floor(index / 3) * 360,
  }));
  await sharp({
    create: {
      width: 1020,
      height: 1080,
      channels: 4,
      background: { r: 0, g: 0, b: 0, alpha: 0 },
    },
  }).composite(layers).png().toFile(`${root}/.build/bridge-open-grid.png`);
})();
