using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Tests;

/// <summary>Integración de servicios sobre BD en memoria con los datos semilla (martes 20 oct 2026).</summary>
public class ServiceTests
{
    static readonly DateOnly Martes = new(2026, 10, 20);

    async Task<TestApp> App()
    {
        var app = new TestApp(Martes);
        await app.SeedAsync();
        return app;
    }

    [Fact]
    public async Task Semilla_trae_la_planilla_de_49_filas()
    {
        var app = await App();
        await using var db = app.Factory.Create();
        Assert.Equal(49, await db.Requerimientos.CountAsync(r => r.Tipo == Equipo.Procesamiento && r.Fecha >= new DateOnly(2026, 10, 19) && r.Fecha <= new DateOnly(2026, 10, 23)));
        Assert.Equal(4, await db.Solicitudes.CountAsync(s => s.Requerimientos.Any(r => r.AnalistaId == null)));
        Assert.True(await db.Requerimientos.AnyAsync(r => r.AtrasoResp == AtrasoResponsable.Campo && r.AtrasoCampo != ""));
    }

    [Fact]
    public async Task Analista_solo_ve_lo_asignado()
    {
        var app = await App();
        var andres = await app.UserAsync("asalinas@empresa.cl");
        var tablero = await app.Reqs.TableroAsync(andres);
        Assert.NotEmpty(tablero);
        Assert.All(tablero, r => Assert.Equal(andres.Id, r.AnalistaId));
        var ajeno = (await app.Reqs.TableroAsync(await app.UserAsync("cmendez@empresa.cl"))).First(r => r.AnalistaId != andres.Id);
        Assert.Null(await app.Reqs.GetAsync(andres, ajeno.Id));
        var comercial = await app.UserAsync("mayala@empresa.cl");
        Assert.Empty(await app.Reqs.TableroAsync(comercial));
    }

    [Fact]
    public async Task Analista_no_asigna_ni_reabre_y_no_edita_lo_ajeno()
    {
        var app = await App();
        var andres = await app.UserAsync("asalinas@empresa.cl");
        var lider = await app.UserAsync("cmendez@empresa.cl");
        var propio = (await app.Reqs.TableroAsync(andres)).First(r => r.Estado == Estado.Ok);
        var ajeno = (await app.Reqs.TableroAsync(lider)).First(r => r.AnalistaId != andres.Id && r.Estado != Estado.Ok);
        await Assert.ThrowsAsync<PermisoException>(() => app.Reqs.AsignarAsync(andres, propio.Id, null));
        await Assert.ThrowsAsync<PermisoException>(() => app.Ok.MoveToAsync(andres, propio.Id, Estado.EnCurso));
        await Assert.ThrowsAsync<PermisoException>(() => app.Ok.MoveToAsync(andres, ajeno.Id, Estado.EnCurso));
        Assert.Equal(MoveResult.Hecho, await app.Ok.MoveToAsync(lider, propio.Id, Estado.EnCurso)); // el líder reabre
    }

    [Fact]
    public async Task OK_registra_datos_y_reabrir_los_limpia()
    {
        var app = await App();
        var andres = await app.UserAsync("asalinas@empresa.cl");
        var r = (await app.Reqs.TableroAsync(andres)).First(x => x.Estado != Estado.Ok && x.Fecha >= Martes);
        Assert.Equal(MoveResult.RequiereOk, await app.Ok.MoveToAsync(andres, r.Id, Estado.Ok));
        Assert.Null(await app.Ok.ConfirmOkAsync(andres, r.Id, new OkInput { Horas = "2,5", Materiales = Martes, Nota = "listo" }));
        var ok = (await app.Reqs.GetAsync(andres, r.Id))!;
        Assert.Equal(Estado.Ok, ok.Estado);
        Assert.Equal(2.5m, ok.Horas);
        Assert.Equal(andres.Id, ok.OkById);
        await app.Ok.ReabrirAsync(await app.UserAsync("cmendez@empresa.cl"), r.Id);
        var re = (await app.Reqs.GetAsync(andres, r.Id))!;
        Assert.Equal(Estado.EnCurso, re.Estado);
        Assert.Null(re.Horas);
        Assert.Equal(Martes, re.MaterialesAt); // se conserva
    }

    [Fact]
    public async Task Solicitud_crea_entregables_sin_analista_y_avisa_al_lider_del_equipo()
    {
        var app = await App();
        var mp = await app.UserAsync("mayala@empresa.cl");
        var s = await app.Sols.EnviarAsync(mp, new SolicitudInput
        {
            Tipo = Equipo.Codificacion, Cliente = "C", Proyecto = "P", Job = "J-NEW", SL = "CEX", Tecnica = "CATI", Fecha = Martes.AddDays(3),
            Entregables = ["Codificación de marcas", "Libro de códigos"],
        });
        Assert.Equal("S-1045", s.Codigo);
        await using var db = app.Factory.Create();
        var reqs = await db.Requerimientos.Where(r => r.SolicitudId == s.Id).ToListAsync();
        Assert.Equal(2, reqs.Count);
        Assert.All(reqs, r => Assert.Null(r.AnalistaId));
        var mail = await db.Notificaciones.OrderByDescending(n => n.Id).FirstAsync();
        Assert.Equal(NotificacionKind.Solicitud, mail.Kind);
        Assert.Equal("rfuentes@empresa.cl", mail.To); // líder de Codificación
        Assert.Equal("mayala@empresa.cl", mail.Cc);
        await Assert.ThrowsAsync<ReglaException>(() => app.Sols.EnviarAsync(mp, new SolicitudInput { Cliente = "C", Proyecto = "P", Job = "J", Tecnica = "CATI", Fecha = Martes.AddDays(-1), Entregables = ["Tablero"] }));
    }

    [Fact]
    public async Task Script_automatico_va_al_primer_disponible_con_correo()
    {
        var app = await App();
        var lider = await app.UserAsync("vnunez@empresa.cl");
        var top = (await app.Scripts.VistaAsync(lider)).Ranking[0].Analista.Id;
        var (r, sent) = await app.Reqs.CrearAsync(lider, new Services.NuevoRequerimiento { Tipo = Equipo.Script, Cliente = "C", Proyecto = "P", Job = "J", Tecnica = "Online", Fecha = Martes, Entregable = "Programación" });
        Assert.Equal(top, r.AnalistaId);
        Assert.True(sent);
    }

    [Fact]
    public async Task Atraso_sin_gestion_un_aviso_por_dia()
    {
        var app = await App();
        var n1 = await app.Notif.CheckOverdueAsync(false);
        Assert.True(n1 > 0); // "Post test AFP Capital" (lun 19) quedó pendiente
        Assert.Equal(0, await app.Notif.CheckOverdueAsync(false));
        Assert.Equal(n1, await app.Notif.CheckOverdueAsync(true));
        await using var db = app.Factory.Create();
        var mail = await db.Notificaciones.FirstAsync(n => n.Kind == NotificacionKind.Atraso && n.Body.Contains("Post test AFP Capital"));
        Assert.Contains("cmendez@empresa.cl", mail.Cc);
    }

    [Fact]
    public async Task Balanceo_aplicado_mueve_jobs_completos_y_avisa()
    {
        var app = await App();
        var lider = await app.UserAsync("cmendez@empresa.cl");
        var cur = new DateOnly(2026, 10, 19);
        var moves = await app.Plan.SuggestAsync(lider, Equipo.Procesamiento, cur);
        Assert.NotEmpty(moves);
        var mails = await app.Plan.ApplyAsync(lider, moves);
        Assert.True(mails > 0);
        await Assert.ThrowsAsync<ReglaException>(() => app.Plan.ApplyAsync(lider, moves)); // propuesta obsoleta
    }
}
