using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Infrastructure.Data;
using GestionRequerimientoDP.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Tests;

public class RutTests
{
    [Theory]
    [InlineData("11.111.111-1", "11111111-1")]
    [InlineData("111111111", "11111111-1")]
    [InlineData("12345678-k", "12345678-K")]
    [InlineData(" 7.654.321-6 ", "7654321-6")]
    [InlineData("abc", "")]
    [InlineData("1234-56", "")]
    [InlineData(null, "")]
    public void Normaliza(string? raw, string esperado) => Assert.Equal(esperado, Rut.Normalize(raw));

    [Theory]
    [InlineData("11.111.111-1", true)]
    [InlineData("12.345.678-5", true)]
    [InlineData("7.654.321-6", true)]
    [InlineData("12.345.678-K", false)]
    [InlineData("11.111.111-2", false)]
    public void Valida_digito_verificador(string rut, bool valido) => Assert.Equal(valido, Rut.IsValid(rut));

    [Fact]
    public void Formatea_con_puntos() => Assert.Equal("12.345.678-5", Rut.Format("123456785"));

    [Theory]
    [InlineData("12.345.678-5", true)]
    [InlineData("persona@empresa.cl", false)]
    [InlineData("persona", false)]
    public void Distingue_rut_de_correo(string login, bool rut) => Assert.Equal(rut, Rut.IsCandidate(login));
}

public class PoliticaContrasenaTests
{
    [Fact]
    public void Acepta_contrasena_robusta() => Assert.Empty(PoliticaContrasena.Validar("Planilla#2026", "persona@empresa.cl", "Persona Ejemplo"));

    [Theory]
    [InlineData("Ab1!", "al menos 8")]
    [InlineData("planilla#2026", "mayúscula")]
    [InlineData("PLANILLA#2026", "minúscula")]
    [InlineData("Planilla#xxxx", "número")]
    [InlineData("Planilla2026", "símbolo")]
    [InlineData("Persona#2026x", "correo")]
    public void Rechaza(string pw, string fragmento) =>
        Assert.Contains(PoliticaContrasena.Validar(pw, "persona@empresa.cl", "Otro Nombre"), e => e.Contains(fragmento));
}

public class AccesoServiceTests
{
    static readonly DateOnly Martes = new(2026, 10, 20);
    const string Clave = "Planilla#2026";

    static async Task<(TestApp App, AccesoService Svc, UsuarioActual Lider)> Setup()
    {
        var app = new TestApp(Martes);
        await app.SeedAsync();
        return (app, new AccesoService(app.Factory, app.Clock, app.Changes, new PasswordService()), await app.UserAsync("cmendez@empresa.cl"));
    }

    static RegistroInput Registro(string email = "nueva.persona@empresa.cl", string? rut = "12.345.678-5", string clave = Clave) => new()
    {
        Nombre = "Nueva Persona", Email = email, Rut = rut, Motivo = "Área comercial, para ingresar solicitudes", Password = clave, Confirmacion = clave,
    };

    [Fact]
    public async Task Ingresa_por_correo_o_por_rut()
    {
        var (app, svc, lider) = await Setup();
        var users = new UsuarioService(app.Factory, app.Changes, new PasswordService());
        var input = UsuarioInput.From((await users.FindByEmailAsync("mayala@empresa.cl"))!);
        input.Rut = "7654321-6";
        await users.SaveAsync(lider, input);

        Assert.Equal(LoginEstado.Ok, (await svc.ValidarAsync("MAYALA@empresa.cl", DbSeeder.DemoPassword, false)).Estado);
        Assert.Equal(LoginEstado.Ok, (await svc.ValidarAsync("7.654.321-6", DbSeeder.DemoPassword, false)).Estado);
        Assert.Equal(LoginEstado.ClaveIncorrecta, (await svc.ValidarAsync("76543216", "otra", false)).Estado);
        Assert.Equal(LoginEstado.NoExiste, (await svc.ValidarAsync("nadie@empresa.cl", "x", false)).Estado);
    }

    [Fact]
    public async Task Solicitud_queda_pendiente_avisa_a_lideres_y_no_puede_ingresar_hasta_aprobarla()
    {
        var (app, svc, lider) = await Setup();
        var u = await svc.SolicitarAsync(Registro());
        Assert.True(u.Pendiente);
        Assert.False(u.Activo);
        Assert.Equal("12345678-5", u.Rut);
        Assert.Equal(LoginEstado.Pendiente, (await svc.ValidarAsync("12.345.678-5", Clave, false)).Estado);
        await using (var db = app.Factory.Create())
            Assert.Single(await db.Notificaciones.Where(n => n.Kind == NotificacionKind.Acceso).ToListAsync());

        // No sale en los listados de asignación mientras está pendiente.
        Assert.Contains(await svc.PendientesAsync(lider), p => p.Id == u.Id);

        var input = UsuarioInput.From(u);
        input.Rol = Rol.Analista;
        input.Equipos = [Equipo.Codificacion];
        var aprobado = await svc.AprobarAsync(lider, input);
        Assert.False(aprobado.Pendiente);
        Assert.True(aprobado.Activo);
        Assert.Equal(LoginEstado.Ok, (await svc.ValidarAsync("nueva.persona@empresa.cl", Clave, false)).Estado);
        Assert.Empty(await svc.PendientesAsync(lider));
        await using (var db = app.Factory.Create())
            Assert.Contains(await db.Notificaciones.Select(n => n.Subject).ToListAsync(), s => s.Contains("aprobado"));
    }

    [Fact]
    public async Task Solicitud_valida_datos_y_duplicados()
    {
        var (_, svc, _) = await Setup();
        await Assert.ThrowsAsync<ReglaException>(() => svc.SolicitarAsync(Registro(rut: "12.345.678-K")));
        await Assert.ThrowsAsync<ReglaException>(() => svc.SolicitarAsync(Registro(email: "cmendez@empresa.cl", rut: null)));
        await Assert.ThrowsAsync<ReglaException>(() => svc.SolicitarAsync(Registro(clave: "corta")));
        await svc.SolicitarAsync(Registro());
        var dup = await Assert.ThrowsAsync<ReglaException>(() => svc.SolicitarAsync(Registro()));
        Assert.Contains("en revisión", dup.Message);
    }

    [Fact]
    public async Task Rechazar_elimina_la_solicitud_y_solo_lo_hace_un_lider()
    {
        var (app, svc, lider) = await Setup();
        var u = await svc.SolicitarAsync(Registro());
        var analista = await app.UserAsync("asalinas@empresa.cl");
        await Assert.ThrowsAsync<PermisoException>(() => svc.RechazarAsync(analista, u.Id));
        await svc.RechazarAsync(lider, u.Id);
        Assert.Equal(LoginEstado.NoExiste, (await svc.ValidarAsync("nueva.persona@empresa.cl", Clave, false)).Estado);
    }

    [Fact]
    public async Task Cuenta_AD_nueva_queda_pendiente_y_administrador_entra_como_lider()
    {
        var (app, svc, _) = await Setup();
        var p = await svc.SolicitarDesdeAdAsync(@"DOM\nuevo", "nuevo@empresa.cl", "Nuevo Usuario", false);
        Assert.True(p.Pendiente);
        await svc.SolicitarDesdeAdAsync(@"DOM\nuevo", "nuevo@empresa.cl", "Nuevo Usuario", false); // segundo intento: sin aviso extra
        await using (var db = app.Factory.Create())
            Assert.Single(await db.Notificaciones.Where(n => n.Kind == NotificacionKind.Acceso).ToListAsync());

        var admin = await svc.SolicitarDesdeAdAsync(@"DOM\admin", "admin@empresa.cl", "Admin", true);
        Assert.Equal(Rol.Lider, admin.Rol);
        Assert.True(admin.Activo);
        Assert.False(admin.Pendiente);

        // Un administrador ya existente y desactivado por un líder no se reactiva solo.
        await using (var db = app.Factory.Create())
        {
            (await db.Usuarios.SingleAsync(x => x.Id == admin.Id)).Activo = false;
            await db.SaveChangesAsync();
        }
        Assert.False((await svc.SolicitarDesdeAdAsync(@"DOM\admin", "admin@empresa.cl", "Admin", true)).Activo);
    }

    [Fact]
    public async Task Recuperacion_con_token_de_un_solo_uso_que_vence()
    {
        var (app, svc, _) = await Setup();
        var (reset, motivo) = await svc.CrearResetAsync("asalinas@empresa.cl", "10.0.0.1");
        Assert.Null(motivo);
        await using (var db = app.Factory.Create())
            Assert.DoesNotContain(await db.ResetTokens.Select(t => t.TokenHash).ToListAsync(), h => h == reset!.Token); // solo el hash

        Assert.False((await svc.RestablecerAsync(reset!.Token, "debil", "debil", "")).Ok);
        var (ok, _) = await svc.RestablecerAsync(reset.Token, Clave, Clave, "");
        Assert.True(ok);
        Assert.Equal(LoginEstado.Ok, (await svc.ValidarAsync("asalinas@empresa.cl", Clave, false)).Estado);
        Assert.False((await svc.RestablecerAsync(reset.Token, Clave + "x", Clave + "x", "")).Ok); // ya usado

        var (r2, _) = await svc.CrearResetAsync("asalinas@empresa.cl", "");
        app.Time.Value = app.Time.Value.Add(AccesoService.ResetVigencia).AddMinutes(1);
        var (u, err) = await svc.ValidarResetAsync(r2!.Token);
        Assert.Null(u);
        Assert.Contains("venció", err);
    }

    [Fact]
    public async Task Recuperacion_limita_intentos_y_solo_vale_el_ultimo_enlace()
    {
        var (_, svc, _) = await Setup();
        var (a, _) = await svc.CrearResetAsync("asalinas@empresa.cl", "");
        var (b, _) = await svc.CrearResetAsync("asalinas@empresa.cl", "");
        Assert.Null((await svc.ValidarResetAsync(a!.Token)).Usuario);
        Assert.NotNull((await svc.ValidarResetAsync(b!.Token)).Usuario);
        await svc.CrearResetAsync("asalinas@empresa.cl", "");
        Assert.Equal("limite", (await svc.CrearResetAsync("asalinas@empresa.cl", "")).Motivo);
        Assert.Equal("no existe", (await svc.CrearResetAsync("nadie@empresa.cl", "")).Motivo);
    }

    [Fact]
    public async Task Auditoria_registra_y_solo_la_ve_un_lider()
    {
        var (app, _, lider) = await Setup();
        var audit = new AuditoriaService(app.Factory, app.Clock);
        await audit.RegistrarAsync("cmendez@empresa.cl", "LOGIN_OK", "Local", "10.0.0.5");
        await audit.RegistrarAsync("x@empresa.cl", "LOGIN_FALLIDO", "Local", "200.1.1.1", new string('z', 5000));
        var todo = await audit.ListAsync(lider, null);
        Assert.Equal(2, todo.Count);
        Assert.Equal(1000, todo.Single(a => a.Accion == "LOGIN_FALLIDO").Detalle.Length);
        Assert.Single(await audit.ListAsync(lider, "FALLIDO"));
        var analista = await app.UserAsync("asalinas@empresa.cl");
        await Assert.ThrowsAsync<PermisoException>(() => audit.ListAsync(analista, null));
    }
}
