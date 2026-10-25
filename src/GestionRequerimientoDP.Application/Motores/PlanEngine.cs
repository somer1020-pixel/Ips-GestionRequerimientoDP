using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Application.Motores;

public sealed record CapCard(Usuario Analista, int N, int NScripts, int Cap, string Label, string Css, string Bar, double Pct, int Pend, int Curso, int Ok, string Mix);
public sealed record AgendaCell(DateOnly Day, int N, int NScripts, string Label, string Bg);
/// <summary>AnalistaId null = fila "Sin asignar".</summary>
public sealed record AgendaRow(int? AnalistaId, string Nombre, List<AgendaCell> Cells, int Total);
public sealed record PlanStats(int Total, int Jobs, double Avg, int Spread, int Unassigned);

public sealed record PlanView(Equipo Unidad, DateOnly Week, int Cap, List<Usuario> Team, List<Requerimiento> WeekItems, List<Requerimiento> WeekScripts,
    List<CapCard> Cards, PlanStats Stats, List<DateOnly> Days, List<AgendaRow> Agenda, List<int> TotalPerDay)
{
    public IEnumerable<Requerimiento> WeekAll => WeekItems.Concat(WeekScripts);
}

/// <summary>Tarjetas de capacidad, indicadores y agenda de la planificación semanal (§3.1).</summary>
public static class PlanEngine
{
    public static (string Label, string Css, string Bar) CapacityStatus(int n, double mean, int cap)
    {
        if (n > cap) return ("Sobre capacidad", "be be-danger", "var(--danger)");
        if (n - mean >= 2) return ("Sobrecargado", "be be-warning", "var(--warning)");
        if (mean - n >= 2) return ("Subutilizado", "be be-info", "var(--color-accent-300)");
        return ("Equilibrado", "be be-success", "var(--teal)");
    }

    public static PlanView Build(Equipo unidad, DateOnly week, IEnumerable<Usuario> users, IEnumerable<Requerimiento> reqs, int cap)
    {
        var team = users.Where(u => u.Rol == Rol.Analista && u.Activo && u.Tiene(unidad)).OrderBy(u => u.Id).ToList();
        var all = reqs.OrderBy(r => r.Id).ToList();
        var items = all.Where(r => r.Tipo == unidad && Fechas.MondayOf(r.Fecha) == week).ToList();
        var scripts = all.Where(r => r.Tipo == Equipo.Script && Fechas.MondayOf(r.Fecha) == week && team.Any(a => a.Id == r.AnalistaId)).ToList();
        var weekAll = items.Concat(scripts).ToList();
        var mean = team.Count > 0 ? (double)items.Count / team.Count : 0;
        var sh = Reglas.Tipo(unidad).Short;

        var cards = team.Select(a =>
        {
            var m = weekAll.Where(r => r.AnalistaId == a.Id).ToList();
            int n = m.Count, nS = m.Count(r => r.Tipo == Equipo.Script);
            var (label, css, bar) = CapacityStatus(n, mean, cap);
            return new CapCard(a, n, nS, cap, label, css, bar, Math.Min(100, n * 100.0 / cap),
                m.Count(r => r.Estado == Estado.Pendiente), m.Count(r => r.Estado == Estado.EnCurso), m.Count(r => r.Estado == Estado.Ok),
                nS > 0 ? $"{n - nS} {sh} + {nS} scripts" : $"{n} entregables {sh}");
        }).ToList();

        var loads = team.Select(a => weekAll.Count(r => r.AnalistaId == a.Id)).ToList();
        var stats = new PlanStats(items.Count, items.Select(r => r.Job).Distinct().Count(), mean,
            loads.Count > 0 ? loads.Max() - loads.Min() : 0, items.Count(r => r.AnalistaId == null));

        var days = Enumerable.Range(0, 5).Select(i => week.AddDays(i)).ToList();
        var rowsFor = team.Select(a => ((int?)a.Id, a.Nombre)).ToList();
        if (items.Any(r => r.AnalistaId == null)) rowsFor.Add((null, "Sin asignar"));
        var agenda = rowsFor.Select(a =>
        {
            var m = weekAll.Where(r => r.AnalistaId == a.Item1).ToList();
            var cells = days.Select(d =>
            {
                var md = m.Where(r => r.Fecha == d).ToList();
                int n = md.Count, nS = md.Count(r => r.Tipo == Equipo.Script);
                return new AgendaCell(d, n, nS, n == 0 ? "·" : nS > 0 ? $"{n - nS} · {nS}S" : n.ToString(),
                    n >= 5 ? "var(--color-accent-300)" : n >= 3 ? "var(--color-accent-200)" : n > 0 ? "var(--color-accent-100)" : "transparent");
            }).ToList();
            return new AgendaRow(a.Item1, a.Item2, cells, m.Count);
        }).ToList();

        return new(unidad, week, cap, team, items, scripts, cards, stats, days, agenda, days.Select(d => weekAll.Count(r => r.Fecha == d)).ToList());
    }
}
