using System.Net;
using System.Text.RegularExpressions;
using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionRequerimientoDP.Web.Tests;

/// <summary>Protecciones de los formularios de cuenta: fuerza bruta, formularios vencidos, límite por IP, cabeceras y modo demostración.</summary>
public partial class SecurityTests
{
    const string Clave = "Planilla#2026";

    sealed class Factory(string env = "Development", int porMinuto = 1000, bool demo = false) : WebApplicationFactory<Program>
    {
        public TestApp App { get; } = Seed();
        static TestApp Seed() { var a = new TestApp(new DateOnly(2026, 10, 20)); a.SeedAsync().GetAwaiter().GetResult(); return a; }

        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment(env);
            b.UseSetting("ConnectionStrings:GestionRequerimientoDP", "Server=none;Database=none");
            b.UseSetting("Database:MigrateOnStartup", "false");
            b.UseSetting("Database:Seed", "false");
            b.UseSetting("Jobs:Enabled", "false");
            b.UseSetting("Auth:DemoMode", demo.ToString());
            b.UseSetting("Seguridad:FormulariosPorMinutoPorIp", porMinuto.ToString());
            b.ConfigureTestServices(s => s.AddSingleton<IAppDbContextFactory>(App.Factory));
        }

        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")] private static partial Regex TokenRx();

    static async Task<HttpResponseMessage> Login(HttpClient c, string email, string password)
    {
        var html = await (await c.GetAsync("/cuenta/ingresar?manual=1")).Content.ReadAsStringAsync();
        var data = new Dictionary<string, string> { ["email"] = email, ["password"] = password, ["__RequestVerificationToken"] = WebUtility.HtmlDecode(TokenRx().Match(html).Groups[1].Value) };
        return await c.PostAsync("/cuenta/login", new FormUrlEncodedContent(data));
    }

    static string Loc(HttpResponseMessage r) => Uri.UnescapeDataString(r.Headers.Location?.OriginalString ?? $"[{(int)r.StatusCode}]");

    [Fact]
    public async Task Formulario_sin_token_antiforgery_vuelve_al_formulario_en_vez_de_error_500()
    {
        using var f = new Factory();
        var r = await f.Client().PostAsync("/cuenta/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = "a", ["password"] = "b" }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("venció", Loc(r));
    }

    [Fact]
    public async Task Cinco_intentos_fallidos_bloquean_la_cuenta_aunque_cambie_el_formato_del_login()
    {
        using var f = new Factory();
        var c = f.Client();
        foreach (var variante in new[] { "rsalgado@empresa.cl", "RSALGADO@empresa.cl", " rsalgado@empresa.cl", "RSalgado@Empresa.cl", "rsalgado@EMPRESA.CL" })
            Assert.Contains("incorrectos", Loc(await Login(c, variante, "Incorrecta#1")));

        var bloqueado = await Login(c, "rsalgado@empresa.cl", "demo123"); // contraseña correcta, pero bloqueada
        Assert.Contains("Demasiados intentos", Loc(bloqueado));
        Assert.False(bloqueado.Headers.TryGetValues("Set-Cookie", out var ck) && ck.Any(v => v.StartsWith("grdp.session=")));
        Assert.DoesNotContain("Demasiados", Loc(await Login(c, "cgomez@empresa.cl", "demo123"))); // las demás cuentas siguen

        await using var db = f.App.Factory.Create();
        Assert.Contains(await db.Auditoria.Select(a => a.Accion).ToListAsync(), a => a == "LOGIN_BLOQUEADO_INTENTOS");
    }

    [Fact]
    public async Task Limite_de_envios_por_IP()
    {
        using var f = new Factory(porMinuto: 3);
        var c = f.Client();
        for (var i = 0; i < 3; i++) Assert.DoesNotContain("Demasiadas solicitudes", Loc(await Login(c, $"x{i}@empresa.cl", "y")));
        Assert.Contains("Demasiadas solicitudes", Loc(await Login(c, "x9@empresa.cl", "y")));
    }

    [Fact]
    public async Task Cabeceras_de_seguridad()
    {
        using var f = new Factory();
        var r = await f.Client().GetAsync("/cuenta/ingresar");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("same-origin", r.Headers.GetValues("Referrer-Policy").Single());
        Assert.Single(r.Headers.GetValues("X-Frame-Options"));
        Assert.Contains("camera=()", r.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public async Task Modo_demostracion_no_se_activa_fuera_de_Development()
    {
        using var f = new Factory(env: "Production", demo: true);
        var html = await (await f.Client().GetAsync("/cuenta/ingresar?manual=1")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Carolina Méndez", html); // sin botones de acceso sin contraseña
        Assert.Contains("incorrectos", Loc(await Login(f.Client(), "cmendez@empresa.cl", "cualquiera")));
    }
}
