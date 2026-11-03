using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

public sealed record ScriptsView(List<RankEntry> Ranking, List<Requerimiento> Scripts, List<Requerimiento> SinAsignar);

public interface IScriptAssignmentService
{
    Task<ScriptsView> VistaAsync(UsuarioActual me, CancellationToken ct = default);
    /// <summary>Asigna la cola sin analista recalculando el ranking tras cada uno. Devuelve (asignados, correos).</summary>
    Task<(int Asignados, int Correos)> AutoAssignAsync(UsuarioActual me, CancellationToken ct = default);
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
        return new(ScriptRanking.Rank(users, reqs, clock.Today), scripts, scripts.Where(r => r.AnalistaId == null && r.Estado != Estado.Ok).OrderBy(r => r.Fecha).ToList());
    }

    /// <summary>Entregables que se asignan juntos: los de una misma solicitud comercial o, si no vienen de una, los del mismo Job.</summary>
    public static string BloqueDe(Requerimiento r) => r.SolicitudId is { } s ? $"S{s}" : $"J{Reglas.Norm(r.Job)}";

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
        var n = notif.QueueAsignacion(db, done, users, me.Id);
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
        if (done.Any(r => r.SolicitudId != null)) changes.Publish(ChangeKind.Solicitudes);
        if (n > 0) changes.Publish(ChangeKind.Notificaciones);
        return (done.Count, n);
    }
}
