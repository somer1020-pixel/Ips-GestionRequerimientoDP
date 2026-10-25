using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GestionRequerimientoDP.Application.Services;

public interface IPlanificacionService
{
    Task<PlanView> SemanaAsync(UsuarioActual me, Equipo unidad, DateOnly week, CancellationToken ct = default);
    /// <summary>Semanas para el selector: anterior, actual, siguiente y futuras con datos de la unidad.</summary>
    Task<List<DateOnly>> SemanasAsync(UsuarioActual me, Equipo unidad, DateOnly seleccionada, CancellationToken ct = default);
}

public interface IBalanceService
{
    Task<List<BalanceMove>> SuggestAsync(UsuarioActual me, Equipo unidad, DateOnly week, CancellationToken ct = default);
    /// <summary>Aplica la propuesta en una transacción. Devuelve los correos enviados.</summary>
    Task<int> ApplyAsync(UsuarioActual me, IReadOnlyList<BalanceMove> moves, CancellationToken ct = default);
}

public sealed class PlanificacionService(IAppDbContextFactory factory, IClock clock, IOptionsMonitor<ReglasOptions> opts, INotificationService notif, IChangeNotifier changes)
    : IPlanificacionService, IBalanceService
{
    public async Task<PlanView> SemanaAsync(UsuarioActual me, Equipo unidad, DateOnly week, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var users = await db.UsuariosConEquipos().ToListAsync(ct);
        var end = week.AddDays(6);
        var reqs = await db.Requerimientos.AsNoTracking().Where(r => r.Fecha >= week && r.Fecha <= end && (r.Tipo == unidad || r.Tipo == Equipo.Script)).ToListAsync(ct);
        return PlanEngine.Build(unidad, week, users, reqs, opts.CurrentValue.CapacityOf(unidad));
    }

    public async Task<List<DateOnly>> SemanasAsync(UsuarioActual me, Equipo unidad, DateOnly seleccionada, CancellationToken ct = default)
    {
        Guard.Lider(me);
        var cur = Fechas.MondayOf(clock.Today);
        var desde = cur.AddDays(-7);
        await using var db = factory.Create();
        var fechas = await db.Requerimientos.AsNoTracking().Where(r => r.Tipo == unidad && r.Fecha >= desde).Select(r => r.Fecha).Distinct().ToListAsync(ct);
        return fechas.Select(Fechas.MondayOf).Append(cur).Append(cur.AddDays(7)).Append(seleccionada).Distinct().Order().ToList();
    }

    public async Task<List<BalanceMove>> SuggestAsync(UsuarioActual me, Equipo unidad, DateOnly week, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var users = await db.UsuariosConEquipos().OrderBy(u => u.Id).ToListAsync(ct);
        var end = week.AddDays(6);
        var reqs = await db.Requerimientos.AsNoTracking().Where(r => r.Fecha >= week && r.Fecha <= end && (r.Tipo == unidad || r.Tipo == Equipo.Script)).ToListAsync(ct);
        return BalanceEngine.Suggest(unidad, week, users, reqs, opts.CurrentValue.CapacityOf(unidad));
    }

    public async Task<int> ApplyAsync(UsuarioActual me, IReadOnlyList<BalanceMove> moves, CancellationToken ct = default)
    {
        Guard.Lider(me);
        // Un job puede moverse más de una vez (A→B y luego B→C): se conserva el origen de la primera
        // propuesta y el destino de la última, como al aplicar los movimientos en orden.
        var map = new Dictionary<int, (int? From, int To)>();
        foreach (var m in moves)
            foreach (var id in m.Ids)
                map[id] = map.TryGetValue(id, out var prev) ? (prev.From, m.To) : (m.From, m.To);
        if (map.Count == 0) return 0;
        await using var db = factory.Create();
        var users = await db.UsuariosMapAsync(ct);
        var ids = map.Keys.ToList();
        var reqs = await db.Requerimientos.Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        // Si algún entregable cambió de analista o dejó de estar pendiente desde la propuesta, se rechaza completa.
        if (reqs.Count != ids.Count || reqs.Any(r => r.AnalistaId != map[r.Id].From || (r.AnalistaId != null && r.Estado != Estado.Pendiente)))
            throw new ReglaException("La planificación cambió desde que se generó la propuesta. Vuelve a sugerir el balanceo.");
        var now = clock.Now;
        foreach (var r in reqs)
        {
            r.AnalistaId = map[r.Id].To;
            r.Actualizado = now;
            db.Log(r, OkAccion.Asignacion, me.Id, now, new { analista = r.AnalistaId, balanceo = true });
        }
        var n = notif.QueueAsignacion(db, reqs, users);
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
        if (reqs.Any(r => r.SolicitudId != null)) changes.Publish(ChangeKind.Solicitudes);
        if (n > 0) changes.Publish(ChangeKind.Notificaciones);
        return n;
    }
}
