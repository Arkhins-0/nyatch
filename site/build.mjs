// Builds nyatch.arkhins.com into dist/: the pages in public/ and the theme packs in characters/.
//
//   node build.mjs
//
// The releases are not part of the build: they are the repository's GitHub releases, which the functions
// in api/ read when asked, so a new release shows without rebuilding the site:
//   /  and  /releases          api/page.js     the pages, filled in with the newest release
//   /download                  api/page.js     the download page: every file of the newest version, and older ones
//   /download/latest           api/download.js the newest installer, straight away
//   /releases/latest.json      api/latest.js   the newest release, read by installed copies of Nyatch
//   /releases/<tag>/<file>     api/file.js     a release's installer, checksum and signature
// build.ps1 -Release makes the GitHub release.

import { cp, readFile, rm, writeFile } from 'node:fs/promises';

const OUT = 'dist';

await rm(OUT, { recursive: true, force: true });
await cp('public', OUT, { recursive: true });
await cp('characters', `${OUT}/characters`, { recursive: true });

// These are served by api/page.js (a copy here would be served in its place).
await rm(`${OUT}/index.html`);
await rm(`${OUT}/releases.html`);
await rm(`${OUT}/download.html`);

const notFound = `${OUT}/404.html`;
await writeFile(notFound, (await readFile(notFound, 'utf8')).replaceAll('{{YEAR}}', String(Math.max(2026, new Date().getFullYear()))));

console.log('Built.');
