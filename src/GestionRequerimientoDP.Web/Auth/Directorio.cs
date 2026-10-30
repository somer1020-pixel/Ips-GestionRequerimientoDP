using System.DirectoryServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace GestionRequerimientoDP.Web.Auth;

/// <summary>Consulta de Active Directory por LDAP (adaptado de Ipsos.Bhe.Auth). Solo disponible en Windows.</summary>
public sealed class ActiveDirectoryOptions
{
    public const string Section = "ActiveDirectory";
    public bool Enabled { get; set; }
    /// <summary>LDAP://… Vacío = dominio del servidor.</summary>
    public string LdapPath { get; set; } = "";
    /// <summary>Cuenta de servicio para consultar el directorio (opcional; vacío = identidad del proceso / App Pool).</summary>
    public string Usuario { get; set; } = "";
    public string Clave { get; set; } = "";
    /// <summary>Nombre NetBIOS del dominio (p. ej. IPSOSGROUP) para guardar la cuenta como DOMINIO\usuario.</summary>
    public string Dominio { get; set; } = "";
}

public sealed record DirectorioUsuario(string Cuenta, string Nombre, string Email, string? Departamento, string? Cargo);

public interface IDirectorioService
{
    bool Disponible { get; }
    Task<List<DirectorioUsuario>> BuscarAsync(string termino, int max = 10, CancellationToken ct = default);
    Task<DirectorioUsuario?> BuscarCuentaAsync(string cuenta, CancellationToken ct = default);
    /// <summary>Cuenta para el campo "Cuenta de dominio": DOMINIO\usuario si el dominio está configurado.</summary>
    string CuentaDominio(string sam);
}

public sealed class SinDirectorio(IOptionsMonitor<ActiveDirectoryOptions> o) : IDirectorioService
{
    public bool Disponible => false;
    public Task<List<DirectorioUsuario>> BuscarAsync(string termino, int max = 10, CancellationToken ct = default) => Task.FromResult(new List<DirectorioUsuario>());
    public Task<DirectorioUsuario?> BuscarCuentaAsync(string cuenta, CancellationToken ct = default) => Task.FromResult<DirectorioUsuario?>(null);
    public string CuentaDominio(string sam) => string.IsNullOrWhiteSpace(o.CurrentValue.Dominio) ? sam : $"{o.CurrentValue.Dominio}\\{sam}";
}

[SupportedOSPlatform("windows")]
public sealed partial class ActiveDirectoryDirectorio(IOptionsMonitor<ActiveDirectoryOptions> opts, ILogger<ActiveDirectoryDirectorio> log) : IDirectorioService
{
    static readonly string[] Props = ["samaccountname", "displayname", "mail", "userprincipalname", "department", "title"];

    public bool Disponible => true;

    public string CuentaDominio(string sam) => string.IsNullOrWhiteSpace(opts.CurrentValue.Dominio) ? sam : $"{opts.CurrentValue.Dominio}\\{sam}";

    [GeneratedRegex(@"[^\w\s\.\-@]")] private static partial Regex Unsafe();

    /// <summary>Evita inyección LDAP: solo letras, dígitos, espacios, punto, guion y @.</summary>
    public static string Sanitize(string s) => Unsafe().Replace(s ?? "", "").Trim();

    DirectoryEntry Root()
    {
        var o = opts.CurrentValue;
        if (string.IsNullOrWhiteSpace(o.LdapPath)) return new DirectoryEntry();
        return string.IsNullOrWhiteSpace(o.Usuario) ? new DirectoryEntry(o.LdapPath) : new DirectoryEntry(o.LdapPath, o.Usuario, o.Clave);
    }

    List<DirectorioUsuario> Search(string filter, int max)
    {
        var list = new List<DirectorioUsuario>();
        try
        {
            using var root = Root();
            using var s = new DirectorySearcher(root) { Filter = filter, SizeLimit = Math.Clamp(max, 1, 30) };
            s.PropertiesToLoad.AddRange(Props);
            using var res = s.FindAll();
            foreach (SearchResult r in res)
            {
                string? P(string n) => r.Properties.Contains(n) && r.Properties[n].Count > 0 ? r.Properties[n][0]?.ToString()?.Trim() : null;
                var sam = P("samaccountname");
                if (string.IsNullOrWhiteSpace(sam)) continue;
                var upn = P("userprincipalname");
                var mail = P("mail") ?? (upn?.Contains('@') == true ? upn : null) ?? sam;
                list.Add(new DirectorioUsuario(sam, P("displayname") ?? sam, mail, P("department"), P("title")));
            }
        }
        catch (Exception ex) { log.LogError(ex, "Error al consultar Active Directory"); }
        return list;
    }

    public Task<List<DirectorioUsuario>> BuscarAsync(string termino, int max = 10, CancellationToken ct = default)
    {
        var t = Sanitize(termino);
        if (t.Length < 2) return Task.FromResult(new List<DirectorioUsuario>());
        // Usuarios activos (no deshabilitados) cuyo usuario, nombre o correo contenga el término.
        var f = $"(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(|(sAMAccountName=*{t}*)(displayName=*{t}*)(mail=*{t}*)(userPrincipalName=*{t}*)))";
        return Task.Run(() => Search(f, max), ct);
    }

    public Task<DirectorioUsuario?> BuscarCuentaAsync(string cuenta, CancellationToken ct = default)
    {
        var sam = Sanitize(SsoRules.SamAccount(cuenta ?? ""));
        if (sam.Length == 0) return Task.FromResult<DirectorioUsuario?>(null);
        return Task.Run(() => Search($"(&(objectCategory=person)(objectClass=user)(sAMAccountName={sam}))", 1).FirstOrDefault(), ct);
    }
}
