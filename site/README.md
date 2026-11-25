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
| Environment variables | `GITHUB_TOKEN` (optional, read-only: raises GitHub's limit), `NYATCH_REPO` |

## Where the releases live

On GitHub only, as the repository's releases. The site's functions read them when they are asked (each copy
keeps the list for a minute) and send downloads on to GitHub's own links. Nothing about a release is built into
the site, so a new release shows without rebuilding it.

## What the site serves

| Path | Served by | For |
|---|---|---|
| `/` | `api/page.js` | The home page, with the newest version under the button |
| `/download` | `api/page.js` | The download page: the MSI and the setup program, their checksums, and older versions |
| `/download/latest` | `api/download.js` | The newest installer (the MSI), straight away |
| `/releases` | `api/page.js` | What changed in each version |
| `/releases/latest.json` | `api/latest.js` | The newest release, read by Nyatch's updater (the same shape as GitHub's API) |
| `/releases/<tag>/<file>` | `api/file.js` | That release's installer, checksum and signature (sent on to GitHub) |
| `/characters/index.json` | static | The theme packs Nyatch lists, with `previews/` and `packs/` |

The pages and `latest.json` are kept by Vercel's cache for a minute.

## Publishing a version

1. Raise the version in `version.txt`.
2. `.uild.ps1 -Release -Notes "What changed"` builds the installer, signs it, and publishes it as a GitHub
   release. The site shows it within a minute.

Installed copies find the new version at their next check (every few hours).

## Publishing a character pack

`dotnet run tools\MakePack.cs -- <pack folder> site\characters` writes the pack's `.nyatch` file and cover
here and lists it in `index.json`. Commit and push; Vercel rebuilds. To update a pack, raise the
`version` in its `pack.json` and run the tool again: installed copies update themselves.

Only publish packs whose pictures you may share.

## Trying it locally

`node build.mjs`, then `node tools/dev.mjs`, and open http://localhost:3000. It serves `dist/` with the routes
in `vercel.json` and runs the functions in `api/`, as Vercel does.
