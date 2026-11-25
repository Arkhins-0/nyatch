// Runs the site on this PC as Vercel runs it: the pages in dist/ (run build.mjs first), the routes in
// vercel.json, and the functions in api/, with the keys from .env.local.
//
//   node build.mjs && node tools/dev.mjs        then open http://localhost:3000

import { createServer } from 'node:http';
import { existsSync, readFileSync, statSync } from 'node:fs';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const site = fileURLToPath(new URL('..', import.meta.url));
for (const name of ['.env.local', '.env']) {
  const path = join(site, name);
  if (!existsSync(path)) continue;
  for (const line of readFileSync(path, 'utf8').split(/\r?\n/)) {
    const match = /^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/.exec(line);
    if (match && !process.env[match[1]] && match[2]) process.env[match[1]] = match[2].replace(/^(['"])(.*)\1$/, '$2');
  }
}

const config = JSON.parse(readFileSync(join(site, 'vercel.json'), 'utf8'));
const dist = join(site, 'dist');
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.json': 'application/json', '.png': 'image/png', '.webp': 'image/webp', '.ico': 'image/x-icon', '.svg': 'image/svg+xml', '.nyatch': 'application/octet-stream' };

/** A vercel.json rewrite's source ("/releases/:tag/:file") as a pattern; its destination filled in. */
function rewrite(path) {
  for (const { source, destination } of config.rewrites ?? []) {
    const names = [];
    const pattern = new RegExp('^' + source.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/:(\w+)/g, (_, n) => (names.push(n), '([^/]+)')) + '$');
    const match = pattern.exec(path);
    if (match) return names.reduce((d, n, i) => d.replaceAll(`:${n}`, encodeURIComponent(decodeURIComponent(match[i + 1]))), destination);
  }
  return null;
}

function staticFile(path) {
  const clean = normalize(decodeURIComponent(path)).replace(/^([/\\])+/, '');
  for (const candidate of [clean, `${clean}.html`, join(clean, 'index.html')]) {
    const file = join(dist, candidate);
    if (file.startsWith(dist) && existsSync(file) && statSync(file).isFile()) return file;
  }
  return null;
}

createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost');
  try {
    let file = staticFile(url.pathname);
    if (!file) {
      const target = rewrite(url.pathname);
      if (target?.startsWith('/api/')) {
        const to = new URL(target, 'http://localhost');
        const module = await import(pathToFileURL(join(site, to.pathname + '.js')));
        const response = await module.GET(new Request(new URL(to.pathname + to.search, `http://${req.headers.host}`), { headers: req.headers }));
        res.writeHead(response.status, Object.fromEntries(response.headers));
        res.end(Buffer.from(await response.arrayBuffer()));
        console.log(response.status, url.pathname);
        return;
      }
      file = target ? staticFile(target) : null;
    }
    if (!file) {
      res.writeHead(404, { 'Content-Type': types['.html'] });
      res.end(existsSync(join(dist, '404.html')) ? readFileSync(join(dist, '404.html')) : 'Not found');
      console.log(404, url.pathname);
      return;
    }
    res.writeHead(200, { 'Content-Type': types[extname(file)] ?? 'application/octet-stream' });
    res.end(readFileSync(file));
  } catch (error) {
    console.error(error);
    res.writeHead(500);
    res.end(String(error));
  }
}).listen(3000, () => console.log('The site is on http://localhost:3000'));
