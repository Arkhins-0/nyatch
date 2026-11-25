// /download/latest: the newest installer (the MSI, or the setup program when a release has no MSI), from GitHub.

import { fileLink, installerOf, releases } from '../lib/releases.js';

export async function GET() {
  let rows;
  try {
    rows = await releases();
  } catch (error) {
    console.error(error);
    return new Response('The download could not be found just now. Try again in a minute.', { status: 503, headers: { 'Cache-Control': 'no-store' } });
  }
  const latest = rows.find((r) => installerOf(r));
  if (!latest) return Response.redirect('https://nyatch.arkhins.com/download', 302);
  return new Response(null, { status: 302, headers: { Location: fileLink(latest, installerOf(latest).name), 'Cache-Control': 'public, s-maxage=60' } });
}
