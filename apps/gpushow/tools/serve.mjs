// Static dev server for the gpushow app. Serves the app root so the gallery
// and demos can fetch the generated .wgsl over localhost (a secure context,
// which WebGPU requires). Node built-ins only. Stays up until killed.
//
// Usage: node serve.mjs [port]

import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, normalize, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const port = Number(process.argv[2] || 8099);
const appRoot = normalize(join(fileURLToPath(import.meta.url), '..', '..')); // apps/gpushow

const MIME = { '.html':'text/html', '.wgsl':'text/plain', '.mjs':'text/javascript',
  '.js':'text/javascript', '.css':'text/css', '.json':'application/json',
  '.png':'image/png', '.md':'text/plain', '.codex':'text/plain' };

createServer(async (req, res) => {
  let rel = decodeURIComponent(req.url.split('?')[0]);
  if (rel === '/' || rel === '') rel = '/web/index.html';
  const path = normalize(join(appRoot, rel));
  if (!path.startsWith(appRoot)) { res.writeHead(403).end(); return; }
  try {
    const body = await readFile(path);
    res.writeHead(200, { 'content-type': MIME[extname(path)] || 'application/octet-stream' });
    res.end(body);
  } catch { res.writeHead(404).end('not found: ' + rel); }
}).listen(port, '127.0.0.1', () => {
  console.log(`gpushow serving ${appRoot}`);
  console.log(`  gallery:   http://127.0.0.1:${port}/`);
  console.log(`  fireworks: http://127.0.0.1:${port}/web/fireworks.html`);
  console.log(`  swarm:     http://127.0.0.1:${port}/web/swarm.html`);
});
