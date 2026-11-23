# nyatch.arkhins.com

Nyatch's home page, and everything installed copies of Nyatch download: its updates and its character
packs. Deployed by Vercel from this folder.

## Vercel project

| Setting | Value |
|---|---|
| Root Directory | `site` |
| Framework Preset | Other |
| Build and output | from `vercel.json` (`node build.mjs`, output `dist`) |
| Domain | `nyatch.arkhins.com` |
| Environment variable | `GITHUB_TOKEN`: a fine-grained token with **Contents: Read** on `Arkhins-0/nyatch` only |

The token lets the build fetch the newest release's installer from the private repository's GitHub
releases. It is only used while building and is never part of the site.

## What the site serves

| Path | For |
|---|---|
| `/` | The home page |
| `/download` | The newest installer |
| `/releases` | What changed in each version |
| `/releases/latest.json` | The newest release, read by Nyatch's updater (the same shape as GitHub's API) |
| `/releases/<tag>/` | That release's installer, checksum and signature |
| `/characters/index.json` | The character packs Nyatch lists, with `previews/` and `packs/` |

## Publishing a version

1. Raise the version in `version.txt`.
2. `.\build.ps1 -Release -Notes "What changed"` builds the installer, signs it, and publishes it as a
   release on the private repository. With `NYATCH_DEPLOY_HOOK` set to a Vercel deploy hook, the site is
   rebuilt at once; otherwise redeploy it in Vercel.

Installed copies find the new version at their next check (every few hours).

## Publishing a character pack

`dotnet run tools\MakePack.cs -- <pack folder> site\characters` writes the pack's `.nyatch` file and cover
here and lists it in `index.json`. Commit and push; Vercel rebuilds. To update a pack, raise the
`version` in its `pack.json` and run the tool again: installed copies update themselves.

Only publish packs whose pictures you may share.

## Trying it locally

`node build.mjs` (with `GITHUB_TOKEN` set to include the releases), then serve `dist/`, for example
with `npx serve dist`.
