<#
.SYNOPSIS
    Build TubaWinUi3 MSIX packages for Microsoft Store submission — WITHOUT bundled Tools/ folder.

.DESCRIPTION
    This script creates a lean MSIX package that does NOT include the Tools/ directory.
    The MSIX version downloads the tools bundle from GitHub/GitCode Release on first launch.

    1. Build x64 and ARM64 self-contained packages (no Tools/)
    2. Remove .pdb and build artifacts
    3. Create clean AppxManifest.xml
    4. Package each architecture as .msix
    5. Create .msixbundle containing both packages
    6. Attempt to sign (Store re-signs anyway)

.NOTES
    SignTool may fail on large packages — Store accepts UNSIGNED .msix/.msixbundle and re-signs them.
#>

$ErrorActionPreference = 'Continue'

$ProjectDir         = 'C:\Users\luolan\Desktop\tubawinui3'
$MakeAppxPath       = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe'
$SignToolPath        = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe'
$CertPath            = 'C:\Users\luolan\Desktop\tubawinui3\TubaWinUi3_StoreKey.pfx'
$CertPassword        = 'EasyNote2026'
$PackageVersion      = '1.0.1.0'
$PackageName         = 'DA3D64F4.winui3'
$Publisher           = 'CN=CC2339A5-C760-46C3-91D8-130408AF3528'
$PublisherDisplayName = [char]0x7F57 + [char]0x6F9C + [char]0x560E + [char]0x560E
$DisplayName         = [char]0x56FE + [char]0x5427 + [char]0x5DE5 + [char]0x5177 + [char]0x7BB1 + 'winui3'
$Description          = $DisplayName + ' - PC' + [char]0x786C + [char]0x4EF6 + [char]0x68C0 + [char]0x6D4B + [char]0x4E0E + [char]0x7CFB + [char]0x7EDF + [char]0x7EF4 + [char]0x62A4 + [char]0x5DE5 + [char]0x5177 + [char]0x96C6

$OutputDir = Join-Path $ProjectDir 'StoreOutputNoTools'
$TempDir   = Join-Path $env:TEMP 'TubaWinUi3_BuildNoTools'

function Write-CleanManifest {
    param([string]$ManifestPath, [string]$Arch)

    $lines = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap rescap">'
        ('  <Identity Name="{0}" Publisher="{1}" Version="{2}" ProcessorArchitecture="{3}" />' -f $PackageName, $Publisher, $PackageVersion, $Arch)
        '  <Properties>'
        ('    <DisplayName>{0}</DisplayName>' -f $DisplayName)
        ('    <PublisherDisplayName>{0}</PublisherDisplayName>' -f $PublisherDisplayName)
        '    <Logo>Assets\StoreLogo.png</Logo>'
        '  </Properties>'
        '  <Dependencies>'
        '    <TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26226.0" />'
        '    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />'
        '    <PackageDependency Name="Microsoft.WindowsAppRuntime.1.8" MinVersion="8000.806.2252.0" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" />'
        '  </Dependencies>'
        '  <Resources>'
        '    <Resource Language="zh-CN" />'
        '  </Resources>'
        '  <Applications>'
        '    <Application Id="App" Executable="TubaWinUi3.exe" EntryPoint="Windows.FullTrustApplication">'
        ('      <uap:VisualElements DisplayName="{0}" Description="{1}" BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png">' -f $DisplayName, $Description)
        '        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />'
        '        <uap:SplashScreen Image="Assets\SplashScreen.png" />'
        '      </uap:VisualElements>'
        '    </Application>'
        '  </Applications>'
        '  <Capabilities>'
        '    <rescap:Capability Name="runFullTrust" />'
        '  </Capabilities>'
        '</Package>'
    )

    $content = $lines -join "`r`n"
    [System.IO.File]::WriteAllText($ManifestPath, $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host '  Manifest written successfully' -ForegroundColor Gray
}

function Remove-BuildArtifacts {
    param([string]$Root)

    Get-ChildItem -LiteralPath $Root -Filter '*.pdb' -Recurse -Force -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $Root -Filter '*.appxrecipe' -Recurse -Force -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Build-ArchPackage {
    param([string]$Arch)

    Write-Host ''
    Write-Host '========================================' -ForegroundColor Cyan
    Write-Host "  Building $Arch package (no Tools)" -ForegroundColor Cyan
    Write-Host '========================================' -ForegroundColor Cyan

    $archDir  = Join-Path $TempDir "TubaWinUi3_$Arch"
    $msixFile = Join-Path $OutputDir "TubaWinUi3_${PackageVersion}_${Arch}.msix"

    if (Test-Path -LiteralPath $archDir) { Remove-Item -LiteralPath $archDir -Recurse -Force }

    Write-Host "  Publishing $Arch..." -ForegroundColor Yellow
    dotnet publish "$ProjectDir\TubaWinUi3.csproj" -c Release -r "win-$Arch" --self-contained false -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:ExcludeToolsFromPublish=true -o $archDir 2>&1 | Write-Host

    if (-not (Test-Path -LiteralPath $archDir)) {
        Write-Host "  ERROR: Publish failed for $Arch" -ForegroundColor Red
        return $null
    }

    # Copy Assets from project
    Copy-Item -Path "$ProjectDir\Assets\*" -Destination "$archDir\Assets\" -Recurse -Force

    # Copy Metadata (tools.json is needed for tool descriptions)
    if (Test-Path -LiteralPath "$ProjectDir\Metadata") {
        Copy-Item -Path "$ProjectDir\Metadata" -Destination $archDir -Recurse -Force
    }

    # Copy CertBlock (needed for built-in tools)
    if (Test-Path -LiteralPath "$ProjectDir\CertBlock") {
        Copy-Item -Path "$ProjectDir\CertBlock" -Destination $archDir -Recurse -Force
    }

    # NOTE: Tools/ folder is NOT copied — MSIX version downloads it on first launch

    Write-Host '  Removing build artifacts...' -ForegroundColor Yellow
    Remove-BuildArtifacts $archDir

    Write-Host '  Writing manifest...' -ForegroundColor Yellow
    Write-CleanManifest (Join-Path $archDir 'AppxManifest.xml') $Arch

    # Remove publish artifacts
    Remove-Item -LiteralPath (Join-Path $archDir 'TubaWinUi3.pdb') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $archDir 'TubaWinUi3.build.appxrecipe') -Force -ErrorAction SilentlyContinue

    # Stats
    $files = Get-ChildItem -LiteralPath $archDir -Recurse -File
    $totalSize = [math]::Round(($files | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    Write-Host "  Package content: $($files.Count) files, $totalSize MB" -ForegroundColor Gray

    Write-Host '  Creating MSIX...' -ForegroundColor Yellow
    $makeappxResult = & $MakeAppxPath pack /d $archDir /p $msixFile /o 2>&1
    Write-Host ($makeappxResult | Select-Object -Last 1)

    $msixSize = [math]::Round((Get-Item -LiteralPath $msixFile).Length / 1MB, 1)
    Write-Host "  MSIX size: $msixSize MB" -ForegroundColor Green

    Write-Host '  Attempting to sign...' -ForegroundColor Yellow
    $signResult = & $SignToolPath sign /fd SHA256 /f $CertPath /p $CertPassword $msixFile 2>&1
    if ($signResult -match 'Successfully signed') {
        Write-Host '  Signing: SUCCESS' -ForegroundColor Green
    } else {
        Write-Host '  Signing: FAILED (OK for Store - Store re-signs)' -ForegroundColor Yellow
    }

    return $msixFile
}

# ============================================================
# Main
# ============================================================
Write-Host 'TubaWinUi3 MSIX Bundle Build Script (NO Tools)' -ForegroundColor Magenta
Write-Host "Version: $PackageVersion" -ForegroundColor Magenta
Write-Host 'Tools will be downloaded from Release on first launch' -ForegroundColor Magenta
Write-Host ''

if (-not (Test-Path -LiteralPath $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
if (-not (Test-Path -LiteralPath $TempDir)) { New-Item -ItemType Directory -Path $TempDir -Force | Out-Null }

$x64Msix = Build-ArchPackage 'x64'
if ($null -eq $x64Msix) {
    Write-Host 'FAILED to build x64 package' -ForegroundColor Red
    exit 1
}

$arm64Msix = Build-ArchPackage 'arm64'
$arm64Available = $null -ne $arm64Msix

if ($arm64Available) {
    Write-Host ''
    Write-Host '========================================' -ForegroundColor Cyan
    Write-Host '  Creating MSIX Bundle' -ForegroundColor Cyan
    Write-Host '========================================' -ForegroundColor Cyan

    $bundleFile = Join-Path $OutputDir "TubaWinUi3_${PackageVersion}.msixbundle"

    $x64Name  = "TubaWinUi3_${PackageVersion}_x64.msix"
    $arm64Name = "TubaWinUi3_${PackageVersion}_arm64.msix"

    $mapping = "[Files]`r`n`"$x64Msix`" `"$x64Name`"`r`n`"$arm64Msix`" `"$arm64Name`"`r`n"
    $mappingPath = Join-Path $TempDir 'bundle_mapping.txt'
    [System.IO.File]::WriteAllText($mappingPath, $mapping, [System.Text.UTF8Encoding]::new($false))

    & $MakeAppxPath bundle /f $mappingPath /p $bundleFile /o 2>&1 | Select-Object -Last 2

    $bundleSize = [math]::Round((Get-Item -LiteralPath $bundleFile).Length / 1MB, 1)
    Write-Host "  Bundle size: $bundleSize MB" -ForegroundColor Green

    Write-Host '  Attempting to sign bundle...' -ForegroundColor Yellow
    $bundleSignResult = & $SignToolPath sign /fd SHA256 /f $CertPath /p $CertPassword $bundleFile 2>&1
    if ($bundleSignResult -match 'Successfully signed') {
        Write-Host '  Bundle signing: SUCCESS' -ForegroundColor Green
    } else {
        Write-Host '  Bundle signing: FAILED (OK for Store - Store re-signs)' -ForegroundColor Yellow
    }
} else {
    Write-Host ''
    Write-Host 'ARM64 build failed - only x64 package available' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '========================================' -ForegroundColor Green
Write-Host '  BUILD COMPLETE (NO Tools)' -ForegroundColor Green
Write-Host '========================================' -ForegroundColor Green
Write-Host "Output: $OutputDir" -ForegroundColor White
Write-Host ''
Write-Host 'REMINDER: Upload Tools.zip to GitHub/GitCode Release for each version!' -ForegroundColor Cyan
Write-Host ''

Get-ChildItem -LiteralPath $OutputDir -Filter '*.msix*' | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.Name)  ($size MB)" -ForegroundColor White
}

Write-Host ''
Write-Host 'If signing failed, that is OK for Store submission.' -ForegroundColor Yellow
Write-Host 'The Store re-signs all packages during certification.' -ForegroundColor Yellow
Write-Host ''
Write-Host 'Upload the .msixbundle (or .msix if x64-only) to Partner Center.' -ForegroundColor Cyan