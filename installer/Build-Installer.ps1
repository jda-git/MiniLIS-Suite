<#
.SYNOPSIS
    Genera el instalador de MiniLIS Suite: installer\out\MiniLIS-Suite-<versión>-instalador.exe

.DESCRIPTION
    1. Publica MiniLIS.Web en Release, autocontenido para Windows x64: el programa lleva su
       propio entorno .NET 9, así que el equipo de destino no necesita instalar nada.
    2. Comprueba que no se cuela ninguna base de datos ni configuración de desarrollo.
    3. Compila installer\MiniLIS.iss con Inno Setup 6 (gratuito: https://jrsoftware.org).

    La versión se toma de Directory.Build.props, igual que la del programa.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
#>
[CmdletBinding()]
param(
    # Solo publica el programa (sin compilar el instalador).
    [switch]$PublishOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot 'out'
$app = Join-Path $out 'app'

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No se encontró <Version> en Directory.Build.props.' }
Write-Host "MiniLIS Suite $version" -ForegroundColor Cyan

# ── 1. Publicar ──
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
& dotnet publish (Join-Path $root 'MiniLIS.Web\MiniLIS.Web.csproj') `
    -c Release -r win-x64 --self-contained true -o $app -p:PublishReadyToRun=false -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish ha fallado.' }

# ── 2. Nada de datos ni configuración de desarrollo en el paquete ──
Remove-Item (Join-Path $app 'appsettings.Development.json') -Force -ErrorAction SilentlyContinue
$prohibidos = Get-ChildItem $app -Recurse -Include '*.db', '*.db-wal', '*.db-shm', 'minilis.settings.json', 'secrets.json' -ErrorAction SilentlyContinue
if ($prohibidos) {
    throw ('El paquete contiene ficheros que no deben distribuirse: ' + (($prohibidos | ForEach-Object { $_.Name }) -join ', '))
}
if (-not (Test-Path (Join-Path $app 'MiniLIS.Web.exe'))) { throw 'No se ha generado MiniLIS.Web.exe.' }
$size = [math]::Round(((Get-ChildItem $app -Recurse | Measure-Object Length -Sum).Sum / 1MB), 0)
Write-Host "Programa publicado en $app ($size MB)" -ForegroundColor Green

if ($PublishOnly) { return }

# ── 3. Compilar el instalador ──
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw ('No se encuentra Inno Setup 6. Instálelo (gratuito) desde https://jrsoftware.org/isdl.php ' +
           'o con: winget install JRSoftware.InnoSetup')
}

& $iscc "/DAppVersion=$version" "/DAppSource=$app" (Join-Path $PSScriptRoot 'MiniLIS.iss')
if ($LASTEXITCODE -ne 0) { throw 'La compilación del instalador ha fallado.' }

$exe = Join-Path $out "MiniLIS-Suite-$version-instalador.exe"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $exe)" | Out-File -FilePath "$exe.sha256" -Encoding ascii
Write-Host "Instalador: $exe" -ForegroundColor Green
Write-Host "SHA-256:    $hash"
