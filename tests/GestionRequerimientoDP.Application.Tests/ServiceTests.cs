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
    public async Task Asignar_a_primer_disponible_no_separa_una_solicitud_de_varios_entregables()
    {
        var app = await App();
        var mp = await app.UserAsync("mayala@empresa.cl");
        var lider = await app.UserAsync("vnunez@empresa.cl");
        await app.Scripts.AutoAssignAsync(lider); // vaciar la cola previa de la semilla
        SolicitudInput Sol(string job, params string[] ents) => new()
        {
            Tipo = Equipo.Script, Cliente = "Banco de Chile", Proyecto = "Sucursales", Job = job, SL = "CEX", Tecnica = "Online", Fecha = Martes.AddDays(7),
            Descripcion = "Configurar nueva ola", Entregables = [.. ents],
        };
        var s1 = await app.Sols.EnviarAsync(mp, Sol("26-0000-000-01", "Programación", "Configurar nueva ola", "Cuotas"));
        var s2 = await app.Sols.EnviarAsync(mp, Sol("26-0000-000-02", "Programación", "Cuotas"));

        var (asignados, correos) = await app.Scripts.AutoAssignAsync(lider);

        Assert.Equal(5, asignados);
        await using var db = app.Factory.Create();
        var a1 = await db.Requerimientos.Where(r => r.SolicitudId == s1.Id).Select(r => r.AnalistaId).Distinct().ToListAsync();
        var a2 = await db.Requerimientos.Where(r => r.SolicitudId == s2.Id).Select(r => r.AnalistaId).Distinct().ToListAsync();
        Assert.Single(a1); // los 3 entregables de la solicitud quedan con el mismo analista
        Assert.Single(a2);
        Assert.NotNull(a1[0]);
        Assert.NotEqual(a1[0], a2[0]); // el ranking se recalcula por bloque: la siguiente solicitud va al siguiente disponible
        Assert.Equal(2, correos); // un correo por analista con todos sus entregables
    }

    [Fact]
    public async Task Lider_elige_quien_toma_una_solicitud_de_scripts_completa()
    {
        var app = await App();
        var mp = await app.UserAsync("mayala@empresa.cl");
        var lider = await app.UserAsync("vnunez@empresa.cl");
        await app.Scripts.AutoAssignAsync(lider); // cola vacía
        var s = await app.Sols.EnviarAsync(mp, new SolicitudInput
        {
            Tipo = Equipo.Script, Cliente = "Banco de Chile", Proyecto = "Sucursales", Job = "26-0000-000-03", SL = "CEX", Tecnica = "Online",
            Fecha = Martes.AddDays(7), Entregables = ["Programación", "Cuotas", "Configurar nueva ola"],
        });
        var v = await app.Scripts.VistaAsync(lider);
        var bloque = Assert.Single(v.Cola);
        Assert.Equal(s.Codigo, bloque.Codigo);
        Assert.Equal(3, bloque.Items.Count);
        var elegido = v.Ranking[^1].Analista.Id; // el último del ranking: no el recomendado, sino quien tiene el perfil
        Assert.NotEqual(v.Ranking[0].Analista.Id, elegido);

        var dpSolo = await app.UserAsync("asalinas@empresa.cl");   // analista solo de DP: no toma scripts
        await Assert.ThrowsAsync<ReglaException>(() => app.Scripts.AsignarBloqueAsync(lider, bloque.Clave, dpSolo.Id));
        await Assert.ThrowsAsync<ReglaException>(() => app.Scripts.AsignarBloqueAsync(lider, bloque.Clave, mp.Id)); // comercial
        await Assert.ThrowsAsync<PermisoException>(() => app.Scripts.AsignarBloqueAsync(dpSolo, bloque.Clave, elegido));

        var (n, correo) = await app.Scripts.AsignarBloqueAsync(lider, bloque.Clave, elegido);
        Assert.Equal(3, n);
        Assert.True(correo);
        await using (var db = app.Factory.Create())
            Assert.All(await db.Requerimientos.Where(r => r.SolicitudId == s.Id).ToListAsync(), r => Assert.Equal(elegido, r.AnalistaId));
        Assert.Empty((await app.Scripts.VistaAsync(lider)).Cola);
        await Assert.ThrowsAsync<ReglaException>(() => app.Scripts.AsignarBloqueAsync(lider, bloque.Clave, elegido)); // ya asignada
    }

    [Fact]
    public async Task PM_de_Operaciones_solicita_a_las_tres_unidades_y_solo_ve_lo_suyo()
    {
        var app = await App();
        var pm = await app.UserAsync("porellana@empresa.cl");
        Assert.Equal(Rol.PM, pm.Rol);
        Assert.True(pm.EsSolicitante);
        Assert.Empty(pm.Equipos);
        foreach (var (tipo, ent) in new[] { (Equipo.Procesamiento, Catalogos.EntregablesDe(Equipo.Procesamiento)[0]), (Equipo.Codificacion, Catalogos.EntregablesDe(Equipo.Codificacion)[0]), (Equipo.Script, Catalogos.EntregablesDe(Equipo.Script)[0]) })
            await app.Sols.EnviarAsync(pm, new SolicitudInput { Tipo = tipo, Cliente = "Cliente PM", Proyecto = "Proyecto PM", Job = $"J-PM-{tipo}", SL = "CEX", Tecnica = "CATI", Fecha = Martes.AddDays(5), Entregables = [ent] });

        var mias = await app.Sols.MiasAsync(pm);
        Assert.Equal(3, mias.Count);
        Assert.All(mias, c => Assert.Equal("Paula Orellana (PM de Operaciones)", c.SolicitanteNombre));
        Assert.DoesNotContain(await app.Sols.MiasAsync(await app.UserAsync("mayala@empresa.cl")), c => c.Items.Any(r => r.Job.StartsWith("J-PM-"))); // el comercial no ve las del PM
        await using (var db = app.Factory.Create())
        {
            var mail = await db.Notificaciones.Where(n => n.Kind == NotificacionKind.Solicitud && n.Subject.Contains("Proyecto PM")).ToListAsync();
            Assert.Equal(3, mail.Count);
            Assert.All(mail, m => Assert.Contains("Paula Orellana (PM de Operaciones) ingresó una nueva solicitud", m.Body));
            Assert.All(mail, m => Assert.Equal("porellana@empresa.cl", m.Cc));
        }
        // como el comercial: no ve la carga, no crea ni asigna requerimientos
        Assert.Empty(await app.Reqs.TableroAsync(pm));
        Assert.Equal(0, await app.Reqs.AtrasadosAsync(pm));
        await Assert.ThrowsAsync<PermisoException>(() => app.Reqs.CrearAsync(pm, new Services.NuevoRequerimiento { Cliente = "C", Proyecto = "P", Job = "J", Tecnica = "CATI", Fecha = Martes }));
        await Assert.ThrowsAsync<PermisoException>(() => app.Sols.TodasAsync(pm));
        // un analista no puede ingresar solicitudes
        var analista = await app.UserAsync("asalinas@empresa.cl");
        await Assert.ThrowsAsync<PermisoException>(() => app.Sols.EnviarAsync(analista, new SolicitudInput { Cliente = "C", Proyecto = "P", Job = "J", Tecnica = "CATI", Fecha = Martes.AddDays(2), Entregables = ["Tablero"] }));
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
