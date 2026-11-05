using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GestionRequerimientoDP.Application.Services;

public interface INotificationService
{
    /// <summary>
    /// Encola en el contexto dado (no guarda) los correos de asignación: uno por analista y, para entregables que vienen
    /// de una solicitud, un aviso al solicitante con quién la gestionará (uno por Job y solicitante). Devuelve cuántos.
    /// </summary>
    /// <param name="actorId">Quien asigna: no recibe correo por lo que se asigna a sí mismo.</param>
    Task<int> QueueAsignacionAsync(IAppDbContext db, IEnumerable<Requerimiento> asignados, IReadOnlyDictionary<int, Usuario> usuarios, int? actorId = null, CancellationToken ct = default);

    /// <summary>Aviso de atraso sin gestión. force = ignorar el límite de un aviso por día.</summary>
    Task<int> CheckOverdueAsync(bool force, CancellationToken ct = default);

    Task<List<Notificacion>> ListAsync(int take = 300, CancellationToken ct = default);
}

public sealed class NotificationService(IAppDbContextFactory factory, IClock clock, IOptionsMonitor<ReglasOptions> opts, IChangeNotifier changes) : INotificationService
{
    public async Task<int> QueueAsignacionAsync(IAppDbContext db, IEnumerable<Requerimiento> asignados, IReadOnlyDictionary<int, Usuario> usuarios, int? actorId = null, CancellationToken ct = default)
    {
        if (!opts.CurrentValue.NotifyAssign) return 0;
        var lista = asignados.ToList();
        var mails = MailBuilder.AsignacionPorAnalista(lista.Where(r => actorId is null || r.AnalistaId != actorId), usuarios);

        // Aviso al solicitante (comercial o PM): quién gestionará su solicitud, un correo por Job y solicitante.
        var conSolicitud = lista.Where(r => r.AnalistaId != null && r.SolicitudId != null).ToList();
        if (conSolicitud.Count > 0)
        {
            var solIds = conSolicitud.Select(r => r.SolicitudId!.Value).Distinct().ToList();
            var solicitudes = await db.Solicitudes.AsNoTracking().Where(s => solIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
            var enLote = lista.Select(r => r.Id).ToHashSet();
            var pendientes = (await db.Requerimientos.AsNoTracking().Where(r => r.SolicitudId != null && solIds.Contains(r.SolicitudId.Value) && r.AnalistaId == null).ToListAsync(ct))
                .Where(r => !enLote.Contains(r.Id)).ToList(); // los del lote ya no están pendientes aunque aún no se guarden
            foreach (var g in conSolicitud.Where(r => solicitudes.ContainsKey(r.SolicitudId!.Value))
                         .GroupBy(r => (solicitudes[r.SolicitudId!.Value].SolicitanteId, Job: Reglas.Norm(r.Job))))
            {
                if (g.Key.SolicitanteId == actorId || !usuarios.TryGetValue(g.Key.SolicitanteId, out var solicitante)) continue;
                var items = g.OrderBy(r => r.Fecha).ThenBy(r => r.Id).ToList();
                var ids = items.Select(r => r.SolicitudId!.Value).Distinct().ToList();
                mails.Add(MailBuilder.SolicitudAsignada(solicitante, ids.Select(i => solicitudes[i].Codigo).ToList(), items, usuarios,
                    pendientes.Where(p => ids.Contains(p.SolicitudId!.Value)).OrderBy(p => p.Fecha).ToList()));
            }
        }
        foreach (var m in mails) db.Notificaciones.Add(m.ToEntity(clock.Now));
        return mails.Count;
    }

    public async Task<int> CheckOverdueAsync(bool force, CancellationToken ct = default)
    {
        var o = opts.CurrentValue;
        if (!o.NotifyOverdue) return 0;
        var today = clock.Today;
        await using var db = factory.Create();
        var cand = (await db.Requerimientos.Where(r => r.Estado != Estado.Ok && r.Fecha < today && (force || r.AlertAt != today)).ToListAsync(ct))
            .Where(r => Reglas.SinGestion(r, today, o.StaleDays))
            .ToList();
        if (cand.Count == 0) return 0;

        var users = await db.UsuariosMapAsync(ct);
        // Líderes y administradores reciben (en copia) el aviso diario de todos los atrasados.
        var leaders = users.Values.Where(u => Reglas.EsGestor(u.Rol) && u.Activo).OrderBy(u => u.Id).ToList();
        var n = 0;
        foreach (var g in cand.OrderBy(r => r.Fecha).GroupBy(r => r.AnalistaId))
        {
            var analista = g.Key is { } id && users.TryGetValue(id, out var u) ? u : null;
            db.Notificaciones.Add(MailBuilder.Atraso(analista, g.ToList(), leaders).ToEntity(clock.Now));
            n++;
        }
        foreach (var r in cand) r.AlertAt = today; // no cuenta como gestión: no toca "Actualizado"
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Notificaciones);
        return n;
    }

    public async Task<List<Notificacion>> ListAsync(int take = 300, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Notificaciones.AsNoTracking().OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(take).ToListAsync(ct);
    }
}
