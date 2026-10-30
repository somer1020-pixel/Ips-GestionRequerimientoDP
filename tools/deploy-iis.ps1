<#
.SYNOPSIS
  Publica GestionRequerimientoDP en IIS (sitio propio o sub-aplicación) con SSO de Windows.

.DESCRIPTION
  - Compila y publica la app (dotnet publish) en la carpeta física.
  - Crea el App Pool (sin código administrado) y el sitio o la sub-aplicación.
  - Habilita autenticación Anónima + Windows (la app pide la cuenta Windows solo en /cuenta/sso).
  - Escribe appsettings.Production.json con la cadena de conexión, el secreto del ticket SSO y la carpeta de claves.
  - Da permisos a la identidad del App Pool sobre las carpetas de claves y logs.

  Requisitos en el servidor: IIS con "Autenticación de Windows" y "Protocolo WebSocket",
  ASP.NET Core Hosting Bundle para .NET 10, módulo WebAdministration y el SDK de .NET 10 si se publica aquí mismo.

.EXAMPLE
  # Sub-aplicación https://servidor/gestionreq dentro de "Default Web Site"
  .\tools\deploy-iis.ps1 -SiteName "Default Web Site" -AppPath "/gestionreq" -PhysicalPath "D:\inetpub\gestionreq" `
      -ConnectionString "Server=SQLSERVER;Database=GestionRequerimientoDP;Trusted_Connection=True;TrustServerCertificate=True" `
      -EmailDomain "empresa.com" -Dominio "EMPRESA" -Administradores "usuario.admin"

.EXAMPLE
  # Sitio propio en el puerto 8443 (enlace https creado aparte con su certificado)
  .\tools\deploy-iis.ps1 -SiteName "GestionRequerimientoDP" -Port 8080 -PhysicalPath "D:\inetpub\gestionreq" -ConnectionString "..."
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SiteName,
    [string] $AppPath = "",
    [Parameter(Mandatory)] [string] $PhysicalPath,
    [string] $AppPoolName = "GestionRequerimientoDP",
    [int] $Port = 80,
    [Parameter(Mandatory)] [string] $ConnectionString,
    [string] $TicketSecret = "",
    [string] $EmailDomain = "",
    [string] $Dominio = "",
    [string[]] $Administradores = @(),
    [string] $KeysPath = "",
    [switch] $SinSso,
    [switch] $SinPublicar
)

$ErrorActionPreference = "Stop"
Import-Module WebAdministration

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\GestionRequerimientoDP.Web\GestionRequerimientoDP.Web.csproj"
if (-not $KeysPath) { $KeysPath = Join-Path (Split-Path -Parent $PhysicalPath) "gestionreq-keys" }
if (-not $TicketSecret) {
    # Secreto aleatorio de 48 bytes. Si hay host interno y público separados, usa el MISMO valor en ambos.
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $TicketSecret = [Convert]::ToBase64String($bytes)
}

# 1. Publicar
$poolExists = Test-Path "IIS:\AppPools\$AppPoolName"
if ($poolExists) { Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2 }
if (-not $SinPublicar) {
    Write-Host "Publicando en $PhysicalPath…" -ForegroundColor Cyan
    dotnet publish $project -c Release -o $PhysicalPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló" }
}

# 2. Configuración de producción (fuera del repositorio)
$settings = [ordered]@{
    ConnectionStrings = @{ GestionRequerimientoDP = $ConnectionString }
    Auth              = @{ DemoMode = $false }
    Sso               = [ordered]@{ Enabled = (-not $SinSso); TicketSecret = $TicketSecret; Administradores = $Administradores }
    ActiveDirectory   = [ordered]@{ Enabled = (-not $SinSso); Dominio = $Dominio }
    DataProtection    = [ordered]@{ KeysPath = $KeysPath; ProtegerConDpapi = $true }
    Database          = @{ MigrateOnStartup = $true; Seed = $false }
}
if ($EmailDomain) { $settings.Sso.EmailDomain = $EmailDomain }
$settings | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $PhysicalPath "appsettings.Production.json")

# 3. App Pool sin código administrado
if (-not $poolExists) { New-WebAppPool -Name $AppPoolName | Out-Null }
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value "AlwaysRunning"
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)

# 4. Sitio o sub-aplicación
if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -Port $Port -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName | Out-Null
    $location = $SiteName
} elseif ($AppPath) {
    $name = $AppPath.Trim('/')
    if (-not (Get-WebApplication -Site $SiteName -Name $name)) {
        New-WebApplication -Site $SiteName -Name $name -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName | Out-Null
    } else {
        Set-ItemProperty "IIS:\Sites\$SiteName\$name" -Name physicalPath -Value $PhysicalPath
        Set-ItemProperty "IIS:\Sites\$SiteName\$name" -Name applicationPool -Value $AppPoolName
    }
    $location = "$SiteName/$name"
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PhysicalPath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    $location = $SiteName
}

# 5. Autenticación: Anónima (pantallas públicas) + Windows (SSO en /cuenta/sso)
$auth = "system.webServer/security/authentication"
Set-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Location $location -Filter "$auth/anonymousAuthentication" -Name enabled -Value $true
Set-WebConfigurationProperty -PSPath "MACHINE/WEBROOT/APPHOST" -Location $location -Filter "$auth/windowsAuthentication" -Name enabled -Value (-not $SinSso)
if (-not $SinSso) {
    # Kerberos primero, NTLM como respaldo (equipos fuera del SPN o accesos por IP).
    Clear-WebConfiguration -PSPath "MACHINE/WEBROOT/APPHOST" -Location $location -Filter "$auth/windowsAuthentication/providers"
    Add-WebConfiguration -PSPath "MACHINE/WEBROOT/APPHOST" -Location $location -Filter "$auth/windowsAuthentication/providers" -Value @{ value = "Negotiate" }
    Add-WebConfiguration -PSPath "MACHINE/WEBROOT/APPHOST" -Location $location -Filter "$auth/windowsAuthentication/providers" -Value @{ value = "NTLM" }
}

# 6. Permisos de la identidad del App Pool
$identity = "IIS AppPool\$AppPoolName"
foreach ($dir in @($KeysPath, (Join-Path $PhysicalPath "logs"))) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    icacls $dir /grant "${identity}:(OI)(CI)M" /T | Out-Null
}
icacls (Join-Path $PhysicalPath "appsettings.Production.json") /inheritance:r /grant "${identity}:R" /grant "Administrators:F" /grant "SYSTEM:F" | Out-Null

Start-WebAppPool -Name $AppPoolName
Write-Host ""
Write-Host "Listo: IIS:\Sites\$location (App Pool $AppPoolName)" -ForegroundColor Green
Write-Host "  Claves DataProtection: $KeysPath"
if (-not $SinSso) {
    Write-Host "  SSO activo. Prueba con: .\tools\test-sso.ps1 -BaseUrl http://localhost$AppPath"
    Write-Host "  Si hay host interno y público separados, configura Sso:InternalHost / Sso:PublicHost y el mismo TicketSecret en ambos."
}
