using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

public sealed class RegistroInput
{
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public string Rut { get; set; } = "";
    public string Password { get; set; } = "";
    public string Confirmacion { get; set; } = "";
    /// <summary>Área y motivo de la solicitud (lo ve el líder al aprobar).</summary>
    public string Motivo { get; set; } = "";
}

public enum LoginEstado { Ok, NoExiste, Pendiente, Inactivo, ClaveIncorrecta }

public sealed record LoginResultado(LoginEstado Estado, Usuario? Usuario)
{
    public string Mensaje => Estado switch
    {
        LoginEstado.Pendiente => "Tu solicitud de acceso está en revisión. Te avisaremos por correo cuando un líder la apruebe.",
        LoginEstado.Inactivo => "Tu cuenta está desactivada. Comunícate con el líder de Operaciones.",
        LoginEstado.NoExiste => "Correo o RUT no registrado.",
        LoginEstado.ClaveIncorrecta => "Correo, RUT o contraseña incorrectos.",
        _ => "",
    };
}

public sealed record ResetCreado(Usuario Usuario, string Token);

/// <summary>Auditoría de autenticación y administración de cuentas.</summary>
public interface IAuditoriaService
{
    Task RegistrarAsync(string usuario, string accion, string metodo, string ip, string detalle = "", CancellationToken ct = default);
    Task<List<AuditoriaAcceso>> ListAsync(UsuarioActual me, string? filtro, int take = 300, CancellationToken ct = default);
}

public sealed class AuditoriaService(IAppDbContextFactory factory, IClock clock) : IAuditoriaService
{
    public async Task RegistrarAsync(string usuario, string accion, string metodo, string ip, string detalle = "", CancellationToken ct = default)
    {
        await using var db = factory.Create();
        db.Auditoria.Add(new AuditoriaAcceso
        {
            At = clock.Now, Usuario = Trunc(usuario, 200), Accion = Trunc(accion, 60), Metodo = Trunc(metodo, 40), Ip = Trunc(ip, 60), Detalle = Trunc(detalle, 1000),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<AuditoriaAcceso>> ListAsync(UsuarioActual me, string? filtro, int take = 300, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var q = db.Auditoria.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var f = filtro.Trim();
            q = q.Where(a => a.Usuario.Contains(f) || a.Accion.Contains(f) || a.Ip.Contains(f) || a.Detalle.Contains(f));
        }
        return await q.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(take).ToListAsync(ct);
    }

    static string Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..n];
}

/// <summary>
/// Acceso a la app (adaptado de Ipsos.Bhe.Auth): ingreso por correo o RUT, solicitud de acceso con aprobación del líder,
/// recuperación de contraseña con token de un solo uso y solicitudes automáticas para cuentas de Active Directory.
/// </summary>
public interface IAccesoService
{
    /// <param name="sinClave">Accesos de demostración: no se valida la contraseña.</param>
    Task<LoginResultado> ValidarAsync(string login, string password, bool sinClave, CancellationToken ct = default);
    Task RegistrarIngresoAsync(int usuarioId, CancellationToken ct = default);
    Task<Usuario> SolicitarAsync(RegistroInput input, CancellationToken ct = default);
    /// <summary>Cuenta de Active Directory sin registro: crea la solicitud pendiente (o, si es administrador configurado, un líder aprobado).</summary>
    Task<Usuario> SolicitarDesdeAdAsync(string cuentaWindows, string email, string nombre, bool esAdministrador, CancellationToken ct = default);
    Task<List<Usuario>> PendientesAsync(UsuarioActual me, CancellationToken ct = default);
    Task<Usuario> AprobarAsync(UsuarioActual me, UsuarioInput datos, CancellationToken ct = default);
    Task RechazarAsync(UsuarioActual me, int id, CancellationToken ct = default);
    /// <summary>Crea un token de recuperación. Null si no corresponde (no existe, inactivo, pendiente, cuenta solo AD o demasiados intentos).</summary>
    Task<(ResetCreado? Reset, string? Motivo)> CrearResetAsync(string login, string ip, CancellationToken ct = default);
    Task<(Usuario? Usuario, string? Error)> ValidarResetAsync(string token, CancellationToken ct = default);
    Task<(bool Ok, string Mensaje)> RestablecerAsync(string token, string password, string confirmacion, string ip, CancellationToken ct = default);
}

public sealed class AccesoService(IAppDbContextFactory factory, IClock clock, IChangeNotifier changes, IPasswordService passwords) : IAccesoService
{
    public static readonly TimeSpan ResetVigencia = TimeSpan.FromMinutes(60);
    public const int ResetMaxPorHora = 3;

    public async Task<LoginResultado> ValidarAsync(string login, string password, bool sinClave, CancellationToken ct = default)
    {
        var l = (login ?? "").Trim();
        await using var db = factory.Create();
        Usuario? u;
        if (Domain.Rut.IsCandidate(l))
        {
            var rut = Domain.Rut.Normalize(l);
            u = await db.UsuariosConEquipos().FirstOrDefaultAsync(x => x.Rut == rut, ct);
        }
        else
        {
            var e = l.ToLowerInvariant();
            u = e.Length == 0 ? null : await db.UsuariosConEquipos().FirstOrDefaultAsync(x => x.Email.ToLower() == e, ct);
        }
        if (u is null) return new(LoginEstado.NoExiste, null);
        // La clave se valida antes que el estado: sin ella no se revela si la cuenta existe, está pendiente o desactivada.
        if (!sinClave && (u.PasswordHash is null || !passwords.Verify(u.PasswordHash, password ?? ""))) return new(LoginEstado.ClaveIncorrecta, u);
        if (u.Pendiente) return new(LoginEstado.Pendiente, u);
        if (!u.Activo) return new(LoginEstado.Inactivo, u);
        return new(LoginEstado.Ok, u);
    }

    public async Task RegistrarIngresoAsync(int usuarioId, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId, ct);
        if (u is null) return;
        u.UltimoAcceso = clock.Now;
        await db.SaveChangesAsync(ct);
    }

    static bool EmailValido(string e)
    {
        try { return new MailAddress(e).Address.Equals(e, StringComparison.OrdinalIgnoreCase) && e.Contains('.'); }
        catch (FormatException) { return false; }
    }

    public async Task<Usuario> SolicitarAsync(RegistroInput d, CancellationToken ct = default)
    {
        var nombre = (d.Nombre ?? "").Trim();
        var email = (d.Email ?? "").Trim();
        if (nombre.Length < 3) throw new ReglaException("Ingresa tu nombre completo.");
        if (!EmailValido(email)) throw new ReglaException("Ingresa un correo válido.");
        string? rut = null;
        if (!string.IsNullOrWhiteSpace(d.Rut))
        {
            if (!Domain.Rut.IsValid(d.Rut)) throw new ReglaException("El RUT no es válido (revisa el dígito verificador).");
            rut = Domain.Rut.Normalize(d.Rut);
        }
        if (string.IsNullOrWhiteSpace(d.Motivo)) throw new ReglaException("Indica tu área y para qué necesitas acceso.");
        if (d.Password != d.Confirmacion) throw new ReglaException("Las contraseñas no coinciden.");
        if (PoliticaContrasena.Validar(d.Password, email, nombre) is { Count: > 0 } errs) throw new ReglaException(string.Join(" ", errs));

        await using var db = factory.Create();
        var lower = email.ToLowerInvariant();
        var existente = await db.Usuarios.FirstOrDefaultAsync(x => x.Email.ToLower() == lower, ct);
        if (existente is { Pendiente: true }) throw new ReglaException("Ya existe una solicitud con ese correo y está en revisión.");
        if (existente is not null) throw new ReglaException("Ese correo ya tiene una cuenta. Si olvidaste tu contraseña, usa \"¿Olvidaste tu contraseña?\".");
        if (rut != null && await db.Usuarios.AnyAsync(x => x.Rut == rut, ct)) throw new ReglaException("Ese RUT ya está registrado.");

        var u = new Usuario
        {
            Nombre = nombre, Email = email, Rut = rut, Rol = Rol.Comercial, Activo = false, Aprobacion = Aprobacion.Pendiente, Origen = OrigenUsuario.Registro,
            PasswordHash = passwords.Hash(d.Password), SolicitudMotivo = d.Motivo.Trim(), CreadoAt = clock.Now,
        };
        db.Usuarios.Add(u);
        await db.SaveAsync(ct);
        await AvisarLideres(db, u, ct);
        changes.Publish(ChangeKind.Usuarios);
        return u;
    }

    public async Task<Usuario> SolicitarDesdeAdAsync(string cuentaWindows, string email, string nombre, bool esAdministrador, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        var lower = email.ToLowerInvariant();
        var u = await db.Usuarios.Include(x => x.Equipos).FirstOrDefaultAsync(x => x.Email.ToLower() == lower, ct);
        var nuevo = u is null;
        if (u is null)
        {
            u = new Usuario
            {
                Nombre = string.IsNullOrWhiteSpace(nombre) ? email.Split('@')[0] : nombre.Trim(), Email = email, DomainAccount = cuentaWindows,
                Rol = Rol.Comercial, Origen = OrigenUsuario.ActiveDirectory, CreadoAt = clock.Now, Activo = false, Aprobacion = Aprobacion.Pendiente,
                SolicitudMotivo = "Ingreso con cuenta corporativa (Active Directory) sin usuario registrado.",
            };
            db.Usuarios.Add(u);
        }
        else if (string.IsNullOrEmpty(u.DomainAccount)) u.DomainAccount = cuentaWindows;

        // Un usuario ya aprobado no se toca (si un líder lo desactivó, sigue desactivado).
        if (esAdministrador && (nuevo || u.Pendiente))
        {
            // Administradores por configuración: entran como líder (a cargo de todos los equipos) sin esperar aprobación.
            u.Rol = Rol.Lider;
            u.Equipos.Clear();
            u.Aprobacion = Aprobacion.Aprobado;
            u.Activo = true;
        }
        await db.SaveAsync(ct);
        if (nuevo && u.Pendiente) await AvisarLideres(db, u, ct); // un aviso por solicitud, no por cada intento
        changes.Publish(ChangeKind.Usuarios);
        return u;
    }

    async Task AvisarLideres(IAppDbContext db, Usuario u, CancellationToken ct)
    {
        var lideres = await db.Usuarios.AsNoTracking().Where(x => x.Rol == Rol.Lider && x.Activo).OrderBy(x => x.Id).ToListAsync(ct);
        if (lideres.Count == 0) return;
        db.Notificaciones.Add(MailBuilder.SolicitudAcceso(lideres, u).ToEntity(clock.Now));
        await db.SaveChangesAsync(ct);
        changes.Publish(ChangeKind.Notificaciones);
    }

    public async Task<List<Usuario>> PendientesAsync(UsuarioActual me, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        return await db.UsuariosConEquipos().Where(u => u.Aprobacion == Aprobacion.Pendiente).OrderBy(u => u.CreadoAt).ThenBy(u => u.Id).ToListAsync(ct);
    }

    public async Task<Usuario> AprobarAsync(UsuarioActual me, UsuarioInput datos, CancellationToken ct = default)
    {
        Guard.Lider(me);
        if (string.IsNullOrWhiteSpace(datos.Nombre) || string.IsNullOrWhiteSpace(datos.Email)) throw new ReglaException("Nombre y correo son obligatorios.");
        var equipos = datos.Rol == Rol.Comercial ? [] : datos.Equipos.Distinct().ToList();
        if (datos.Rol == Rol.Analista && equipos.Count == 0) throw new ReglaException("Selecciona al menos un equipo.");
        await using var db = factory.Create();
        var u = await db.Usuarios.Include(x => x.Equipos).FirstOrDefaultAsync(x => x.Id == datos.Id, ct) ?? throw new ReglaException("La solicitud ya no existe.");
        if (!u.Pendiente) throw new ReglaException("Esta solicitud ya fue aprobada.");
        var lower = datos.Email.Trim().ToLowerInvariant();
        if (await db.Usuarios.AnyAsync(x => x.Id != u.Id && x.Email.ToLower() == lower, ct)) throw new ReglaException("Ya existe un usuario con ese correo.");
        u.Nombre = datos.Nombre.Trim();
        u.Email = datos.Email.Trim();
        u.Rol = datos.Rol;
        u.DomainAccount = string.IsNullOrWhiteSpace(datos.DomainAccount) ? u.DomainAccount : datos.DomainAccount.Trim();
        u.Equipos.Clear();
        u.Equipos.AddRange(equipos.Select(e => new UsuarioEquipo { Equipo = e }));
        u.Aprobacion = Aprobacion.Aprobado;
        u.Activo = true;
        db.Notificaciones.Add(MailBuilder.AccesoAprobado(u).ToEntity(clock.Now));
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Usuarios);
        changes.Publish(ChangeKind.Notificaciones);
        return u;
    }

    public async Task RechazarAsync(UsuarioActual me, int id, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaException("La solicitud ya no existe.");
        if (!u.Pendiente) throw new ReglaException("Solo se pueden rechazar solicitudes pendientes.");
        db.Notificaciones.Add(MailBuilder.AccesoRechazado(u).ToEntity(clock.Now));
        db.Usuarios.Remove(u);
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Usuarios);
        changes.Publish(ChangeKind.Notificaciones);
    }

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public async Task<(ResetCreado? Reset, string? Motivo)> CrearResetAsync(string login, string ip, CancellationToken ct = default)
    {
        var r = await ValidarAsync(login, "", sinClave: true, ct);
        if (r.Usuario is null) return (null, "no existe");
        if (r.Estado != LoginEstado.Ok) return (null, r.Estado.ToString());
        var u = r.Usuario;
        if (u.PasswordHash is null && !string.IsNullOrEmpty(u.DomainAccount)) return (null, "ad");
        var now = clock.Now;
        await using var db = factory.Create();
        var recientes = await db.ResetTokens.CountAsync(t => t.UsuarioId == u.Id && t.CreatedAt > now.AddHours(-1), ct);
        if (recientes >= ResetMaxPorHora) return (null, "limite");
        foreach (var t in await db.ResetTokens.Where(t => t.UsuarioId == u.Id && t.UsedAt == null).ToListAsync(ct)) t.UsedAt = now; // solo vale el último enlace
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.ResetTokens.Add(new PasswordResetToken { UsuarioId = u.Id, TokenHash = HashToken(token), CreatedAt = now, ExpiresAt = now.Add(ResetVigencia), Ip = ip ?? "" });
        await db.SaveChangesAsync(ct);
        return (new ResetCreado(u, token), null);
    }

    public async Task<(Usuario? Usuario, string? Error)> ValidarResetAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return (null, "El enlace de recuperación está incompleto.");
        var hash = HashToken(token.Trim());
        await using var db = factory.Create();
        var t = await db.ResetTokens.AsNoTracking().FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (t is null) return (null, "El enlace de recuperación no es válido.");
        if (t.UsedAt != null) return (null, "Este enlace ya fue usado o fue reemplazado por uno más reciente. Solicita uno nuevo.");
        if (t.ExpiresAt < clock.Now) return (null, "El enlace de recuperación venció. Solicita uno nuevo.");
        var u = await db.UsuariosConEquipos().FirstOrDefaultAsync(x => x.Id == t.UsuarioId, ct);
        if (u is null || !u.Activo || u.Pendiente) return (null, "La cuenta no está activa. Comunícate con el líder de Operaciones.");
        return (u, null);
    }

    public async Task<(bool Ok, string Mensaje)> RestablecerAsync(string token, string password, string confirmacion, string ip, CancellationToken ct = default)
    {
        var (u, error) = await ValidarResetAsync(token, ct);
        if (u is null) return (false, error!);
        if (password != confirmacion) return (false, "Las contraseñas no coinciden.");
        if (PoliticaContrasena.Validar(password, u.Email, u.Nombre) is { Count: > 0 } errs) return (false, string.Join(" ", errs));
        var hash = HashToken(token.Trim());
        await using var db = factory.Create();
        var t = await db.ResetTokens.FirstAsync(x => x.TokenHash == hash, ct);
        var user = await db.Usuarios.FirstAsync(x => x.Id == u.Id, ct);
        if (t.UsedAt != null) return (false, "Este enlace ya fue usado. Solicita uno nuevo.");
        t.UsedAt = clock.Now;
        user.PasswordHash = passwords.Hash(password);
        await db.SaveChangesAsync(ct);
        return (true, "Tu contraseña fue actualizada. Ya puedes ingresar con ella.");
    }
}
