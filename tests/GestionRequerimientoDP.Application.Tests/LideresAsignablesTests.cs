using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Tests;

/// <summary>Un requerimiento se puede asignar a otro líder o a uno mismo, además de los analistas del equipo.</summary>
public class LideresAsignablesTests
{
    static readonly DateOnly Martes = new(2026, 10, 20);

    static async Task<(TestApp App, UsuarioActual Carolina, UsuarioActual Ricardo)> Setup()
    {
        var app = new TestApp(Martes);
        await app.SeedAsync();
        return (app, await app.UserAsync("cmendez@empresa.cl"), await app.UserAsync("rfuentes@empresa.cl"));
    }

    static Task<(Requerimiento Req, bool Correo)> Nuevo(TestApp app, UsuarioActual me, int? analista, Equipo tipo = Equipo.Procesamiento) =>
        app.Reqs.CrearAsync(me, new NuevoRequerimiento { Tipo = tipo, Cliente = "C", Proyecto = "P", Job = "J-LID", Tecnica = "CATI", Fecha = Martes, AnalistaId = analista });

    [Fact]
    public void Asignable_incluye_lideres_activos_de_cualquier_equipo()
    {
        var lider = new Usuario { Id = 1, Rol = Rol.Lider, Activo = true, Equipos = [new UsuarioEquipo { Equipo = Equipo.Codificacion }] };
        var analista = new Usuario { Id = 2, Rol = Rol.Analista, Activo = true, Equipos = [new UsuarioEquipo { Equipo = Equipo.Script }] };
        Assert.True(Reglas.Asignable(lider, Equipo.Procesamiento));
        Assert.False(Reglas.Asignable(analista, Equipo.Procesamiento)); // los analistas, solo de su equipo
        Assert.False(Reglas.Asignable(new Usuario { Rol = Rol.Lider, Activo = false }, Equipo.Script));
        Assert.False(Reglas.Asignable(new Usuario { Rol = Rol.Comercial, Activo = true }, Equipo.Script));
        Assert.Equal([2, 1], Reglas.Asignables([lider, analista], Equipo.Script).Select(u => u.Id)); // analistas primero
        Assert.Equal("Ana (líder)", Reglas.NombreResponsable(new Usuario { Nombre = "Ana", Rol = Rol.Lider }));
    }

    [Fact]
    public async Task Asignarse_a_si_mismo_no_envia_correo_y_a_otro_lider_si()
    {
        var (app, carolina, ricardo) = await Setup();
        var (req, correo) = await Nuevo(app, carolina, carolina.Id);
        Assert.Equal(carolina.Id, req.AnalistaId);
        Assert.False(correo);

        Assert.True(await app.Reqs.AsignarAsync(carolina, req.Id, ricardo.Id));
        await using var db = app.Factory.Create();
        Assert.Equal(ricardo.Id, (await db.Requerimientos.SingleAsync(r => r.Id == req.Id)).AnalistaId);
        Assert.Single(await db.Notificaciones.Where(n => n.Kind == NotificacionKind.Asignacion && n.To == "rfuentes@empresa.cl").ToListAsync());
        Assert.Empty(await db.Notificaciones.Where(n => n.Kind == NotificacionKind.Asignacion && n.To == "cmendez@empresa.cl").ToListAsync());
    }

    [Fact]
    public async Task No_se_asigna_a_comerciales_ni_a_lideres_inactivos()
    {
        var (app, carolina, _) = await Setup();
        var comercial = await app.UserAsync("mayala@empresa.cl");
        await Assert.ThrowsAsync<ReglaException>(() => Nuevo(app, carolina, comercial.Id));
        await using (var db = app.Factory.Create())
        {
            (await db.Usuarios.SingleAsync(u => u.Email == "vnunez@empresa.cl")).Activo = false;
            await db.SaveChangesAsync();
        }
        var valeria = await app.UserAsync("vnunez@empresa.cl");
        await Assert.ThrowsAsync<ReglaException>(() => Nuevo(app, carolina, valeria.Id));
    }

    [Fact]
    public async Task Lider_ve_lo_asignado_en_sus_propios_y_aparece_en_la_planificacion_sin_alterar_el_promedio()
    {
        var (app, carolina, ricardo) = await Setup();
        var (req, _) = await Nuevo(app, carolina, ricardo.Id);
        Assert.Contains(await app.Reqs.PropiosAsync(ricardo), r => r.Id == req.Id);

        await using var db = app.Factory.Create();
        var users = await db.Usuarios.Include(u => u.Equipos).AsNoTracking().ToListAsync();
        var reqs = await db.Requerimientos.AsNoTracking().ToListAsync();
        var week = Fechas.MondayOf(Martes);
        var v = PlanEngine.Build(Equipo.Procesamiento, week, users, reqs, 10);
        var card = Assert.Single(v.Cards, c => c.Analista.Id == ricardo.Id);
        Assert.Equal("Líder", card.Label);
        Assert.Equal(1, card.N);
        Assert.DoesNotContain(v.Cards, c => c.Analista.Id == carolina.Id); // sin entregables esta semana → sin fila

        var sinLider = PlanEngine.Build(Equipo.Procesamiento, week, users, reqs.Where(r => r.Id != req.Id).ToList(), 10);
        Assert.Equal(sinLider.Stats.Avg, v.Stats.Avg);
        Assert.Equal(sinLider.Stats.Spread, v.Stats.Spread); // el entregable del líder no cambia el promedio de los analistas
    }
}
