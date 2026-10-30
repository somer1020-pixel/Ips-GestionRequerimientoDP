using System.Net;
using System.Text.RegularExpressions;
using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Tests;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionRequerimientoDP.Web.Tests;

/// <summary>Ingreso por RUT, solicitud de acceso y recuperación de contraseña por HTTP, con la app publicada bajo una sub-ruta (/gestionreq).</summary>
public partial class AccountFlowTests : IClassFixture<AccountFlowTests.Factory>
{
    const string Base = "/gestionreq";
    const string Clave = "Planilla#2026";
    readonly Factory _f;
    public AccountFlowTests(Factory f) => _f = f;

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public TestApp App { get; } = new(new DateOnly(2026, 10, 20));

        public Factory() => App.SeedAsync().GetAwaiter().GetResult();

        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:GestionRequerimientoDP", "Server=none;Database=none");
            b.UseSetting("Database:MigrateOnStartup", "false");
            b.UseSetting("Database:Seed", "false");
            b.UseSetting("Jobs:Enabled", "false");
            b.UseSetting("Auth:DemoMode", "false");
            b.UseSetting("Proxy:PathBase", Base);
            b.ConfigureTestServices(s => s.AddSingleton<IAppDbContextFactory>(App.Factory));
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")] private static partial Regex TokenRx();

    HttpClient Client() => _f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>Abre la página del formulario (cookie + token antiforgery) y lo envía.</summary>
    static async Task<HttpResponseMessage> Post(HttpClient c, string page, string action, Dictionary<string, string> data)
    {
        var html = await (await c.GetAsync(Base + page)).Content.ReadAsStringAsync();
        var m = TokenRx().Match(html);
        if (!m.Success) throw new Xunit.Sdk.XunitException("Sin token en " + page + ": " + Regex.Match(html, ".{0,80}RequestVerificationToken.{0,200}").Value);
        data["__RequestVerificationToken"] = WebUtility.HtmlDecode(m.Groups[1].Value);
        return await c.PostAsync(Base + action, new FormUrlEncodedContent(data));
    }

    static string Loc(HttpResponseMessage r) => r.Headers.Location is { } l ? Uri.UnescapeDataString(l.OriginalString)
        : throw new Xunit.Sdk.XunitException($"{(int)r.StatusCode}: {r.Content.ReadAsStringAsync().Result[..Math.Min(600, (int)(r.Content.Headers.ContentLength ?? 600))]}");

    [Fact]
    public async Task Pantalla_de_ingreso_usa_la_sub_ruta()
    {
        var html = await (await Client().GetAsync(Base + "/cuenta/ingresar")).Content.ReadAsStringAsync();
        Assert.Contains($"<base href=\"{Base}/\"", html);
        Assert.Contains("action=\"cuenta/login\"", html);
        Assert.Contains("href=\"cuenta/recuperar\"", html);
        Assert.Contains("href=\"cuenta/registro\"", html);
    }

    [Fact]
    public async Task Sin_sesion_redirige_al_ingreso_dentro_de_la_sub_ruta_y_vuelve_a_la_pagina_pedida()
    {
        var c = Client();
        var r = await c.GetAsync(Base + "/planificacion");
        Assert.StartsWith($"http://localhost{Base}/cuenta/ingresar?ReturnUrl={Uri.EscapeDataString(Base + "/planificacion")}", r.Headers.Location!.OriginalString);

        var login = await Post(c, $"/cuenta/ingresar?ReturnUrl={Uri.EscapeDataString(Base + "/planificacion")}", "/cuenta/login",
            new() { ["email"] = "cmendez@empresa.cl", ["password"] = Infrastructure.Data.DbSeeder.DemoPassword, ["returnUrl"] = Base + "/planificacion" });
        Assert.Equal($"{Base}/planificacion", login.Headers.Location!.OriginalString); // sin la sub-ruta duplicada
    }

    [Fact]
    public async Task Clave_incorrecta_no_revela_si_el_correo_existe_y_queda_en_la_auditoria()
    {
        var a = await Post(Client(), "/cuenta/ingresar", "/cuenta/login", new() { ["email"] = "noexiste@empresa.cl", ["password"] = "x" });
        var b = await Post(Client(), "/cuenta/ingresar", "/cuenta/login", new() { ["email"] = "cmendez@empresa.cl", ["password"] = "x" });
        static string Msg(HttpResponseMessage r) => Regex.Match(Loc(r), "error=([^&]*)").Groups[1].Value;
        Assert.StartsWith($"{Base}/cuenta/ingresar?manual=1", Loc(a));
        Assert.Equal(Msg(a), Msg(b));
        await using var db = _f.App.Factory.Create();
        Assert.Contains(await db.Auditoria.Where(x => x.Usuario == "noexiste@empresa.cl").Select(x => x.Accion).ToListAsync(), x => x == "LOGIN_FALLIDO");
    }

    [Fact]
    public async Task Solicitud_de_acceso_aprobada_permite_ingresar_con_RUT()
    {
        var c = Client();
        var r = await Post(c, "/cuenta/registro", "/cuenta/solicitar-acceso", new()
        {
            ["nombre"] = "Paula Soto", ["email"] = "psoto@empresa.cl", ["rut"] = "11.111.111-1", ["motivo"] = "Comercial CEX",
            ["password"] = Clave, ["confirmacion"] = Clave,
        });
        Assert.Contains("Recibimos tu solicitud", Loc(r));

        var pendiente = await Post(c, "/cuenta/ingresar", "/cuenta/login", new() { ["email"] = "11111111-1", ["password"] = Clave });
        Assert.Contains("en revisión", Loc(pendiente));
        var otraClave = await Post(c, "/cuenta/ingresar", "/cuenta/login", new() { ["email"] = "11111111-1", ["password"] = "Otra#2026x" });
        Assert.DoesNotContain("revisión", Loc(otraClave)); // sin la clave no se revela el estado

        await using (var db = _f.App.Factory.Create())
        {
            var u = await db.Usuarios.SingleAsync(x => x.Email == "psoto@empresa.cl");
            u.Aprobacion = Aprobacion.Aprobado;
            u.Activo = true;
            await db.SaveChangesAsync();
        }
        var ok = await Post(c, "/cuenta/ingresar", "/cuenta/login", new() { ["email"] = "11.111.111-1", ["password"] = Clave });
        Assert.Equal($"{Base}/solicitar", ok.Headers.Location!.OriginalString); // comercial
        Assert.Contains(ok.Headers.GetValues("Set-Cookie"), v => v.StartsWith("grdp.session=") && v.Contains($"path={Base}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Recuperacion_de_contrasena_de_punta_a_punta()
    {
        var c = Client();
        var r = await Post(c, "/cuenta/recuperar", "/cuenta/enviar-recuperacion", new() { ["login"] = "therrera@empresa.cl" });
        var loc = Loc(r);
        Assert.StartsWith($"{Base}/cuenta/recuperar?enviado=1", loc);
        // Sin SMTP en Development el enlace vuelve en la URL para poder probar.
        var enlace = Regex.Match(loc, "enlace=(.+)$").Groups[1].Value;
        Assert.StartsWith($"http://localhost{Base}/cuenta/restablecer?token=", enlace);
        var token = Uri.UnescapeDataString(enlace[(enlace.IndexOf("token=") + 6)..]);

        var noExiste = await Post(c, "/cuenta/recuperar", "/cuenta/enviar-recuperacion", new() { ["login"] = "nadie@empresa.cl" });
        Assert.Equal($"{Base}/cuenta/recuperar?enviado=1", Loc(noExiste)); // misma respuesta

        var debil = await Post(c, $"/cuenta/restablecer?token={Uri.EscapeDataString(token)}", "/cuenta/cambiar-clave",
            new() { ["token"] = token, ["password"] = "abc", ["confirmacion"] = "abc" });
        Assert.Contains("error=", Loc(debil));

        var ok = await Post(c, $"/cuenta/restablecer?token={Uri.EscapeDataString(token)}", "/cuenta/cambiar-clave",
            new() { ["token"] = token, ["password"] = Clave, ["confirmacion"] = Clave });
        Assert.Contains("contraseña fue actualizada", Loc(ok));

        var login = await Post(c, "/cuenta/ingresar", "/cuenta/login", new() { ["email"] = "therrera@empresa.cl", ["password"] = Clave });
        Assert.DoesNotContain("error=", login.Headers.Location!.OriginalString);

        await using var db = _f.App.Factory.Create();
        var acciones = await db.Auditoria.Select(a => a.Accion).ToListAsync();
        Assert.Contains("RESET_SOLICITADO", acciones);
        Assert.Contains("RESET_OK", acciones);
        Assert.Empty(await db.Notificaciones.Where(n => n.Body.Contains(token)).ToListAsync()); // el enlace no queda en el registro de correos
    }

    [Theory]
    [InlineData("/gestionreq/planificacion?semana=2026-10-19", "/planificacion?semana=2026-10-19")]
    [InlineData("/gestionreq", "/")]
    [InlineData("/gestionreqx/otra", "/gestionreqx/otra")]
    [InlineData("/planificacion", "/planificacion")]
    [InlineData("https://evil.com", "/")]
    public void ReturnUrl_relativo_a_la_sub_ruta(string input, string esperado) =>
        Assert.Equal(esperado, SsoRules.SafeReturnUrl(input, new PathString(Base)));
}
