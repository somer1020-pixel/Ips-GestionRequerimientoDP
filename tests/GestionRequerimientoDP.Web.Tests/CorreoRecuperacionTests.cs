using System.Net;
using System.Text.RegularExpressions;
using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Tests;
using GestionRequerimientoDP.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionRequerimientoDP.Web.Tests;

/// <summary>
/// La pantalla de recuperación responde siempre igual (no revela cuentas), pero el resultado real del envío
/// queda en la auditoría: enviado, simulado, error del servidor de correo o por qué no se envió.
/// </summary>
public partial class CorreoRecuperacionTests
{
    sealed class Correo(Exception? falla) : IEmailSender
    {
        public List<(string To, string Body)> Enviados { get; } = [];
        public bool IsReal => true;
        public string Descripcion => "relay de prueba:2550 · sin cifrado";
        public Task SendAsync(IReadOnlyList<string> to, IReadOnlyList<string> cc, string subject, string body, CancellationToken ct)
        {
            if (falla != null) throw falla;
            Enviados.Add((to[0], body));
            return Task.CompletedTask;
        }
    }

    sealed class Factory(Correo correo) : WebApplicationFactory<Program>
    {
        public TestApp App { get; } = Seed();
        static TestApp Seed() { var a = new TestApp(new DateOnly(2026, 10, 20)); a.SeedAsync().GetAwaiter().GetResult(); return a; }
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:GestionRequerimientoDP", "Server=none;Database=none");
            b.UseSetting("Database:MigrateOnStartup", "false");
            b.UseSetting("Database:Seed", "false");
            b.UseSetting("Jobs:Enabled", "false");
            b.UseSetting("Auth:DemoMode", "false");
            b.ConfigureTestServices(s => { s.AddSingleton<IAppDbContextFactory>(App.Factory); s.AddSingleton<IEmailSender>(correo); });
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")] private static partial Regex TokenRx();

    static async Task<string> Recuperar(Factory f, string login)
    {
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var html = await (await c.GetAsync("/cuenta/recuperar")).Content.ReadAsStringAsync();
        var r = await c.PostAsync("/cuenta/enviar-recuperacion", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["login"] = login, ["__RequestVerificationToken"] = WebUtility.HtmlDecode(TokenRx().Match(html).Groups[1].Value) }));
        return r.Headers.Location!.OriginalString;
    }

    static async Task<List<(string Accion, string Detalle)>> Auditoria(Factory f)
    {
        await using var db = f.App.Factory.Create();
        return (await db.Auditoria.OrderBy(a => a.Id).ToListAsync()).Select(a => (a.Accion, a.Detalle)).ToList();
    }

    [Fact]
    public async Task Envia_el_enlace_y_lo_registra()
    {
        var correo = new Correo(null);
        using var f = new Factory(correo);
        Assert.StartsWith("/cuenta/recuperar?enviado=1", await Recuperar(f, "lruiz@empresa.cl"));
        var (to, body) = Assert.Single(correo.Enviados);
        Assert.Equal("lruiz@empresa.cl", to);
        Assert.Contains("/cuenta/restablecer?token=", body);
        Assert.Contains(await Auditoria(f), a => a.Accion == "RESET_CORREO_ENVIADO");
    }

    [Fact]
    public async Task Si_el_servidor_de_correo_falla_la_pantalla_no_cambia_pero_el_error_queda_en_la_auditoria()
    {
        using var f = new Factory(new Correo(new NotSupportedException("The SMTP server does not support the STARTTLS extension.")));
        Assert.StartsWith("/cuenta/recuperar?enviado=1", await Recuperar(f, "lruiz@empresa.cl"));
        var error = Assert.Single(await Auditoria(f), a => a.Accion == "RESET_CORREO_ERROR");
        Assert.Contains("STARTTLS", error.Detalle);
        Assert.Contains("relay de prueba", error.Detalle);
    }

    [Fact]
    public async Task Cuenta_inexistente_y_limite_por_hora_dicen_el_motivo_en_la_auditoria()
    {
        var correo = new Correo(null);
        using var f = new Factory(correo);
        await Recuperar(f, "nadie@empresa.cl");
        for (var i = 0; i < 4; i++) await Recuperar(f, "jperez@empresa.cl");
        Assert.Equal(3, correo.Enviados.Count); // máximo 3 por hora
        var rechazos = (await Auditoria(f)).Where(a => a.Accion == "RESET_RECHAZADO").Select(a => a.Detalle).ToList();
        Assert.Contains(rechazos, d => d.Contains("no existe"));
        Assert.Contains(rechazos, d => d.Contains("última hora"));
    }
}
