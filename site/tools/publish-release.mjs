// Publishes a release of Nyatch to the site: its files to Cloudflare R2, then its row in Neon. The site
// serves it from then on, with no rebuild. build.ps1 -Release runs this after the GitHub release.
//
//   node tools/publish-release.mjs --tag v1.2.0 [--name "Nyatch 1.2.0"] [--notes "..."] [--date 2026-11-24T10:00:00Z] [--replace] <files...>
//
// The keys come from the environment, or from site/.env.local (never committed): DATABASE_URL,
// R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, R2_BUCKET.

import { readFileSync, existsSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
for (const name of ['.env.local', '.env']) {
  const path = join(here, '..', name);
  if (!existsSync(path)) continue;
  for (const line of readFileSync(path, 'utf8').split(/\r?\n/)) {
    const match = /^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/.exec(line);
    if (match && !process.env[match[1]] && match[2]) process.env[match[1]] = match[2].replace(/^(['"])(.*)\1$/, '$2');
  }
}

const { isFile, isTag, makeTable, sql, upload } = await import('../lib/releases.js');

const args = process.argv.slice(2);
const options = {};
const files = [];
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--replace') options.replace = true;
  else if (args[i].startsWith('--')) options[args[i].slice(2)] = args[++i];
  else files.push(args[i]);
}
const fail = (message) => {
  console.error(message);
  process.exit(1);
};
if (!isTag(options.tag)) fail('Give the release with --tag v1.2.3.');
if (!files.length) fail('Give the release files.');
for (const file of files) {
  if (!existsSync(file)) fail(`${file} is not there.`);
  if (!isFile(basename(file))) fail(`${basename(file)} is not a plain file name.`);
}

const db = sql();
await makeTable(db);
const [existing] = await db`select tag from releases where tag = ${options.tag}`;
if (existing && !options.replace) fail(`The site already has ${options.tag}. Raise the version, or pass --replace.`);

const type = (name) =>
  /\.msi$/i.test(name) ? 'application/x-msi' : /\.exe$/i.test(name) ? 'application/vnd.microsoft.portable-executable' : 'text/plain; charset=utf-8';

// The files first: the release is listed only once all of them are there.
const assets = [];
for (const file of files) {
  const name = basename(file);
  const bytes = readFileSync(file);
  process.stdout.write(`${name} (${(bytes.length / 1024 / 1024).toFixed(1)} MB)… `);
  let shown = 0;
  await upload(options.tag, name, bytes, type(name), (sent, total) => {
    const tenth = Math.floor((sent / total) * 10);
    if (tenth > shown) process.stdout.write(`${(shown = tenth) * 10}% `);
  });
  assets.push({ name, size: bytes.length });
  console.log('uploaded');
}

const name = options.name || `Nyatch ${options.tag.slice(1)}`;
const notes = options.notes ?? '';
const date = options.date ? new Date(options.date) : new Date();
await db`insert into releases (tag, name, notes, published_at, assets, listed)
  values (${options.tag}, ${name}, ${notes}, ${date.toISOString()}, ${JSON.stringify(assets)}::jsonb, true)
  on conflict (tag) do update set name = excluded.name, notes = excluded.notes, published_at = excluded.published_at,
    assets = excluded.assets, listed = true`;
console.log(`${options.tag} is on nyatch.arkhins.com.`);
