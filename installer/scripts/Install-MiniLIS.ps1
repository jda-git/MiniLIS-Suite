<#
.SYNOPSIS
    Instalación, actualización y desinstalación de MiniLIS Suite como servicio de Windows.

.DESCRIPTION
    Lo usa el instalador (MiniLIS-Suite-x.y.z-instalador.exe), pero también se puede ejecutar
    a mano o de forma desatendida desde una consola de PowerShell como administrador. Ver
    docs/INSTALACION.md.

    Acciones:
      Check            Comprueba los requisitos. No cambia nada.
      TestCertificate  Comprueba un certificado PFX (fichero y contraseña). No cambia nada.
      Configure        Instalación nueva: certificado, configuración, servicio, permisos,
                       cortafuegos, arranque y comprobación. Los ficheros del programa deben
                       estar ya copiados en -InstallDir.
      PrepareUpgrade   Antes de actualizar: detiene el servicio y copia la base de datos.
      CompleteUpgrade  Después de copiar los ficheros nuevos: vuelve a arrancar el servicio.
      Uninstall        Detiene y elimina el servicio y la regla del cortafuegos. NUNCA borra
                       los datos (base de datos, copias, configuración).

    Los secretos (contraseña del administrador inicial y del certificado PFX) no se pasan
    como argumentos, porque serían visibles en la lista de procesos: se leen de un fichero
    JSON (-SecretsFile) que el script borra en cuanto lo ha leído.

.EXAMPLE
    # Instalación desatendida en un servidor, con el certificado del hospital:
    .\Install-MiniLIS.ps1 -Action Configure -InstallDir "C:\Program Files\MiniLIS" `
        -Mode Servidor -Port 443 -HostNames "minilis.hospital.local;minilis" `
        -CertPfxPath "D:\certs\minilis.pfx" -SecretsFile "D:\temp\secretos.json"
    # secretos.json: { "adminPassword": "...", "certPassword": "..." }
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Check', 'TestCertificate', 'Configure', 'PrepareUpgrade', 'CompleteUpgrade', 'Uninstall')]
    [string]$Action,

    [string]$InstallDir = (Split-Path -Parent $PSScriptRoot),
    [string]$DataDir = (Join-Path $env:ProgramData 'MiniLIS'),

    # Servidor: accesible desde la red. Puesto: solo desde este equipo (https://localhost).
    [ValidateSet('Servidor', 'Puesto')]
    [string]$Mode = 'Servidor',

    [ValidateRange(1, 65535)]
    [int]$Port = 443,

    # Nombres con los que se accederá (separados por ";"). Por defecto, los de este equipo.
    [string]$HostNames,

    # Certificado del hospital (PFX). Si no se indica, se genera uno autofirmado.
    [string]$CertPfxPath,

    [string]$AdminUser = 'admin@minilis.com',

    # JSON con "adminPassword" y, si hay PFX, "certPassword". Se borra tras leerlo.
    [string]$SecretsFile,

    # Fichero donde se escribe el resultado para el asistente (una línea por comprobación:
    # NIVEL|Comprobación|Detalle).
    [string]$ReportFile,

    [string]$AppVersion = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$ServiceName = 'MiniLIS'
$ServiceAccount = 'NT SERVICE\MiniLIS'
$ExeName = 'MiniLIS.Web.exe'
$RegistryKey = 'HKLM:\SOFTWARE\MiniLIS'
$FirewallRule = 'MiniLIS-HTTPS'
$EventSource = 'MiniLIS'
$MinBuild = 14393   # Windows 10 1607 / Windows Server 2016

$script:Report = New-Object System.Collections.Generic.List[string]
$script:HasErrors = $false

# ── Utilidades ───────────────────────────────────────────────────────────────────────

function Write-Log([string]$Message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $Message
    Write-Host $line
    try {
        $logDir = Join-Path $DataDir 'logs'
        if (Test-Path $DataDir) {
            if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
            Add-Content -Path (Join-Path $logDir 'instalacion.log') -Value $line -Encoding UTF8
        }
    } catch { }
}

# Nivel: OK, AVISO, ERROR, INFO
function Add-Result([string]$Level, [string]$Check, [string]$Detail) {
    $script:Report.Add(('{0}|{1}|{2}' -f $Level, $Check, ($Detail -replace '[\r\n]+', ' ')))
    if ($Level -eq 'ERROR') { $script:HasErrors = $true }
    Write-Log ('[{0}] {1}: {2}' -f $Level, $Check, $Detail)
}

function Save-Report {
    if ($ReportFile) {
        $dir = Split-Path -Parent $ReportFile
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        # UTF-8 con BOM: es lo que lee sin problemas el asistente (Inno Setup).
        $script:Report | Out-File -FilePath $ReportFile -Encoding utf8
    }
}

function Test-IsAdmin {
    $p = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-DefaultHostNames {
    $names = New-Object System.Collections.Generic.List[string]
    $names.Add($env:COMPUTERNAME.ToLowerInvariant())
    try {
        $fqdn = [System.Net.Dns]::GetHostEntry('localhost').HostName
        $cs = Get-CimInstance Win32_ComputerSystem
        if ($cs.PartOfDomain -and $cs.Domain) {
            $fqdn = ('{0}.{1}' -f $env:COMPUTERNAME, $cs.Domain).ToLowerInvariant()
        }
        if ($fqdn -and -not $names.Contains($fqdn.ToLowerInvariant())) { $names.Add($fqdn.ToLowerInvariant()) }
    } catch { }
    return $names
}

function Get-HostList {
    if ($Mode -eq 'Puesto') { return @('localhost') }
    $list = New-Object System.Collections.Generic.List[string]
    $source = $HostNames
    if ([string]::IsNullOrWhiteSpace($source)) { $source = (Get-DefaultHostNames) -join ';' }
    foreach ($h in ($source -split '[;,\s]+')) {
        $t = $h.Trim().ToLowerInvariant()
        if ($t -and -not $list.Contains($t)) { $list.Add($t) }
    }
    if (-not $list.Contains('localhost')) { $list.Add('localhost') }
    return $list
}

function Get-AppUrl {
    $hosts = @(Get-HostList)
    $h = $hosts[0]
    if ($Port -eq 443) { return "https://$h/" }
    return "https://${h}:$Port/"
}

function Read-Secrets {
    $secrets = @{ adminPassword = $null; certPassword = $null }
    if ($SecretsFile -and (Test-Path $SecretsFile)) {
        try {
            $json = Get-Content -Path $SecretsFile -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($json.PSObject.Properties['adminPassword']) { $secrets.adminPassword = [string]$json.adminPassword }
            if ($json.PSObject.Properties['certPassword']) { $secrets.certPassword = [string]$json.certPassword }
        } finally {
            Remove-Item -Path $SecretsFile -Force -ErrorAction SilentlyContinue
        }
    }
    return $secrets
}

function Get-SettingsPath { return (Join-Path $DataDir 'minilis.settings.json') }

function Get-DatabasePath {
    $settings = Get-SettingsPath
    if (Test-Path $settings) {
        $json = Get-Content -Path $settings -Raw -Encoding UTF8 | ConvertFrom-Json
        $cs = [string]$json.ConnectionStrings.DefaultConnection
        if ($cs -match 'Data Source\s*=\s*([^;]+)') { return $Matches[1].Trim() }
    }
    return (Join-Path $DataDir 'db\minilis.db')
}

function Get-InstalledVersion {
    try { return (Get-ItemProperty -Path $RegistryKey -Name Version -ErrorAction Stop).Version } catch { return $null }
}

# ── Requisitos ───────────────────────────────────────────────────────────────────────

function Invoke-Check {
    # Sistema operativo
    $os = Get-CimInstance Win32_OperatingSystem
    $build = [int]$os.BuildNumber
    if ($build -ge $MinBuild) { Add-Result 'OK' 'Sistema operativo' ('{0} (compilación {1})' -f $os.Caption, $build) }
    else { Add-Result 'ERROR' 'Sistema operativo' ('{0} (compilación {1}). Se necesita Windows 10 / Windows Server 2016 o posterior.' -f $os.Caption, $build) }

    if ([Environment]::Is64BitOperatingSystem) { Add-Result 'OK' 'Arquitectura' 'Windows de 64 bits' }
    else { Add-Result 'ERROR' 'Arquitectura' 'Se necesita Windows de 64 bits.' }

    if (Test-IsAdmin) { Add-Result 'OK' 'Permisos' 'Ejecutando como administrador' }
    else { Add-Result 'ERROR' 'Permisos' 'Hay que ejecutar la instalación como administrador.' }

    # .NET: el programa se distribuye autocontenido, así que no hay nada que instalar.
    Add-Result 'OK' '.NET' 'No hace falta instalarlo: MiniLIS incluye su propio entorno .NET 9.'
    Add-Result 'OK' 'Base de datos' 'No hace falta instalar ningún motor: MiniLIS usa SQLite, incluido.'

    # Memoria
    $ramGb = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
    if ($ramGb -ge 4) { Add-Result 'OK' 'Memoria' "$ramGb GB" }
    elseif ($ramGb -ge 2) { Add-Result 'AVISO' 'Memoria' "$ramGb GB. Funciona, pero se recomiendan 4 GB o más." }
    else { Add-Result 'ERROR' 'Memoria' "$ramGb GB. Se necesitan al menos 2 GB." }

    # Espacio en disco (programa y datos)
    $drives = @()
    foreach ($p in @($InstallDir, $DataDir)) {
        if ($p) { $d = [System.IO.Path]::GetPathRoot($p); if ($d -and ($drives -notcontains $d)) { $drives += $d } }
    }
    foreach ($d in $drives) {
        try {
            $info = New-Object System.IO.DriveInfo($d)
            $freeGb = [math]::Round($info.AvailableFreeSpace / 1GB, 1)
            if ($freeGb -ge 5) { Add-Result 'OK' "Espacio libre en $d" "$freeGb GB" }
            elseif ($freeGb -ge 1) { Add-Result 'AVISO' "Espacio libre en $d" "$freeGb GB. Suficiente para instalar; deje margen para la base de datos y las copias." }
            else { Add-Result 'ERROR' "Espacio libre en $d" "$freeGb GB. Se necesita al menos 1 GB." }
        } catch { Add-Result 'AVISO' "Espacio libre en $d" 'No se pudo comprobar.' }
    }

    # Instalación existente
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    $installed = Get-InstalledVersion
    if ($svc) {
        $v = $installed; if (-not $v) { $v = 'desconocida' }
        Add-Result 'INFO' 'Instalación existente' "Se actualizará la versión $v. La base de datos se copia antes de actualizar y no se modifica la configuración."
    }

    # Puerto
    try {
        $listen = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
        if ($listen.Count -eq 0) { Add-Result 'OK' "Puerto $Port" 'Libre' }
        else {
            $proc = Get-Process -Id $listen[0].OwningProcess -ErrorAction SilentlyContinue
            $pname = 'desconocido'; if ($proc) { $pname = $proc.ProcessName }
            if ($pname -eq 'MiniLIS.Web') { Add-Result 'OK' "Puerto $Port" 'En uso por MiniLIS (se detendrá durante la actualización).' }
            else { Add-Result 'ERROR' "Puerto $Port" "En uso por otro programa ($pname). Elija otro puerto o libere este." }
        }
    } catch { Add-Result 'AVISO' "Puerto $Port" 'No se pudo comprobar.' }

    # Cifrado del volumen de datos (README, N-6): los datos reales de paciente no deben
    # residir en un volumen sin cifrar. Es un aviso, no un bloqueo: una instalación de
    # pruebas no lo necesita.
    $dataDrive = ([System.IO.Path]::GetPathRoot($DataDir)).TrimEnd('\')
    try {
        $bl = Get-BitLockerVolume -MountPoint $dataDrive -ErrorAction Stop
        if ($bl.ProtectionStatus -eq 'On') { Add-Result 'OK' 'Cifrado de los datos' "El volumen $dataDrive está cifrado con BitLocker." }
        else { Add-Result 'AVISO' 'Cifrado de los datos' "El volumen $dataDrive NO está cifrado. Antes de tratar datos reales de pacientes, cífrelo (BitLocker) o use otro volumen cifrado para los datos." }
    } catch {
        Add-Result 'AVISO' 'Cifrado de los datos' "No se pudo comprobar si $dataDrive está cifrado. Antes de tratar datos reales de pacientes, confirme que el volumen de datos está cifrado."
    }

    if ($Mode -eq 'Servidor') {
        $hosts = @(Get-HostList)
        Add-Result 'INFO' 'Acceso' ('Los usuarios entrarán con {0}' -f (Get-AppUrl))
        foreach ($h in $hosts) {
            if ($h -eq 'localhost') { continue }
            try { [System.Net.Dns]::GetHostAddresses($h) | Out-Null; Add-Result 'OK' "Nombre $h" 'Se resuelve en la red.' }
            catch { Add-Result 'AVISO' "Nombre $h" 'No se resuelve desde este equipo. Pida al Servicio de Informática que lo dé de alta en el DNS.' }
        }
    } else {
        Add-Result 'INFO' 'Acceso' ('Solo desde este equipo: {0}' -f (Get-AppUrl))
    }
}

# ── Certificado ──────────────────────────────────────────────────────────────────────

function Test-Pfx([string]$Path, [string]$Password) {
    if (-not $Path -or -not (Test-Path $Path)) { Add-Result 'ERROR' 'Certificado' "No se encuentra el fichero $Path"; return $null }
    try {
        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Path, $Password, $flags)
    } catch {
        Add-Result 'ERROR' 'Certificado' 'No se puede abrir: la contraseña no es correcta o el fichero no es un PFX válido.'
        return $null
    }
    if (-not $cert.HasPrivateKey) { Add-Result 'ERROR' 'Certificado' 'El fichero no incluye la clave privada.' }
    $now = Get-Date
    if ($cert.NotAfter -lt $now) { Add-Result 'ERROR' 'Certificado' ('Caducado el {0:dd/MM/yyyy}.' -f $cert.NotAfter) }
    elseif ($cert.NotAfter -lt $now.AddDays(30)) { Add-Result 'AVISO' 'Certificado' ('Caduca pronto: {0:dd/MM/yyyy}.' -f $cert.NotAfter) }
    else { Add-Result 'OK' 'Certificado' ('{0}, válido hasta {1:dd/MM/yyyy}' -f $cert.Subject, $cert.NotAfter) }

    $eku = $cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }
    if ($eku) {
        $usages = @($eku.EnhancedKeyUsages | ForEach-Object { $_.Value })
        if ($usages -notcontains '1.3.6.1.5.5.7.3.1') { Add-Result 'ERROR' 'Certificado' 'No está emitido para autenticación de servidor (TLS).' }
    }
    # Nombres cubiertos por el certificado frente a los de acceso
    $dns = @()
    $san = $cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' }
    if ($san) { $dns = @(($san.Format($false) -split ',\s*') | Where-Object { $_ -like 'DNS*' } | ForEach-Object { ($_ -split '[=:]', 2)[1].Trim().ToLowerInvariant() }) }
    foreach ($h in @(Get-HostList)) {
        if ($h -eq 'localhost') { continue }
        $covered = $false
        foreach ($d in $dns) { if ($d -eq $h -or ($d.StartsWith('*.') -and $h.EndsWith($d.Substring(1)))) { $covered = $true } }
        if (-not $covered) { Add-Result 'AVISO' 'Certificado' "No cubre el nombre ${h}: el navegador mostrará un aviso al entrar con ese nombre." }
    }
    return $cert
}

function Install-Certificate([hashtable]$Secrets, [string[]]$Hosts) {
    if ($CertPfxPath) {
        $probe = Test-Pfx $CertPfxPath $Secrets.certPassword
        if ($script:HasErrors -or -not $probe) { throw 'El certificado PFX no es válido (ver detalles).' }
        $secure = ConvertTo-SecureString -String $Secrets.certPassword -AsPlainText -Force
        $cert = Import-PfxCertificate -FilePath $CertPfxPath -CertStoreLocation Cert:\LocalMachine\My -Password $secure
        $cert = @($cert)[0]
        Write-Log "Certificado importado: $($cert.Subject) ($($cert.Thumbprint))"
        return @{ Cert = $cert; SelfSigned = $false }
    }

    # Autofirmado: válido para un puesto único y para pruebas. En un servidor, los demás
    # equipos mostrarán un aviso hasta que se sustituya por uno del hospital.
    $subject = 'CN=MiniLIS Suite ' + $env:COMPUTERNAME
    $cert = New-SelfSignedCertificate -Subject $subject -DnsName $Hosts `
        -CertStoreLocation Cert:\LocalMachine\My -KeyExportPolicy NonExportable `
        -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(5) -FriendlyName 'MiniLIS Suite (autofirmado)' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.1')
    # De confianza en ESTE equipo, para que el navegador local no avise.
    $cer = Join-Path $env:TEMP ('minilis-{0}.cer' -f $cert.Thumbprint)
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Remove-Item $cer -Force -ErrorAction SilentlyContinue
    Write-Log "Certificado autofirmado creado: $subject ($($cert.Thumbprint))"
    return @{ Cert = $cert; SelfSigned = $true }
}

# La cuenta del servicio solo puede usar el certificado si puede leer su clave privada.
function Grant-PrivateKeyAccess($Cert) {
    $keyPath = $null
    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Cert)
    if ($rsa -is [System.Security.Cryptography.RSACng]) {
        $keyPath = Join-Path $env:ProgramData ('Microsoft\Crypto\Keys\' + $rsa.Key.UniqueName)
    } elseif ($rsa -and $rsa.PSObject.Properties['CspKeyContainerInfo']) {
        $keyPath = Join-Path $env:ProgramData ('Microsoft\Crypto\RSA\MachineKeys\' + $rsa.CspKeyContainerInfo.UniqueKeyContainerName)
    } else {
        $ec = [System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($Cert)
        if ($ec -is [System.Security.Cryptography.ECDsaCng]) { $keyPath = Join-Path $env:ProgramData ('Microsoft\Crypto\Keys\' + $ec.Key.UniqueName) }
    }
    if (-not $keyPath -or -not (Test-Path $keyPath)) { throw 'No se encontró la clave privada del certificado para dar acceso al servicio.' }
    & icacls.exe $keyPath /grant "${ServiceAccount}:R" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudo dar acceso a la clave privada ($keyPath)." }
}

# ── Servicio ─────────────────────────────────────────────────────────────────────────

function Wait-ServiceState([string]$State, [int]$Seconds) {
    $limit = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $limit) {
        $s = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($s -and $s.Status -eq $State) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

# El puerto se abre DESPUÉS de migrar la base de datos y crear el administrador (Program.cs),
# así que "escuchando" significa "listo".
function Wait-Listening([int]$Seconds) {
    $limit = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $limit) {
        $client = New-Object System.Net.Sockets.TcpClient
        try { $client.Connect('127.0.0.1', $Port); return $true } catch { } finally { $client.Close() }
        $s = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($s -and $s.Status -eq 'Stopped') { return $false }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Get-RecentServiceErrors([datetime]$Since) {
    try {
        return @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = $EventSource; Level = 1, 2; StartTime = $Since } -ErrorAction Stop |
            Select-Object -First 5 | ForEach-Object { ($_.Message -split "`n")[0] })
    } catch { return @() }
}

function Start-MiniLIS {
    $since = (Get-Date).AddSeconds(-2)
    Start-Service -Name $ServiceName
    if (Wait-Listening 180) {
        Add-Result 'OK' 'Servicio' "MiniLIS está en marcha y responde en el puerto $Port."
        foreach ($e in (Get-RecentServiceErrors $since)) { Add-Result 'AVISO' 'Visor de eventos' $e }
        return $true
    }
    Add-Result 'ERROR' 'Servicio' 'MiniLIS no ha arrancado. Revise el Visor de eventos (Registro de Windows > Aplicación, origen MiniLIS).'
    foreach ($e in (Get-RecentServiceErrors $since)) { Add-Result 'ERROR' 'Visor de eventos' $e }
    return $false
}

function Set-DirectoryAcl([string]$Path, [string]$ServiceRights) {
    # Solo administradores, SYSTEM y la cuenta del servicio: la carpeta de datos contiene la
    # base de datos con datos de salud, y la configuración, la clave de las copias.
    & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' "${ServiceAccount}:(OI)(CI)$ServiceRights" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudieron fijar los permisos de $Path" }
}

function New-BackupKey {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes); $rng.Dispose()
    return [Convert]::ToBase64String($bytes)
}

function Write-Settings([hashtable]$Cert, [string[]]$Hosts, [string]$AdminPassword) {
    $settingsPath = Get-SettingsPath
    $existingKey = $null
    if (Test-Path $settingsPath) {
        # Reinstalación sobre datos existentes: se conserva la clave de las copias. Si se
        # cambiara, las copias ya hechas no se podrían restaurar.
        try { $existingKey = (Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json).Backup.EncryptionKey } catch { }
    }
    $backupKey = $existingKey
    if (-not $backupKey) { $backupKey = New-BackupKey }

    $url = "https://*:$Port"
    if ($Mode -eq 'Puesto') { $url = "https://localhost:$Port" }
    $simpleName = $Cert.Cert.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)

    $settings = [ordered]@{
        ConnectionStrings = [ordered]@{ DefaultConnection = 'Data Source=' + (Join-Path $DataDir 'db\minilis.db') }
        AllowedHosts      = ($Hosts -join ';')
        Backup            = [ordered]@{ EncryptionKey = $backupKey }
        ConfigTransfer    = [ordered]@{ BackupDirectory = (Join-Path $DataDir 'config-backups') }
        DataProtection    = [ordered]@{ KeysDirectory = (Join-Path $DataDir 'keys') }
        Kestrel           = [ordered]@{
            Endpoints = [ordered]@{
                Https = [ordered]@{
                    Url         = $url
                    Certificate = [ordered]@{ Subject = $simpleName; Store = 'My'; Location = 'LocalMachine'; AllowInvalid = [bool]$Cert.SelfSigned }
                }
            }
        }
        Logging           = [ordered]@{ EventLog = [ordered]@{ LogLevel = [ordered]@{ Default = 'Warning'; 'Microsoft.Hosting.Lifetime' = 'Information' } } }
        MiniLIS           = [ordered]@{ Mode = $Mode; InstalledAtUtc = (Get-Date).ToUniversalTime().ToString('o') }
    }
    if ($AdminPassword) { $settings['Seed'] = [ordered]@{ AdminUser = $AdminUser; AdminPassword = $AdminPassword } }

    $settings | ConvertTo-Json -Depth 8 | Out-File -FilePath $settingsPath -Encoding utf8
    return @{ Path = $settingsPath; KeyIsNew = (-not $existingKey); BackupKey = $backupKey }
}

# La contraseña inicial solo hace falta hasta que se crea el administrador: se quita del
# fichero en cuanto el servicio ha arrancado.
function Remove-SeedPassword {
    $settingsPath = Get-SettingsPath
    $json = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($json.PSObject.Properties['Seed']) {
        $json.PSObject.Properties.Remove('Seed')
        $json | ConvertTo-Json -Depth 8 | Out-File -FilePath $settingsPath -Encoding utf8
    }
}

function Register-Service([string]$SettingsPath) {
    $exe = Join-Path $InstallDir $ExeName
    if (-not (Test-Path $exe)) { throw "No se encuentra $exe" }

    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Wait-ServiceState 'Stopped' 60 | Out-Null
        & sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    & sc.exe create $ServiceName binPath= "`"$exe`"" start= delayed-auto obj= $ServiceAccount DisplayName= 'MiniLIS Suite' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el servicio de Windows.' }
    & sc.exe description $ServiceName 'MiniLIS Suite - LIS de citometría de flujo (servidor web).' | Out-Null
    # Si se cae, se reinicia solo: al minuto, al minuto y a los 5 minutos.
    & sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

    $svcKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    New-ItemProperty -Path $svcKey -Name 'Environment' -PropertyType MultiString -Force `
        -Value @('ASPNETCORE_ENVIRONMENT=Production', "MINILIS_SETTINGS=$SettingsPath") | Out-Null

    if (-not [System.Diagnostics.EventLog]::SourceExists($EventSource)) {
        New-EventLog -LogName Application -Source $EventSource
    }
    Write-Log 'Servicio de Windows registrado (cuenta NT SERVICE\MiniLIS, inicio automático retrasado).'
}

function Set-Firewall {
    Remove-NetFirewallRule -Name $FirewallRule -ErrorAction SilentlyContinue
    if ($Mode -eq 'Servidor') {
        New-NetFirewallRule -Name $FirewallRule -DisplayName "MiniLIS Suite (HTTPS $Port)" -Direction Inbound `
            -Protocol TCP -LocalPort $Port -Action Allow -Profile Domain, Private | Out-Null
        Add-Result 'OK' 'Cortafuegos' "Abierto el puerto $Port para las redes de dominio y privadas."
    }
}

function Save-Registry([string]$Url) {
    if (-not (Test-Path $RegistryKey)) { New-Item -Path $RegistryKey -Force | Out-Null }
    Set-ItemProperty -Path $RegistryKey -Name InstallDir -Value $InstallDir
    Set-ItemProperty -Path $RegistryKey -Name DataDir -Value $DataDir
    Set-ItemProperty -Path $RegistryKey -Name Mode -Value $Mode
    Set-ItemProperty -Path $RegistryKey -Name Port -Value ([string]$Port)
    Set-ItemProperty -Path $RegistryKey -Name Url -Value $Url
    if ($AppVersion) { Set-ItemProperty -Path $RegistryKey -Name Version -Value $AppVersion }
}

function Read-Registry {
    if (Test-Path $RegistryKey) {
        $r = Get-ItemProperty -Path $RegistryKey
        if ($r.PSObject.Properties['DataDir']) { $script:DataDir = $r.DataDir }
        if ($r.PSObject.Properties['Port']) { $script:Port = [int]$r.Port }
        if ($r.PSObject.Properties['Mode']) { $script:Mode = $r.Mode }
    }
}

# ── Acciones ─────────────────────────────────────────────────────────────────────────

function Invoke-Configure {
    if (-not (Test-IsAdmin)) { throw 'Hay que ejecutarlo como administrador.' }
    $secrets = Read-Secrets
    $hosts = [string[]]@(Get-HostList)

    foreach ($sub in @('', 'db', 'backups', 'config-backups', 'keys', 'logs')) {
        $p = Join-Path $DataDir $sub
        if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p -Force | Out-Null }
    }
    Write-Log "Instalación de MiniLIS $AppVersion — modo $Mode, puerto $Port, datos en $DataDir"

    $dbExists = Test-Path (Join-Path $DataDir 'db\minilis.db')
    if ($dbExists) { Add-Result 'INFO' 'Base de datos' 'Ya existía una base de datos en la carpeta de datos: se conserva.' }

    $cert = Install-Certificate $secrets $hosts
    if ($cert.SelfSigned) {
        if ($Mode -eq 'Servidor') { Add-Result 'AVISO' 'Certificado' 'Se ha generado un certificado autofirmado. Los demás equipos mostrarán un aviso de seguridad hasta que se instale uno del hospital (ver docs/INSTALACION.md).' }
        else { Add-Result 'OK' 'Certificado' 'Certificado autofirmado creado y marcado como de confianza en este equipo.' }
    }

    $adminPassword = $secrets.adminPassword
    if ($dbExists) { $adminPassword = $null }  # el administrador ya existe
    $settings = Write-Settings $cert $hosts $adminPassword

    Register-Service $settings.Path
    Set-DirectoryAcl $DataDir 'M'
    & icacls.exe $InstallDir /grant "${ServiceAccount}:(OI)(CI)RX" | Out-Null
    Grant-PrivateKeyAccess $cert.Cert
    Set-Firewall

    $url = Get-AppUrl
    Save-Registry $url
    $ok = Start-MiniLIS
    if ($adminPassword) { Remove-SeedPassword }

    if ($ok) {
        Add-Result 'INFO' 'Dirección' $url
        if ($adminPassword) { Add-Result 'INFO' 'Usuario administrador' "$AdminUser (se le pedirá cambiar la contraseña en el primer acceso)" }
        Add-Result 'INFO' 'Carpeta de datos' $DataDir
        if ($settings.KeyIsNew) {
            Add-Result 'AVISO' 'Clave de las copias de seguridad' ('Guárdela en un lugar seguro, fuera de este equipo (sin ella no se pueden restaurar las copias): ' + $settings.BackupKey)
        }
    }
    return $ok
}

function Invoke-PrepareUpgrade {
    Read-Registry
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        if (-not (Wait-ServiceState 'Stopped' 90)) { throw 'No se pudo detener el servicio MiniLIS.' }
    }
    Add-Result 'OK' 'Servicio' 'Detenido para actualizar.'

    # Copia de la base de datos con el servicio parado (fichero consistente, incluidos los
    # ficheros -wal/-shm de SQLite si existen). Si la actualización sale mal, se restaura
    # copiando estos ficheros de vuelta.
    $db = Get-DatabasePath
    if (Test-Path $db) {
        $old = Get-InstalledVersion; if (-not $old) { $old = 'anterior' }
        $dest = Join-Path $DataDir ('backups\antes-de-actualizar-{0:yyyyMMdd-HHmmss}-v{1}' -f (Get-Date), $old)
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        foreach ($f in @($db, "$db-wal", "$db-shm")) { if (Test-Path $f) { Copy-Item -Path $f -Destination $dest -Force } }
        Add-Result 'OK' 'Copia de la base de datos' $dest
    } else {
        Add-Result 'AVISO' 'Copia de la base de datos' "No se encontró la base de datos en $db"
    }
}

function Invoke-CompleteUpgrade {
    Read-Registry
    & icacls.exe $InstallDir /grant "${ServiceAccount}:(OI)(CI)RX" | Out-Null
    $url = Get-AppUrl
    try { $url = (Get-ItemProperty -Path $RegistryKey -Name Url).Url } catch { }
    Save-Registry $url
    $ok = Start-MiniLIS
    if ($ok) { Add-Result 'INFO' 'Dirección' $url }
    return $ok
}

function Invoke-Uninstall {
    Read-Registry
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Wait-ServiceState 'Stopped' 60 | Out-Null
        & sc.exe delete $ServiceName | Out-Null
    }
    Remove-NetFirewallRule -Name $FirewallRule -ErrorAction SilentlyContinue
    Remove-Item -Path $RegistryKey -Recurse -Force -ErrorAction SilentlyContinue
    # Los datos NO se borran: son registros asistenciales con plazo de conservación.
    Write-Log "Servicio eliminado. Los datos se conservan en $DataDir."
}

# ── Punto de entrada ─────────────────────────────────────────────────────────────────

$exitCode = 0
try {
    switch ($Action) {
        'Check' { Invoke-Check }
        'TestCertificate' { $s = Read-Secrets; Test-Pfx $CertPfxPath $s.certPassword | Out-Null }
        'Configure' { if (-not (Invoke-Configure)) { $exitCode = 3 } }
        'PrepareUpgrade' { Invoke-PrepareUpgrade }
        'CompleteUpgrade' { if (-not (Invoke-CompleteUpgrade)) { $exitCode = 3 } }
        'Uninstall' { Invoke-Uninstall }
    }
    if ($script:HasErrors -and $exitCode -eq 0) { $exitCode = 2 }
} catch {
    Add-Result 'ERROR' 'Instalación' $_.Exception.Message
    $exitCode = 1
} finally {
    Save-Report
}
exit $exitCode
