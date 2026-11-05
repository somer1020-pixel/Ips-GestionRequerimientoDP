using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Tests;

public class MencionesReglasTests
{
    static Usuario U(int id, string n) => new() { Id = id, Nombre = n, Activo = true };

    [Fact]
    public void Ubica_menciones_por_nombre_completo_sin_confundir_prefijos()
    {
        var ana = U(1, "Ana Pérez"); var anaMaria = U(2, "Ana Pérez Soto"); var luis = U(3, "Luis Ruiz");
        var m = Reglas.Menciones("Hola @ana pérez soto y @Luis Ruiz, revisen. @Luis Ruizz no existe; correo luis@x.cl", [ana, anaMaria, luis]);
        Assert.Equal([2, 3], m.Select(u => u.Id));
    }

    [Fact]
    public void Trozos_resaltan_solo_las_menciones()
    {
        var t = Reglas.Trozos("Ok @Luis Ruiz, gracias", [U(3, "Luis Ruiz")]);
        Assert.Equal(["Ok ", "@Luis Ruiz", ", gracias"], t.Select(x => x.Texto));
        Assert.Equal([null, 3, null], t.Select(x => x.UsuarioId));
    }
}

public class ComentarioServiceTests
{
    static readonly DateOnly Martes = new(2026, 10, 20);
    const string Enlace = "https://app/r/1";

    static async Task<(TestApp App, ComentarioService Svc)> Setup()
    {
        var app = new TestApp(Martes);
        await app.SeedAsync();
        return (app, new ComentarioService(app.Factory, app.Clock, app.Changes));
    }

    static async Task<Requerimiento> DeSolicitud(TestApp app, string solicitanteMail)
    {
        await using var db = app.Factory.Create();
        var sid = (await db.Usuarios.SingleAsync(u => u.Email == solicitanteMail)).Id;
        return await db.Requerimientos.Include(r => r.Solicitud).FirstAsync(r => r.Solicitud != null && r.Solicitud.SolicitanteId == sid && r.AnalistaId != null);
    }

    static async Task<List<Notificacion>> Menciones(TestApp app)
    {
        await using var db = app.Factory.Create();
        return await db.Notificaciones.Where(n => n.Kind == NotificacionKind.Mencion).ToListAsync();
    }

    [Fact]
    public async Task Mencion_avisa_por_correo_con_texto_datos_y_enlace_y_copia_al_responsable()
    {
        var (app, svc) = await Setup();
        var r = await DeSolicitud(app, "mayala@empresa.cl");
        var lider = await app.UserAsync("cmendez@empresa.cl");
        await using (var db = app.Factory.Create())
        {
            var comercial = await db.Usuarios.SingleAsync(u => u.Email == "mayala@empresa.cl");
            var avisados = await svc.AgregarAsync(lider, r.Id, $"@{comercial.Nombre} ¿confirmas el cuestionario final?", Enlace);
            Assert.Equal([comercial.Id], avisados.Select(u => u.Id));
        }
        var mail = Assert.Single(await Menciones(app));
        Assert.Equal("mayala@empresa.cl", mail.To);
        var resp = (await app.Factory.Create().Usuarios.SingleAsync(u => u.Id == r.AnalistaId)).Email;
        Assert.Equal(resp, mail.Cc); // el responsable va en copia
        Assert.Contains("te mencionó", mail.Subject);
        Assert.Contains(r.Job, mail.Subject);
        Assert.Contains("¿confirmas el cuestionario final?", mail.Body);
        Assert.Contains(Enlace, mail.Body);
        Assert.Contains(r.Entregable, mail.Body);
    }

    [Fact]
    public async Task Sin_menciones_no_hay_correo_y_el_comentario_queda_en_el_hilo()
    {
        var (app, svc) = await Setup();
        var r = await DeSolicitud(app, "mayala@empresa.cl");
        var lider = await app.UserAsync("cmendez@empresa.cl");
        Assert.Empty(await svc.AgregarAsync(lider, r.Id, "Materiales recibidos, parte mañana.", Enlace));
        Assert.Empty(await Menciones(app));
        var hilo = await svc.HiloAsync(lider, r.Id);
        Assert.Equal("Materiales recibidos, parte mañana.", Assert.Single(hilo!.Comentarios).Texto);
        await Assert.ThrowsAsync<ReglaException>(() => svc.AgregarAsync(lider, r.Id, "   ", Enlace));
        await Assert.ThrowsAsync<ReglaException>(() => svc.AgregarAsync(lider, r.Id, new string('x', Reglas.ComentarioMax + 1), Enlace));
    }

    [Fact]
    public async Task Solicitante_comenta_lo_suyo_pero_no_lo_ajeno_y_el_mencionado_puede_responder()
    {
        var (app, svc) = await Setup();
        var r = await DeSolicitud(app, "mayala@empresa.cl");
        var comercial = await app.UserAsync("mayala@empresa.cl");
        var otroComercial = await app.UserAsync("wmunoz@empresa.cl");
        Assert.NotNull(await svc.HiloAsync(comercial, r.Id));
        Assert.Null(await svc.HiloAsync(otroComercial, r.Id));
        await Assert.ThrowsAsync<PermisoException>(() => svc.AgregarAsync(otroComercial, r.Id, "hola", Enlace));

        // El comercial menciona a un analista que no tiene el requerimiento asignado: desde ese momento participa del hilo.
        await using var db = app.Factory.Create();
        var ajeno = await db.Usuarios.FirstAsync(u => u.Rol == Rol.Analista && u.Activo && u.Id != r.AnalistaId);
        var yo = await app.UserAsync(ajeno.Email);
        Assert.Null(await svc.HiloAsync(yo, r.Id));
        await svc.AgregarAsync(comercial, r.Id, $"@{ajeno.Nombre} ¿puedes apoyar?", Enlace);
        Assert.NotNull(await svc.HiloAsync(yo, r.Id));
        await svc.AgregarAsync(yo, r.Id, $"@{comercial.Nombre} sí, lo reviso hoy", Enlace);
        Assert.Equal(2, (await Menciones(app)).Count);
    }

    [Fact]
    public async Task No_se_menciona_a_si_mismo_ni_a_inactivos_y_el_autor_no_va_en_copia()
    {
        var (app, svc) = await Setup();
        var r = await DeSolicitud(app, "mayala@empresa.cl");
        await using var db = app.Factory.Create();
        var resp = await db.Usuarios.SingleAsync(u => u.Id == r.AnalistaId);
        var inactivo = await db.Usuarios.FirstAsync(u => u.Rol == Rol.Analista && u.Id != resp.Id);
        inactivo.Activo = false;
        await db.SaveChangesAsync();
        var me = await app.UserAsync(resp.Email);
        Assert.Empty(await svc.AgregarAsync(me, r.Id, $"@{resp.Nombre} @{inactivo.Nombre} nota para mí", Enlace));
        var lider = await db.Usuarios.SingleAsync(u => u.Email == "cmendez@empresa.cl");
        await svc.AgregarAsync(me, r.Id, $"@{lider.Nombre} listo", Enlace);
        var mail = Assert.Single(await Menciones(app));
        Assert.Equal("", mail.Cc); // el autor es el responsable: no se copia
    }

    [Fact]
    public async Task Autor_de_comentarios_tiene_historial_y_eliminar_el_requerimiento_borra_su_hilo()
    {
        var (app, svc) = await Setup();
        var r = await DeSolicitud(app, "mayala@empresa.cl");
        var analista = await app.UserAsync("asalinas@empresa.cl");
        var lider = await app.UserAsync("cmendez@empresa.cl");
        await svc.AgregarAsync(lider, r.Id, "nota", Enlace);
        var users = new UsuarioService(app.Factory, app.Changes, new GestionRequerimientoDP.Infrastructure.Security.PasswordService());
        Assert.Contains(lider.Id, await users.ConHistorialAsync());
        await app.Reqs.EliminarAsync(lider, r.Id);
        await using var db = app.Factory.Create();
        Assert.False(await db.Comentarios.AnyAsync(c => c.RequerimientoId == r.Id));
        _ = analista;
    }
}
