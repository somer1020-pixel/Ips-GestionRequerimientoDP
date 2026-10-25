# GestionRequerimientoDP

- Solución .NET 10 / Blazor Server: `GestionRequerimientoDP.slnx`. Compilar y probar con `dotnet build` y `dotnet test` (los tests usan EF InMemory, no requieren SQL Server).
- La especificación funcional está en `docs/handoff/README-handoff.md` y el mockup ejecutable en `docs/handoff/Carga Ops.dc.html`.
- Mantener `docs/handoff/README-handoff.md` sincronizado con cada cambio de diseño o lógica en el mockup; los cambios de diseño se hacen primero en el mockup y en ese README.
- Las reglas de negocio viven en `src/GestionRequerimientoDP.Domain/Reglas.cs` y `src/GestionRequerimientoDP.Application/Motores/` (funciones puras con tests). Los permisos se aplican en los servicios (`Application/Services`), nunca solo en la UI.
- Migraciones: `dotnet ef migrations add <Nombre> -p src/GestionRequerimientoDP.Infrastructure -s src/GestionRequerimientoDP.Infrastructure -o Data/Migrations`.
