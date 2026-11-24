// /releases/<tag>/<file>: one file of a release (installer, checksum, signature), handed out from R2
// through a link that works for ten minutes.

import { fileLink, isFile, isTag, release } from '../lib/releases.js';

const gone = () => new Response('Not found.', { status: 404, headers: { 'Cache-Control': 'public, s-maxage=60' } });

export async function GET(request) {
  const query = new URL(request.url).searchParams;
  const tag = query.get('tag');
  const file = query.get('file');
  if (!isTag(tag) || !isFile(file)) return gone();
  let row;
  try {
    row = await release(tag);
  } catch (error) {
    console.error(error);
    return new Response('The releases could not be read.', { status: 503, headers: { 'Cache-Control': 'no-store' } });
  }
  if (!row || !row.assets.some((a) => a.name === file)) return gone();
  return new Response(null, { status: 302, headers: { Location: await fileLink(tag, file), 'Cache-Control': 'no-store' } });
}
