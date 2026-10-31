using GestionRequerimientoDP.Application;
using GestionRequerimientoDP.Application.Services;
using GestionRequerimientoDP.Domain;
using GestionRequerimientoDP.Infrastructure.Email;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace GestionRequerimientoDP.Web.Auth;

/// <summary>Ingreso (correo o RUT), salida, solicitud de acceso y recuperación de contraseña. Todo queda en la auditoría.</summary>
public static class AccountEndpoints
{
    /// <summary>Ruta dentro de la app, respetando la sub-ruta de IIS / proxy (PathBase).</summary>
    public static string In(HttpContext http, string path) => $"{http.Request.PathBase}{path}";

    static string Q(string s) => Uri.EscapeDataString(s ?? "");

    /// <summary>
    /// Formulario con token antiforgery vencido o ausente (pestaña abierta mucho rato, claves rotadas, envío directo):
    /// se vuelve al formulario con un aviso en vez de responder 500.
    /// </summary>
    static IResult? FormularioVencido(HttpContext http, string pagina)
    {
        if (http.Features.Get<IAntiforgeryValidationFeature>() is not { IsValid: false }) return null;
        var sep = pagina.Contains('?') ? '&' : '?';
        return Results.Redirect(In(http, $"{pagina}{sep}error={Q("El formulario venció por seguridad. Vuelve a intentarlo.")}"));
    }

    public static void MapAccountEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/cuenta").AllowAnonymous().RequireRateLimiting(Hosting.PoliticaCuenta);

        g.MapPost("/login", async (HttpContext http, IAccesoService acceso, IAuditoriaService audit, IOptionsMonitor<AuthOptions> o, IOptionsMonitor<SsoOptions> sso) =>
        {
            if (FormularioVencido(http, "/cuenta/ingresar?manual=1") is { } vencido) return vencido;
            var form = await http.Request.ReadFormAsync();
            var login = form["email"].ToString().Trim();
            var clave = AccesoService.ClaveLogin(login);
            var ret = SsoRules.SafeReturnUrl(form["returnUrl"].ToString(), http.Request.PathBase);
            var ip = SsoRules.ClientIp(http, sso.CurrentValue);
            var demo = o.CurrentValue.DemoMode;
            if (!demo && await acceso.DemasiadosIntentosAsync(login))
            {
                await audit.RegistrarAsync(clave, "LOGIN_BLOQUEADO_INTENTOS", "Local", ip);
                return Results.Redirect(In(http, $"/cuenta/ingresar?manual=1&error={Q($"Demasiados intentos fallidos. Espera {AccesoService.VentanaIntentos.TotalMinutes:0} minutos o usa \"¿Olvidaste tu contraseña?\".")}&email={Q(login)}"));
            }
            var r = await acceso.ValidarAsync(login, form["password"].ToString(), sinClave: demo);
            if (r.Estado != LoginEstado.Ok)
            {
                await audit.RegistrarAsync(clave, r.Estado switch
                {
                    LoginEstado.Pendiente => "LOGIN_BLOQUEADO_PENDIENTE", LoginEstado.Inactivo => "LOGIN_BLOQUEADO_INACTIVO", _ => "LOGIN_FALLIDO",
                }, "Local", ip, r.Estado.ToString());
                var msg = r.Estado == LoginEstado.NoExiste && !demo ? new LoginResultado(LoginEstado.ClaveIncorrecta, null).Mensaje : r.Mensaje;
                return Results.Redirect(In(http, $"/cuenta/ingresar?manual=1&error={Q(msg)}&email={Q(login)}"));
            }
            var u = r.Usuario!;
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Claims.Principal(u, CookieAuthenticationDefaults.AuthenticationScheme),
                new AuthenticationProperties { IsPersistent = true });
            await acceso.RegistrarIngresoAsync(u.Id);
            await audit.RegistrarAsync(login, "LOGIN_OK", demo ? "Demo" : "Local", ip, $"{u.Email} · {u.Rol}");
            return Results.Redirect(In(http, ret != "/" ? ret : u.Rol == Rol.Comercial ? "/solicitar" : "/"));
        }).WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        g.MapPost("/salir", async (HttpContext http, IOptionsMonitor<SsoOptions> sso, IAuditoriaService audit) =>
        {
            if (FormularioVencido(http, "/") is { } vencido) return vencido;
            var me = Claims.ToUsuario(http.User);
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (me != null) await audit.RegistrarAsync(me.Nombre, "LOGOUT", "Cookie", SsoRules.ClientIp(http, sso.CurrentValue));
            // Con SSO, volver al ingreso manual: si no, la cuenta Windows volvería a entrar sola.
            return Results.Redirect(In(http, sso.CurrentValue.Enabled ? "/cuenta/ingresar?manual=1" : "/cuenta/ingresar"));
        }).WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        g.MapPost("/solicitar-acceso", async (HttpContext http, IAccesoService acceso, IAuditoriaService audit, IOptionsMonitor<AuthOptions> o, IOptionsMonitor<SsoOptions> sso) =>
        {
            if (!o.CurrentValue.RegistroHabilitado) return Results.NotFound();
            if (FormularioVencido(http, "/cuenta/registro") is { } vencido) return vencido;
            var f = await http.Request.ReadFormAsync();
            var input = new RegistroInput
            {
                Nombre = f["nombre"].ToString(), Email = f["email"].ToString(), Rut = f["rut"].ToString(), Motivo = f["motivo"].ToString(),
                Password = f["password"].ToString(), Confirmacion = f["confirmacion"].ToString(),
            };
            try
            {
                var u = await acceso.SolicitarAsync(input);
                await audit.RegistrarAsync(u.Email, "ACCESO_SOLICITADO", "Registro", SsoRules.ClientIp(http, sso.CurrentValue), input.Motivo);
                return Results.Redirect(In(http, $"/cuenta/ingresar?manual=1&ok={Q("Recibimos tu solicitud de acceso. Un líder de Operaciones la revisará y te avisaremos por correo.")}"));
            }
            catch (ReglaException ex)
            {
                return Results.Redirect(In(http, $"/cuenta/registro?error={Q(ex.Message)}&nombre={Q(input.Nombre)}&email={Q(input.Email)}&rut={Q(input.Rut)}&motivo={Q(input.Motivo)}"));
            }
        }).WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        g.MapPost("/enviar-recuperacion", async (HttpContext http, IAccesoService acceso, IAuditoriaService audit, IEmailSender mail, IWebHostEnvironment env,
            IOptionsMonitor<SsoOptions> sso, ILogger<IAccesoService> log) =>
        {
            if (FormularioVencido(http, "/cuenta/recuperar") is { } vencido) return vencido;
            var login = (await http.Request.ReadFormAsync())["login"].ToString().Trim();
            var ip = SsoRules.ClientIp(http, sso.CurrentValue);
            var (reset, motivo) = await acceso.CrearResetAsync(login, ip);
            if (motivo == "ad")
                return Results.Redirect(In(http, $"/cuenta/recuperar?error={Q("Esa cuenta ingresa con la cuenta corporativa de Windows. Para cambiar esa contraseña usa las herramientas de Windows o la Mesa de Ayuda TI.")}"));
            var extra = "";
            if (reset != null)
            {
                // El enlace no pasa por el registro de Notificaciones (lo verían los líderes): se envía directo.
                var link = $"{http.Request.Scheme}://{http.Request.Host}{In(http, $"/cuenta/restablecer?token={Uri.EscapeDataString(reset.Token)}")}";
                try
                {
                    await mail.SendAsync([reset.Usuario.Email], [], $"[{AppInfo.Nombre}] Restablecer tu contraseña", string.Join('\n',
                        $"Hola {reset.Usuario.Nombre.Split(' ')[0]},", "",
                        $"Recibimos una solicitud para restablecer tu contraseña de {AppInfo.Nombre}. Usa este enlace dentro de los próximos {AccesoService.ResetVigencia.TotalMinutes:0} minutos:",
                        link, "", "Si no lo pediste, ignora este correo: tu contraseña no cambia.", "", $"Mensaje automático de {AppInfo.Nombre}."), http.RequestAborted);
                }
                catch (Exception ex) { log.LogError(ex, "No se pudo enviar el correo de recuperación a {Email}", reset.Usuario.Email); }
                // Sin SMTP en Development, el enlace se muestra en pantalla para poder probar.
                if (env.IsDevelopment() && !mail.IsReal) extra = $"&enlace={Q(link)}";
            }
            await audit.RegistrarAsync(login, reset != null ? "RESET_SOLICITADO" : "RESET_RECHAZADO", "Recuperacion", ip, motivo ?? "");
            return Results.Redirect(In(http, "/cuenta/recuperar?enviado=1" + extra)); // misma respuesta exista o no la cuenta
        }).WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        g.MapPost("/cambiar-clave", async (HttpContext http, IAccesoService acceso, IAuditoriaService audit, IOptionsMonitor<SsoOptions> sso) =>
        {
            if (FormularioVencido(http, "/cuenta/recuperar") is { } vencido) return vencido;
            var f = await http.Request.ReadFormAsync();
            var token = f["token"].ToString();
            var ip = SsoRules.ClientIp(http, sso.CurrentValue);
            var (u, _) = await acceso.ValidarResetAsync(token);
            var (ok, msg) = await acceso.RestablecerAsync(token, f["password"].ToString(), f["confirmacion"].ToString(), ip);
            if (!ok) return Results.Redirect(In(http, $"/cuenta/restablecer?token={Q(token)}&error={Q(msg)}"));
            await audit.RegistrarAsync(u?.Email ?? "", "RESET_OK", "Recuperacion", ip);
            return Results.Redirect(In(http, $"/cuenta/ingresar?manual=1&ok={Q(msg)}&email={Q(u?.Email ?? "")}"));
        }).WithMetadata(new RequireAntiforgeryTokenAttribute(true));
    }
}
