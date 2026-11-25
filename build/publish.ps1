<#
.SYNOPSIS
    Builds a FrameForge release: a self-contained, ReadyToRun app for 64-bit Windows, a portable ZIP and (when Inno
    Setup 6 is installed) the installer. See docs/decisions/0019-installer-updates-crash-reports.md.

.EXAMPLE
    ./build/publish.ps1
    ./build/publish.ps1 -Version 0.2.0
    ./build/publish.ps1 -SkipTests

.NOTES
    Output goes to artifacts\ (ignored by git). The tests run first unless -SkipTests is given.
#>
param(
    [string]$Version,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish\FrameForge'

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }
Write-Host "FrameForge $Version"

if (-not $SkipTests) {
    Write-Host 'Building and testing…'
    dotnet build (Join-Path $root 'FrameForge.sln') -c Release -m:1 --nologo -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet test (Join-Path $root 'FrameForge.sln') -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing was published.' }
}

Write-Host 'Publishing (self-contained, ReadyToRun)…'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\FrameForge.App\FrameForge.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:Version=$Version -o $publish --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# A self-contained app ships the .NET runtime, so its licence and notices ship too (docs/licence-checklist.md, 4).
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
foreach ($pack in 'microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64') {
    $dir = Get-ChildItem (Join-Path $packages $pack) -Directory | Where-Object Name -like '10.*' |
        Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
    if (-not $dir) { throw "Runtime pack $pack 10.x not found in $packages." }
    $short = if ($pack -like '*windowsdesktop*') { 'windowsdesktop' } else { 'netcore' }
    # The packs name these files differently (LICENSE.TXT or LICENSE); take whichever is there.
    $licence = 'LICENSE.TXT', 'LICENSE' | ForEach-Object { Join-Path $dir.FullName $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $licence) { throw "No licence file in $($dir.FullName)." }
    Copy-Item $licence (Join-Path $publish "licenses\dotnet-$short-runtime-$($dir.Name)-LICENSE.txt")
    $notices = Join-Path $dir.FullName 'THIRD-PARTY-NOTICES.TXT'
    if (Test-Path $notices) {
        Copy-Item $notices (Join-Path $publish "licenses\dotnet-$short-runtime-$($dir.Name)-THIRD-PARTY-NOTICES.txt")
    }
}

$size = [math]::Round(((Get-ChildItem $publish -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB)
Write-Host "Published: $publish ($size MB)"

$zip = Join-Path $artifacts "FrameForge-$Version-win-x64-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip
Write-Host "Portable ZIP: $zip"

$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    Write-Host 'Building the installer…'
    $isccArgs = @('/Q', "/DAppVersion=$Version", "/DSourceDir=$publish", "/DOutputDir=$artifacts")

    # Microsoft's Visual C++ runtime installer (native ONNX Runtime and LibRaw need it). Downloaded once, by hand, into
    # build\cache\ (git ignores it); see ADR 0019. The installer runs it only on computers that lack this version.
    $vcRedist = Join-Path $PSScriptRoot 'cache\vc_redist.x64.exe'
    if (Test-Path $vcRedist) {
        $signature = Get-AuthenticodeSignature $vcRedist
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike 'CN=Microsoft Corporation,*') {
            throw "$vcRedist is not validly signed by Microsoft; not bundling it."
        }
        $v = [version](Get-Item $vcRedist).VersionInfo.FileVersion
        $isccArgs += "/DVCRedist=$vcRedist", "/DVCRedistMajor=$($v.Major)", "/DVCRedistMinor=$($v.Minor)", "/DVCRedistBuild=$($v.Build)"
        Write-Host "Bundling the Visual C++ runtime $v"
    } else {
        Write-Warning "No ${vcRedist}: the installer won't install the Visual C++ runtime. AI models (and RAW) need it on computers that don't have it."
    }

    & $iscc @isccArgs (Join-Path $PSScriptRoot 'FrameForge.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    Write-Host "Installer: $(Join-Path $artifacts "FrameForge-$Version-setup.exe")"
} else {
    Write-Host 'Inno Setup 6 not found: skipped the installer (the portable ZIP is ready).'
}
