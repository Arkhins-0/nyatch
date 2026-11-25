// Nyatch's releases, read from the repository's GitHub releases when the site is asked for them, so a new
// release shows without rebuilding the site (as CTR's site does). The files stay on GitHub: a download is
// sent on to GitHub's own link.
//
// The repository is public, so no token is needed; GITHUB_TOKEN (read-only), when set, raises GitHub's limit
// of 60 calls an hour per address to 5,000. Each copy of a function keeps the list for a minute, and Vercel's
// cache keeps the pages for a minute, so GitHub is asked rarely.

export const SITE = 'https://nyatch.arkhins.com';

const REPO = () => process.env.NYATCH_REPO || 'Arkhins-0/nyatch';
const KEEP_MS = 60 * 1000;
let kept = null;

/** The full releases with files, newest first, in the site's own shape: tag, name, notes, date, files. */
export async function releases() {
  if (kept && Date.now() - kept.at < KEEP_MS) return kept.rows;
  const token = process.env.GITHUB_TOKEN;
  const response = await fetch(`https://api.github.com/repos/${REPO()}/releases?per_page=30`, {
    headers: {
      Accept: 'application/vnd.github+json',
      'User-Agent': 'nyatch-site',
      'X-GitHub-Api-Version': '2022-11-28',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  });
  if (!response.ok) {
    // A failed call keeps what was read before, when there is something.
    if (kept) return kept.rows;
    throw new Error(`GitHub answered ${response.status}.`);
  }
  const rows = (await response.json())
    .filter((r) => !r.draft && !r.prerelease)
    .map((r) => ({
      tag: r.tag_name,
      name: r.name || `Nyatch ${String(r.tag_name).replace(/^v/, '')}`,
      notes: r.body || '',
      published_at: r.published_at,
      assets: r.assets
        .filter((a) => /^[A-Za-z0-9._-]+$/.test(a.name))
        .map((a) => ({
          name: a.name,
          size: a.size,
          url: a.browser_download_url,
          sha256: /^sha256:([0-9a-f]{64})$/.exec(a.digest ?? '')?.[1] ?? null,
        })),
    }));
  kept = { at: Date.now(), rows };
  return rows;
}

/** One release, or null. */
export async function release(tag) {
  return (await releases()).find((r) => r.tag === tag) ?? null;
}

/** Where a release's file is downloaded from: GitHub. */
export const fileLink = (row, file) => row.assets.find((a) => a.name === file)?.url ?? null;

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

// ---------- The download page ----------

const fileUrl = (tag, name) => `/releases/${encodeURIComponent(tag)}/${encodeURIComponent(name)}`;

/** One way to install a version: its file, what it is, and its checksum and signature. */
function optionHtml(row, asset, { title, line, recommended }) {
  const sum = row.assets.find((a) => a.name === `${asset.name}.sha256`);
  const sig = row.assets.find((a) => a.name === `${asset.name}.sig`);
  const kind = /\.msi$/i.test(asset.name) ? '.msi' : '.exe';
  const extras = [sum && `<a href="${fileUrl(row.tag, sum.name)}">Checksum file</a>`, sig && `<a href="${fileUrl(row.tag, sig.name)}">Signature</a>`].filter(Boolean);
  return `
      <article class="option${recommended ? ' first' : ''}">
        <div class="option-head">
          <h2>${escape(title)}</h2>
          ${recommended ? '<span class="tag">Recommended</span>' : ''}
        </div>
        <p class="file">${escape(asset.name)} · ${megabytes(asset.size)}</p>
        <p class="what">${escape(line)}</p>
        <a class="pill ${recommended ? 'dark' : 'outline'}" href="${fileUrl(row.tag, asset.name)}">Download ${kind}</a>
        ${asset.sha256 ? `<p class="hash"><span>SHA-256</span><code>${escape(asset.sha256)}</code></p>` : ''}
        ${extras.length ? `<p class="extras">${extras.join(' · ')}</p>` : ''}
      </article>`;
}

/** The download page's content: the newest version's files, how to check them, and the older versions. */
export function downloadsHtml(rows) {
  const withInstaller = rows.filter((r) => installerOf(r));
  const latest = withInstaller[0];
  if (!latest) return '<p class="quiet downloads-empty">No version to download yet. The first one is on its way.</p>';
  const msi = latest.assets.find((a) => /\.msi$/i.test(a.name));
  const exe = latest.assets.find((a) => /\.exe$/i.test(a.name));
  const options = [
    msi && optionHtml(latest, msi, { title: 'Windows Installer', line: 'The standard installer. Open it and follow the steps.', recommended: true }),
    exe && optionHtml(latest, exe, { title: 'Setup program', line: 'The same installer inside a setup program. Use it if .msi files do not open on your PC.', recommended: !msi }),
  ].filter(Boolean);
  const first = msi ?? exe;
  const version = latest.tag.replace(/^v/, '');
  const older = withInstaller.slice(1).map((r) => {
    const files = r.assets.filter((a) => /\.(msi|exe)$/i.test(a.name));
    return `
        <li>
          <span class="version">${escape(r.name || r.tag)}</span>
          <time datetime="${escape(new Date(r.published_at).toISOString())}">${day(r.published_at)}</time>
          <span class="links">${files.map((a) => `<a href="${fileUrl(r.tag, a.name)}">${escape(a.name.replace(/^.*\./, '.'))} · ${megabytes(a.size)}</a>`).join('')}</span>
        </li>`;
  });
  return `
    <p class="version-line">Version ${escape(version)} · ${day(latest.published_at)} · <a href="/releases#${escape(latest.tag)}">What changed</a></p>
    <div class="options">${options.join('')}
    </div>

    <section class="before">
      <h2>Before you open it</h2>
      <dl>
        <div><dt>Windows may warn you</dt><dd>Nyatch is not signed with a paid certificate yet, so SmartScreen can say it protected your PC. Click <strong>More info</strong>, then <strong>Run anyway</strong>.</dd></div>
        <div><dt>Check the file</dt><dd>In PowerShell, in the folder you saved it to: <code>Get-FileHash .\\${escape(first.name)}</code>. The SHA-256 should be the one shown above.</dd></div>
        <div><dt>Updates come by themselves</dt><dd>Once installed, Nyatch checks for new versions and updates itself. You will not need this page again.</dd></div>
      </dl>
    </section>

    <section class="older">
      <h2>Older versions</h2>
      ${older.length ? `<ul>${older.join('')}\n      </ul>` : `<p class="quiet">None yet: ${escape(version)} is the first version.</p>`}
    </section>`;
}
