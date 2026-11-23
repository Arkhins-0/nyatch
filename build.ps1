# Builds Nyatch: the native island library (C++), then the app (C#).
#   .\build.ps1            Release build
#   .\build.ps1 -Run       build, then start the app
#   .\build.ps1 -Test      build, then run the tests
#   .\build.ps1 -Exe       build, then make the installer: out\Nyatch-Setup-<version>.exe (the .msi inside it)
#   .\build.ps1 -Release   make the installer, sign it and publish it as a release of the (private)
#                          repository; nyatch.arkhins.com is then rebuilt from it and serves it
#                          (version from version.txt; notes with -Notes "..."; NYATCH_DEPLOY_HOOK, a
#                          Vercel deploy hook, rebuilds the site at once)
param(
    [ValidateSet("Debug", "Release")] [string]$Configuration = "Release",
    [switch]$Run,
    [switch]$Test,
    [switch]$Exe,
    [switch]$Release,
    [string]$Notes = "",
    [string]$ReleaseRepo = "Arkhins-0/nyatch",
    [string]$Version = (Get-Content (Join-Path $PSScriptRoot "version.txt") -TotalCount 1).Trim()
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if ($Release) { $Exe = $true }

# The C++ library needs Visual Studio's MSBuild (with the C++ desktop workload).
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = $null
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}
if (-not $msbuild) {
    # vswhere does not always answer; look where Visual Studio puts it.
    $msbuild = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\MSBuild.exe" -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $msbuild) { throw "Visual Studio's MSBuild was not found. Install Visual Studio with 'Desktop development with C++'." }

& $msbuild (Join-Path $root "native\Nyatch.Island\Nyatch.Island.vcxproj") /p:Configuration=$Configuration /p:Platform=x64 /nologo /v:m
if ($LASTEXITCODE -ne 0) { throw "The island library did not build." }
& $msbuild (Join-Path $root "native\Nyatch.Explorer\Nyatch.Explorer.vcxproj") /p:Configuration=$Configuration /p:Platform=x64 /nologo /v:m
if ($LASTEXITCODE -ne 0) { throw "The File Explorer library did not build." }

dotnet build (Join-Path $root "src\Nyatch.App\Nyatch.App.csproj") -c $Configuration -p:IslandConfiguration=$Configuration --nologo -v:m
if ($LASTEXITCODE -ne 0) { throw "The app did not build." }

if ($Test) {
    dotnet test (Join-Path $root "tests\Nyatch.Core.Tests\Nyatch.Core.Tests.csproj") --nologo -v:m
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

if ($Exe) {
    # The app with its own copy of .NET, so it runs on a PC that does not have .NET 10.
    $publish = Join-Path $root "out\publish"
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet publish (Join-Path $root "src\Nyatch.App\Nyatch.App.csproj") -c $Configuration -r win-x64 --self-contained true `
        -p:IslandConfiguration=$Configuration -p:Version=$Version -o $publish --nologo -v:m
    if ($LASTEXITCODE -ne 0) { throw "The app did not publish." }

    # The file list, one component per folder. Files in the user's profile are tracked by a
    # registry value under HKCU (not by the files themselves), and each folder is removed on
    # uninstall: what Windows Installer expects of a per-user install.
    $generated = Join-Path $root "out\AppFiles.wxs"
    $xml = New-Object System.Text.StringBuilder
    [void]$xml.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment>')
    $script:folderCount = 0
    $script:fileCount = 0
    $components = New-Object System.Collections.Generic.List[string]
    function Add-Folder([string]$path, [string]$directoryId) {
        $script:folderCount++
        $component = "AppFolder$($script:folderCount)"
        $components.Add($component)
        # A fixed ID per folder, made from its path, so it is the same in every build (upgrades rely on that).
        $relative = $path.Substring($publish.Length).TrimStart('\').ToLowerInvariant()
        $hash = [System.Security.Cryptography.MD5]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes("Nyatch folder:" + $relative))
        $guid = (New-Object System.Guid (, $hash)).ToString().ToUpperInvariant()
        [void]$xml.AppendLine("<Component Id=`"$component`" Directory=`"$directoryId`" Guid=`"$guid`">")
        [void]$xml.AppendLine("<RegistryValue Root=`"HKLM`" Key=`"Software\Nyatch\Files`" Name=`"$component`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" />")
        [void]$xml.AppendLine("<RemoveFolder Id=`"Remove$component`" On=`"uninstall`" />")
        foreach ($file in Get-ChildItem $path -File) {
            $script:fileCount++
            [void]$xml.AppendLine("<File Id=`"AppFile$($script:fileCount)`" Source=`"$([System.Security.SecurityElement]::Escape($file.FullName))`" />")
        }
        [void]$xml.AppendLine('</Component>')
        foreach ($sub in Get-ChildItem $path -Directory) {
            $subId = "AppDir$($script:folderCount)_$($sub.Name -replace '[^A-Za-z0-9_]', '_')"
            [void]$xml.AppendLine("<DirectoryRef Id=`"$directoryId`"><Directory Id=`"$subId`" Name=`"$([System.Security.SecurityElement]::Escape($sub.Name))`" /></DirectoryRef>")
            Add-Folder $sub.FullName $subId
        }
    }
    Add-Folder $publish "INSTALLFOLDER"
    [void]$xml.AppendLine('<ComponentGroup Id="AppFiles">')
    foreach ($component in $components) { [void]$xml.AppendLine("<ComponentRef Id=`"$component`" />") }
    [void]$xml.AppendLine('</ComponentGroup></Fragment></Wix>')
    [System.IO.File]::WriteAllText($generated, $xml.ToString(), (New-Object System.Text.UTF8Encoding $false))

    # WiX is a tool of this project (dotnet-tools.json), with its extensions in installer\.wix
    # (not in the repository: fetched here the first time).
    dotnet tool restore | Out-Null
    foreach ($extension in "WixToolset.UI.wixext", "WixToolset.Util.wixext", "WixToolset.Bal.wixext") {
        if (-not (Test-Path (Join-Path $root "installer\.wix\extensions\$extension"))) {
            Push-Location (Join-Path $root "installer")
            try { dotnet wix extension add "$extension/5.0.2" | Out-Null } finally { Pop-Location }
        }
    }
    $msiPath = Join-Path $root "out\Nyatch-$Version.msi"
    Push-Location (Join-Path $root "installer")
    try {
        dotnet wix build Package.wxs $generated -arch x64 -ext WixToolset.Util.wixext `
            -d "Version=$Version" -d "PublishDir=$publish" -d "IconFile=$(Join-Path $root 'src\Nyatch.App\nyatch.ico')" `
            -d "BrandDir=$(Join-Path $root 'assets\brand')" -ext WixToolset.UI.wixext `
            -o $msiPath
        if ($LASTEXITCODE -ne 0) { throw "The installer did not build." }
        # The setup program people download: the MSI inside an .exe (Bundle.wxs).
        $setupPath = Join-Path $root "out\Nyatch-Setup-$Version.exe"
        # (Its extension's DLL has another name than its package: it is named in full.)
        $bal = Join-Path $root "installer\.wix\extensions\WixToolset.Bal.wixext\5.0.2\wixext5\WixToolset.BootstrapperApplications.wixext.dll"
        dotnet wix build Bundle.wxs -arch x64 -ext $bal `
            -d "Version=$Version" -d "MsiFile=$msiPath" -d "IconFile=$(Join-Path $root 'src\Nyatch.App\nyatch.ico')" `
            -o $setupPath
        if ($LASTEXITCODE -ne 0) { throw "The setup program did not build." }
    }
    finally {
        Pop-Location
    }
    Write-Host "Installer: $setupPath"

    if ($Release) {
        # The checksum, and the signature that installed copies check before updating (tools\ReleaseSigning.cs).
        # The setup program for downloads, and the MSI too: copies from before the setup program (1.0.0)
        # update only from an .msi.
        $files = @()
        foreach ($file in $setupPath, $msiPath) {
            $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
            $name = Split-Path $file -Leaf
            [System.IO.File]::WriteAllText("$file.sha256", "$hash  $name`n", (New-Object System.Text.UTF8Encoding $false))
            dotnet run (Join-Path $root "tools\ReleaseSigning.cs") -- sign $file $Version
            if ($LASTEXITCODE -ne 0) { throw "The installer could not be signed." }
            $files += $file, "$file.sha256", "$file.sig"
        }

        $tag = "v$Version"
        gh release view $tag --repo $ReleaseRepo *> $null
        if ($LASTEXITCODE -eq 0) { throw "$ReleaseRepo already has a release $tag. Raise the version in version.txt first." }
        if ($Notes.Trim().Length -eq 0) { $Notes = "Nyatch $Version." }
        gh release create $tag @files --repo $ReleaseRepo --title "Nyatch $Version" --notes $Notes
        if ($LASTEXITCODE -ne 0) { throw "The release was not published." }
        Write-Host "Published Nyatch $Version to https://github.com/$ReleaseRepo/releases/tag/$tag"
        # The site serves it once it is rebuilt.
        if ($env:NYATCH_DEPLOY_HOOK) {
            Invoke-RestMethod -Method Post -Uri $env:NYATCH_DEPLOY_HOOK | Out-Null
            Write-Host "nyatch.arkhins.com is being rebuilt with it."
        }
        else {
            Write-Host "Redeploy nyatch.arkhins.com in Vercel to serve it (or set NYATCH_DEPLOY_HOOK to do it here)."
        }
    }
}

if ($Run) {
    Start-Process (Join-Path $root "src\Nyatch.App\bin\$Configuration\net10.0-windows\Nyatch.exe")
}
