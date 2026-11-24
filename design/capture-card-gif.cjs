// Capture the card-spread notification loop as frames via Chrome DevTools Protocol.
// Requires Chrome started with --remote-debugging-port=9333.
// usage: node capture-card-gif.cjs 1 7 9
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');

const PORT = 9333;
const ROOT = __dirname;
const FRAMES = path.join(ROOT, 'shots', 'gif-frames');
const STYLES = process.argv.slice(2).map(Number).filter(Boolean);

async function connect() {
  const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
  const page = list.find(x => x.type === 'page');
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  let id = 0;
  const pending = new Map();
  ws.onmessage = e => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  };
  const send = (method, params = {}) => new Promise(res => {
    const i = ++id;
    pending.set(i, res);
    ws.send(JSON.stringify({ id: i, method, params }));
  });
  return { ws, send };
}

(async () => {
  if (!STYLES.length) { console.error('usage: node capture-card-gif.cjs 1 7 9'); process.exit(1); }
  const { ws, send } = await connect();
  await send('Page.enable');
  await send('Runtime.enable');
  await send('Emulation.setDeviceMetricsOverride', {
    width: 648, height: 548, deviceScaleFactor: 2, mobile: false
  });

  for (const n of STYLES) {
    const dir = path.join(FRAMES, 's' + n);
    fs.rmSync(dir, { recursive: true, force: true });
    fs.mkdirSync(dir, { recursive: true });
    const url = 'file:///' + (ROOT + '/' + (process.env.SPREAD_FILE || 'card-spread-notify-v3.html')).replace(/\\/g, '/') +
      `?s=${n}&snap=1&t=0.01`;
    await send('Page.navigate', { url });
    await new Promise(r => setTimeout(r, 1600));
    const frames = 70;
    for (let f = 0; f < frames; f++) {
      const t = Math.min(6.9, f * 0.1);
      await send('Runtime.evaluate', {
        expression: `document.getAnimations().forEach(function(a){a.pause();a.currentTime=${(t * 1000).toFixed(1)}});`
      });
      await new Promise(r => setTimeout(r, 32));
      const shot = await send('Page.captureScreenshot', { format: 'png', fromSurface: true });
      const buf = Buffer.from(shot.result.data, 'base64');
      const out = path.join(dir, 'f' + String(f).padStart(3, '0') + '.png');
      await sharp(buf)
        .extract({ left: 48, top: 48, width: 1200, height: 1000 })
        .resize(480, 400, { kernel: 'lanczos3' })
        .png({ compressionLevel: 9 })
        .toFile(out);
    }
    console.log('captured style', n, '->', dir);
  }
  ws.close();
})().catch(e => { console.error(e); process.exit(1); });
