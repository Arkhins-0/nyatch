// /characters/...: the themes Nyatch lists, read from the repository's nyatch-themes branch, which holds
// only the released themes (index.json, and <id>/<id>.nyatch and <id>/preview.png for each). A theme added
// there shows without rebuilding the site.
//
//   /characters/index.json          the list, passed on (kept a minute)
//   /characters/<id>/<file>         a theme's pack or cover, sent on to GitHub
//   /characters/packs/<id>.nyatch   the layout before the branch (lists kept by copies of Nyatch 1.0.0)
//   /characters/previews/<id>.png

const REPO = () => process.env.NYATCH_REPO || 'Arkhins-0/nyatch';
const BRANCH = () => process.env.NYATCH_THEMES_BRANCH || 'nyatch-themes';
const raw = (path) => `https://raw.githubusercontent.com/${REPO()}/${BRANCH()}/${path}`;

const plain = (text) => /^[a-z0-9][a-z0-9-]{0,39}$/.test(text ?? '');
const gone = () => new Response('Not found.', { status: 404, headers: { 'Cache-Control': 'public, s-maxage=60' } });

/** The path within the branch for a path asked of the site, or null. */
function within(folder, file) {
  if (!folder && file === 'index.json') return 'index.json';
  if (folder === 'packs' && /^[a-z0-9-]+\.nyatch$/.test(file ?? '')) return `${file.slice(0, -7)}/${file}`;
  if (folder === 'previews' && /^[a-z0-9-]+\.(png|gif)$/.test(file ?? '')) return `${file.replace(/\.\w+$/, '')}/preview${file.slice(file.lastIndexOf('.'))}`;
  if (plain(folder) && (file === `${folder}.nyatch` || /^preview\.(png|gif)$/.test(file ?? ''))) return `${folder}/${file}`;
  return null;
}

export async function GET(request) {
  const query = new URL(request.url).searchParams;
  const path = within(query.get('folder'), query.get('file'));
  if (!path) return gone();
  if (path !== 'index.json') {
    return new Response(null, { status: 302, headers: { Location: raw(path), 'Cache-Control': 'public, s-maxage=300' } });
  }
  try {
    const response = await fetch(raw(path), { headers: { 'User-Agent': 'nyatch-site' } });
    if (!response.ok) throw new Error(`GitHub answered ${response.status}.`);
    return new Response(await response.text(), {
      headers: { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'public, max-age=0, s-maxage=60, stale-while-revalidate=600' },
    });
  } catch (error) {
    console.error(error);
    // An error, not an empty list: Nyatch then shows the list it kept.
    return new Response('The theme list could not be read.', { status: 503, headers: { 'Cache-Control': 'no-store' } });
  }
}
