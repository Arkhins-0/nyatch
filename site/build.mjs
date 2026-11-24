// Builds nyatch.arkhins.com into dist/: the pages in public/ and the theme packs in characters/.
//
//   node build.mjs
//
// The releases are not part of the build: they live in Neon (the list) and Cloudflare R2 (the files),
// and the functions in api/ serve them, so a new release shows without rebuilding the site:
//   /  and  /releases          api/page.js     the pages, filled in with the newest release
//   /download                  api/download.js the newest installer
//   /releases/latest.json      api/latest.js   the newest release, read by installed copies of Nyatch
//   /releases/<tag>/<file>     api/file.js     a release's installer, checksum and signature
// tools/publish-release.mjs puts a release there (build.ps1 -Release runs it).

import { cp, readFile, rm, writeFile } from 'node:fs/promises';

const OUT = 'dist';

await rm(OUT, { recursive: true, force: true });
await cp('public', OUT, { recursive: true });
await cp('characters', `${OUT}/characters`, { recursive: true });

// These two are served by api/page.js (a copy here would be served in its place).
await rm(`${OUT}/index.html`);
await rm(`${OUT}/releases.html`);

const notFound = `${OUT}/404.html`;
await writeFile(notFound, (await readFile(notFound, 'utf8')).replaceAll('{{YEAR}}', String(Math.max(2026, new Date().getFullYear()))));

console.log('Built.');
