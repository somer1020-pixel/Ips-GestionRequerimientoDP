using System.Net;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Extensions.Options;

namespace GestionRequerimientoDP.Web.Auth;

/// <summary>
/// /cuenta/sso: negocia la cuenta Windows (Kerberos / NTLM) y abre la sesión, o emite un ticket hacia el host público.
/// /cuenta/sso/consumir: valida el ticket en el host público y abre la sesión.
/// </summary>
public static class SsoEndpoints
{
    const string FailureKey = "grdp:sso-error";

    /// <summary>
    /// El intercambio Kerberos/NTLM falló (cuenta no de dominio, Entra ID / Azure AD, SPN, etc.).
    /// Negotiate respondería 400 sin cuerpo; se registra la causa para la vuelta al ingreso.
    /// </summary>
    public static Task OnNegotiateFailed(Microsoft.AspNetCore.Authentication.Negotiate.AuthenticationFailedContext ctx)
    {
        ctx.HttpContext.RequestServices.GetRequiredService<ILogger<SsoTicketService>>()
            .LogWarning(ctx.Exception, "SSO: falló la negociación de la cuenta Windows");
        ctx.HttpContext.Items[FailureKey] = ctx.Exception.Message;
        return Task.CompletedTask;
    }

    /// <summary>Reemplaza la respuesta 400 de Negotiate por la vuelta al ingreso manual con la causa (detallada en Development).</summary>
    public static IApplicationBuilder UseSsoErrorRedirect(this WebApplication app) => app.Use(async (ctx, next) =>
    {
        await next();
        if (ctx.Response.HasStarted || ctx.Response.StatusCode != StatusCodes.Status400BadRequest || !ctx.Items.TryGetValue(FailureKey, out var msg)) return;
        var o = ctx.RequestServices.GetRequiredService<IOptionsMonitor<SsoOptions>>().CurrentValue;
        var detail = app.Environment.IsDevelopment() ? $" Detalle: {msg}" : "";
        ctx.Response.Clear();
        ctx.Response.Redirect(LoginUrl(ctx, o, $"Windows no pudo validar tu cuenta corporativa en este equipo. Ingresa con tu correo y contraseña.{detail}", true));
    });

    public static void MapSsoEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/cuenta/sso").AllowAnonymous();

        g.MapGet("/", async (HttpContext http, string? returnUrl, IOptionsMonitor<SsoOptions> opts, IUsuarioService users,
            SsoTicketService tickets, ILogger<SsoTicketService> log) =>
        {
            var o = opts.CurrentValue;
            if (!o.Enabled) return Results.NotFound();
            var ret = SsoRules.SafeReturnUrl(returnUrl);

            var res = await http.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
            var winName = res.Succeeded ? res.Principal?.Identity?.Name : null;
            if (string.IsNullOrWhiteSpace(winName))
            {
                // 401 + WWW-Authenticate: el navegador envía la cuenta Windows sin preguntar en la intranet.
                // Si no puede (InPrivate, fuera del dominio, cancelado), el cuerpo HTML lo devuelve al ingreso manual.
                await http.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
                var back = WebUtility.HtmlEncode(LoginUrl(http, o, "No se pudo validar tu cuenta corporativa. Ingresa con tu correo y contraseña.", true));
                http.Response.ContentType = "text/html; charset=utf-8";
                await http.Response.WriteAsync($"""
                    <!DOCTYPE html><html lang="es"><head><meta charset="utf-8"><meta http-equiv="refresh" content="0;url={back}"><title>Validando cuenta corporativa…</title></head>
                    <body style="font-family:system-ui,'Segoe UI',sans-serif;text-align:center;padding-top:60px">
                    <p>Validando cuenta corporativa…</p><p><a href="{back}">Continuar al ingreso con correo y contraseña</a></p>
                    <script>window.location.replace({System.Text.Json.JsonSerializer.Serialize(WebUtility.HtmlDecode(back))});</script></body></html>
                    """);
                return Results.Empty;
            }

            var user = SsoRules.Match(await users.ListAsync(), winName, o.EmailDomain);
            if (user is null || !user.Activo)
            {
                log.LogWarning("SSO: cuenta {Account} sin acceso ({Motivo})", winName, user is null ? "no registrada" : "inactiva");
                return Results.Redirect(LoginUrl(http, o,
                    user is null
                        ? $"Tu cuenta corporativa ({SsoRules.SamAccount(winName)}) no está registrada en {Application.AppInfo.Nombre}. Pide acceso al líder de Operaciones."
                        : "Tu usuario está inactivo. Comunícate con el líder de Operaciones.", true));
            }
            log.LogInformation("SSO: {Account} autenticado como {Email}", winName, user.Email);

            if (o.UsesTickets && !SameHost(http, o.PublicHost))
            {
                var ticket = tickets.Create(user.Email, user.Email, user.Rol.ToString());
                return Results.Redirect($"{PublicBase(http, o)}/cuenta/sso/consumir?ticket={Uri.EscapeDataString(ticket)}&returnUrl={Uri.EscapeDataString(ret)}");
            }
            await SignIn(http, user);
            return Results.Redirect(Home(user, ret));
        });

        g.MapGet("/consumir", async (HttpContext http, string? ticket, string? returnUrl, IOptionsMonitor<SsoOptions> opts,
            IUsuarioService users, SsoTicketService tickets) =>
        {
            var o = opts.CurrentValue;
            if (!o.Enabled) return Results.NotFound();
            var t = tickets.Validate(ticket);
            var user = t is null ? null : await users.FindByEmailAsync(t.Email);
            if (user is null || !user.Activo)
                return Results.Redirect(LoginUrl(http, o, "El ticket de la cuenta corporativa no es válido o expiró. Ingresa con tu correo y contraseña.", false));
            await SignIn(http, user);
            return Results.Redirect(Home(user, SsoRules.SafeReturnUrl(returnUrl)));
        });
    }

    static Task SignIn(HttpContext http, Usuario u) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Claims.Principal(u, CookieAuthenticationDefaults.AuthenticationScheme),
            new AuthenticationProperties { IsPersistent = true });

    static string Home(Usuario u, string ret) => ret != "/" ? ret : u.Rol == Rol.Comercial ? "/solicitar" : "/";

    static string PublicBase(HttpContext http, SsoOptions o) =>
        string.IsNullOrWhiteSpace(o.PublicHost) ? $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}" : o.PublicHost.TrimEnd('/');

    static bool SameHost(HttpContext http, string publicHost) =>
        !string.IsNullOrWhiteSpace(publicHost) && Uri.TryCreate(publicHost, UriKind.Absolute, out var u)
        && string.Equals(u.Authority, http.Request.Host.Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Ingreso manual (en el host público si el error ocurre en el host interno).</summary>
    static string LoginUrl(HttpContext http, SsoOptions o, string error, bool toPublic)
    {
        var path = $"/cuenta/ingresar?manual=1&error={Uri.EscapeDataString(error)}";
        return toPublic && o.UsesTickets ? PublicBase(http, o) + path : path;
    }
}
