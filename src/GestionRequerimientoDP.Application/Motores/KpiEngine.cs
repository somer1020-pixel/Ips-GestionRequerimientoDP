using System.Globalization;
using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Application.Motores;

public enum PeriodoModo { Semana, Mes }

public sealed record Periodo(PeriodoModo Modo, DateOnly Ref, DateOnly Desde, DateOnly Hasta, string Label)
{
    public static Periodo De(PeriodoModo modo, DateOnly refDate)
    {
        if (modo == PeriodoModo.Semana)
        {
            var from = Fechas.MondayOf(refDate);
            return new(modo, from, from, from.AddDays(6), $"Semana {Fechas.WeekLabel(from)}");
        }
        var first = new DateOnly(refDate.Year, refDate.Month, 1);
        return new(modo, first, first, first.AddMonths(1).AddDays(-1), $"{Fechas.MesesLargos[first.Month - 1]} {first.Year}");
    }

    public Periodo Shift(int k) => Modo == PeriodoModo.Semana ? De(Modo, Desde.AddDays(7 * k)) : De(Modo, Desde.AddMonths(k));
}

public sealed record Kpi(string Label, string Value, string Note, string Color = "var(--ink)");
public sealed record PeriodStats(int N, int Ok, int OkPct, int OtPct, int Late);
public sealed record HistBucket(string Label, DateOnly? Week, PeriodStats Stats, double Pct, double OkPct);
public sealed record HistAnalyst(Usuario Analista, int Dp, int Cod, int Sc, decimal Horas, int Registros, PeriodStats Stats);

public sealed record HistoricoView(Periodo Periodo, List<Kpi> Kpis, List<HistBucket> Buckets, List<HistAnalyst> Analistas, List<Requerimiento> Lista, int Total, List<Requerimiento> Todos);

public sealed record StackItem(string Key, string Label, string Color, int N, int Pct, string Dias);
public sealed record UnidadAtraso(string Unidad, int Entregas, int Atrasos, int Pct, int Ops, int Com, int Campo, string Dias);
public sealed record MotivoTop(string Motivo, string RespLabel, string Color, int N, double Pct);
public sealed record GrupoTop(string Nombre, int N, string Dias, string Top);
public sealed record AtrasosView(Periodo Periodo, List<Kpi> Kpis, List<StackItem> Stack, List<UnidadAtraso> Unidades, List<MotivoTop> Motivos,
    List<GrupoTop> Comerciales, List<GrupoTop> Campo, List<Requerimiento> Lista, int Total, List<Requerimiento> Todos);

/// <summary>KPIs de Histórico (§3.3c) y Motivos de atraso (§3.3e).</summary>
public static class KpiEngine
{
    public static PeriodStats Stats(IReadOnlyCollection<Requerimiento> list, DateOnly today)
    {
        var ok = list.Where(r => r.Estado == Estado.Ok).ToList();
        var ot = ok.Count(Reglas.IsOnTime);
        return new(list.Count, ok.Count, list.Count > 0 ? (int)Math.Round(ok.Count * 100.0 / list.Count) : 0,
            ok.Count > 0 ? (int)Math.Round(ot * 100.0 / ok.Count) : 0,
            list.Count(r => (r.Estado == Estado.Ok && !Reglas.IsOnTime(r)) || Reglas.Atrasado(r, today)));
    }

    /// <param name="periodItems">Todos los requerimientos con fecha en el periodo.</param>
    public static HistoricoView Historico(Periodo p, IReadOnlyCollection<Requerimiento> periodItems, int? anFilter, IEnumerable<Usuario> users, DateOnly today)
    {
        var hAll = periodItems.Where(r => r.Fecha >= p.Desde && r.Fecha <= p.Hasta).ToList();
        var items = hAll.Where(r => anFilter is null || r.AnalistaId == anFilter).ToList();
        var hs = Stats(items, today);
        var conHoras = items.Where(r => r.Horas > 0).ToList();
        var kpis = new List<Kpi>
        {
            new("Entregables", hs.N.ToString(), $"{items.Count(r => r.Tipo == Equipo.Procesamiento)} DP · {items.Count(r => r.Tipo == Equipo.Codificacion)} COD · {items.Count(r => r.Tipo == Equipo.Script)} scripts"),
            new("Con OK", hs.OkPct + "%", $"{hs.Ok} de {hs.N}"),
            new("OK a tiempo", hs.Ok > 0 ? hs.OtPct + "%" : "—", "Sobre los que tienen OK"),
            new("Con atraso", hs.Late.ToString(), "OK tardío o abierto vencido"),
            new("Horas registradas", Reglas.FmtH(items.Sum(r => r.Horas ?? 0)),
                conHoras.Count > 0 ? $"Prom. {Reglas.FmtH(conHoras.Sum(r => r.Horas!.Value) / conHoras.Count)} por entregable" : "DP + scripts con OK"),
            new("Registros codificados", Reglas.FmtN(items.Sum(r => (long)(r.Registros ?? 0))),
                $"IA {Reglas.FmtN(items.Where(r => r.Metodo == MetodoCodificacion.IA).Sum(r => (long)(r.Registros ?? 0)))} · Manual {Reglas.FmtN(items.Where(r => r.Metodo == MetodoCodificacion.Manual).Sum(r => (long)(r.Registros ?? 0)))}"),
        };

        var raw = new List<(string Label, DateOnly? W, List<Requerimiento> L)>();
        if (p.Modo == PeriodoModo.Semana)
            for (var i = 0; i < 5; i++) { var d = p.Desde.AddDays(i); raw.Add((Fechas.FmtD(d), null, items.Where(r => r.Fecha == d).ToList())); }
        else
            for (var w = Fechas.MondayOf(p.Desde); w <= p.Hasta; w = w.AddDays(7))
            { var ww = w; raw.Add(($"Sem. {Fechas.WeekLabel(ww)}", ww, items.Where(r => Fechas.MondayOf(r.Fecha) == ww).ToList())); }
        var maxB = Math.Max(1, raw.Count == 0 ? 0 : raw.Max(b => b.L.Count));
        var buckets = raw.Select(b => { var st = Stats(b.L, today); return new HistBucket(b.Label, b.W, st, st.N * 100.0 / maxB, st.N > 0 ? st.Ok * 100.0 / st.N : 0); }).ToList();

        var analistas = users.Where(u => u.Rol != Rol.Comercial).OrderBy(u => u.Rol == Rol.Lider).ThenBy(u => u.Id)
            .Select(u => (u, m: hAll.Where(r => r.AnalistaId == u.Id).ToList()))
            .Where(x => x.m.Count > 0)
            .Select(x => new HistAnalyst(x.u, x.m.Count(r => r.Tipo == Equipo.Procesamiento), x.m.Count(r => r.Tipo == Equipo.Codificacion),
                x.m.Count(r => r.Tipo == Equipo.Script), x.m.Sum(r => r.Horas ?? 0), x.m.Sum(r => r.Registros ?? 0), Stats(x.m, today)))
            .ToList();

        var sorted = items.OrderBy(r => r.Fecha).ThenBy(r => r.Job, StringComparer.Ordinal).ToList();
        return new(p, kpis, buckets, analistas, sorted.Take(100).ToList(), sorted.Count, sorted);
    }

    static string AvgD(IReadOnlyCollection<Requerimiento> l) =>
        l.Count > 0 ? Math.Round(l.Average(r => (double)Reglas.DiasAtraso(r)), 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') : "—";

    static int PctOf(int n, int d) => d > 0 ? (int)Math.Round(n * 100.0 / d) : 0;

    public static AtrasosView Atrasos(Periodo p, IReadOnlyCollection<Requerimiento> periodItems, int? anFilter, Equipo? unidad, DateOnly today)
    {
        var items = periodItems.Where(r => r.Fecha >= p.Desde && r.Fecha <= p.Hasta && (anFilter is null || r.AnalistaId == anFilter) && (unidad is null || r.Tipo == unidad)).ToList();
        var aOk = items.Where(r => r.Estado == Estado.Ok).ToList();
        var aLate = aOk.Where(Reglas.IsLateOk).ToList();
        List<Requerimiento> ByResp(string k) => aLate.Where(r => k == "operaciones" ? r.AtrasoResp == AtrasoResponsable.Operaciones : Reglas.EffResp(r) == k).ToList();
        var aOpen = items.Where(r => Reglas.Atrasado(r, today)).ToList();
        var units = new[] { Equipo.Procesamiento, Equipo.Script, Equipo.Codificacion }.Where(t => unidad is null || unidad == t).ToList();
        var opKeys = units.Select(t => "op_" + Reglas.TipoKey(t)).ToList();

        var kpis = new List<Kpi>
        {
            new("Entregas con atraso", aLate.Count.ToString(), $"{PctOf(aLate.Count, aOk.Count)}% de {aOk.Count} entregas con OK", "var(--danger)"),
            new("Días promedio de atraso", AvgD(aLate), "Desde el día comprometido al OK"),
        };
        foreach (var k in opKeys.Append("comercial").Append("campo"))
        {
            var l = ByResp(k);
            kpis.Add(new(k == "comercial" ? "Causa materiales comerciales" : k == "campo" ? "Causa campo" : "Causa " + Reglas.RespX(k).Label,
                PctOf(l.Count, aLate.Count) + "%", $"{l.Count} entregas · {AvgD(l)} días prom.",
                k == "comercial" ? "var(--warning)" : k == "campo" ? "var(--ink)" : "var(--teal-text)"));
        }
        kpis.Add(new("Vencidos sin cerrar", aOpen.Count.ToString(), "Abiertos con fecha pasada", aOpen.Count > 0 ? "var(--danger)" : "var(--ink)"));

        var respKeys = opKeys.Append("comercial").Append("campo").ToList();
        if (ByResp("sin").Count > 0) respKeys.Add("sin");
        var stack = respKeys.Select(k => { var l = ByResp(k); var x = Reglas.RespX(k); return new StackItem(k, x.Label, x.Color, l.Count, PctOf(l.Count, aLate.Count), AvgD(l)); }).ToList();

        var unidades = units.Select(t =>
        {
            var ok = aOk.Where(r => r.Tipo == t).ToList();
            var lt = ok.Where(Reglas.IsLateOk).ToList();
            return new UnidadAtraso(Reglas.Tipo(t).Label, ok.Count, lt.Count, PctOf(lt.Count, ok.Count),
                lt.Count(r => r.AtrasoResp == AtrasoResponsable.Operaciones), lt.Count(r => r.AtrasoResp == AtrasoResponsable.Comercial),
                lt.Count(r => r.AtrasoResp == AtrasoResponsable.Campo), AvgD(lt));
        }).ToList();

        var mg = aLate.GroupBy(r => (Key: Reglas.EffResp(r), Motivo: string.IsNullOrEmpty(r.AtrasoMotivo) ? "Sin motivo" : r.AtrasoMotivo))
            .Select(g => (g.Key, N: g.Count())).OrderByDescending(x => x.N).ToList();
        var mMax = Math.Max(1, mg.Count > 0 ? mg.Max(x => x.N) : 0);
        var motivos = mg.Take(8).Select(x => { var rx = Reglas.RespX(x.Key.Key); return new MotivoTop(x.Key.Motivo, rx.Label, rx.Color, x.N, x.N * 100.0 / mMax); }).ToList();

        List<GrupoTop> GroupTop(IEnumerable<Requerimiento> list, Func<Requerimiento, string> key, int lim) =>
            list.GroupBy(r => key(r) is { Length: > 0 } k ? k : "—")
                .OrderByDescending(g => g.Count()).Take(lim)
                .Select(g =>
                {
                    var top = g.GroupBy(r => r.AtrasoMotivo).OrderByDescending(x => x.Count()).FirstOrDefault();
                    return new GrupoTop(g.Key, g.Count(), AvgD(g.ToList()), top is null ? "—" : $"{top.Key} ({top.Count()})");
                }).ToList();

        var comerciales = GroupTop(ByResp("comercial"), r => (r.Comercial ?? "").Split('/')[0].Trim(), 8);
        var campo = GroupTop(ByResp("campo"), r => string.IsNullOrEmpty(r.AtrasoCampo) ? "Sin área" : r.AtrasoCampo, Catalogos.CAMPOS.Count + 1);
        var sorted = aLate.OrderByDescending(Reglas.DiasAtraso).ThenByDescending(r => r.Fecha).ToList();
        return new(p, kpis, stack, unidades, motivos, comerciales, campo, sorted.Take(100).ToList(), sorted.Count, sorted);
    }
}
