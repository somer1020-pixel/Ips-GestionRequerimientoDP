using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

public sealed record ResumenView(List<Requerimiento> Scope, List<Requerimiento> Pendientes, List<Kpi> Kpis, string Texto);

public interface IKpiService
{
    Task<HistoricoView> HistoricoAsync(UsuarioActual me, Periodo p, int? analista, CancellationToken ct = default);
    Task<AtrasosView> AtrasosAsync(UsuarioActual me, Periodo p, int? analista, Equipo? unidad, CancellationToken ct = default);
}

public interface ISummaryService
{
    Task<ResumenView> ResumenAsync(UsuarioActual me, CancellationToken ct = default);
}

public sealed class KpiService(IAppDbContextFactory factory, IClock clock, IRequerimientoService reqs) : IKpiService, ISummaryService
{
    public async Task<HistoricoView> HistoricoAsync(UsuarioActual me, Periodo p, int? analista, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var users = await db.UsuariosConEquipos().ToListAsync(ct);
        var items = await db.Requerimientos.AsNoTracking().Where(r => r.Fecha >= p.Desde && r.Fecha <= p.Hasta).ToListAsync(ct);
        return KpiEngine.Historico(p, items, analista, users, clock.Today);
    }

    public async Task<AtrasosView> AtrasosAsync(UsuarioActual me, Periodo p, int? analista, Equipo? unidad, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var items = await db.Requerimientos.AsNoTracking().Where(r => r.Fecha >= p.Desde && r.Fecha <= p.Hasta).ToListAsync(ct);
        return KpiEngine.Atrasos(p, items, analista, unidad, clock.Today);
    }

    public async Task<ResumenView> ResumenAsync(UsuarioActual me, CancellationToken ct = default)
    {
        Guard.Lider(me);
        var today = clock.Today;
        var cur = Fechas.MondayOf(today);
        var scope = await reqs.TableroAsync(me, ct);
        await using var db = factory.Create();
        var users = await db.UsuariosMapAsync(ct);
        string NameOf(int? id) => id is { } i && users.TryGetValue(i, out var u) ? u.Nombre : "Sin asignar";

        var pend = scope.Where(r => r.Estado != Estado.Ok)
            .OrderBy(r => r.Fecha).ThenBy(r => r.Job, StringComparer.Ordinal).ThenBy(r => r.Estado == Estado.Pendiente ? 0 : 1).ToList();
        var sp = scope.Where(r => r.Tipo == Equipo.Procesamiento).ToList();
        var sc = scope.Where(r => r.Tipo == Equipo.Codificacion).ToList();
        var ss = scope.Where(r => r.Tipo == Equipo.Script).ToList();
        var kpis = new List<Kpi>
        {
            new("Pendientes de OK", pend.Count.ToString(), $"{pend.Count(r => Reglas.Atrasado(r, today))} atrasados"),
            new("En curso", scope.Count(r => r.Estado == Estado.EnCurso).ToString(), "Procesamiento + scripts"),
            new("OK procesamiento", $"{sp.Count(r => r.Estado == Estado.Ok)}/{sp.Count}", $"Semana {Fechas.WeekLabel(cur)}"),
            new("OK codificación", $"{sc.Count(r => r.Estado == Estado.Ok)}/{sc.Count}", $"{Reglas.FmtN(sc.Sum(r => (long)(r.Registros ?? 0)))} registros codificados"),
            new("OK scripts", $"{ss.Count(r => r.Estado == Estado.Ok)}/{ss.Count}", "Activos + cerrados esta semana"),
        };
        return new(scope, pend, kpis, SummaryBuilder.Build(scope, NameOf, today));
    }
}
