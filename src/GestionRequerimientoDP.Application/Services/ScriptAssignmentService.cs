using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

/// <summary>Bloque de la cola sin asignar: entregables de una misma solicitud (o del mismo Job) que se asignan juntos.</summary>
public sealed record ScriptBloque(string Clave, string? Codigo, List<Requerimiento> Items)
{
    public Requerimiento Primero => Items[0];
    public DateOnly Fecha => Items.Min(r => r.Fecha);
}

/// <param name="Lideres">Líderes activos: también pueden tomar un script (asignación manual).</param>
public sealed record ScriptsView(List<RankEntry> Ranking, List<Requerimiento> Scripts, List<Requerimiento> SinAsignar, List<ScriptBloque> Cola, List<Usuario> Lideres);

public interface IScriptAssignmentService
{
    Task<ScriptsView> VistaAsync(UsuarioActual me, CancellationToken ct = default);
    /// <summary>Asigna la cola sin analista por bloque (solicitud o Job) al primero del ranking, recalculándolo tras cada bloque. Devuelve (asignados, correos).</summary>
    Task<(int Asignados, int Correos)> AutoAssignAsync(UsuarioActual me, CancellationToken ct = default);
    /// <summary>
    /// Asigna una solicitud completa de la cola (todos sus entregables sin analista) a la persona que elige el líder:
    /// no todos los analistas tienen el perfil para cada tipo de script. Devuelve (asignados, correo enviado).
    /// </summary>
    Task<(int Asignados, bool Correo)> AsignarBloqueAsync(UsuarioActual me, string bloque, int analistaId, CancellationToken ct = default);
}

public sealed class ScriptAssignmentService(IAppDbContextFactory factory, IClock clock, IChangeNotifier changes, INotificationService notif) : IScriptAssignmentService
{
    static IQueryable<Requerimiento> RankingSource(IAppDbContext db) =>
        db.Requerimientos.Where(r => r.Tipo == Equipo.Script || (r.Tipo == Equipo.Procesamiento && r.Estado != Estado.Ok));

    public async Task<ScriptsView> VistaAsync(UsuarioActual me, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var users = await db.UsuariosConEquipos().OrderBy(u => u.Id).ToListAsync(ct);
        var reqs = await RankingSource(db).AsNoTracking().ToListAsync(ct);
        var scripts = reqs.Where(r => r.Tipo == Equipo.Script).ToList();
        var sinAsignar = scripts.Where(r => r.AnalistaId == null && r.Estado != Estado.Ok).OrderBy(r => r.Fecha).ToList();
        var ids = sinAsignar.Where(r => r.SolicitudId != null).Select(r => r.SolicitudId!.Value).Distinct().ToList();
        var codigos = await db.Solicitudes.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Codigo, ct);
        var cola = sinAsignar.GroupBy(BloqueDe)
            .Select(g => new ScriptBloque(g.Key, g.First().SolicitudId is { } sid ? codigos.GetValueOrDefault(sid) : null, g.OrderBy(r => r.Id).ToList()))
            .OrderBy(b => b.Fecha).ThenBy(b => b.Primero.Id).ToList();
        var lideres = users.Where(u => u.Rol == Rol.Lider && u.Activo).OrderBy(u => u.Nombre, StringComparer.CurrentCulture).ToList();
        return new(ScriptRanking.Rank(users, reqs, clock.Today), scripts, sinAsignar, cola, lideres);
    }

    /// <summary>Entregables que se asignan juntos: los de una misma solicitud comercial o, si no vienen de una, los del mismo Job.</summary>
    public static string BloqueDe(Requerimiento r) => r.SolicitudId is { } s ? $"S{s}" : $"J{Reglas.Norm(r.Job)}";

    public async Task<(int Asignados, bool Correo)> AsignarBloqueAsync(UsuarioActual me, string bloque, int analistaId, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var users = await db.UsuariosMapAsync(ct);
        if (!(users.TryGetValue(analistaId, out var u) && Reglas.Asignable(u, Equipo.Script)))
            throw new ReglaException("Elige un analista de scripting o un líder activo.");
        var items = (await db.Requerimientos.Where(r => r.Tipo == Equipo.Script && r.AnalistaId == null && r.Estado != Estado.Ok).ToListAsync(ct))
            .Where(r => BloqueDe(r) == bloque).ToList();
        if (items.Count == 0) throw new ReglaException("Esa solicitud ya fue asignada por otra persona. Se recargó la cola.");
        var now = clock.Now;
        foreach (var r in items)
        {
            r.AnalistaId = analistaId;
            r.Actualizado = now;
            db.Log(r, OkAccion.Asignacion, me.Id, now, new { analista = analistaId, bloque });
        }
        var sent = await notif.QueueAsignacionAsync(db, items, users, me.Id, ct) > 0;
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
        if (items.Any(r => r.SolicitudId != null)) changes.Publish(ChangeKind.Solicitudes);
        if (sent) changes.Publish(ChangeKind.Notificaciones);
        return (items.Count, sent);
    }

    public async Task<(int Asignados, int Correos)> AutoAssignAsync(UsuarioActual me, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var users = await db.UsuariosMapAsync(ct);
        var reqs = await RankingSource(db).ToListAsync(ct);
        var cola = reqs.Where(r => r.Tipo == Equipo.Script && r.AnalistaId == null && r.Estado != Estado.Ok).OrderBy(r => r.Id).ToList();
        var now = clock.Now;
        var done = new List<Requerimiento>();
        // Una solicitud (o un Job sin solicitud) con varios entregables va completa al mismo analista: no se separa,
        // igual que en el balanceo. El ranking se recalcula después de cada bloque.
        foreach (var bloque in cola.GroupBy(BloqueDe).OrderBy(g => g.Min(r => r.Id)))
        {
            var top = ScriptRanking.Rank(users.Values.OrderBy(u => u.Id), reqs, clock.Today).FirstOrDefault();
            if (top is null) break;
            foreach (var r in bloque)
            {
                r.AnalistaId = top.Analista.Id;
                r.Actualizado = now;
                db.Log(r, OkAccion.Asignacion, me.Id, now, new { analista = r.AnalistaId, auto = true });
                done.Add(r);
            }
        }
        var n = await notif.QueueAsignacionAsync(db, done, users, me.Id, ct);
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
        if (done.Any(r => r.SolicitudId != null)) changes.Publish(ChangeKind.Solicitudes);
        if (n > 0) changes.Publish(ChangeKind.Notificaciones);
        return (done.Count, n);
    }
}
