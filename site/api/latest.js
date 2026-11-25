// /releases/latest.json: the newest release, in the shape of GitHub's API, which installed copies of Nyatch read.

import { githubShape, installerOf, releases } from '../lib/releases.js';

export async function GET() {
  let rows;
  try {
    rows = await releases();
  } catch (error) {
    console.error(error);
    // An error, not an empty answer: a copy of Nyatch then simply tries again later.
    return new Response('The releases could not be read.', { status: 503, headers: { 'Cache-Control': 'no-store' } });
  }
  const latest = rows.find((r) => installerOf(r));
  if (!latest) return new Response('No release yet.', { status: 404, headers: { 'Cache-Control': 'public, s-maxage=60' } });
  return Response.json(githubShape(latest), { headers: { 'Cache-Control': 'public, max-age=0, s-maxage=60, stale-while-revalidate=600' } });
}
