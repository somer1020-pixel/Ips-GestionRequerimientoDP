using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Tests;

public class UsuarioServiceTests
{
    static readonly DateOnly Martes = new(2026, 10, 20);

    static async Task<(TestApp App, UsuarioService Svc, UsuarioActual Lider)> Setup()
    {
        var app = new TestApp(Martes);
        await app.SeedAsync();
        return (app, new UsuarioService(app.Factory, app.Changes, new PasswordService()), await app.UserAsync("cmendez@empresa.cl"));
    }

    [Fact]
    public async Task Elimina_usuario_sin_historial_y_libera_sus_requerimientos_abiertos()
    {
        var (app, svc, lider) = await Setup();
        var nuevo = await svc.SaveAsync(lider, new UsuarioInput { Nombre = "Temporal", Email = "temporal@empresa.cl", Rol = Rol.Analista, Equipos = [Equipo.Procesamiento] });
        var (req, _) = await app.Reqs.CrearAsync(lider, new NuevoRequerimiento { Cliente = "C", Proyecto = "P", Job = "J-T", Tecnica = "CATI", Fecha = Martes, AnalistaId = nuevo.Id });
        Assert.DoesNotContain(nuevo.Id, await svc.ConHistorialAsync());

        Assert.Equal(1, await svc.EliminarAsync(lider, nuevo.Id));

        Assert.Null(await svc.GetAsync(nuevo.Id));
        await using var db = app.Factory.Create();
        Assert.Null((await db.Requerimientos.FirstAsync(r => r.Id == req.Id)).AnalistaId); // queda sin asignar
    }

    [Fact]
    public async Task No_elimina_con_historial_ni_a_si_mismo_ni_si_no_es_lider()
    {
        var (app, svc, lider) = await Setup();
        var historial = await svc.ConHistorialAsync();
        var andres = await app.UserAsync("asalinas@empresa.cl");   // tiene entregables con OK
        var comercial = await app.UserAsync("mayala@empresa.cl");  // tiene solicitudes
        Assert.Contains(andres.Id, historial);
        Assert.Contains(comercial.Id, historial);

        await Assert.ThrowsAsync<ReglaException>(() => svc.EliminarAsync(lider, andres.Id));
        await Assert.ThrowsAsync<ReglaException>(() => svc.EliminarAsync(lider, comercial.Id));
        await Assert.ThrowsAsync<ReglaException>(() => svc.EliminarAsync(lider, lider.Id));
        await Assert.ThrowsAsync<PermisoException>(() => svc.EliminarAsync(andres, lider.Id));
        Assert.NotNull(await svc.GetAsync(andres.Id));
    }
}
