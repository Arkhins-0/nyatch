// Nyatch's releases, kept outside the site so a new one is served without rebuilding it:
//   Neon (DATABASE_URL)   one row per release: its tag, name, notes, date and files
//   Cloudflare R2         the files themselves, at <R2_RELEASES_PREFIX><tag>/<file>, in the private bucket
//
// tools/publish-release.mjs writes both; the functions in api/ read them.

import { neon } from '@neondatabase/serverless';
import { AwsClient } from 'aws4fetch';

export const SITE = 'https://nyatch.arkhins.com';

const need = (name) => {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is not set.`);
  return value;
};

export const sql = () => neon(need('DATABASE_URL'));

export async function makeTable(db = sql()) {
  await db`create table if not exists releases (
    tag          text primary key,
    name         text not null,
    notes        text not null default '',
    published_at timestamptz not null default now(),
    assets       jsonb not null,
    listed       boolean not null default true
  )`;
}

/** The listed releases, newest first. */
export async function releases(limit = 30) {
  return sql()`select tag, name, notes, published_at, assets from releases where listed order by published_at desc limit ${limit}`;
}

/** One listed release, or null. */
export async function release(tag) {
  const rows = await sql()`select tag, name, notes, published_at, assets from releases where listed and tag = ${tag}`;
  return rows[0] ?? null;
}

// ---------- R2 ----------

const prefix = () => process.env.R2_RELEASES_PREFIX || 'releases/';
const r2 = () => new AwsClient({ accessKeyId: need('R2_ACCESS_KEY_ID'), secretAccessKey: need('R2_SECRET_ACCESS_KEY'), service: 's3', region: 'auto' });
const objectUrl = (tag, file) =>
  new URL(`https://${need('R2_ACCOUNT_ID')}.r2.cloudflarestorage.com/${encodeURIComponent(need('R2_BUCKET'))}/${prefix()}${encodeURIComponent(tag)}/${encodeURIComponent(file)}`);

/** A link to a release file that works for ten minutes (the bucket itself stays private). */
export async function fileLink(tag, file) {
  const url = objectUrl(tag, file);
  url.searchParams.set('X-Amz-Expires', '600');
  url.searchParams.set('response-content-disposition', `attachment; filename="${file}"`);
  const signed = await r2().sign(new Request(url, { method: 'GET' }), { aws: { signQuery: true } });
  return signed.url;
}

/** One request to R2, tried up to four times (a slow connection can drop one). */
async function send(client, url, init, what) {
  for (let attempt = 1; ; attempt++) {
    try {
      const response = await client.fetch(url, init);
      if (response.ok) return response;
      const text = await response.text();
      if (response.status < 500 || attempt >= 4) throw new Error(`R2 answered ${response.status} for ${what}: ${text}`);
    } catch (error) {
      if (attempt >= 4 || /R2 answered 4/.test(error.message)) throw error;
    }
    await new Promise((resolve) => setTimeout(resolve, attempt * 2000));
  }
}

const PART = 8 * 1024 * 1024;

/** Puts one release file in the bucket: in 8 MB parts when it is big, each part retried on its own. */
export async function upload(tag, file, bytes, type, progress = () => {}) {
  const client = r2();
  const url = objectUrl(tag, file);
  if (bytes.length <= PART) {
    await send(client, url, { method: 'PUT', body: bytes, headers: { 'Content-Type': type } }, file);
    return;
  }
  const start = new URL(url);
  start.searchParams.set('uploads', '');
  const created = await (await send(client, start, { method: 'POST', headers: { 'Content-Type': type } }, file)).text();
  const uploadId = /<UploadId>([^<]+)<\/UploadId>/.exec(created)?.[1];
  if (!uploadId) throw new Error(`R2 gave no upload id for ${file}.`);
  const parts = [];
  try {
    for (let offset = 0, number = 1; offset < bytes.length; offset += PART, number++) {
      const part = new URL(url);
      part.searchParams.set('partNumber', String(number));
      part.searchParams.set('uploadId', uploadId);
      const response = await send(client, part, { method: 'PUT', body: bytes.subarray(offset, offset + PART) }, `${file} part ${number}`);
      parts.push(`<Part><PartNumber>${number}</PartNumber><ETag>${response.headers.get('etag')}</ETag></Part>`);
      progress(Math.min(offset + PART, bytes.length), bytes.length);
    }
    const done = new URL(url);
    done.searchParams.set('uploadId', uploadId);
    await send(client, done, { method: 'POST', body: `<CompleteMultipartUpload>${parts.join('')}</CompleteMultipartUpload>` }, file);
  } catch (error) {
    const cancel = new URL(url);
    cancel.searchParams.set('uploadId', uploadId);
    await client.fetch(cancel, { method: 'DELETE' }).catch(() => {});
    throw error;
  }
}

// ---------- What the site shows ----------

export const isTag = (text) => /^v\d+\.\d+\.\d+$/.test(String(text ?? ''));
export const isFile = (text) => /^[A-Za-z0-9._-]{1,120}$/.test(String(text ?? '')) && !String(text).startsWith('.');

/** The download button's file: the MSI, or the setup program. */
export const installerOf = (row) =>
  row.assets.find((a) => /\.msi$/i.test(a.name)) ?? row.assets.find((a) => /\.exe$/i.test(a.name)) ?? null;

/** A release in the shape of GitHub's API, which installed copies of Nyatch read. */
export const githubShape = (row) => ({
  tag_name: row.tag,
  name: row.name,
  body: row.notes,
  html_url: `${SITE}/releases`,
  published_at: new Date(row.published_at).toISOString(),
  draft: false,
  prerelease: false,
  assets: row.assets.map((a) => ({ name: a.name, size: a.size, browser_download_url: `${SITE}/releases/${row.tag}/${a.name}` })),
});

const escape = (text) => String(text ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

/** A release's notes (Markdown) as plain HTML: paragraphs, lists, headings, code, links. */
export function notesHtml(markdown) {
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

const megabytes = (bytes) => `${(bytes / 1024 / 1024).toFixed(1)} MB`;
const day = (date) => new Date(date).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });

/** The line under the download button. */
export const downloadMeta = (row) => {
  const installer = row && installerOf(row);
  return installer ? `Version ${row.tag.replace(/^v/, '')} · ${megabytes(installer.size)} · ${day(row.published_at)}` : '';
};

/** The releases page's list. */
export const releasesHtml = (rows) =>
  rows.length
    ? rows
        .map(
          (r) => `
      <article class="release" id="${escape(r.tag)}">
        <header><h2>${escape(r.name || r.tag)}</h2><time datetime="${escape(new Date(r.published_at).toISOString())}">${day(r.published_at)}</time></header>
        <div class="notes">${notesHtml(r.notes) || '<p>No notes for this one.</p>'}</div>
      </article>`,
        )
        .join('\n')
    : '<p class="quiet">No releases yet. The first one is on its way.</p>';
