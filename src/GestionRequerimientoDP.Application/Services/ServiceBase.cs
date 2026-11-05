using System.Text.Json;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

internal static class Guard
{
    public static void Lider(UsuarioActual me)
    {
        if (!me.EsLider) throw new PermisoException("Solo el líder de equipo puede realizar esta acción.");
    }

    /// <summary>
    /// ¿Administra usuarios (crear, editar, roles, accesos, auditoría)? Solo el administrador. Transición: mientras no
    /// exista ningún administrador activo, los líderes siguen haciéndolo (así alguien puede asignar el primer administrador).
    /// </summary>
    public static async Task<bool> AdministraUsuariosAsync(IAppDbContext db, UsuarioActual me, CancellationToken ct) =>
        me.EsAdmin || (me.Rol == Rol.Lider && !await db.Usuarios.AnyAsync(u => u.Rol == Rol.Admin && u.Activo, ct));

    public static async Task AdministraUsuarios(IAppDbContext db, UsuarioActual me, CancellationToken ct)
    {
        if (!await AdministraUsuariosAsync(db, me, ct)) throw new PermisoException("Solo el administrador gestiona usuarios y accesos.");
    }

    /// <summary>El líder edita todo; el analista solo lo asignado a él.</summary>
    public static bool CanEdit(UsuarioActual me, Requerimiento r) => me.EsLider || (me.EsAnalista && r.AnalistaId == me.Id);

    public static void Edit(UsuarioActual me, Requerimiento r)
    {
        if (!CanEdit(me, r)) throw new PermisoException("Solo puedes actualizar requerimientos asignados a ti.");
    }
}

internal static class DbExtensions
{
    public static IQueryable<Usuario> UsuariosConEquipos(this IAppDbContext db) => db.Usuarios.Include(u => u.Equipos).AsNoTracking();

    public static async Task<Dictionary<int, Usuario>> UsuariosMapAsync(this IAppDbContext db, CancellationToken ct = default) =>
        await db.UsuariosConEquipos().ToDictionaryAsync(u => u.Id, ct);

    /// <summary>Filtro de seguridad: el analista solo ve lo asignado a él y el comercial no ve la carga.</summary>
    public static IQueryable<Requerimiento> Visibles(this IAppDbContext db, UsuarioActual me) => me.Rol switch
    {
        Rol.Lider or Rol.Admin => db.Requerimientos,
        Rol.Analista => db.Requerimientos.Where(r => r.AnalistaId == me.Id),
        _ => db.Requerimientos.Where(_ => false),
    };

    public static void Log(this IAppDbContext db, Requerimiento r, OkAccion accion, int? usuarioId, DateTimeOffset at, object payload) =>
        db.OkLogs.Add(new OkLog { ReqId = r.Id, Accion = accion, UsuarioId = usuarioId, At = at, Payload = JsonSerializer.Serialize(payload) });

    /// <summary>Guarda y traduce los conflictos de concurrencia (rowversion) a un mensaje para el usuario.</summary>
    public static async Task SaveAsync(this IAppDbContext db, CancellationToken ct = default)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ReglaException("Otra persona modificó estos datos al mismo tiempo. Se recargaron los datos; vuelve a intentarlo.");
        }
    }
}
