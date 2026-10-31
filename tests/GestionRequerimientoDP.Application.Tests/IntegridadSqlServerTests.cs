using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Infrastructure.Data;
using GestionRequerimientoDP.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Tests;

/// <summary>
/// Integridad contra SQL Server real (restricciones, rowversion, carreras). Solo corre si la variable de entorno
/// GRDP_TEST_SQL tiene una cadena de conexión a un servidor de pruebas: crea una base temporal y la elimina al terminar.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public static string? Servidor => Environment.GetEnvironmentVariable("GRDP_TEST_SQL");
    public string Cadena { get; private set; } = "";
    public Factory Db { get; private set; } = null!;

    public sealed class Factory(string cs) : IAppDbContextFactory
    {
        readonly DbContextOptions<AppDbContext> _o = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
        public IAppDbContext Create() => new AppDbContext(_o);
        public AppDbContext Concreto() => new(_o);
    }

    public async Task InitializeAsync()
    {
        if (Servidor is null) return;
        var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Servidor) { InitialCatalog = "GRDP_Integridad_" + Guid.NewGuid().ToString("N")[..8] };
        Cadena = b.ConnectionString;
        Db = new Factory(Cadena);
        await using var db = Db.Concreto();
        await db.Database.MigrateAsync(); // las migraciones reales, no EnsureCreated
    }

    public async Task DisposeAsync()
    {
        if (Servidor is null) return;
        await using var db = Db.Concreto();
        await db.Database.EnsureDeletedAsync();
    }
}

public class IntegridadSqlServerTests(SqlServerFixture fx) : IClassFixture<SqlServerFixture>
{
    static readonly DateOnly Martes = new(2026, 10, 20);
    static bool Sin => SqlServerFixture.Servidor is null;

    async Task<TestApp> App()
    {
        var app = new TestApp(Martes, fx.Db);
        await using var db = fx.Db.Create();
        if (!await db.Usuarios.AnyAsync()) await app.SeedAsync();
        return app;
    }

    AccesoService Acceso(TestApp app) => new(fx.Db, app.Clock, app.Changes, new PasswordService());

    [SkippableFact]
    public async Task Migraciones_aplican_en_SQL_Server_y_coinciden_con_el_modelo()
    {
        Skip.If(Sin, "Sin GRDP_TEST_SQL");
        await using var db = fx.Db.Concreto();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges(), "El modelo tiene cambios sin migración");
    }

    [SkippableFact]
    public async Task Edicion_simultanea_del_mismo_requerimiento_la_segunda_recibe_aviso()
    {
        Skip.If(Sin, "Sin GRDP_TEST_SQL");
        await App();
        await using var a = fx.Db.Create();
        await using var b = fx.Db.Create();
        var id = (await a.Requerimientos.OrderBy(r => r.Id).FirstAsync()).Id;
        var ra = await a.Requerimientos.FirstAsync(r => r.Id == id);
        var rb = await b.Requerimientos.FirstAsync(r => r.Id == id);
        ra.Nota = "edición A"; rb.Nota = "edición B";
        await a.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync()); // rowversion; los servicios lo traducen a un aviso
        await using var c = fx.Db.Create();
        Assert.Equal("edición A", (await c.Requerimientos.FirstAsync(r => r.Id == id)).Nota); // no se pisó
    }

    [SkippableFact]
    public async Task Asignaciones_concurrentes_terminan_en_un_estado_coherente()
    {
        Skip.If(Sin, "Sin GRDP_TEST_SQL");
        var app = await App();
        var lider = await app.UserAsync("cmendez@empresa.cl");
        await using var db = fx.Db.Create();
        var equipo = await db.Usuarios.Where(u => u.Rol == Rol.Analista && u.Activo && u.Equipos.Any(e => e.Equipo == Equipo.Procesamiento)).Select(u => u.Id).ToListAsync();
        var req = await db.Requerimientos.FirstAsync(r => r.Tipo == Equipo.Procesamiento && r.Estado == Estado.Pendiente);
        var inesperadas = new List<Exception>();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            try { await app.Reqs.AsignarAsync(lider, req.Id, equipo[i % equipo.Count]); }
            catch (ReglaException) { } // aviso de concurrencia: esperado
            catch (Exception ex) { lock (inesperadas) inesperadas.Add(ex); }
        }));
        Assert.Empty(inesperadas);
        await using var c = fx.Db.Create();
        var final = await c.Requerimientos.FirstAsync(r => r.Id == req.Id);
        Assert.Contains(final.AnalistaId!.Value, equipo);
        var ultimo = await c.OkLogs.Where(l => l.ReqId == req.Id && l.Accion == OkAccion.Asignacion).OrderByDescending(l => l.Id).FirstAsync();
        Assert.Contains($"{final.AnalistaId}", ultimo.Payload); // el historial termina en el mismo analista
    }

    [SkippableFact]
    public async Task Solicitudes_de_acceso_simultaneas_con_el_mismo_correo_crean_una_sola_cuenta()
    {
        Skip.If(Sin, "Sin GRDP_TEST_SQL");
        var app = await App();
        var inesperadas = new List<Exception>(); var ok = 0;
        await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
        {
            try
            {
                await Acceso(app).SolicitarAsync(new RegistroInput { Nombre = "Carrera Doble", Email = "carrera@empresa.cl", Rut = "12.345.678-5", Motivo = "x", Password = "Planilla#2026", Confirmacion = "Planilla#2026" });
                Interlocked.Increment(ref ok);
            }
            catch (ReglaException) { }
            catch (Exception ex) { lock (inesperadas) inesperadas.Add(ex); }
        }));
        Assert.Empty(inesperadas);
        Assert.Equal(1, ok);
        await using var c = fx.Db.Create();
        Assert.Equal(1, await c.Usuarios.CountAsync(u => u.Email == "carrera@empresa.cl"));
    }

    [SkippableFact]
    public async Task Dos_lideres_aprueban_la_misma_solicitud_a_la_vez_solo_una_vale()
    {
        Skip.If(Sin, "Sin GRDP_TEST_SQL");
        var app = await App();
        var u = await Acceso(app).SolicitarAsync(new RegistroInput { Nombre = "Doble Aprobación", Email = "doble@empresa.cl", Motivo = "x", Password = "Planilla#2026", Confirmacion = "Planilla#2026" });
        var lideres = new[] { await app.UserAsync("cmendez@empresa.cl"), await app.UserAsync("rfuentes@empresa.cl") };
        var inesperadas = new List<Exception>(); var ok = 0;
        await Task.WhenAll(lideres.Select(async l =>
        {
            var input = UsuarioInput.From(u); input.Rol = Rol.Comercial;
            try { await Acceso(app).AprobarAsync(l, input); Interlocked.Increment(ref ok); }
            catch (ReglaException) { }
            catch (Exception ex) { lock (inesperadas) inesperadas.Add(ex); }
        }));
        Assert.Empty(inesperadas);
        Assert.Equal(1, ok);
        await using var c = fx.Db.Create();
        Assert.Equal(1, await c.Notificaciones.CountAsync(n => n.Kind == NotificacionKind.Acceso && n.To == "doble@empresa.cl" && n.Subject.Contains("aprobado")));
    }

    [SkippableFact]
    public async Task Enlace_de_recuperacion_usado_en_paralelo_solo_cambia_la_clave_una_vez()
    {
        Skip.If(Sin, "Sin GRDP_TEST_SQL");
        var app = await App();
        var (reset, _) = await Acceso(app).CrearResetAsync("jperez@empresa.cl", "");
        var oks = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Acceso(app).RestablecerAsync(reset!.Token, $"Planilla#20{i:00}", $"Planilla#20{i:00}", "")));
        Assert.Equal(1, oks.Count(r => r.Ok));
    }

    [SkippableFact]
    public async Task Restricciones_de_la_base_rechazan_duplicados_y_borran_en_cascada()
    {
        Skip.If(Sin, "Sin GRDP_TEST_SQL");
        var app = await App();
        await using (var db = fx.Db.Create())
        {
            db.Usuarios.Add(new Usuario { Nombre = "Dup", Email = "cmendez@empresa.cl", Rol = Rol.Analista });
            var ex = await Assert.ThrowsAsync<ReglaException>(() => db.SaveChangesAsync()); // índice único de correo → aviso, no error técnico
            Assert.Equal("Ya existe una cuenta con ese correo.", ex.Message);
        }
        var (reset, _) = await Acceso(app).CrearResetAsync("fsilva@empresa.cl", "");
        var lider = await app.UserAsync("cmendez@empresa.cl");
        var users = new UsuarioService(fx.Db, app.Changes, new PasswordService());
        var nuevo = await users.SaveAsync(lider, new UsuarioInput { Nombre = "Temporal Cascada", Email = "cascada@empresa.cl", Rol = Rol.Analista, Equipos = [Equipo.Script], Password = "Planilla#2026" });
        await Acceso(app).CrearResetAsync("cascada@empresa.cl", "");
        await users.EliminarAsync(lider, nuevo.Id);
        await using var c = fx.Db.Concreto();
        Assert.False(await c.ResetTokens.AnyAsync(t => t.UsuarioId == nuevo.Id)); // tokens borrados con el usuario
        Assert.False(await c.Set<UsuarioEquipo>().AnyAsync(e => e.UsuarioId == nuevo.Id));
        Assert.True(await c.ResetTokens.AnyAsync(t => t.TokenHash == AccesoService.HashToken(reset!.Token))); // los demás quedan
    }
}
