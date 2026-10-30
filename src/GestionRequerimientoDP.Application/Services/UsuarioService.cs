using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public sealed class UsuarioInput
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public Rol Rol { get; set; } = Rol.Analista;
    public List<Equipo> Equipos { get; set; } = [Equipo.Procesamiento];
    public string DomainAccount { get; set; } = "";
    /// <summary>RUT opcional (cualquier formato; se guarda normalizado).</summary>
    public string Rut { get; set; } = "";
    /// <summary>Vacío = no cambiar.</summary>
    public string Password { get; set; } = "";

    public static UsuarioInput From(Usuario u) => new()
    {
        Id = u.Id, Nombre = u.Nombre, Email = u.Email, Rol = u.Rol, Equipos = u.EquiposOrdenados.ToList(), DomainAccount = u.DomainAccount ?? "",
        Rut = string.IsNullOrEmpty(u.Rut) ? "" : Domain.Rut.Format(u.Rut),
    };
}

public interface IUsuarioService
{
    Task<List<Usuario>> ListAsync(CancellationToken ct = default);
    Task<Usuario?> GetAsync(int id, CancellationToken ct = default);
    Task<Usuario?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<Usuario?> FindByDomainAccountAsync(string account, CancellationToken ct = default);
    /// <summary>Busca por correo o por RUT (cualquier formato).</summary>
    Task<Usuario?> FindByLoginAsync(string emailOrRut, CancellationToken ct = default);
    Task<Dictionary<int, int>> CargaActivaAsync(CancellationToken ct = default);
    Task<Dictionary<int, int>> SolicitudesPorComercialAsync(CancellationToken ct = default);
    Task<Usuario> SaveAsync(UsuarioActual me, UsuarioInput input, CancellationToken ct = default);
    /// <summary>Activa o desactiva. Devuelve un aviso si quedan requerimientos activos por reasignar.</summary>
    Task<string?> ToggleActivoAsync(UsuarioActual me, int id, CancellationToken ct = default);
    /// <summary>Ids de usuarios con historial (entregables terminados, OK dados o solicitudes): no se pueden eliminar.</summary>
    Task<HashSet<int>> ConHistorialAsync(CancellationToken ct = default);
    /// <summary>Elimina un usuario sin historial. Sus requerimientos abiertos quedan sin asignar. Devuelve cuántos se liberaron.</summary>
    Task<int> EliminarAsync(UsuarioActual me, int id, CancellationToken ct = default);
}

public sealed class UsuarioService(IAppDbContextFactory factory, IChangeNotifier changes, IPasswordService passwords) : IUsuarioService
{
    public async Task<List<Usuario>> ListAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.UsuariosConEquipos().OrderBy(u => u.Id).ToListAsync(ct);
    }

    public async Task<Usuario?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.UsuariosConEquipos().FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<Usuario?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        if (e.Length == 0) return null;
        await using var db = factory.Create();
        return await db.UsuariosConEquipos().FirstOrDefaultAsync(u => u.Email.ToLower() == e, ct);
    }

    public async Task<Usuario?> FindByLoginAsync(string emailOrRut, CancellationToken ct = default)
    {
        if (!Domain.Rut.IsCandidate(emailOrRut)) return await FindByEmailAsync(emailOrRut, ct);
        var rut = Domain.Rut.Normalize(emailOrRut);
        await using var db = factory.Create();
        return await db.UsuariosConEquipos().FirstOrDefaultAsync(u => u.Rut == rut, ct);
    }

    public async Task<Usuario?> FindByDomainAccountAsync(string account, CancellationToken ct = default)
    {
        var a = (account ?? "").Trim().ToLowerInvariant();
        if (a.Length == 0) return null;
        await using var db = factory.Create();
        return await db.UsuariosConEquipos().FirstOrDefaultAsync(u => u.DomainAccount != null && u.DomainAccount.ToLower() == a, ct);
    }

    public async Task<Dictionary<int, int>> CargaActivaAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Requerimientos.Where(r => r.AnalistaId != null && r.Estado != Estado.Ok)
            .GroupBy(r => r.AnalistaId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }

    public async Task<Dictionary<int, int>> SolicitudesPorComercialAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Solicitudes.GroupBy(s => s.SolicitanteId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }

    public async Task<Usuario> SaveAsync(UsuarioActual me, UsuarioInput u, CancellationToken ct = default)
    {
        Guard.Lider(me);
        if (string.IsNullOrWhiteSpace(u.Nombre) || string.IsNullOrWhiteSpace(u.Email)) throw new ReglaException("Nombre y correo son obligatorios.");
        var email = u.Email.Trim();
        await using var db = factory.Create();
        var lower = email.ToLowerInvariant();
        if (await db.Usuarios.AnyAsync(x => x.Id != u.Id && x.Email.ToLower() == lower, ct)) throw new ReglaException("Ya existe un usuario con ese correo.");
        if (u.Id == me.Id && u.Rol != Rol.Lider) throw new ReglaException("No puedes quitarte el rol de líder.");
        var equipos = u.Rol == Rol.Comercial ? [] : u.Equipos.Distinct().ToList();
        if (u.Rol == Rol.Analista && equipos.Count == 0) throw new ReglaException("Selecciona al menos un equipo.");
        string? rut = null;
        if (!string.IsNullOrWhiteSpace(u.Rut))
        {
            if (!Domain.Rut.IsValid(u.Rut)) throw new ReglaException("El RUT no es válido (revisa el dígito verificador).");
            rut = Domain.Rut.Normalize(u.Rut);
            if (await db.Usuarios.AnyAsync(x => x.Id != u.Id && x.Rut == rut, ct)) throw new ReglaException("Ya existe un usuario con ese RUT.");
        }
        if (!string.IsNullOrWhiteSpace(u.Password) && PoliticaContrasena.Validar(u.Password, email, u.Nombre) is { Count: > 0 } errs)
            throw new ReglaException("Contraseña: " + string.Join(" ", errs));

        Usuario entity;
        if (u.Id == 0)
        {
            entity = new Usuario { Activo = true, Origen = OrigenUsuario.Admin, CreadoAt = DateTimeOffset.UtcNow };
            db.Usuarios.Add(entity);
        }
        else
        {
            entity = await db.Usuarios.Include(x => x.Equipos).FirstOrDefaultAsync(x => x.Id == u.Id, ct) ?? throw new ReglaException("El usuario ya no existe.");
            entity.Equipos.Clear();
        }
        entity.Nombre = u.Nombre.Trim();
        entity.Email = email;
        entity.Rol = u.Rol;
        entity.DomainAccount = string.IsNullOrWhiteSpace(u.DomainAccount) ? null : u.DomainAccount.Trim();
        entity.Rut = rut;
        entity.Equipos.AddRange(equipos.Select(e => new UsuarioEquipo { Equipo = e }));
        if (!string.IsNullOrWhiteSpace(u.Password)) entity.PasswordHash = passwords.Hash(u.Password);
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Usuarios);
        return entity;
    }

    public async Task<HashSet<int>> ConHistorialAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();
        var ok = await db.Requerimientos.Where(r => r.OkById != null).Select(r => r.OkById!.Value).Distinct().ToListAsync(ct);
        var hechos = await db.Requerimientos.Where(r => r.AnalistaId != null && r.Estado == Estado.Ok).Select(r => r.AnalistaId!.Value).Distinct().ToListAsync(ct);
        var sols = await db.Solicitudes.Select(s => s.SolicitanteId).Distinct().ToListAsync(ct);
        return [.. ok, .. hechos, .. sols];
    }

    public async Task<int> EliminarAsync(UsuarioActual me, int id, CancellationToken ct = default)
    {
        Guard.Lider(me);
        if (id == me.Id) throw new ReglaException("No puedes eliminar tu propio usuario.");
        if ((await ConHistorialAsync(ct)).Contains(id))
            throw new ReglaException("El usuario tiene historial (entregables terminados, OK registrados o solicitudes). Desactívalo en vez de eliminarlo.");
        await using var db = factory.Create();
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaException("El usuario ya no existe.");
        var abiertos = await db.Requerimientos.Where(r => r.AnalistaId == id).ToListAsync(ct);
        foreach (var r in abiertos) r.AnalistaId = null;
        db.Usuarios.Remove(u);
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Usuarios);
        if (abiertos.Count > 0) changes.Publish(ChangeKind.Requerimientos);
        return abiertos.Count;
    }

    public async Task<string?> ToggleActivoAsync(UsuarioActual me, int id, CancellationToken ct = default)
    {
        Guard.Lider(me);
        if (id == me.Id) throw new ReglaException("No puedes desactivar tu propio usuario");
        await using var db = factory.Create();
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaException("El usuario ya no existe.");
        if (u.Pendiente) throw new ReglaException("La solicitud de acceso está pendiente: apruébala o recházala en Solicitudes de acceso.");
        u.Activo = !u.Activo;
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Usuarios);
        if (u.Activo) return null;
        var act = await db.Requerimientos.CountAsync(r => r.AnalistaId == id && r.Estado != Estado.Ok, ct);
        return act > 0 ? $"{u.Nombre} tiene {act} requerimientos activos: reasígnalos" : null;
    }
}
