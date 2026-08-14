param(
    [string]$Tag = 'v805.0.0-alpha.1',
    [switch]$Latest,
    [switch]$SelfContained,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'

$ReleaseRepo = 'tyhpproject/tyhp'
$UserAgent = 'tyhp-install-script'
$InstallName = 'tyhp.exe'
$Token = $env:GITHUB_TOKEN

$installDataRoot = $env:LOCALAPPDATA
if ([string]::IsNullOrWhiteSpace($installDataRoot)) {
    $installDataRoot = Join-Path $env:USERPROFILE 'AppData\Local'
}
$InstallDir = Join-Path $installDataRoot 'Programs\tyhp'

function Get-Headers {
    $headers = @{
        'User-Agent' = $UserAgent
        'Accept' = 'application/vnd.github+json'
    }
    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers.Authorization = "token $Token"
    }
    return $headers
}

function Test-DotNet9 {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        return $false
    }
    $runtimes = dotnet --list-runtimes
    return ($runtimes -match 'Microsoft\.NETCore\.App 9\.')
}

function Resolve-Variant {
    if ($SelfContained -and $FrameworkDependent) {
        throw 'Cannot combine -SelfContained and -FrameworkDependent.'
    }
    if ($SelfContained) {
        return 'self-contained'
    }
    if ($FrameworkDependent) {
        if (-not (Test-DotNet9)) {
            throw 'Requested framework-dependent variant, but .NET 9 was not detected.'
        }
        return 'framework-dependent'
    }
    if (Test-DotNet9) {
        return 'framework-dependent'
    }
    return 'self-contained'
}

$Variant = Resolve-Variant
$AssetName = if ($Variant -eq 'framework-dependent') {
    'tyhp-win-x64-fxdependent.exe'
} else {
    'tyhp-win-x64.exe'
}

$headers = Get-Headers
if ($Latest) {
    $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$ReleaseRepo/releases?per_page=20" -Headers $headers
    $release = $releases | Where-Object { -not $_.draft } | Select-Object -First 1
} else {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$ReleaseRepo/releases/tags/$Tag" -Headers $headers
}

if (-not $release -or -not $release.tag_name) {
    throw 'Unable to determine a GitHub release tag. The compiler repo may not have a public release yet.'
}

$asset = $release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
if (-not $asset) {
    throw "Unable to find asset '$AssetName' in release $($release.tag_name)."
}

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("tyhp-" + [guid]::NewGuid().ToString('N') + '.exe')
Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile $tmp

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
$destination = Join-Path $InstallDir $InstallName
Move-Item -Path $tmp -Destination $destination -Force

Write-Host "Installed tyhp $($release.tag_name) ($Variant)."
Write-Host "Path: $destination"
Write-Host "Add $InstallDir to PATH if 'tyhp' is not found."
