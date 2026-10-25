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
| `Auth:ConsultaRequiereSesion` | Exige sesión para la consulta pública `/consulta` |
| `Reglas:*` | `ProcCapacity`, `CodCapacity`, `ScriptWip`, `StaleDays`, `NotifyAssign`, `NotifyOverdue`, `CompactCards` |
| `Smtp:*` | Servidor de correo. Sin `Host`, los correos se simulan y quedan registrados en **Notificaciones** |
| `Database:MigrateOnStartup` / `Database:Seed` | Migrar y cargar datos semilla al iniciar (activos en Development) |
| `Jobs:Enabled` | Despachador de correos y revisión diaria de atrasos (09:00, hora de Chile) |

## Pruebas

```bash
dotnet test GestionRequerimientoDP.slnx
```

- **xUnit (42):**
  - Ranking de scripts (empates, WIP lleno, mixtos).
  - Balanceo (no separa jobs, no mueve en curso ni OK, converge).
  - `ToIsoDate` y duplicados en la importación.
  - Validaciones del OK.
  - `campoFrom`, `effResp`, sin gestión, atrasado, próximo día hábil y `teamLeadersOf`.
  - Servicios con filtros de seguridad por rol, solicitudes, avisos de atraso y balanceo aplicado.
- **bUnit (10):**
  - Íconos rojo y amarillo.
  - `ReqCard`.
  - `OkModal` según el tipo y con atraso.
  - Menú por rol.
  - La consulta pública no muestra notas.

## Diferencias respecto del mockup

- **Nombre y marca:** la aplicación se llama **GestionRequerimientoDP** (marca "GR"). El asunto de los correos usa `[GestionRequerimientoDP]`.
- **Tiempo real:** se usa un `ChangeNotifier` singleton, la opción simple que propone §6.5. Cuando alguien cambia datos, las demás pestañas abiertas se refrescan solas.
- **Correos:** quedan en la tabla `Notificaciones` y los envía el `EmailDispatcher`, que reintenta los errores y omite a los usuarios inactivos.
- **Importación:** acepta `.xlsx`, `.csv` o filas pegadas. Los `.xls` antiguos deben guardarse como `.xlsx`.
- **Correlativo de solicitudes:** `S-####` se calcula como el máximo existente + 1, dentro de la misma operación.
- **Balanceo:** al aplicar la propuesta se verifica que los entregables no hayan cambiado desde que se generó.
