# GestionRequerimientoDP

Aplicación web para administrar la carga laboral de Operaciones en tres unidades: **Procesamiento de Datos (DP)** y **Codificación** (planificación semanal) y **Scripting** (demanda ad-hoc). Funciona como un tablero Kanban con control de acceso por rol y un módulo de solicitudes para las áreas comerciales.

Implementa el diseño de Claude Design (`docs/handoff/Carga Ops.dc.html`) y la especificación `docs/handoff/README-handoff.md`.

## Stack

| Capa | Tecnología |
| --- | --- |
| Runtime | .NET 10 (LTS) · C# 14 |
| UI | Blazor Web App, render **Interactive Server** (SignalR) |
| Estilos | Bootstrap 5.3 + `wwwroot/css/site.css` (guía corporativa) + `wwwroot/css/app.css` (tema del mockup) |
| Datos | EF Core 10 + SQL Server 2025, migraciones en el repo |
| Excel | ClosedXML (importar y descargar `.xlsx`) |
| Correo | MailKit (SMTP) detrás de `IEmailSender`, con cola en la tabla `Notificaciones` |
| Auth | Pantalla de ingreso propia (cookie) con accesos de demostración, o Windows Authentication (Negotiate + AD) por configuración |
| Pruebas | xUnit (reglas y servicios) + bUnit (componentes) |

## Estructura

```
GestionRequerimientoDP.slnx
├─ src/
│  ├─ GestionRequerimientoDP.Domain/          Entidades, enums, catálogos y reglas puras (fechas, íconos de vencimiento, atrasos)
│  ├─ GestionRequerimientoDP.Application/     Motores (ranking de scripts, balanceo, importación, KPIs, correos, resumen) y servicios con permisos
│  ├─ GestionRequerimientoDP.Infrastructure/  EF Core (DbContext, migraciones, datos semilla), ClosedXML, correo, tareas programadas
│  └─ GestionRequerimientoDP.Web/             Blazor (páginas, diálogos, layout), autenticación y descargas
├─ tests/
│  ├─ GestionRequerimientoDP.Application.Tests/  xUnit
│  └─ GestionRequerimientoDP.Web.Tests/          bUnit
└─ docs/handoff/                              Mockup y especificación de Claude Design
```

## Ejecutar en Visual Studio 2026 (Windows)

1. Git → Clonar repositorio → abrir `GestionRequerimientoDP.slnx`.
2. Configurar la cadena de conexión con user-secrets. La cadena **no** va en `appsettings.json`:
   ```powershell
   cd src\GestionRequerimientoDP.Web
   dotnet user-secrets set "ConnectionStrings:GestionRequerimientoDP" "Server=localhost;Database=GestionRequerimientoDP;Trusted_Connection=True;TrustServerCertificate=True"
   ```
3. F5 con el perfil `http` o `https`. En **Development** la app aplica las migraciones y carga los datos semilla al iniciar. Para hacerlo a mano: `Update-Database` (Package Manager Console, proyecto Infrastructure).

## Acceso y cuentas

- **Ingreso con correo o RUT** y contraseña. Con una clave incorrecta el mensaje es siempre el mismo: no revela si la cuenta existe, está pendiente o desactivada.
- **Política de contraseñas:** 8 a 100 caracteres, con mayúscula, minúscula, número y símbolo, y sin el nombre ni la parte inicial del correo. Se aplica al crear usuarios, al solicitar acceso y al restablecer.
- **Solicitar acceso** (`/cuenta/registro`, se desactiva con `Auth:RegistroHabilitado = false`): la persona deja nombre, correo, RUT opcional, motivo y contraseña. La cuenta queda **pendiente** e inactiva, y los líderes reciben un correo. En **Usuarios → Solicitudes de acceso** el líder asigna rol y equipos y la aprueba, o la rechaza. En ambos casos se avisa por correo.
- **Recuperar contraseña** (`/cuenta/recuperar`): envía un enlace de un solo uso que vence en 60 minutos. Solo se guarda el hash SHA-256 del token, vale solo el último enlace y hay un máximo de 3 por hora. La respuesta es la misma exista o no la cuenta. Las cuentas que entran solo con Windows se derivan a TI. En Development sin SMTP, el enlace se muestra en pantalla para poder probar.
- **Protección contra fuerza bruta:**
  - 5 intentos fallidos para un mismo correo o RUT, en cualquier formato, bloquean el ingreso con contraseña de esa cuenta por 15 minutos. Queda `LOGIN_BLOQUEADO_INTENTOS` en la auditoría.
  - Además hay un límite de envíos de formularios de cuenta por IP (`Seguridad:FormulariosPorMinutoPorIp`, por defecto 30).
  - Una cuenta inexistente tarda lo mismo en responder que una real, así que el tiempo no revela qué correos existen.
- **Modo demostración:** los accesos sin contraseña (`Auth:DemoMode`) solo funcionan en Development. En el servidor se ignoran aunque estén activados por error.
- **Auditoría de accesos** (**Usuarios → Auditoría de accesos**, solo líderes): ingresos, fallos, bloqueos, salidas, SSO, solicitudes, aprobaciones, recuperaciones y cambios de usuarios, con IP y detalle.
- **Búsqueda en Active Directory** al crear un usuario (servidor Windows con `ActiveDirectory:Enabled = true`): busca por nombre, usuario o correo y completa nombre, correo y cuenta de dominio. Usa la identidad del App Pool o una cuenta de servicio (`ActiveDirectory:Usuario` / `Clave`, en variables de entorno).

## Single Sign-On con Active Directory

Adaptado del proyecto Ipsos.Bhe.Auth. Los colaboradores en la red interna o la VPN ingresan con su cuenta de Windows, sin escribir contraseña. El ingreso con correo y contraseña sigue disponible.

- **Quién entra:** usuarios registrados, aprobados y activos en **Usuarios**. La cuenta `DOMINIO\usuario` se busca primero en "Cuenta de dominio" y luego por el correo `usuario@<Sso:EmailDomain>`.
- **Cuenta corporativa sin usuario** (`Sso:SolicitudAutomatica`, activo por defecto): se crea una solicitud de acceso pendiente con el nombre y el correo de Active Directory, y se avisa a los líderes una sola vez. La persona ve "Registramos tu solicitud…".
- **Administradores** (`Sso:Administradores`): cuentas de Windows o correos que entran como líder sin aprobación la primera vez. Sirve para arrancar con una base vacía. Configúralo en el servidor, no en el repositorio. Si después un líder desactiva esa cuenta, sigue desactivada.
- **Intento automático:** desde una IP interna (`Sso:InternalIpPrefixes`), la pantalla de ingreso va sola a `/cuenta/sso`. En ventanas InPrivate o incógnito, o si no hay respuesta en 3,5 s, muestra el formulario. `?manual=1` o `?external=1` fuerzan el formulario. Al salir se vuelve al ingreso manual para poder cambiar de cuenta.
- **Un solo servidor** (`Sso:InternalHost` vacío): la misma app negocia Kerberos/NTLM y abre la sesión.
- **Dos servidores** (sitio público detrás de un proxy + IIS interno con Windows Authentication): el host interno emite un ticket HMAC-SHA256 de un solo uso (60 s) hacia `Sso:PublicHost/cuenta/sso/consumir`. Ambos hosts deben compartir `Sso:TicketSecret` (mínimo 32 caracteres) y `DataProtection:ApplicationName`. El formato del ticket es compatible con Ipsos.Bhe.Auth.

Configuración (`appsettings.json` → `Sso`; el secreto, en user-secrets o variables de entorno):

```json
"Sso": { "Enabled": true, "EmailDomain": "ipsos.com", "InternalHost": "", "PublicHost": "", "SolicitudAutomatica": true, "Administradores": [] }
```

```powershell
dotnet user-secrets set "Sso:TicketSecret" "<clave aleatoria de 32+ caracteres>"   # solo en modo de dos servidores
```

**En Visual Studio (Kestrel en Windows):** con `Sso:Enabled = true`, el perfil `http` negocia tu sesión de Windows de inmediato. Prueba entrando con el botón "Ingresar con mi cuenta corporativa".

## Publicar en IIS

`tools/deploy-iis.ps1` (PowerShell como administrador, en el servidor) hace lo siguiente:

- Publica la app.
- Crea el App Pool (sin código administrado, siempre activo).
- Crea el sitio o la sub-aplicación (`-AppPath /gestionreq`).
- Activa la autenticación Anónima + Windows (Negotiate y luego NTLM).
- Escribe `appsettings.Production.json` con la cadena de conexión, un `TicketSecret` aleatorio y la carpeta de claves, legible solo por el App Pool.
- Da permisos sobre las claves y los logs.

```powershell
.\tools\deploy-iis.ps1 -SiteName "Default Web Site" -AppPath "/gestionreq" -PhysicalPath "D:\inetpub\gestionreq" `
    -ConnectionString "Server=SQLSERVER;Database=GestionRequerimientoDP;Trusted_Connection=True;TrustServerCertificate=True" `
    -EmailDomain "ipsos.com" -Dominio "DOMINIO" -Administradores "usuario.admin"
.\tools\test-sso.ps1 -BaseUrl http://localhost/gestionreq
```

Requisitos del servidor:

- ASP.NET Core Hosting Bundle para .NET 10.
- Características de IIS "Autenticación de Windows" y "Protocolo WebSocket" (Blazor Server usa WebSockets).
- Si el App Pool usa una identidad de dominio, el SPN `HTTP/<host>` debe estar registrado para que Kerberos funcione. Si no, se usa NTLM.

Además:

- **Sub-ruta:** como sub-aplicación de IIS, la app recibe la sub-ruta sola. Todos los enlaces son relativos a `<base href>`, así que funciona en `/gestionreq` sin cambios.
- **Claves de sesión (`DataProtection`):** `KeysPath` persiste las claves que cifran la cookie y los formularios. Sin esto, cada reciclaje del App Pool cierra todas las sesiones. Con `ProtegerConDpapi` las claves quedan cifradas con DPAPI de la máquina.
- **Zona Intranet:** los navegadores envían la cuenta automáticamente solo a sitios de la zona Intranet local. Si hace falta, agrega el host por GPO.
- `web.config` incluye:
  - `forwardWindowsAuthToken`.
  - `existingResponse="PassThrough"`, para la página de vuelta del 401 de `/cuenta/sso`.
  - WebSockets.
  - Cabeceras de seguridad.

**Detrás de un proxy inverso (Apache / Nginx / Cloudflare):**

- `Proxy:Enabled = true` procesa `X-Forwarded-For`, `-Proto`, `-Host` y `-Prefix`. Limítalo con `Proxy:KnownProxies` o `KnownNetworks`.
- `Proxy:PathBase` fija la sub-ruta cuando el proxy no envía `X-Forwarded-Prefix`.
- `Sso:TrustForwardedHeaders = true` toma la IP del cliente de `CF-Connecting-IP` / `X-Forwarded-For`, para el intento automático de SSO y la auditoría.
- Hay una plantilla genérica de Apache en `infrastructure/apache/gestionreq.conf.template`.
- Kerberos no atraviesa bien un proxy. Para el SSO desde afuera usa el modo de dos servidores.

### Correo: cómo saber si sale

- En **Notificaciones**, la tarjeta "Servidor de correo" muestra si el envío es real o simulado, y el servidor, puerto, cifrado y remitente que usa la app. El botón **Enviar correo de prueba** envía al instante a tu correo y muestra el error exacto del servidor si falla.
- La recuperación de contraseña responde siempre lo mismo en pantalla, para no revelar qué cuentas existen. El resultado real queda en **Usuarios → Auditoría de accesos**:
  - `RESET_CORREO_ENVIADO`.
  - `RESET_CORREO_ERROR`, con el mensaje del servidor.
  - `RESET_CORREO_SIMULADO`, cuando no hay `Smtp:Host`.
  - `RESET_RECHAZADO`, con el motivo: no existe la cuenta, ya se enviaron 3 enlaces en la última hora, la cuenta está pendiente o está desactivada.
- Un relay interno sin TLS necesita `"Seguridad": "Ninguna"`, o el valor por defecto `StartTlsSiDisponible`. Con `StartTls` el envío falla con *"The SMTP server does not support the STARTTLS extension"*.

### Problemas frecuentes en IIS

Para ver el sub-código del error, revisa el log de IIS (`C:\inetpub\logs\LogFiles\W3SVC*\`, columnas `sc-status sc-substatus sc-win32-status`). El detalle de un 500.30 queda en el Visor de eventos → Aplicación.

| Error | Causa | Solución |
| --- | --- | --- |
| 500.19 (código 13) | Atributo o sección que esta versión de IIS no reconoce (p. ej. en IIS 8.5 / Windows Server 2012 R2) | Usar el `web.config` del repositorio, que es compatible con IIS 8.5 |
| 500.19 (código 33) | Sección bloqueada para las aplicaciones (p. ej. `<webSocket>`) | No declararla en `web.config`; la característica se activa para el servidor |
| 500.30 "The Negotiate Authentication handler cannot be used on a server that directly supports Windows Authentication" | `Sso:Enabled = true` sin Autenticación de Windows habilitada en IIS | Instalar `Web-Windows-Auth` y habilitar Windows + Anónima en la aplicación |
| 500.30 / 500 "Falta la cadena de conexión" | No hay `appsettings.Production.json` junto a la DLL | Crearlo (lo hace `deploy-iis.ps1`) |
| 500.35 o fallos raros | La app comparte App Pool con otros sitios | Un App Pool propio, "Sin código administrado" |
| No se puede crear el login `IIS APPPOOL\X` en SQL | `X` debe ser el nombre del **App Pool**, no de la aplicación | Revisarlo con `Get-WebApplication` |

En la cadena de conexión no mezcles `User Id`/`Password` con `Trusted_Connection=True`: este último manda y SQL Server ignora el usuario.

## Crear la base de datos con script (SSMS)

Como alternativa a las migraciones automáticas, `database/GestionRequerimientoDP.sql` crea la base `GestionRequerimientoDP` y todas sus tablas. Se abre en SSMS 22 y se ejecuta con F5, o con `sqlcmd -S <servidor> -E -i database\GestionRequerimientoDP.sql`. Es idempotente y registra la migración, así que la app no intenta recrear nada. Los datos de ejemplo los carga la app al iniciar si `Database:Seed` está activo y no hay usuarios.

Para regenerarlo después de una migración nueva: `dotnet ef migrations script --idempotent -p src/GestionRequerimientoDP.Infrastructure -s src/GestionRequerimientoDP.Infrastructure`.

## Ejecutar en Linux / contenedor

```bash
docker run -d --name sql2025 -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Dev_Passw0rd!' -p 1433:1433 mcr.microsoft.com/mssql/server:2025-latest
export ConnectionStrings__GestionRequerimientoDP='Server=localhost,1433;Database=GestionRequerimientoDP;User Id=sa;Password=Dev_Passw0rd!;TrustServerCertificate=True'
dotnet run --project src/GestionRequerimientoDP.Web --launch-profile http
```

Abrir http://localhost:5283.

## Usuarios de demostración

En Development (`Auth:DemoMode = true`), la pantalla de ingreso muestra botones para entrar con cualquier usuario semilla, agrupados por rol. Con `DemoMode = false` se exige contraseña. Todos los usuarios semilla tienen la contraseña `demo123`, que debe cambiarse desde **Usuarios** antes de usar la app en producción.

| Rol | Usuarios |
| --- | --- |
| Líder | Carolina Méndez (Procesamiento), Ricardo Fuentes (Codificación), Valeria Núñez (Scripting) |
| Analista DP | Alexis Duran, Andrea Pérez, Andrés Salinas, Rodrigo Salgado |
| Analista Scripting | Javiera Pérez, Tomás Herrera, Fernanda Silva |
| Analista Codificación | Marcela Olivares, Pablo Vergara, Daniela Rojas |
| Mixtos | José Miguel Carreño (DP + Cod), Cristian Gómez y Luis Ruiz (DP + Script) |
| Comercial | Maria Paz Ayala, Wiliam Muñoz, Lorena Montalvo Rosado |

Los datos semilla reproducen la planilla `Prog DP.xlsx` (49 entregables), 8 semanas de historia, scripts, codificación y 4 solicitudes comerciales. Las fechas se desplazan por semanas completas para que la semana de la planilla coincida con la semana actual al momento de cargar.

## Configuración (`appsettings.json`)

| Clave | Descripción |
| --- | --- |
| `ConnectionStrings:GestionRequerimientoDP` | SQL Server (user-secrets o variable de entorno) |
| `Auth:Mode` | `Cookie` (ingreso propio) o `Windows` (Negotiate; mapea `DOMINIO\usuario` a `Usuario.DomainAccount`) |
| `Auth:DemoMode` | Accesos de demostración sin contraseña |
| `Auth:RegistroHabilitado` | Muestra "Solicitar acceso" en el ingreso (por defecto `true`) |
| `Sso:*` | Single Sign-On con Active Directory (ver sección Single Sign-On) |
| `ActiveDirectory:*` | Búsqueda en el directorio al crear usuarios y nombre de las cuentas nuevas (solo Windows): `Enabled`, `LdapPath`, `Dominio`, `Usuario`, `Clave` |
| `Proxy:*` | Proxy inverso y sub-ruta: `Enabled`, `KnownProxies`, `KnownNetworks`, `PathBase` |
| `Seguridad:FormulariosPorMinutoPorIp` | Límite de envíos de ingreso, solicitud de acceso y recuperación por IP y minuto (por defecto 30). Detrás de un proxy, configura `Proxy` para que la IP sea la del usuario y no la del proxy |
| `DataProtection:*` | `KeysPath`, `ApplicationName`, `ProtegerConDpapi` (ver Publicar en IIS) |
| `Auth:ConsultaRequiereSesion` | Exige sesión para la consulta pública `/consulta` |
| `Reglas:*` | `ProcCapacity`, `CodCapacity`, `ScriptWip`, `StaleDays`, `NotifyAssign`, `NotifyOverdue`, `CompactCards` |
| `Smtp:*` | Servidor de correo (`Host`, `Port`, `From`, `User`/`Password` si el servidor pide autenticación). `Seguridad`: `Ninguna` (relay interno sin TLS), `StartTls`, `StartTlsSiDisponible` o `Ssl`. Sin `Host`, los correos se simulan y quedan registrados en **Notificaciones** |
| `Database:MigrateOnStartup` / `Database:Seed` | Migrar y cargar datos semilla al iniciar (activos en Development) |
| `Jobs:Enabled` | Despachador de correos y revisión diaria de atrasos (09:00, hora de Chile) |

## Pruebas

```bash
dotnet test GestionRequerimientoDP.slnx
```

- **Application.Tests (86, xUnit + EF InMemory):**
  - Motores: ranking de scripts, balanceo, importación, OK, atrasos, `teamLeadersOf`.
  - Servicios con filtros de seguridad por rol, solicitudes, balanceo, eliminación de usuarios.
  - Acceso: RUT, política de contraseñas, solicitud/aprobación, cuentas AD, tokens de recuperación, auditoría.
  - Asignación a líderes.
  - **Integridad contra SQL Server real (7)**, solo si `GRDP_TEST_SQL` tiene una cadena de conexión de prueba. Crean y borran una base temporal:
    - Migraciones y modelo.
    - Rowversion.
    - Asignaciones concurrentes.
    - Altas y aprobaciones simultáneas.
    - Enlace de recuperación usado en paralelo.
    - Índices únicos y borrado en cascada.
- **Web.Tests (50, bUnit + WebApplicationFactory):**
  - Componentes.
  - SSO.
  - Flujos HTTP de cuenta bajo `/gestionreq`.
  - Seguridad: bloqueo por intentos, formularios vencidos, límite por IP, cabeceras y modo demostración fuera de Development.
- **Carga, seguridad, circuitos y funcional por rol:** scripts en `tools/pruebas/` (ver su README).

## Diferencias respecto del mockup

- **Nombre y marca:** la aplicación se llama **GestionRequerimientoDP** (marca "GR"). El asunto de los correos usa `[GestionRequerimientoDP]`.
- **Tiempo real:** se usa un `ChangeNotifier` singleton, la opción simple que propone §6.5. Cuando alguien cambia datos, las demás pestañas abiertas se refrescan solas.
- **Correos:** quedan en la tabla `Notificaciones` y los envía el `EmailDispatcher`, que reintenta los errores y omite a los usuarios inactivos.
- **Importación:** acepta `.xlsx`, `.csv` o filas pegadas. Los `.xls` antiguos deben guardarse como `.xlsx`.
- **Correlativo de solicitudes:** `S-####` se calcula como el máximo existente + 1, dentro de la misma operación.
- **Balanceo:** al aplicar la propuesta se verifica que los entregables no hayan cambiado desde que se generó.
