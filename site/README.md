# nyatch.arkhins.com

Nyatch's home page, and everything installed copies of Nyatch download: its updates and its character
packs. Deployed by Vercel from this folder.

## Vercel project

| Setting | Value |
|---|---|
| Root Directory | `site` |
| Framework Preset | Other |
| Build and output | from `vercel.json` (`node build.mjs`, output `dist`, functions in `api/`) |
| Domain | `nyatch.arkhins.com` |
| Environment variables | `DATABASE_URL` and the `R2_*` names (see `.env.example`) |

## Where the releases live

Not in the build: a new release shows without rebuilding the site.

- **Neon** (`DATABASE_URL`): the table `releases`, one row per version (tag, name, notes, date, files).
- **Cloudflare R2** (`R2_*`): the files, at `releases/<tag>/<file>` in the private bucket. The site hands
  out links to them that work for ten minutes.

## What the site serves

| Path | Served by | For |
|---|---|---|
| `/` | `api/page.js` | The home page, with the newest version under the button |
| `/download` | `api/download.js` | The newest installer (the MSI) |
| `/releases` | `api/page.js` | What changed in each version |
| `/releases/latest.json` | `api/latest.js` | The newest release, read by Nyatch's updater (the same shape as GitHub's API) |
| `/releases/<tag>/<file>` | `api/file.js` | That release's installer, checksum and signature |
| `/characters/index.json` | static | The theme packs Nyatch lists, with `previews/` and `packs/` |

The pages and `latest.json` are kept by Vercel's cache for a minute.

## Publishing a version

1. Raise the version in `version.txt`.
2. `.uild.ps1 -Release -Notes "What changed"` builds the installer, signs it, publishes it as a GitHub
   release, then runs `tools/publish-release.mjs`: the files go to R2 and the release to Neon (keys from
   `site/.env.local`). The site serves it within a minute.

To put a release on the site by hand: `node tools/publish-release.mjs --tag v1.2.0 --notes "..." <files>`
(`--replace` to send one again). To take one off the site: `update releases set listed = false where tag = 'v1.2.0'`
in Neon's SQL editor.

Installed copies find the new version at their next check (every few hours).

## Publishing a character pack

`dotnet run tools\MakePack.cs -- <pack folder> site\characters` writes the pack's `.nyatch` file and cover
here and lists it in `index.json`. Commit and push; Vercel rebuilds. To update a pack, raise the
`version` in its `pack.json` and run the tool again: installed copies update themselves.

Only publish packs whose pictures you may share.

## Trying it locally

`node build.mjs`, then serve `dist/`, for example with `npx serve dist` (the pages in `api/` need
`vercel dev`, with the keys in `.env.local`).
