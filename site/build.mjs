// Builds nyatch.arkhins.com into dist/: the pages in public/, the character packs in characters/,
// and the newest release of Nyatch, fetched from the private repository's GitHub releases with a
// read-only token (GITHUB_TOKEN, set in Vercel; never part of the site).
//
//   node build.mjs
//
// What installed copies of Nyatch read:
//   /releases/latest.json      the newest release, in the shape GitHub's API gives (the app reads that shape)
//   /releases/<tag>/<file>     its installer, checksum and signature
//   /characters/index.json     the character packs, with /characters/previews/ and /characters/packs/
//
// Without a token (a preview build on a PC) the site is built without releases.

import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';

const SITE = 'https://nyatch.arkhins.com';
const REPO = process.env.NYATCH_REPO || 'Arkhins-0/nyatch';
const TOKEN = process.env.GITHUB_TOKEN || '';
const OUT = 'dist';

const escape = (text) => String(text ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

/** A release's notes (Markdown as GitHub keeps them) as plain HTML: paragraphs, lists, headings, code, links. */
function notesHtml(markdown) {
  const inline = (text) =>
    escape(text)
      .replace(/`([^`]+)`/g, '<code>$1</code>')
      .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
      .replace(/\[([^\]]+)\]\((https:\/\/[^)\s]+)\)/g, '<a href="$2">$1</a>');
  const out = [];
  let list = null;
  let para = [];
  const flush = () => {
    if (para.length) out.push(`<p>${inline(para.join(' '))}</p>`);
    para = [];
    if (list) out.push(`<ul>${list.map((item) => `<li>${inline(item)}</li>`).join('')}</ul>`);
    list = null;
  };
  for (const raw of String(markdown ?? '').replace(/\r\n/g, '\n').split('\n')) {
    const line = raw.trim();
    const bullet = /^[-*] (.*)$/.exec(line);
    const heading = /^#{1,6} (.*)$/.exec(line);
    if (!line) flush();
    else if (heading) {
      flush();
      out.push(`<h3>${inline(heading[1])}</h3>`);
    } else if (bullet) {
      if (para.length) flush();
      (list ??= []).push(bullet[1]);
    } else {
      if (list) flush();
      para.push(line);
    }
  }
  flush();
  return out.join('\n');
}

async function github(path, accept = 'application/vnd.github+json') {
  const response = await fetch(`https://api.github.com/repos/${REPO}${path}`, {
    headers: {
      Accept: accept,
      Authorization: `Bearer ${TOKEN}`,
      'User-Agent': 'nyatch-site-build',
      'X-GitHub-Api-Version': '2022-11-28',
    },
  });
  if (!response.ok) throw new Error(`GitHub answered ${response.status} for ${path}`);
  return response;
}

const megabytes = (bytes) => `${(bytes / 1024 / 1024).toFixed(1)} MB`;
const day = (iso) => new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });

// ---------- The pages and the packs ----------
await rm(OUT, { recursive: true, force: true });
await cp('public', OUT, { recursive: true });
await cp('characters', `${OUT}/characters`, { recursive: true });

// ---------- The releases ----------
let releases = [];
if (TOKEN) {
  // A failure here stops the build, so the site already online (with its release) stays up rather
  // than one telling every installed copy there is nothing to update to.
  releases = (await (await github('/releases?per_page=30')).json()).filter((r) => !r.draft && !r.prerelease);
} else {
  console.warn('No GITHUB_TOKEN: built without releases (a preview).');
}

let installer = null;
// The setup program (.exe); a release from before it came as an .msi.
const latest = releases.find((r) => r.assets.some((a) => /\.(exe|msi)$/i.test(a.name)));
if (latest) {
  const folder = `${OUT}/releases/${latest.tag_name}`;
  await mkdir(folder, { recursive: true });
  const assets = [];
  for (const asset of latest.assets.filter((a) => /\.(exe|msi)(\.sha256|\.sig)?$/i.test(a.name) && /^[A-Za-z0-9._-]+$/.test(a.name))) {
    const file = Buffer.from(await (await github(`/releases/assets/${asset.id}`, 'application/octet-stream')).arrayBuffer());
    await writeFile(`${folder}/${asset.name}`, file);
    assets.push({ name: asset.name, size: file.length, browser_download_url: `${SITE}/releases/${latest.tag_name}/${asset.name}` });
    console.log(`${asset.name}: ${megabytes(file.length)}`);
  }
  installer = assets.find((a) => /\.exe$/i.test(a.name)) ?? assets.find((a) => /\.msi$/i.test(a.name));
  await writeFile(
    `${OUT}/releases/latest.json`,
    JSON.stringify(
      {
        tag_name: latest.tag_name,
        name: latest.name || `Nyatch ${latest.tag_name.replace(/^v/, '')}`,
        body: latest.body || '',
        html_url: `${SITE}/releases`,
        published_at: latest.published_at,
        draft: false,
        prerelease: false,
        assets,
      },
      null,
      2,
    ),
  );
  // A link that always gives the newest installer.
  await mkdir(`${OUT}/download`, { recursive: true });
  await writeFile(
    `${OUT}/download/index.html`,
    `<!doctype html><meta charset="utf-8"><title>Downloading Nyatch</title><meta http-equiv="refresh" content="0; url=${installer.browser_download_url}"><p>Downloading <a href="${installer.browser_download_url}">${escape(installer.name)}</a>…</p>`,
  );
}

// ---------- Filling in the pages ----------
const version = latest ? latest.tag_name.replace(/^v/, '') : '';
const fill = (html) =>
  html
    .replaceAll('{{DOWNLOAD_URL}}', installer ? '/download' : '#download')
    .replaceAll('{{DOWNLOAD_STATE}}', installer ? '' : 'aria-disabled="true" data-soon')
    .replaceAll('{{DOWNLOAD_LABEL}}', installer ? 'Download for Windows' : 'Coming soon')
    .replaceAll('{{DOWNLOAD_META}}', installer ? `Version ${escape(version)} · ${megabytes(installer.size)} · ${day(latest.published_at)}` : 'The first release is on its way.')
    .replaceAll('{{YEAR}}', String(Math.max(2026, new Date().getFullYear())));

for (const page of ['index.html', '404.html']) {
  const path = `${OUT}/${page}`;
  if (existsSync(path)) await writeFile(path, fill(await readFile(path, 'utf8')));
}

const list = releases.length
  ? releases
      .map(
        (r) => `
      <article class="release" id="${escape(r.tag_name)}">
        <header><h2>${escape(r.name || r.tag_name)}</h2><time datetime="${escape(r.published_at)}">${day(r.published_at)}</time></header>
        <div class="notes">${notesHtml(r.body) || '<p>No notes for this one.</p>'}</div>
      </article>`,
      )
      .join('\n')
  : '<p class="quiet">No releases yet. The first one is on its way.</p>';
const template = await readFile(`${OUT}/releases.html`, 'utf8');
await writeFile(`${OUT}/releases.html`, fill(template.replace('{{RELEASES}}', list)));

console.log(latest ? `Built with ${latest.tag_name}.` : 'Built without a release.');
