// Downscale a 2x screenshot to its intended CSS pixel size with a good filter.
// usage: node downscale.cjs <src> <dst> <width> <height>
const sharp = require('sharp');
const [src, dst, w, h] = process.argv.slice(2);
sharp(src)
  .resize(Number(w), Number(h), { kernel: 'lanczos3' })
  .png({ compressionLevel: 9 })
  .toFile(dst)
  .then(() => console.log('ok ' + dst))
  .catch((e) => { console.error(e); process.exit(1); });
