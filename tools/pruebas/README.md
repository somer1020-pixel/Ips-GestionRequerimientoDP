# Pruebas de carga, seguridad y funcionales

Scripts de Node 22 + Playwright (`npm i playwright` y `npx playwright install chromium` si no están instalados).
Están pensados para un **ambiente de prueba con los datos de demostración** (usuarios semilla con contraseña `demo123`).
No los ejecutes contra producción: crean y modifican datos, generan cientos de ingresos fallidos (que bloquean cuentas
15 minutos) y cargan el servidor.

```bash
export GRDP_URL=http://localhost:5288      # URL de la app de prueba (con sub-ruta si aplica)
export GRDP_PID=$(pgrep -f GestionRequerimientoDP.Web | head -1)   # opcional, Linux: mide la memoria de la app
node tools/pruebas/funcional.js            # recorrido por rol: pantallas, accesos prohibidos y flujos principales
node tools/pruebas/seguridad.js            # autorización por rol, cookies, redirecciones, CSRF, cabeceras, XSS, tiempos
node tools/pruebas/carga.js 20             # carga HTTP: N clientes × 20 s por escenario (latencias, errores, memoria)
node tools/pruebas/circuitos.js 100        # 100 pestañas Blazor simultáneas + propagación en tiempo real
```

Para el límite de envíos por IP, la app de prueba debe correr con `Seguridad__FormulariosPorMinutoPorIp=100000`
(todas las solicitudes salen del mismo equipo). Inyección SQL: `sqlmap` (pip) contra `/consulta?job=`, `/cuenta/login`
(con `--csrf-token=__RequestVerificationToken`) y las descargas `/export/*` con la cookie de un líder.

Pruebas de integridad contra SQL Server (crean y borran una base temporal):

```bash
GRDP_TEST_SQL="Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=True" dotnet test
```
