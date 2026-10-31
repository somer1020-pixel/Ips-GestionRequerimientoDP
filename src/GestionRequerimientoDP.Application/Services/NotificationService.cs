using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GestionRequerimientoDP.Application.Services;

public interface INotificationService
{
    /// <summary>Encola correos de asignación (uno por analista) en el contexto dado. No guarda.</summary>
    /// <param name="actorId">Quien asigna: no recibe correo por lo que se asigna a sí mismo.</param>
    int QueueAsignacion(IAppDbContext db, IEnumerable<Requerimiento> asignados, IReadOnlyDictionary<int, Usuario> usuarios, int? actorId = null);

    /// <summary>Aviso de atraso sin gestión. force = ignorar el límite de un aviso por día.</summary>
    Task<int> CheckOverdueAsync(bool force, CancellationToken ct = default);

    Task<List<Notificacion>> ListAsync(int take = 300, CancellationToken ct = default);
}

public sealed class NotificationService(IAppDbContextFactory factory, IClock clock, IOptionsMonitor<ReglasOptions> opts, IChangeNotifier changes) : INotificationService
{
    public int QueueAsignacion(IAppDbContext db, IEnumerable<Requerimiento> asignados, IReadOnlyDictionary<int, Usuario> usuarios, int? actorId = null)
    {
        if (!opts.CurrentValue.NotifyAssign) return 0;
        var mails = MailBuilder.AsignacionPorAnalista(asignados.Where(r => actorId is null || r.AnalistaId != actorId), usuarios);
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
        var leaders = users.Values.Where(u => u.Rol == Rol.Lider && u.Activo).OrderBy(u => u.Id).ToList();
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
