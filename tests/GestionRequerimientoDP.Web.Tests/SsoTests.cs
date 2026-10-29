using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Tests;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GestionRequerimientoDP.Web.Tests;

public class SsoTicketTests
{
    const string Secret = "clave-de-prueba-de-al-menos-32-caracteres!!";
    readonly FixedTime _time = new(new DateOnly(2026, 10, 29));

    SsoTicketService Svc(MemoryCache? cache = null) => new(
        new Monitor(new SsoOptions { TicketSecret = Secret, TicketTtlSeconds = 60 }), cache ?? new MemoryCache(new MemoryCacheOptions()), _time, NullLogger<SsoTicketService>.Instance);

    [Fact]
    public void Ticket_valido_se_consume_una_sola_vez()
    {
        var svc = Svc();
        var t = svc.Create("sergio.ramirez@ipsos.com", "sergio.ramirez@ipsos.com", "Lider");
        var ok = svc.Validate(t);
        Assert.Equal("sergio.ramirez@ipsos.com", ok!.Email);
        Assert.Null(svc.Validate(t)); // anti-replay
    }

    [Fact]
    public void Firma_alterada_o_expirado_se_rechaza()
    {
        var svc = Svc();
        var t = svc.Create("u", "u@ipsos.com");
        var parts = t.Split('.');
        var otro = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin|admin@ipsos.com|Lider|1|x")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Null(svc.Validate($"{otro}.{parts[1]}"));
        Assert.Null(svc.Validate("basura"));
        Assert.Null(svc.Validate(""));
        var t2 = svc.Create("u", "u@ipsos.com");
        _time.Value = _time.Value.AddSeconds(61);
        Assert.Null(svc.Validate(t2));
    }

    [Fact]
    public void Formato_compatible_con_Ipsos_Bhe_Auth()
    {
        // Ticket construido como lo hace SsoTicketService de Ipsos.Bhe.Auth (payload|firma HMAC-SHA256, Base64Url).
        var payload = $"sergio.ramirez|sergio.ramirez@ipsos.com|User|{_time.GetUtcNow().ToUnixTimeSeconds()}|{Guid.NewGuid():N}";
        var key = Encoding.UTF8.GetBytes(Secret.PadRight(32, 'X'));
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var ticket = $"{B64(Encoding.UTF8.GetBytes(payload))}.{B64(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)))}";
        Assert.Equal("sergio.ramirez@ipsos.com", Svc().Validate(ticket)!.Email);
    }

    [Fact]
    public void Secreto_corto_no_se_acepta()
    {
        var svc = new SsoTicketService(new Monitor(new SsoOptions { TicketSecret = "corto" }), new MemoryCache(new MemoryCacheOptions()), _time, NullLogger<SsoTicketService>.Instance);
        Assert.Throws<InvalidOperationException>(() => svc.Create("u", "u@x"));
    }

    sealed class Monitor(SsoOptions v) : IOptionsMonitor<SsoOptions>
    {
        public SsoOptions CurrentValue => v;
        public SsoOptions Get(string? name) => v;
        public IDisposable? OnChange(Action<SsoOptions, string?> listener) => null;
    }
}

public class SsoRulesTests
{
    static Usuario U(int id, string email, string? dom = null) => new() { Id = id, Email = email, DomainAccount = dom, Activo = true };

    [Fact]
    public void Mapea_cuenta_Windows_por_cuenta_de_dominio_o_correo()
    {
        var users = new[] { U(1, "sergio.ramirez@ipsos.com"), U(2, "otro@ipsos.com", @"IPSOSGROUP\maria.perez") };
        Assert.Equal(1, SsoRules.Match(users, @"IPSOSGROUP\sergio.ramirez", "ipsos.com")!.Id);
        Assert.Equal(1, SsoRules.Match(users, "SERGIO.RAMIREZ@ipsos.com", "ipsos.com")!.Id);
        Assert.Equal(2, SsoRules.Match(users, @"ipsosgroup\MARIA.PEREZ", "ipsos.com")!.Id);
        Assert.Null(SsoRules.Match(users, @"IPSOSGROUP\desconocido", "ipsos.com"));
    }

    [Theory]
    [InlineData("10.0.1.5", true)]
    [InlineData("192.168.0.10", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("200.1.2.3", false)]
    [InlineData("", false)]
    public void Red_interna(string ip, bool interna) => Assert.Equal(interna, SsoRules.IsInternal(ip, new SsoOptions()));

    [Theory]
    [InlineData("/planificacion", "/planificacion")]
    [InlineData("//evil.com", "/")]
    [InlineData("https://evil.com/", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData(null, "/")]
    public void ReturnUrl_solo_local(string? input, string esperado) => Assert.Equal(esperado, SsoRules.SafeReturnUrl(input));
}

/// <summary>Flujo HTTP completo con una cuenta Windows simulada (cabecera X-Test-Windows) en lugar de Kerberos/NTLM.</summary>
public class SsoFlowTests : IClassFixture<SsoFlowTests.Factory>
{
    readonly Factory _f;
    public SsoFlowTests(Factory f) => _f = f;

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public TestApp App { get; } = new(new DateOnly(2026, 10, 20));
        public string Mode { get; set; } = "direct";

        public Factory() => App.SeedAsync().GetAwaiter().GetResult();

        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:GestionRequerimientoDP", "Server=none;Database=none");
            b.UseSetting("Database:MigrateOnStartup", "false");
            b.UseSetting("Database:Seed", "false");
            b.UseSetting("Jobs:Enabled", "false");
            b.UseSetting("Auth:DemoMode", "false");
            b.UseSetting("Sso:Enabled", "true");
            b.UseSetting("Sso:TicketSecret", "clave-de-prueba-de-al-menos-32-caracteres!!");
            b.UseSetting("Sso:EmailDomain", "empresa.cl");
            b.UseSetting("Sso:TrustForwardedHeaders", "true");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IAppDbContextFactory>(App.Factory);
                s.PostConfigure<AuthenticationOptions>(o => o.SchemeMap["Negotiate"].HandlerType = typeof(FakeWindowsHandler));
            });
        }
    }

    sealed class FakeWindowsHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, Microsoft.Extensions.Logging.ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var name = Request.Headers["X-Test-Windows"].FirstOrDefault();
            return Task.FromResult(string.IsNullOrEmpty(name)
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "Negotiate")), "Negotiate")));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties p)
        {
            Response.StatusCode = 401;
            Response.Headers.WWWAuthenticate = "Negotiate";
            return Task.CompletedTask;
        }
    }

    HttpClient Client() => _f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Sin_cuenta_Windows_responde_401_con_vuelta_al_ingreso_manual()
    {
        var r = await Client().GetAsync("/cuenta/sso");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("Negotiate", r.Headers.WwwAuthenticate.ToString());
        Assert.Contains("/cuenta/ingresar?manual=1", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cuenta_registrada_abre_sesion_y_respeta_returnUrl_local()
    {
        var c = Client();
        var req = new HttpRequestMessage(HttpMethod.Get, "/cuenta/sso?returnUrl=%2Fplanificacion");
        req.Headers.Add("X-Test-Windows", @"IPSOSGROUP\cmendez");
        var r = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/planificacion", r.Headers.Location!.OriginalString);
        Assert.Contains(r.Headers.GetValues("Set-Cookie"), v => v.StartsWith("grdp.session="));
    }

    [Fact]
    public async Task Cuenta_no_registrada_vuelve_con_mensaje()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/cuenta/sso?returnUrl=https%3A%2F%2Fevil.com");
        req.Headers.Add("X-Test-Windows", @"IPSOSGROUP\nadie");
        var r = await Client().SendAsync(req);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var loc = Uri.UnescapeDataString(r.Headers.Location!.OriginalString);
        Assert.StartsWith("/cuenta/ingresar?manual=1", loc);
        Assert.Contains("no está registrada", loc);
        Assert.False(r.Headers.TryGetValues("Set-Cookie", out var ck) && ck.Any(v => v.StartsWith("grdp.session=")));
    }

    [Fact]
    public async Task Ticket_del_host_interno_se_consume_una_vez()
    {
        var tickets = _f.Services.GetRequiredService<SsoTicketService>();
        var t = tickets.Create("cmendez@empresa.cl", "cmendez@empresa.cl", "Lider");
        var c = Client();
        var r = await c.GetAsync($"/cuenta/sso/consumir?ticket={Uri.EscapeDataString(t)}&returnUrl=%2Fresumen");
        Assert.Equal("/resumen", r.Headers.Location!.OriginalString);
        Assert.Contains(r.Headers.GetValues("Set-Cookie"), v => v.StartsWith("grdp.session="));
        var again = await c.GetAsync($"/cuenta/sso/consumir?ticket={Uri.EscapeDataString(t)}");
        Assert.StartsWith("/cuenta/ingresar?manual=1", again.Headers.Location!.OriginalString);
    }

    async Task<string> LoginHtml(string url, string ip)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Forwarded-For", ip);
        return await (await Client().SendAsync(req)).Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Login_desde_red_interna_intenta_SSO_y_desde_afuera_muestra_formulario()
    {
        var interna = await LoginHtml("/cuenta/ingresar?ReturnUrl=%2Fscripts", "10.0.20.5");
        Assert.Contains("id=\"sso-auto\"", interna);
        Assert.Contains("data-sso-url=\"/cuenta/sso?returnUrl=%2Fscripts\"", interna);
        Assert.Contains("class=\"d-none\"", interna); // formulario oculto mientras se intenta

        var externa = await LoginHtml("/cuenta/ingresar", "200.1.2.3");
        Assert.DoesNotContain("id=\"sso-auto\"", externa);
        Assert.Contains("Ingresar con mi cuenta corporativa", externa);

        var manual = await LoginHtml("/cuenta/ingresar?manual=1", "10.0.20.5");
        Assert.DoesNotContain("id=\"sso-auto\"", manual);
    }
}
