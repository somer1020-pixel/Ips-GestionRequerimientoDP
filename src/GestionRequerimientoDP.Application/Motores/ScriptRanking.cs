using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Application.Motores;

public sealed record RankEntry(Usuario Analista, int Activos, int DpHoy, int OkCount, DateTimeOffset? LastOk);

/// <summary>Ranking de disponibilidad del equipo de scripting (§3.2).</summary>
public static class ScriptRanking
{
    /// <summary>
    /// 1) menos scripts activos; 2) menos entregables DP abiertos con fecha ≤ hoy (analistas mixtos);
    /// 3) último OK más antiguo (o ninguno).
    /// </summary>
    public static List<RankEntry> Rank(IEnumerable<Usuario> scriptAnalysts, IReadOnlyCollection<Requerimiento> reqs, DateOnly today) =>
        scriptAnalysts
            .Where(a => a.Activo && a.Rol == Rol.Analista && a.Tiene(Equipo.Script))
            .Select(a =>
            {
                var mine = reqs.Where(r => r.Tipo == Equipo.Script && r.AnalistaId == a.Id).ToList();
                var oks = mine.Where(r => r.OkAt != null).Select(r => r.OkAt!.Value).OrderBy(x => x).ToList();
                var dpHoy = a.Tiene(Equipo.Procesamiento)
                    ? reqs.Count(r => r.Tipo == Equipo.Procesamiento && r.AnalistaId == a.Id && r.Estado != Estado.Ok && r.Fecha <= today)
                    : 0;
                return new RankEntry(a, mine.Count(r => r.Estado != Estado.Ok), dpHoy, oks.Count, oks.Count > 0 ? oks[^1] : null);
            })
            .OrderBy(x => x.Activos)
            .ThenBy(x => x.DpHoy)
            .ThenBy(x => x.LastOk ?? DateTimeOffset.MinValue)
            .ToList();

    public static string Disponibilidad(RankEntry x, int index, int wip) =>
        index == 0 ? "Siguiente" : x.Activos == 0 ? "Libre" : x.Activos >= wip ? "Al límite" : "Disponible";

    public static string DisponibilidadCss(RankEntry x, int index, int wip) =>
        index == 0 ? "be be-success" : x.Activos >= wip ? "be be-danger" : "be be-neutral";

    public static string Motivo(RankEntry top, int wip)
    {
        if (top.Activos == 0) return "Libre: sin scripts activos" + (top.LastOk is { } l ? $" · último OK {Fechas.FmtDT(l)}" : "");
        if (top.Activos >= wip) return $"Todo el equipo está al límite ({top.Activos}/{wip}). Es quien tiene menos carga.";
        return $"Menor carga activa del equipo: {top.Activos} de {wip}" + (top.DpHoy > 0 ? $" · además {top.DpHoy} entregables DP hoy" : "");
    }
}
