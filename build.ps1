<#
.SYNOPSIS
    Builds the KulyCopy release artifacts into .\dist:
      - dist\publish\kulycopy.exe                 self-contained single-file executable
      - dist\KulyCopy-<ver>-portable-win-x64.zip  portable package (settings kept next to the exe)
      - dist\KulyCopy-Setup-<ver>.exe             Windows installer (Inno Setup)
      - dist\KulyCopy-<ver>-win-x64.msix          Microsoft Store / sideload package (with -Msix)

.DESCRIPTION
    Needs only the .NET 8 SDK. The Inno Setup compiler is taken from an existing installation or
    downloaded automatically from NuGet (Tools.InnoSetup). MSIX packaging needs the Windows 10/11 SDK
    (makeappx.exe / signtool.exe), which is present on GitHub's windows-latest runners.

.EXAMPLE
    .\build.ps1                      # exe + portable zip + installer
    .\build.ps1 -Msix                # also the MSIX package (unsigned, as the Store expects)
    .\build.ps1 -Msix -MsixIdentityName 12345Publisher.KulyCopy -MsixPublisher "CN=..." -MsixPublisherDisplayName "..."
    .\build.ps1 -Msix -SignCertificate .\dev.pfx -SignPassword secret   # signed for local testing
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller,
    [switch]$SkipPortable,
    [switch]$Msix,
    [string]$MsixIdentityName = "KulyCopy.Dev",
    [string]$MsixPublisher = "CN=KulyCopy Dev",
    [string]$MsixPublisherDisplayName = "KulyCopy contributors",
    [string]$SignCertificate = "",
    [string]$SignPassword = "",
    [string]$InnoSetupVersion = "6.7.3"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
$csproj = Join-Path $root "src\KulyCopy\KulyCopy.csproj"

function Step([string]$text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }

function Get-Version {
    $xml = [xml](Get-Content $csproj)
    $v = $xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $v) { throw "Version not found in $csproj" }
    return $v.Trim()
}

function Find-Iscc {
    $candidates = @()
    if (${env:ProgramFiles(x86)}) { $candidates += (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe") }
    if ($env:ProgramFiles) { $candidates += (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe") }
    if ($env:LOCALAPPDATA) { $candidates += (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe") }
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }

    $cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
    $pattern = Join-Path $cache "tools.innosetup\*\tools\ISCC.exe"
    $found = Get-ChildItem $pattern -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if ($found) { return $found.FullName }

    Step "Downloading Inno Setup compiler (NuGet package Tools.InnoSetup $InnoSetupVersion)"
    $tmp = Join-Path $dist ".tools\innosetup"
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Tools.InnoSetup" Version="$InnoSetupVersion" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $tmp "innosetup.csproj") -Encoding UTF8
    Copy-Item (Join-Path $root "nuget.config") $tmp -Force
    & dotnet restore (Join-Path $tmp "innosetup.csproj") --packages $cache -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore of Tools.InnoSetup failed" }
    $found = Get-ChildItem $pattern -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $found) { throw "ISCC.exe not found after restoring Tools.InnoSetup" }
    return $found.FullName
}

function Find-SdkTool([string]$name) {
    $kits = @()
    if (${env:ProgramFiles(x86)}) { $kits += (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin") }
    if ($env:ProgramFiles) { $kits += (Join-Path $env:ProgramFiles "Windows Kits\10\bin") }
    foreach ($bin in $kits) {
        if (-not (Test-Path $bin)) { continue }
        $tool = Get-ChildItem (Join-Path $bin "10.*\x64\$name") -ErrorAction SilentlyContinue |
            Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
        if ($tool) { return $tool.FullName }
    }
    return $null
}

$version = Get-Version
Write-Host "KulyCopy $version ($Configuration, $Runtime)" -ForegroundColor Green
New-Item -ItemType Directory -Force -Path $dist | Out-Null

# ---------------------------------------------------------------- publish (single file)
Step "Publishing self-contained single-file executable"
$publishDir = Join-Path $dist "publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
& dotnet publish $csproj -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false -o $publishDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
$exe = Join-Path $publishDir "kulycopy.exe"
if (-not (Test-Path $exe)) { throw "kulycopy.exe not produced" }
Write-Host ("    {0}  ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))

# ---------------------------------------------------------------- portable zip
if (-not $SkipPortable) {
    Step "Creating portable zip"
    $portableRoot = Join-Path $dist "portable"
    $portableDir = Join-Path $portableRoot "KulyCopy"
    if (Test-Path $portableRoot) { Remove-Item $portableRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
    Copy-Item (Join-Path $publishDir "*") $portableDir -Recurse -Force
    # Empty marker file: settings and history stay in a "data" folder next to the exe.
    New-Item -ItemType File -Path (Join-Path $portableDir "portable") | Out-Null
    @"
KulyCopy $version (portable)

Run kulycopy.exe. Settings and clipboard history are stored in the "data" folder next to it,
so you can keep the whole folder on a USB stick or move it between machines.
Remove the empty "portable" file to use the regular per-user folders instead.

Project page and documentation: https://github.com/GuilhermeKulyk/kulycopy
"@ | Set-Content (Join-Path $portableDir "README.txt") -Encoding UTF8
    $zip = Join-Path $dist "KulyCopy-$version-portable-$Runtime.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $portableDir -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ("    {0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}

# ---------------------------------------------------------------- installer
if (-not $SkipInstaller) {
    Step "Compiling installer (Inno Setup)"
    $iscc = Find-Iscc
    Write-Host "    using $iscc"
    & $iscc /Qp "/DAppVersion=$version" "/DPublishDir=$publishDir" "/O$dist" (Join-Path $root "installer\KulyCopy.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }
    $setup = Join-Path $dist "KulyCopy-Setup-$version.exe"
    Write-Host ("    {0}  ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
}

# ---------------------------------------------------------------- MSIX
if ($Msix) {
    Step "Building MSIX package"
    $makeappx = Find-SdkTool "makeappx.exe"
    if (-not $makeappx) { throw "makeappx.exe not found: install the Windows 10/11 SDK (or run on a machine that has it)" }

    $msixRoot = Join-Path $dist "msix"
    $appDir = Join-Path $msixRoot "app"
    if (Test-Path $msixRoot) { Remove-Item $msixRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $appDir | Out-Null

    # Regular (non single-file) publish: MSIX prefers plain files, no self-extraction.
    & dotnet publish $csproj -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $appDir --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (msix) failed" }

    Copy-Item (Join-Path $root "packaging\msix\Assets") (Join-Path $appDir "Assets") -Recurse -Force

    $parts = $version.Split(".")
    while ($parts.Count -lt 4) { $parts += "0" }
    $msixVersion = ($parts[0..3] -join ".")

    $manifest = Get-Content (Join-Path $root "packaging\msix\AppxManifest.template.xml") -Raw
    $manifest = $manifest.Replace("{{IdentityName}}", $MsixIdentityName)
    $manifest = $manifest.Replace("{{Publisher}}", $MsixPublisher)
    $manifest = $manifest.Replace("{{PublisherDisplayName}}", $MsixPublisherDisplayName)
    $manifest = $manifest.Replace("{{Version}}", $msixVersion)
    [IO.File]::WriteAllText((Join-Path $appDir "AppxManifest.xml"), $manifest, (New-Object System.Text.UTF8Encoding $false))

    $msixFile = Join-Path $dist "KulyCopy-$version-$Runtime.msix"
    if (Test-Path $msixFile) { Remove-Item $msixFile -Force }
    $makeappxOutput = & $makeappx pack /d $appDir /p $msixFile /o 2>&1
    if ($LASTEXITCODE -ne 0) {
        $makeappxOutput | ForEach-Object { Write-Host "    $_" }
        throw "makeappx failed"
    }
    Write-Host ("    {0}  ({1:N1} MB)" -f $msixFile, ((Get-Item $msixFile).Length / 1MB))

    if ($SignCertificate) {
        $signtool = Find-SdkTool "signtool.exe"
        if (-not $signtool) { throw "signtool.exe not found" }
        Step "Signing MSIX"
        $args = @("sign", "/fd", "SHA256", "/f", $SignCertificate)
        if ($SignPassword) { $args += @("/p", $SignPassword) }
        $args += $msixFile
        & $signtool @args
        if ($LASTEXITCODE -ne 0) { throw "signtool failed (the certificate subject must equal the manifest Publisher: $MsixPublisher)" }
    }
    else {
        Write-Host "    (unsigned: fine for Microsoft Store submission, which signs it; sign with -SignCertificate for sideload tests)"
    }
}

Step "Done"
Get-ChildItem $dist -File | Where-Object { $_.Extension -in ".exe", ".zip", ".msix" } |
    ForEach-Object { Write-Host ("    {0,-48} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
