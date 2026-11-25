// The home page and the releases page, filled in with the newest release when they are asked for (and kept
// by Vercel's cache for a minute), so a new release shows without rebuilding the site.

import { readFileSync } from 'node:fs';
import { downloadMeta, installerOf, releases, releasesHtml } from '../lib/releases.js';

// Found next to this file (on Vercel the site sits in a folder of its own, not where the function starts).
const templates = {
  index: readFileSync(new URL('../public/index.html', import.meta.url), 'utf8'),
  releases: readFileSync(new URL('../public/releases.html', import.meta.url), 'utf8'),
};

export async function GET(request) {
  const name = new URL(request.url).searchParams.get('name') === 'releases' ? 'releases' : 'index';
  let rows = null;
  try {
    rows = await releases();
  } catch (error) {
    console.error(error);
  }
  const latest = rows?.find((r) => installerOf(r)) ?? null;
  // Without the list (Neon not answering), the button still points at /download.
  const ready = latest || rows === null;
  const html = templates[name]
    .replaceAll('{{DOWNLOAD_URL}}', ready ? '/download' : '#download')
    .replaceAll('{{DOWNLOAD_STATE}}', ready ? '' : 'aria-disabled="true" data-soon')
    .replaceAll('{{DOWNLOAD_LABEL}}', ready ? 'Download for Windows' : 'Coming soon')
    .replaceAll('{{DOWNLOAD_META}}', latest ? downloadMeta(latest) : ready ? '' : 'The first release is on its way.')
    .replaceAll('{{YEAR}}', String(Math.max(2026, new Date().getFullYear())))
    .replace('{{RELEASES}}', rows ? releasesHtml(rows) : '<p class="quiet">The list could not be loaded. Try again in a minute.</p>');
  return new Response(html, {
    headers: {
      'Content-Type': 'text/html; charset=utf-8',
      'Cache-Control': rows ? 'public, max-age=0, s-maxage=60, stale-while-revalidate=600' : 'no-store',
    },
  });
}
