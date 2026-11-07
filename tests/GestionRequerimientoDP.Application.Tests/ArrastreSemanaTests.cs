using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Tests;

/// <summary>Al cambiar de semana, lo que quedó sin OK sigue en el tablero (no se pierde de vista) con su día original.</summary>
public class ArrastreSemanaTests
{
    static readonly DateOnly Martes = new(2026, 10, 20);

    [Fact]
    public async Task Pendientes_y_en_curso_de_la_semana_anterior_siguen_en_el_tablero_y_los_terminados_no()
    {
        var app = new TestApp(Martes);
        await app.SeedAsync();
        var lider = await app.UserAsync("cmendez@empresa.cl");
        var semana = Fechas.MondayOf(Martes);
        List<Requerimiento> deLaSemana;
        await using (var db = app.Factory.Create())
            deLaSemana = await db.Requerimientos.AsNoTracking()
                .Where(r => r.Tipo != Equipo.Script && r.Fecha >= semana && r.Fecha <= semana.AddDays(6)).ToListAsync();
        var abiertos = deLaSemana.Where(r => r.Estado != Estado.Ok).Select(r => r.Id).ToHashSet();
        var cerrados = deLaSemana.Where(r => r.Estado == Estado.Ok).Select(r => r.Id).ToHashSet();
        Assert.NotEmpty(abiertos);
        Assert.NotEmpty(cerrados);

        app.Time.Value = app.Time.Value.AddDays(7); // martes siguiente
        var hoy = app.Clock.Today;
        var tablero = await app.Reqs.TableroAsync(lider);
        var ids = tablero.Select(r => r.Id).ToHashSet();
        Assert.True(abiertos.IsSubsetOf(ids), "lo que quedó sin OK sigue en el tablero");
        Assert.False(cerrados.Overlaps(ids), "lo terminado la semana pasada sale del tablero");
        Assert.All(tablero.Where(r => abiertos.Contains(r.Id)), r =>
        {
            Assert.True(Reglas.DeSemanaAnterior(r, hoy));
            Assert.True(Reglas.EnSemanaActual(r, hoy));
            Assert.True(Reglas.Atrasado(r, hoy));
        });
        Assert.True(await app.Reqs.AtrasadosAsync(lider) >= abiertos.Count);

        // El analista también los ve en su tablero.
        await using var db2 = app.Factory.Create();
        var uno = await db2.Requerimientos.FirstAsync(r => abiertos.Contains(r.Id) && r.AnalistaId != null);
        var analista = UsuarioActual.From(await db2.Usuarios.Include(u => u.Equipos).FirstAsync(u => u.Id == uno.AnalistaId));
        Assert.Contains(await app.Reqs.TableroAsync(analista), r => r.Id == uno.Id);
    }
}
