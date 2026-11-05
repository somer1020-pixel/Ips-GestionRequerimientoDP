using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

/// <summary>Hilo de comentarios de un requerimiento: datos para mostrarlo y saber si el usuario puede escribir.</summary>
public sealed record HiloComentarios(Requerimiento Req, string Responsable, string? Solicitante, List<Comentario> Comentarios, Dictionary<int, Usuario> Usuarios, bool VeDetalle);

public interface IComentarioService
{
    /// <summary>Hilo del requerimiento, o null si no existe o el usuario no participa en él.</summary>
    Task<HiloComentarios?> HiloAsync(UsuarioActual me, int reqId, CancellationToken ct = default);
    /// <summary>Usuarios que se pueden mencionar: todos los activos y aprobados, menos uno mismo.</summary>
    Task<List<Usuario>> MencionablesAsync(UsuarioActual me, CancellationToken ct = default);
    /// <summary>Cantidad de comentarios por requerimiento (para los contadores).</summary>
    Task<Dictionary<int, int>> ConteoAsync(IEnumerable<int> reqIds, CancellationToken ct = default);
    /// <summary>
    /// Agrega un comentario. Las menciones "@Nombre" a usuarios activos se avisan por correo con el enlace
    /// <paramref name="enlace"/>. Devuelve los mencionados que recibirán el aviso.
    /// </summary>
    Task<List<Usuario>> AgregarAsync(UsuarioActual me, int reqId, string texto, string enlace, CancellationToken ct = default);
}

public sealed class ComentarioService(IAppDbContextFactory factory, IClock clock, IChangeNotifier changes) : IComentarioService
{
    /// <summary>
    /// Participan del hilo: líderes y administrador, el responsable asignado, el solicitante (comercial o PM) de la solicitud
    /// de origen y quien haya sido mencionado antes en el mismo requerimiento.
    /// </summary>
    static async Task<bool> ParticipaAsync(IAppDbContext db, UsuarioActual me, Requerimiento r, CancellationToken ct)
    {
        if (me.EsLider || r.AnalistaId == me.Id) return true;
        if (r.SolicitudId is { } sid && await db.Solicitudes.AnyAsync(s => s.Id == sid && s.SolicitanteId == me.Id, ct)) return true;
        var marca = "," + me.Id + ",";
        return await db.Comentarios.AnyAsync(c => c.RequerimientoId == r.Id && c.Menciones.Contains(marca), ct);
    }

    public async Task<HiloComentarios?> HiloAsync(UsuarioActual me, int reqId, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        var r = await db.Requerimientos.Include(x => x.Solicitud).AsNoTracking().FirstOrDefaultAsync(x => x.Id == reqId, ct);
        if (r is null || !await ParticipaAsync(db, me, r, ct)) return null;
        var comentarios = await db.Comentarios.AsNoTracking().Where(c => c.RequerimientoId == reqId).OrderBy(c => c.CreadoAt).ThenBy(c => c.Id).ToListAsync(ct);
        var usuarios = await db.UsuariosMapAsync(ct);
        string Nombre(int? id) => id is { } i && usuarios.TryGetValue(i, out var u) ? Reglas.NombreResponsable(u) : "Sin asignar";
        var solicitante = r.Solicitud is { } s && usuarios.TryGetValue(s.SolicitanteId, out var su) ? MailBuilder.NombreSolicitante(su) : null;
        return new(r, Nombre(r.AnalistaId), solicitante, comentarios, usuarios, me.EsLider || (me.EsAnalista && r.AnalistaId == me.Id));
    }

    public async Task<List<Usuario>> MencionablesAsync(UsuarioActual me, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return (await db.Usuarios.AsNoTracking().Where(u => u.Activo && u.Id != me.Id).ToListAsync(ct))
            .Where(u => !u.Pendiente).OrderBy(u => u.Nombre, StringComparer.CurrentCulture).ToList();
    }

    public async Task<Dictionary<int, int>> ConteoAsync(IEnumerable<int> reqIds, CancellationToken ct = default)
    {
        var ids = reqIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var db = factory.Create();
        return await db.Comentarios.Where(c => ids.Contains(c.RequerimientoId)).GroupBy(c => c.RequerimientoId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }

    public async Task<List<Usuario>> AgregarAsync(UsuarioActual me, int reqId, string texto, string enlace, CancellationToken ct = default)
    {
        texto = (texto ?? "").Replace("\r\n", "\n").Trim();
        if (texto.Length == 0) throw new ReglaException("Escribe un comentario.");
        if (texto.Length > Reglas.ComentarioMax) throw new ReglaException($"El comentario supera los {Reglas.ComentarioMax} caracteres.");
        await using var db = factory.Create();
        var r = await db.Requerimientos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == reqId, ct) ?? throw new ReglaException("El requerimiento ya no existe.");
        if (!await ParticipaAsync(db, me, r, ct)) throw new PermisoException("No participas en este requerimiento.");

        var usuarios = await db.UsuariosMapAsync(ct);
        var autor = usuarios[me.Id];
        var candidatos = usuarios.Values.Where(u => u.Activo && !u.Pendiente && u.Id != me.Id);
        var mencionados = Reglas.Menciones(texto, candidatos);
        db.Comentarios.Add(new Comentario
        {
            RequerimientoId = reqId, AutorId = me.Id, Texto = texto, Menciones = Comentario.Codificar(mencionados.Select(u => u.Id)), CreadoAt = clock.Now,
        });
        if (mencionados.Count > 0)
        {
            var resp = r.AnalistaId is { } a && usuarios.TryGetValue(a, out var ru) ? ru : null;
            var copia = resp is { Activo: true } && resp.Id != me.Id && mencionados.All(m => m.Id != resp.Id) ? resp : null;
            var responsable = resp is null ? "Sin asignar" : Reglas.NombreResponsable(resp);
            db.Notificaciones.Add(MailBuilder.Mencion(autor, mencionados, copia, r, responsable, texto, enlace).ToEntity(clock.Now));
        }
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Comentarios);
        if (mencionados.Count > 0) changes.Publish(ChangeKind.Notificaciones);
        return mencionados;
    }
}
