using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Tests;

/// <summary>Con años de historia, las pantallas leen solo lo vigente (abierto, de esta semana o reciente).</summary>
public class HistoriaTests
{
    static readonly DateOnly Martes = new(2026, 10, 20);

    static async Task<TestApp> ConHistoriaAntigua()
    {
        var app = new TestApp(Martes);
        await app.SeedAsync();
        await using var db = app.Factory.Create();
        var analista = await db.Usuarios.FirstAsync(u => u.Email == "asalinas@empresa.cl");
        var comercial = await db.Usuarios.FirstAsync(u => u.Email == "mayala@empresa.cl");
        var hace2Anios = Martes.AddYears(-2);
        var sol = new Solicitud { Codigo = "S-9000", SolicitanteId = comercial.Id, CreadaAt = Fechas.FromLocal(hace2Anios, 9, 0) };
        db.Solicitudes.Add(sol);
        for (var i = 0; i < 30; i++)
            db.Requerimientos.Add(new Requerimiento
            {
                Tipo = Equipo.Procesamiento, Fecha = hace2Anios.AddDays(i), Cliente = "Viejo", Proyecto = "Histórico", Job = $"24-OLD-{i}", Entregable = "Base",
                AnalistaId = analista.Id, Estado = Estado.Ok, OkAt = Fechas.FromLocal(hace2Anios.AddDays(i), 12, 0), OkById = analista.Id,
                Creado = sol.CreadaAt, Actualizado = sol.CreadaAt, Solicitud = i == 0 ? sol : null,
            });
        await db.SaveChangesAsync();
        return app;
    }

    [Fact]
    public async Task Lo_propio_y_el_tablero_no_traen_la_historia_terminada()
    {
        var app = await ConHistoriaAntigua();
        var analista = await app.UserAsync("asalinas@empresa.cl");
        Assert.DoesNotContain(await app.Reqs.PropiosAsync(analista), r => r.Job.StartsWith("24-OLD"));
        Assert.DoesNotContain(await app.Reqs.TableroAsync(analista), r => r.Job.StartsWith("24-OLD"));
        var lider = await app.UserAsync("cmendez@empresa.cl");
        Assert.DoesNotContain(await app.Reqs.TableroAsync(lider), r => r.Job.StartsWith("24-OLD"));
        // Lo vigente sí: abierto de cualquier fecha y lo de la semana.
        var hoy = app.Clock.Today;
        Assert.All(await app.Reqs.PropiosAsync(analista), r => Assert.True(r.Estado != Estado.Ok || r.Fecha >= Fechas.MondayOf(hoy) || r.OkAt >= Fechas.FromLocal(Fechas.MondayOf(hoy), 0, 0)));
    }

    [Fact]
    public async Task Solicitudes_entregadas_antiguas_quedan_fuera_hasta_pedirlas()
    {
        var app = await ConHistoriaAntigua();
        var comercial = await app.UserAsync("mayala@empresa.cl");
        Assert.DoesNotContain(await app.Sols.MiasAsync(comercial), c => c.Solicitud.Codigo == "S-9000");
        Assert.True(await app.Sols.HayAnterioresAsync(comercial, 90, soloMias: true));
        Assert.Contains(await app.Sols.MiasAsync(comercial, 365 * 3), c => c.Solicitud.Codigo == "S-9000");
        var lider = await app.UserAsync("cmendez@empresa.cl");
        Assert.DoesNotContain(await app.Sols.TodasAsync(lider), c => c.Solicitud.Codigo == "S-9000");
    }

    [Fact]
    public async Task Una_solicitud_antigua_con_algo_abierto_sigue_visible()
    {
        var app = await ConHistoriaAntigua();
        await using (var db = app.Factory.Create())
        {
            var r = await db.Requerimientos.FirstAsync(x => x.Job == "24-OLD-0");
            r.Estado = Estado.EnCurso;
            await db.SaveChangesAsync();
        }
        var comercial = await app.UserAsync("mayala@empresa.cl");
        Assert.Contains(await app.Sols.MiasAsync(comercial), c => c.Solicitud.Codigo == "S-9000");
    }
}
