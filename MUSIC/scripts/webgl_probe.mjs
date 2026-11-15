// Drive a running headless Edge/Chrome (started with --remote-debugging-port) over CDP, no dependencies.
// Observes Web Audio technically (what starts, when, how long, at what gain); it does not listen.
//
// usage: node webgl_probe.mjs <port> <command> [args...]
//   fresh <origin|keep>         disable/clear HTTP cache and origin storage ('keep' leaves PlayerPrefs storage intact)
//   init <url>                  install the audio/console hook for new documents, then navigate
//   shot <file.png>             save a screenshot
//   click <x> <y>               left click in CSS pixels
//   rclick <x> <y>              right click
//   key <key> [code] [keyCode]  press and release a key (e.g. key Escape Escape 27, key b KeyB 66)
//   wait <ms>
//   eval <js>                   print the JSON result of an expression
//   log [sinceIndex]            print hooked audio and console events since an index
//   seq <json>                  run [["click",x,y],["wait",500],["shot","a.png"],...] in one connection
const [port, cmd, ...args] = process.argv.slice(2);

const HOOK = `(() => {
  if (window.__probe) return;
  const log = window.__probe = [];
  const t0 = performance.now();
  const push = (e) => { e.t = +((performance.now() - t0) / 1000).toFixed(2); log.push(e); };
  const AC = window.AudioContext || window.webkitAudioContext;
  if (AC) {
    const origStart = AudioBufferSourceNode.prototype.start;
    AudioBufferSourceNode.prototype.start = function (when, offset, duration) {
      push({ k: 'start', dur: this.buffer ? +this.buffer.duration.toFixed(3) : null, loop: this.loop, offset: offset || 0 });
      this.addEventListener('ended', () => push({ k: 'end', dur: this.buffer ? +this.buffer.duration.toFixed(3) : null }));
      return origStart.apply(this, arguments);
    };
    const origResume = AC.prototype.resume;
    AC.prototype.resume = function () { push({ k: 'resume', state: this.state }); return origResume.apply(this, arguments); };
    const Orig = AC;
    window.__contexts = [];
    const Wrapped = function (...a) { const c = new Orig(...a); window.__contexts.push(c); c.addEventListener('statechange', () => push({ k: 'ctx', state: c.state })); push({ k: 'ctx-new', state: c.state }); return c; };
    Wrapped.prototype = Orig.prototype;
    window.AudioContext = Wrapped; if (window.webkitAudioContext) window.webkitAudioContext = Wrapped;
  }
  // Unity WebGL plays compressed-in-memory clips (music, ambience) through off-DOM <audio> elements.
  const mediaPlay = HTMLMediaElement.prototype.play, mediaPause = HTMLMediaElement.prototype.pause;
  HTMLMediaElement.prototype.play = function () {
    const el = this;
    if (!el.__probed) {
      el.__probed = true;
      el.addEventListener('ended', () => push({ k: 'media-end', dur: +(el.duration || 0).toFixed(2) }));
    }
    const p = mediaPlay.apply(this, arguments);
    const report = () => push({ k: 'media-play', dur: +(el.duration || 0).toFixed(2), at: +el.currentTime.toFixed(2), loop: el.loop });
    if (el.readyState >= 1) report(); else el.addEventListener('loadedmetadata', report, { once: true });
    if (p && p.catch) p.catch((e) => push({ k: 'media-play-rejected', msg: String(e).slice(0, 120) }));
    return p;
  };
  HTMLMediaElement.prototype.pause = function () {
    push({ k: 'media-pause', dur: +(this.duration || 0).toFixed(2), at: +this.currentTime.toFixed(2) });
    return mediaPause.apply(this, arguments);
  };
  for (const level of ['error', 'warn']) {
    const orig = console[level];
    console[level] = function (...a) { push({ k: level, msg: a.map(String).join(' ').slice(0, 300) }); return orig.apply(this, a); };
  }
  window.addEventListener('error', (e) => push({ k: 'pageerror', msg: String(e.message).slice(0, 300) }));
})();`;

async function connect() {
  const list = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
  const page = list.find((t) => t.type === 'page');
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
  let id = 0;
  const pending = new Map();
  ws.onmessage = (m) => {
    const d = JSON.parse(m.data);
    if (d.id && pending.has(d.id)) { pending.get(d.id)(d); pending.delete(d.id); }
  };
  const send = (method, params = {}) => new Promise((r) => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  return { ws, send };
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function run(c, step) {
  const [op, ...a] = step;
  const { send } = c;
  switch (op) {
    case 'init':
      await send('Page.enable');
      await send('Page.addScriptToEvaluateOnNewDocument', { source: HOOK });
      await send('Page.navigate', { url: a[0] });
      return 'navigated';
    case 'fresh': // no HTTP cache and no Unity IndexedDB data cache, so a rebuilt player is really loaded
      await send('Network.enable');
      await send('Network.setCacheDisabled', { cacheDisabled: true });
      await send('Network.clearBrowserCache');
      if (a[0] !== 'keep') await send('Storage.clearDataForOrigin', { origin: a[0], storageTypes: 'all' });
      return a[0] === 'keep' ? 'http cache disabled, storage kept' : 'cache cleared';
    case 'shot': {
      const r = await send('Page.captureScreenshot', { format: 'png' });
      (await import('node:fs')).writeFileSync(a[0], Buffer.from(r.result.data, 'base64'));
      return a[0];
    }
    case 'click': case 'rclick': {
      const button = op === 'click' ? 'left' : 'right';
      const x = +a[0], y = +a[1];
      await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y });
      await sleep(60);
      await send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button, clickCount: 1 });
      await sleep(90);
      await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button, clickCount: 1 });
      return `${op} ${x},${y}`;
    }
    case 'move':
      await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: +a[0], y: +a[1] });
      return 'moved';
    case 'key': {
      const key = a[0], code = a[1] || key, kc = +(a[2] || 0);
      await send('Input.dispatchKeyEvent', { type: 'keyDown', key, code, windowsVirtualKeyCode: kc });
      await sleep(80);
      await send('Input.dispatchKeyEvent', { type: 'keyUp', key, code, windowsVirtualKeyCode: kc });
      return `key ${key}`;
    }
    case 'wait': await sleep(+a[0]); return `waited ${a[0]}`;
    case 'eval': {
      const r = await send('Runtime.evaluate', { expression: a[0], returnByValue: true, awaitPromise: true });
      return JSON.stringify(r.result.result ? r.result.result.value : r.result);
    }
    case 'log': {
      const r = await send('Runtime.evaluate', { expression: `JSON.stringify((window.__probe||[]).slice(${+(a[0] || 0)}))`, returnByValue: true });
      return r.result.result.value;
    }
    default: throw new Error('unknown op ' + op);
  }
}

const c = await connect();
const steps = cmd === 'seq' ? JSON.parse(args[0]) : [[cmd, ...args]];
for (const s of steps) console.log(await run(c, s));
c.ws.close();
