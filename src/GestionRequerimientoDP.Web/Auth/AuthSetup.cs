using System.Security.Claims;
using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Components.Authorization;

namespace GestionRequerimientoDP.Web.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";
    /// <summary>"Cookie" (pantalla de ingreso propia) o "Windows" (Negotiate + Active Directory).</summary>
    public string Mode { get; set; } = "Cookie";
    /// <summary>Accesos de demostración y contraseña sin validar (como en el mockup). Desactivar en producción.</summary>
    public bool DemoMode { get; set; }
    /// <summary>La consulta pública requiere sesión (en producción: SSO corporativo sin rol en la app).</summary>
    public bool ConsultaRequiereSesion { get; set; }
    public bool IsWindows => string.Equals(Mode, "Windows", StringComparison.OrdinalIgnoreCase);
}

public static class Claims
{
    public const string UserId = ClaimTypes.NameIdentifier;
    public const string Equipos = "grdp:equipos";

    public static ClaimsPrincipal Principal(Usuario u, string scheme) => new(new ClaimsIdentity(
    [
        new Claim(UserId, u.Id.ToString()), new Claim(ClaimTypes.Name, u.Nombre), new Claim(ClaimTypes.Email, u.Email),
        new Claim(ClaimTypes.Role, u.Rol.ToString()), new Claim(Equipos, string.Join(',', u.EquiposOrdenados)),
    ], scheme, ClaimTypes.Name, ClaimTypes.Role));

    public static UsuarioActual? ToUsuario(ClaimsPrincipal p)
    {
        if (p.Identity?.IsAuthenticated != true || !int.TryParse(p.FindFirstValue(UserId), out var id)) return null;
        if (!Enum.TryParse<Rol>(p.FindFirstValue(ClaimTypes.Role), out var rol)) return null;
        var eq = (p.FindFirstValue(Equipos) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => Enum.TryParse<Equipo>(e, out var x) ? x : (Equipo?)null).OfType<Equipo>().ToList();
        return new UsuarioActual(id, p.FindFirstValue(ClaimTypes.Name) ?? "", rol, eq);
    }
}

/// <summary>Windows Auth: mapea DOMINIO\usuario a Usuario.DomainAccount y agrega id, rol y equipos.</summary>
public sealed class DomainClaimsTransformation(IServiceScopeFactory scopes) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.HasClaim(c => c.Type == Claims.Equipos)) return principal;
        using var scope = scopes.CreateScope();
        var u = await scope.ServiceProvider.GetRequiredService<IUsuarioService>().FindByDomainAccountAsync(principal.Identity.Name ?? "");
        if (u is null || !u.Activo) return principal;
        return Claims.Principal(u, principal.Identity.AuthenticationType ?? NegotiateDefaults.AuthenticationScheme);
    }
}

public static class AuthSetup
{
    public static void AddAppAuth(this WebApplicationBuilder builder)
    {
        var opts = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));

        if (opts.IsWindows)
        {
            builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
            builder.Services.AddTransient<IClaimsTransformation, DomainClaimsTransformation>();
        }
        else
        {
            var auth = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);
            // SSO con Active Directory: la cuenta Windows se negocia solo en /cuenta/sso; la sesión sigue siendo la cookie.
            if (builder.Configuration.GetValue<bool>($"{SsoOptions.Section}:Enabled"))
                auth.AddNegotiate(o => o.Events = new() { OnAuthenticationFailed = SsoEndpoints.OnNegotiateFailed });
            auth.AddCookie(o =>
            {
                o.Cookie.Name = "grdp.session"; // la sesión persiste hasta "Salir"
                o.LoginPath = "/cuenta/ingresar";
                o.AccessDeniedPath = "/sin-acceso";
                // Las descargas responden con códigos HTTP en vez de redirigir a una página.
                o.Events.OnRedirectToLogin = ctx => Api(ctx, StatusCodes.Status401Unauthorized);
                o.Events.OnRedirectToAccessDenied = ctx => Api(ctx, StatusCodes.Status403Forbidden);
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;
                // Usuarios desactivados o con cambios de rol / equipo: se valida contra la BD en cada request.
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    var me = Claims.ToUsuario(ctx.Principal!);
                    var users = ctx.HttpContext.RequestServices.GetRequiredService<IUsuarioService>();
                    var u = me is null ? null : await users.GetAsync(me.Id);
                    if (u is null || !u.Activo) { ctx.RejectPrincipal(); await ctx.HttpContext.SignOutAsync(); return; }
                    var fresh = UsuarioActual.From(u);
                    if (fresh.Rol != me!.Rol || fresh.Nombre != me.Nombre || !fresh.Equipos.SequenceEqual(me.Equipos))
                    {
                        ctx.ReplacePrincipal(Claims.Principal(u, CookieAuthenticationDefaults.AuthenticationScheme));
                        ctx.ShouldRenew = true;
                    }
                };
            });
        }
        builder.Services.Configure<SsoOptions>(builder.Configuration.GetSection(SsoOptions.Section));
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<SsoTicketService>();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Lider, p => p.RequireRole(nameof(Rol.Lider)))
            .AddPolicy(Policies.Analista, p => p.RequireRole(nameof(Rol.Analista)))
            .AddPolicy(Policies.Comercial, p => p.RequireRole(nameof(Rol.Comercial)))
            .AddPolicy(Policies.Gestion, p => p.RequireRole(nameof(Rol.Lider), nameof(Rol.Analista)));
        builder.Services.AddCascadingAuthenticationState();
    }

    static Task Api(RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/export")) ctx.Response.StatusCode = status;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    public static void MapAccountEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/cuenta");
        g.MapPost("/login", async (HttpContext http, IUsuarioService users, IPasswordService passwords, Microsoft.Extensions.Options.IOptionsMonitor<AuthOptions> o) =>
        {
            var form = await http.Request.ReadFormAsync();
            var email = form["email"].ToString();
            var password = form["password"].ToString();
            var demo = form["demo"].ToString() == "1";
            var u = await users.FindByEmailAsync(email);
            string? err = null;
            if (u is null || !u.Activo) err = "Correo no registrado o usuario inactivo.";
            else if (!o.CurrentValue.DemoMode && (demo || u.PasswordHash is null || !passwords.Verify(u.PasswordHash, password))) err = "Correo o contraseña incorrectos.";
            if (err != null) return Results.Redirect($"/cuenta/ingresar?error={Uri.EscapeDataString(err)}&email={Uri.EscapeDataString(email)}");
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Claims.Principal(u!, CookieAuthenticationDefaults.AuthenticationScheme),
                new AuthenticationProperties { IsPersistent = true });
            return Results.Redirect(u!.Rol == Rol.Comercial ? "/solicitar" : "/");
        }).WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        g.MapPost("/salir", async (HttpContext http, Microsoft.Extensions.Options.IOptionsMonitor<SsoOptions> sso) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            // Con SSO, volver al ingreso manual: si no, la cuenta Windows volvería a entrar sola.
            return Results.Redirect(sso.CurrentValue.Enabled ? "/cuenta/ingresar?manual=1" : "/cuenta/ingresar");
        }).WithMetadata(new RequireAntiforgeryTokenAttribute(true));
    }
}

public static class Policies
{
    public const string Lider = "Lider";
    public const string Analista = "Analista";
    public const string Comercial = "Comercial";
    /// <summary>Líder o analista (tablero).</summary>
    public const string Gestion = "Gestion";
}
