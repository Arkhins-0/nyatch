# Nyatch themes

The released themes, and only them: this branch holds no code. Nyatch's Theme page lists what is in
`index.json` and downloads a theme when someone asks for it, through nyatch.arkhins.com/characters/, which
reads this branch.

Each theme has a folder: its pack (`<id>.nyatch`) and its cover (`preview.png`).

## Adding or updating a theme

From the code branch (`main`), with this branch checked out in a folder of its own:

    dotnet run tools\MakePack.cs -- <theme folder> <this branch's folder>

It writes `<id>/<id>.nyatch` and `<id>/preview.png` and lists the theme in `index.json`. To update a theme,
raise the `version` in its `pack.json` first: installed copies then update themselves. Commit and push this
branch; the site shows the change within a few minutes, without being rebuilt.

Only publish themes whose pictures you may share.
