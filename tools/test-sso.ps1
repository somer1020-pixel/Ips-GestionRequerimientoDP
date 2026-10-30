<#
.SYNOPSIS
  Verifica el SSO con la cuenta Windows del usuario actual contra una instalación de GestionRequerimientoDP.

.EXAMPLE
  .\tools\test-sso.ps1 -BaseUrl https://servidor/gestionreq
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $BaseUrl)

$ErrorActionPreference = "Stop"
$base = $BaseUrl.TrimEnd('/')
Write-Host "Cuenta Windows actual: $env:USERDOMAIN\$env:USERNAME"

# 1. Sin credenciales: /cuenta/sso debe pedir Negotiate (401 + WWW-Authenticate).
try {
    Invoke-WebRequest "$base/cuenta/sso" -MaximumRedirection 0 -UseBasicParsing | Out-Null
    Write-Host "[!] /cuenta/sso no pidió credenciales: ¿Sso:Enabled y Autenticación de Windows en IIS?" -ForegroundColor Yellow
} catch {
    $r = $_.Exception.Response
    if ($r -and [int]$r.StatusCode -eq 401) {
        Write-Host "[ok] 401 con WWW-Authenticate: $($r.Headers['WWW-Authenticate'])" -ForegroundColor Green
    } else { Write-Host "[x] Respuesta inesperada: $($_.Exception.Message)" -ForegroundColor Red }
}

# 2. Con la cuenta Windows: debe redirigir y crear la cookie de sesión.
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try {
    $resp = Invoke-WebRequest "$base/cuenta/sso" -UseDefaultCredentials -MaximumRedirection 0 -WebSession $session -UseBasicParsing -ErrorAction SilentlyContinue
} catch { $resp = $_.Exception.Response }
$location = $resp.Headers["Location"]
$cookie = $session.Cookies.GetCookies($base) | Where-Object Name -eq "grdp.session"
Write-Host "Redirección: $location"
if ($cookie) {
    Write-Host "[ok] Sesión creada con la cuenta Windows." -ForegroundColor Green
} elseif ($location -match "ok=") {
    Write-Host "[ok] La cuenta no estaba registrada: quedó una solicitud de acceso para los líderes." -ForegroundColor Yellow
} elseif ($location -match "error=") {
    Write-Host "[x] $([Uri]::UnescapeDataString(($location -split 'error=')[1]))" -ForegroundColor Red
} else {
    Write-Host "[x] No se creó la sesión. Revisa el registro de la app y la auditoría de accesos." -ForegroundColor Red
}
