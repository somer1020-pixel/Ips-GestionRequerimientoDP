using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace GestionRequerimientoDP.Web;

/// <summary>
/// Publicación detrás de IIS o de un proxy inverso (Apache / Nginx / Cloudflare): IP real del cliente,
/// esquema https y sub-ruta (p. ej. https://servidor/gestionreq).
/// </summary>
public sealed class ProxyOptions
{
    public const string Section = "Proxy";

    /// <summary>Procesa X-Forwarded-For / -Proto / -Host / -Prefix. Solo detrás de un proxy de confianza.</summary>
    public bool Enabled { get; set; }

    /// <summary>IPs de los proxies de confianza. Vacío junto a KnownNetworks vacío = se acepta cualquier origen (solo si el servidor no es accesible directamente).</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Redes de los proxies en notación CIDR (p. ej. 10.0.0.0/8).</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    /// Sub-ruta fija de la app (p. ej. /gestionreq) cuando el proxy no envía X-Forwarded-Prefix.
    /// En IIS, como sub-aplicación, no hace falta: IIS la informa solo.
    /// </summary>
    public string PathBase { get; set; } = "";
}

/// <summary>Claves que cifran la cookie de sesión y los tokens antiforgery. Persistirlas evita cerrar todas las sesiones al reciclar el App Pool o con varios servidores.</summary>
public sealed class KeysOptions
{
    public const string Section = "DataProtection";

    /// <summary>Carpeta para las claves (con permisos de escritura para la identidad del App Pool). Vacío = ubicación por defecto de ASP.NET Core.</summary>
    public string KeysPath { get; set; } = "";

    /// <summary>Nombre compartido por todas las instancias (host interno y público) para que entiendan las mismas cookies.</summary>
    public string ApplicationName { get; set; } = "GestionRequerimientoDP";

    /// <summary>En Windows, cifra las claves en disco con DPAPI de la máquina.</summary>
    public bool ProtegerConDpapi { get; set; }
}

public static class Hosting
{
    public static WebApplicationBuilder AddAppHosting(this WebApplicationBuilder builder)
    {
        var keys = builder.Configuration.GetSection(KeysOptions.Section).Get<KeysOptions>() ?? new();
        var dp = builder.Services.AddDataProtection().SetApplicationName(keys.ApplicationName);
        if (!string.IsNullOrWhiteSpace(keys.KeysPath))
        {
            dp.PersistKeysToFileSystem(Directory.CreateDirectory(keys.KeysPath));
            if (keys.ProtegerConDpapi && OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }

        var proxy = builder.Configuration.GetSection(ProxyOptions.Section).Get<ProxyOptions>() ?? new();
        builder.Services.Configure<ProxyOptions>(builder.Configuration.GetSection(ProxyOptions.Section));
        if (proxy.Enabled)
            builder.Services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedPrefix;
                o.ForwardLimit = 2;
                if (proxy.KnownProxies.Length > 0 || proxy.KnownNetworks.Length > 0)
                {
                    o.KnownProxies.Clear();
                    o.KnownIPNetworks.Clear();
                    foreach (var p in proxy.KnownProxies.Where(p => !string.IsNullOrWhiteSpace(p))) o.KnownProxies.Add(IPAddress.Parse(p.Trim()));
                    foreach (var n in proxy.KnownNetworks.Where(n => !string.IsNullOrWhiteSpace(n))) o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(n.Trim()));
                }
                else
                {
                    // Sin lista: se confía en el proxy que llegue (el servidor solo debe ser alcanzable a través de él).
                    o.KnownProxies.Clear();
                    o.KnownIPNetworks.Clear();
                }
                // Si se configura AllowedHosts, el host reenviado también debe estar permitido.
                o.AllowedHosts = builder.Configuration["AllowedHosts"] is { Length: > 0 } h && h != "*" ? h.Split(';', StringSplitOptions.RemoveEmptyEntries) : [];
            });
        return builder;
    }

    /// <summary>Va primero en el pipeline: corrige IP, esquema y sub-ruta antes de autenticación, redirecciones y enlaces.</summary>
    public static WebApplication UseAppHosting(this WebApplication app)
    {
        var proxy = app.Configuration.GetSection(ProxyOptions.Section).Get<ProxyOptions>() ?? new();
        if (proxy.Enabled) app.UseForwardedHeaders();
        if (!string.IsNullOrWhiteSpace(proxy.PathBase)) app.UsePathBase("/" + proxy.PathBase.Trim('/'));
        // Enrutamiento explícito después de la sub-ruta (si no, WebApplication lo agrega al inicio y no la vería).
        app.UseRouting();
        return app;
    }
}
