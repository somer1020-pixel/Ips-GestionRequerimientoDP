using System.Net;
using System.Security.Cryptography;
using System.Text;
using GestionRequerimientoDP.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GestionRequerimientoDP.Web.Auth;

/// <summary>
/// Single Sign-On con Active Directory (patrón de Ipsos.Bhe.Auth): el navegador negocia Kerberos/NTLM
/// contra un host interno y, si el sitio público es otro host, vuelve con un ticket firmado de un solo uso.
/// </summary>
public sealed class SsoOptions
{
    public const string Section = "Sso";

    /// <summary>Activa el ingreso con la cuenta corporativa (Windows / Active Directory) junto al ingreso con contraseña.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// URL base del host interno con Windows Authentication (p. ej. https://servidor-interno/gestionreq).
    /// Vacío = este mismo sitio negocia la cuenta Windows y no se usan tickets.
    /// </summary>
    public string InternalHost { get; set; } = "";

    /// <summary>URL base pública a la que vuelve el ticket. Vacío = mismo host de la solicitud.</summary>
    public string PublicHost { get; set; } = "";

    /// <summary>Secreto compartido HMAC-SHA256 entre host interno y público (mín. 32 caracteres). Va en user-secrets o variables de entorno.</summary>
    public string TicketSecret { get; set; } = "";

    public int TicketTtlSeconds { get; set; } = 60;

    /// <summary>Dominio de correo para mapear DOMINIO\usuario → usuario@dominio.</summary>
    public string EmailDomain { get; set; } = "ipsos.com";

    /// <summary>Desde la red interna, la pantalla de ingreso intenta el SSO automáticamente.</summary>
    public bool AutoRedirect { get; set; } = true;

    /// <summary>Prefijos de IP considerados red interna / VPN (además de loopback).</summary>
    public string[] InternalIpPrefixes { get; set; } = ["10.", "172.16.", "172.17.", "172.18.", "172.19.", "172.2", "172.30.", "172.31.", "192.168."];

    /// <summary>Usar CF-Connecting-IP / X-Forwarded-For para la IP del cliente (solo detrás de un proxy de confianza).</summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>Una cuenta corporativa sin usuario registrado deja una solicitud de acceso pendiente para los líderes.</summary>
    public bool SolicitudAutomatica { get; set; } = true;

    /// <summary>
    /// Cuentas (usuario de Windows o correo) que entran como líder sin aprobación la primera vez.
    /// Sirve para arrancar una base vacía; se configura en el servidor, no en el repositorio.
    /// </summary>
    public string[] Administradores { get; set; } = [];

    public bool UsesTickets => !string.IsNullOrWhiteSpace(InternalHost);

    public bool EsAdministrador(string windowsName, string email) =>
        Administradores.Any(a => !string.IsNullOrWhiteSpace(a) && (
            string.Equals(a.Trim(), windowsName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(SsoRules.SamAccount(a.Trim()), SsoRules.SamAccount(windowsName), StringComparison.OrdinalIgnoreCase) && !a.Contains('@')
            || string.Equals(a.Trim(), email, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>Lo que quedó registrado al iniciar la app (la configuración puede cambiar después sin reiniciar).</summary>
public sealed record SsoRuntime(bool NegotiateRegistrado)
{
    /// <summary>¿Este sitio puede ofrecer el ingreso con la cuenta corporativa ahora?</summary>
    public bool Disponible(SsoOptions o) => o.Enabled && (NegotiateRegistrado || o.UsesTickets);
}

public sealed record SsoTicket(string Username, string Email, string Role);

/// <summary>
/// Ticket HMAC-SHA256 con TTL y protección anti-replay. Formato compatible con Ipsos.Bhe.Auth:
/// Base64Url("username|email|role|unixTs|nonce") + "." + Base64Url(HMAC).
/// </summary>
public sealed class SsoTicketService(IOptionsMonitor<SsoOptions> opts, IMemoryCache cache, TimeProvider time, ILogger<SsoTicketService> log)
{
    byte[] Key
    {
        get
        {
            var s = opts.CurrentValue.TicketSecret;
            if (string.IsNullOrWhiteSpace(s) || s.Length < 32)
                throw new InvalidOperationException("Sso:TicketSecret debe tener al menos 32 caracteres (configúralo con user-secrets o variables de entorno).");
            return Encoding.UTF8.GetBytes(s);
        }
    }

    public string Create(string username, string email, string role = "User")
    {
        if (new[] { username, email, role }.Any(x => x.Contains('|'))) throw new ArgumentException("Campos inválidos para el ticket.");
        var payload = $"{username}|{email}|{role}|{time.GetUtcNow().ToUnixTimeSeconds()}|{Guid.NewGuid():N}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        return $"{B64(bytes)}.{B64(HMACSHA256.HashData(Key, bytes))}";
    }

    public SsoTicket? Validate(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket)) return null;
        try
        {
            var parts = ticket.Split('.');
            if (parts.Length != 2) return null;
            var payload = FromB64(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(FromB64(parts[1]), HMACSHA256.HashData(Key, payload)))
            {
                log.LogWarning("SSO: firma de ticket inválida");
                return null;
            }
            var f = Encoding.UTF8.GetString(payload).Split('|');
            if (f.Length != 5 || !long.TryParse(f[3], out var ts)) return null;
            var ttl = opts.CurrentValue.TicketTtlSeconds;
            if (Math.Abs(time.GetUtcNow().ToUnixTimeSeconds() - ts) > ttl)
            {
                log.LogWarning("SSO: ticket expirado para {User}", f[0]);
                return null;
            }
            // Un solo uso: el nonce queda registrado mientras el ticket podría seguir vigente.
            var key = "sso_nonce_" + f[4];
            if (cache.TryGetValue(key, out _))
            {
                log.LogWarning("SSO: ticket reutilizado para {User} (replay evitado)", f[0]);
                return null;
            }
            cache.Set(key, true, TimeSpan.FromSeconds(ttl * 2 + 60));
            return new SsoTicket(f[0], f[1], f[2]);
        }
        catch (FormatException) { return null; }
    }

    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

public static class SsoRules
{
    /// <summary>"DOMINIO\usuario" → "usuario"; "usuario@dominio" se deja igual.</summary>
    public static string SamAccount(string windowsName) =>
        windowsName.Contains('\\') ? windowsName[(windowsName.LastIndexOf('\\') + 1)..] : windowsName;

    public static string EmailFor(string windowsName, string domain)
    {
        var sam = SamAccount(windowsName);
        return sam.Contains('@') ? sam : $"{sam}@{domain}";
    }

    /// <summary>
    /// Usuario de la app que corresponde a la cuenta Windows: primero por "Cuenta de dominio" exacta,
    /// luego por correo usuario@dominio. Solo entran usuarios registrados y activos.
    /// </summary>
    public static Usuario? Match(IEnumerable<Usuario> users, string windowsName, string domain)
    {
        var list = users.ToList();
        var email = EmailFor(windowsName, domain);
        return list.FirstOrDefault(u => !string.IsNullOrEmpty(u.DomainAccount) && string.Equals(u.DomainAccount, windowsName, StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault(u => !string.IsNullOrEmpty(u.DomainAccount) && string.Equals(u.DomainAccount, SamAccount(windowsName), StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    public static string ClientIp(HttpContext ctx, SsoOptions o)
    {
        if (o.TrustForwardedHeaders)
        {
            var fwd = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                      ?? ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                      ?? ctx.Request.Headers["X-Real-IP"].FirstOrDefault();
            var first = fwd?.Split(',')[0].Trim();
            if (IPAddress.TryParse(first, out _)) return first!;
        }
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "";
    }

    public static bool IsInternal(string ip, SsoOptions o)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return false;
        if (IPAddress.IsLoopback(addr)) return true;
        if (addr.IsIPv4MappedToIPv6) ip = addr.MapToIPv4().ToString();
        return o.InternalIpPrefixes.Any(p => !string.IsNullOrWhiteSpace(p) && ip.StartsWith(p.Trim(), StringComparison.Ordinal));
    }

    /// <summary>Solo rutas locales ("/algo", nunca "//host" ni URLs absolutas): evita redirecciones abiertas.</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\") ? returnUrl : "/";

    /// <summary>
    /// Igual, pero relativa a la app: la cookie de sesión manda ReturnUrl con la sub-ruta incluida (/gestionreq/planificacion → /planificacion).
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl, PathString pathBase)
    {
        var r = SafeReturnUrl(returnUrl);
        return pathBase.HasValue && new PathString(r.Split('?')[0]).StartsWithSegments(pathBase, out var rest)
            ? (rest.HasValue ? rest.Value! : "/") + (r.Contains('?') ? r[r.IndexOf('?')..] : "")
            : r;
    }
}
