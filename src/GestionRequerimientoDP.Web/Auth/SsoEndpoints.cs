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
        await ctx.RequestServices.GetRequiredService<IAuditoriaService>().RegistrarAsync("", "SSO_FALLO", "SSO", SsoRules.ClientIp(ctx, o), msg?.ToString() ?? "");
        var detail = app.Environment.IsDevelopment() ? $" Detalle: {msg}" : "";
        ctx.Response.Clear();
        ctx.Response.Redirect(LoginUrl(ctx, o, "error", $"Windows no pudo validar tu cuenta corporativa en este equipo. Ingresa con tu correo y contraseña.{detail}", true));
    });

    public static void MapSsoEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/cuenta/sso").AllowAnonymous();

        g.MapGet("/", async (HttpContext http, string? returnUrl, IOptionsMonitor<SsoOptions> opts, IUsuarioService users, IAccesoService acceso,
            IAuditoriaService audit, IDirectorioService directorio, SsoTicketService tickets, SsoRuntime runtime, ILogger<SsoTicketService> log) =>
        {
            var o = opts.CurrentValue;
            if (!o.Enabled) return Results.NotFound();
            if (!runtime.NegotiateRegistrado)
            {
                log.LogWarning("SSO: Sso:Enabled se activó con la app en ejecución; reiníciala para habilitar la autenticación de Windows");
                return Results.Redirect(AccountEndpoints.In(http, $"/cuenta/ingresar?manual=1&error={Uri.EscapeDataString(
                    "El ingreso con cuenta corporativa se activó después de iniciar la aplicación. Reiníciala para habilitarlo; mientras tanto, ingresa con tu correo y contraseña.")}"));
            }
            var ret = SsoRules.SafeReturnUrl(returnUrl, http.Request.PathBase);
            var ip = SsoRules.ClientIp(http, o);

            var res = await http.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
            var winName = res.Succeeded ? res.Principal?.Identity?.Name : null;
            if (string.IsNullOrWhiteSpace(winName))
            {
                // 401 + WWW-Authenticate: el navegador envía la cuenta Windows sin preguntar en la intranet.
                // Si no puede (InPrivate, fuera del dominio, cancelado), el cuerpo HTML lo devuelve al ingreso manual.
                await http.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
                var back = WebUtility.HtmlEncode(LoginUrl(http, o, "error", "No se pudo validar tu cuenta corporativa. Ingresa con tu correo y contraseña.", true));
                http.Response.ContentType = "text/html; charset=utf-8";
                await http.Response.WriteAsync($"""
                    <!DOCTYPE html><html lang="es"><head><meta charset="utf-8"><meta http-equiv="refresh" content="0;url={back}"><title>Validando cuenta corporativa…</title></head>
                    <body style="font-family:system-ui,'Segoe UI',sans-serif;text-align:center;padding-top:60px">
                    <p>Validando cuenta corporativa…</p><p><a href="{back}">Continuar al ingreso con correo y contraseña</a></p>
                    <script>window.location.replace({System.Text.Json.JsonSerializer.Serialize(WebUtility.HtmlDecode(back))});</script></body></html>
                    """);
                return Results.Empty;
            }

            var sam = SsoRules.SamAccount(winName);
            var user = SsoRules.Match(await users.ListAsync(), winName, o.EmailDomain);
            var esAdmin = o.EsAdministrador(winName, user?.Email ?? SsoRules.EmailFor(winName, o.EmailDomain));
            if ((user is null && o.SolicitudAutomatica) || (user is { Pendiente: true } && esAdmin))
            {
                // Cuenta corporativa válida sin usuario: queda la solicitud para los líderes (o entra como líder si es administrador configurado).
                var ad = user is null ? await directorio.BuscarCuentaAsync(winName, http.RequestAborted) : null;
                var email = user?.Email ?? (ad is { Email: var e } && e.Contains('@') ? e : SsoRules.EmailFor(winName, o.EmailDomain));
                esAdmin = o.EsAdministrador(winName, email);
                var nuevo = user is null;
                user = await acceso.SolicitarDesdeAdAsync(winName, email, user?.Nombre ?? ad?.Nombre ?? sam, esAdmin, http.RequestAborted);
                if (nuevo || user.Activo)
                    await audit.RegistrarAsync(winName, esAdmin ? "ADMIN_AUTORIZADO_AD" : "ACCESO_SOLICITADO_AD", "SSO", ip, $"{user.Email}{(ad is null ? "" : " · " + ad.Departamento)}");
                if (user.Pendiente)
                    return Results.Redirect(LoginUrl(http, o, "ok",
                        $"Registramos tu solicitud de acceso con la cuenta corporativa {sam}. Un líder de Operaciones la revisará y te avisaremos por correo ({user.Email}).", true));
            }
            if (user is null || user.Pendiente || !user.Activo)
            {
                var motivo = user is null ? "no registrada" : user.Pendiente ? "pendiente" : "inactiva";
                log.LogWarning("SSO: cuenta {Account} sin acceso ({Motivo})", winName, motivo);
                await audit.RegistrarAsync(winName, user is null ? "LOGIN_FALLIDO" : user.Pendiente ? "LOGIN_BLOQUEADO_PENDIENTE" : "LOGIN_BLOQUEADO_INACTIVO", "SSO", ip, motivo);
                return Results.Redirect(LoginUrl(http, o, "error", user is null
                    ? $"Tu cuenta corporativa ({sam}) no está registrada en {Application.AppInfo.Nombre}. Pide acceso al líder de Operaciones."
                    : user.Pendiente
                        ? "Tu solicitud de acceso está pendiente de aprobación. Te avisaremos por correo cuando un líder la apruebe."
                        : "Tu usuario está inactivo. Comunícate con el líder de Operaciones.", true));
            }
            log.LogInformation("SSO: {Account} autenticado como {Email}", winName, user.Email);

            if (o.UsesTickets && !SameHost(http, o.PublicHost))
            {
                var ticket = tickets.Create(user.Email, user.Email, user.Rol.ToString());
                await audit.RegistrarAsync(winName, "SSO_TICKET_EMITIDO", "SSO", ip, user.Email);
                return Results.Redirect($"{PublicBase(http, o)}/cuenta/sso/consumir?ticket={Uri.EscapeDataString(ticket)}&returnUrl={Uri.EscapeDataString(ret)}");
            }
            await SignIn(http, user, acceso);
            await audit.RegistrarAsync(winName, "LOGIN_OK_SSO", "SSO", ip, $"{user.Email} · {user.Rol}");
            return Results.Redirect(Home(http, user, ret));
        });

        g.MapGet("/consumir", async (HttpContext http, string? ticket, string? returnUrl, IOptionsMonitor<SsoOptions> opts,
            IUsuarioService users, IAccesoService acceso, IAuditoriaService audit, SsoTicketService tickets) =>
        {
            var o = opts.CurrentValue;
            if (!o.Enabled) return Results.NotFound();
            var ip = SsoRules.ClientIp(http, o);
            var t = tickets.Validate(ticket);
            var user = t is null ? null : await users.FindByEmailAsync(t.Email);
            if (user is null || user.Pendiente || !user.Activo)
            {
                await audit.RegistrarAsync(t?.Email ?? "", "SSO_TICKET_INVALIDO", "SSO-Ticket", ip, t is null ? "firma, vigencia o reutilización" : "usuario sin acceso");
                return Results.Redirect(LoginUrl(http, o, "error", "El ticket de la cuenta corporativa no es válido o expiró. Ingresa con tu correo y contraseña.", false));
            }
            await SignIn(http, user, acceso);
            await audit.RegistrarAsync(t!.Username, "LOGIN_OK_SSO", "SSO-Ticket", ip, $"{user.Email} · {user.Rol}");
            return Results.Redirect(Home(http, user, SsoRules.SafeReturnUrl(returnUrl, http.Request.PathBase)));
        });
    }

    static async Task SignIn(HttpContext http, Usuario u, IAccesoService acceso)
    {
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Claims.Principal(u, CookieAuthenticationDefaults.AuthenticationScheme),
            new AuthenticationProperties { IsPersistent = true });
        await acceso.RegistrarIngresoAsync(u.Id);
    }

    static string Home(HttpContext http, Usuario u, string ret) => AccountEndpoints.In(http, ret != "/" ? ret : u.Rol == Rol.Comercial ? "/solicitar" : "/");

    static string PublicBase(HttpContext http, SsoOptions o) =>
        string.IsNullOrWhiteSpace(o.PublicHost) ? $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}" : o.PublicHost.TrimEnd('/');

    static bool SameHost(HttpContext http, string publicHost) =>
        !string.IsNullOrWhiteSpace(publicHost) && Uri.TryCreate(publicHost, UriKind.Absolute, out var u)
        && string.Equals(u.Authority, http.Request.Host.Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Ingreso manual (en el host público si el aviso ocurre en el host interno). <paramref name="kind"/>: "error" u "ok".</summary>
    static string LoginUrl(HttpContext http, SsoOptions o, string kind, string mensaje, bool toPublic)
    {
        var path = $"/cuenta/ingresar?manual=1&{kind}={Uri.EscapeDataString(mensaje)}";
        return toPublic && o.UsesTickets ? PublicBase(http, o) + path : AccountEndpoints.In(http, path);
    }
}
