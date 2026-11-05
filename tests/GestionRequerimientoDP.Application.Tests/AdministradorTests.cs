using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Tests;

/// <summary>
/// Administrador: ve y hace todo lo de un líder, es el único que administra usuarios y solo recibe correos de lo que le
/// asignan, el resumen diario de atrasados y las solicitudes de acceso.
/// </summary>
public class AdministradorTests
{
    static readonly DateOnly Martes = new(2026, 10, 20);
    const string AdminMail = "admin.ops@empresa.cl";

    sealed record Ctx(TestApp App, UsuarioService Users, AccesoService Acceso, UsuarioActual Lider, UsuarioActual Admin);

    static async Task<Ctx> Setup()
    {
        var app = new TestApp(Martes);
        await app.SeedAsync();
        var users = new UsuarioService(app.Factory, app.Changes, new PasswordService());
        var acceso = new AccesoService(app.Factory, app.Clock, app.Changes, new PasswordService());
        var lider = await app.UserAsync("cmendez@empresa.cl");
        // Sin administradores, el líder administra usuarios y crea el primero.
        Assert.True(await users.PuedeAdministrarAsync(lider));
        await users.SaveAsync(lider, new UsuarioInput { Nombre = "Admin Ops", Email = AdminMail, Rol = Rol.Admin, Equipos = [Equipo.Script] });
        return new(app, users, acceso, lider, await app.UserAsync(AdminMail));
    }

    static async Task<List<Notificacion>> Correos(TestApp app, string mail)
    {
        await using var db = app.Factory.Create();
        return (await db.Notificaciones.AsNoTracking().ToListAsync()).Where(n => n.To.Contains(mail) || n.Cc.Contains(mail)).ToList();
    }

    [Fact]
    public async Task Administrador_queda_sin_equipos_y_ve_todo_como_un_lider()
    {
        var c = await Setup();
        Assert.Empty(c.Admin.Equipos);
        Assert.True(c.Admin.EsLider);
        Assert.True(c.Admin.EsAdmin);
        Assert.Equal("Todos", Reglas.EquipoLabel((await c.Users.FindByEmailAsync(AdminMail))!));
        var todos = await c.App.Reqs.PropiosAsync(c.Lider);
        Assert.Equal(await c.App.Reqs.AtrasadosAsync(c.Lider), await c.App.Reqs.AtrasadosAsync(c.Admin));
        var (req, _) = await c.App.Reqs.CrearAsync(c.Admin, new NuevoRequerimiento { Tipo = Equipo.Procesamiento, Cliente = "C", Proyecto = "P", Job = "J-ADM", Tecnica = "CATI", Fecha = Martes, AnalistaId = c.Lider.Id });
        Assert.Equal(c.Lider.Id, req.AnalistaId);
    }

    [Fact]
    public async Task Existiendo_administrador_solo_el_administra_usuarios_y_accesos()
    {
        var c = await Setup();
        Assert.False(await c.Users.PuedeAdministrarAsync(c.Lider));
        Assert.True(await c.Users.PuedeAdministrarAsync(c.Admin));
        await Assert.ThrowsAsync<PermisoException>(() => c.Users.SaveAsync(c.Lider, new UsuarioInput { Nombre = "X", Email = "x@empresa.cl" }));
        await Assert.ThrowsAsync<PermisoException>(() => c.Acceso.PendientesAsync(c.Lider));
        var analista = await c.App.UserAsync("asalinas@empresa.cl");
        await Assert.ThrowsAsync<PermisoException>(() => c.Users.ToggleActivoAsync(c.Lider, analista.Id));
        await c.Users.SaveAsync(c.Admin, new UsuarioInput { Nombre = "X", Email = "x@empresa.cl" });
        Assert.Empty(await c.Acceso.PendientesAsync(c.Admin));
    }

    [Fact]
    public async Task No_se_puede_quedar_sin_administrador_activo()
    {
        var c = await Setup();
        await Assert.ThrowsAsync<ReglaException>(() => c.Users.ToggleActivoAsync(c.Admin, c.Admin.Id));
        var input = UsuarioInput.From((await c.Users.FindByEmailAsync(AdminMail))!);
        input.Rol = Rol.Lider;
        await Assert.ThrowsAsync<ReglaException>(() => c.Users.SaveAsync(c.Admin, input));

        // Con un segundo administrador, el primero sí puede pasar a otro rol (lo hace el otro administrador).
        await c.Users.SaveAsync(c.Admin, new UsuarioInput { Nombre = "Admin Dos", Email = "admin.dos@empresa.cl", Rol = Rol.Admin });
        var dos = await c.App.UserAsync("admin.dos@empresa.cl");
        await c.Users.SaveAsync(dos, input);
        Assert.Equal(Rol.Lider, (await c.Users.FindByEmailAsync(AdminMail))!.Rol);
    }

    [Fact]
    public async Task No_recibe_correos_de_solicitudes_ni_asignaciones_de_terceros()
    {
        var c = await Setup();
        var mp = await c.App.UserAsync("mayala@empresa.cl");
        await c.App.Sols.EnviarAsync(mp, new SolicitudInput
        {
            Tipo = Equipo.Codificacion, Cliente = "C", Proyecto = "P", Job = "J-ADM", SL = "CEX", Tecnica = "CATI", Fecha = Martes.AddDays(3), Entregables = ["Libro de códigos"],
        });
        var analista = await c.App.UserAsync("asalinas@empresa.cl");
        await c.App.Reqs.CrearAsync(c.Lider, new NuevoRequerimiento { Tipo = Equipo.Procesamiento, Cliente = "C", Proyecto = "P", Job = "J-TER", Tecnica = "CATI", Fecha = Martes, AnalistaId = analista.Id });
        Assert.Empty(await Correos(c.App, AdminMail));
    }

    [Fact]
    public async Task Recibe_lo_que_le_asignan_el_resumen_de_atrasados_y_las_solicitudes_de_acceso()
    {
        var c = await Setup();
        var (req, correo) = await c.App.Reqs.CrearAsync(c.Lider, new NuevoRequerimiento { Tipo = Equipo.Script, Cliente = "C", Proyecto = "P", Job = "J-ADM", Tecnica = "Online", Fecha = Martes, Entregable = "Programación", AnalistaId = c.Admin.Id });
        Assert.True(correo);
        Assert.Contains(await Correos(c.App, AdminMail), n => n.Kind == NotificacionKind.Asignacion && n.To == AdminMail);

        Assert.True(await c.App.Notif.CheckOverdueAsync(true) > 0);
        var atrasos = (await Correos(c.App, AdminMail)).Where(n => n.Kind == NotificacionKind.Atraso).ToList();
        Assert.NotEmpty(atrasos);
        Assert.Contains(atrasos, n => n.Body.Contains("Post test AFP Capital")); // atrasado de otro analista: va en copia

        await c.Acceso.SolicitarDesdeAdAsync(@"DOM\nuevo", "nuevo@empresa.cl", "Nuevo Usuario", false);
        var acceso = Assert.Single((await Correos(c.App, "")).Where(n => n.Kind == NotificacionKind.Acceso));
        Assert.Contains(AdminMail, acceso.To);
        Assert.DoesNotContain("cmendez@empresa.cl", acceso.To);
    }
}
