using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Application.Motores;

/// <summary>Texto plano "listo para enviar" del Resumen (§5).</summary>
public static class SummaryBuilder
{
    public static string Build(IReadOnlyCollection<Requerimiento> reqs, Func<int?, string> nameOf, DateOnly today)
    {
        var cur = Fechas.MondayOf(today);
        var cod = reqs.Where(r => r.Tipo == Equipo.Codificacion && Fechas.MondayOf(r.Fecha) == cur).ToList();
        var proc = reqs.Where(r => r.Tipo == Equipo.Procesamiento && Fechas.MondayOf(r.Fecha) == cur).ToList();
        var scr = reqs.Where(r => r.Tipo == Equipo.Script && Reglas.EnSemanaActual(r, today)).ToList();

        string Line(Requerimiento r) =>
            $"  - {Fechas.FmtD(r.Fecha)}{(r.Fecha < today ? " (ATRASADO)" : "")} · {r.Job} · {r.Proyecto} — {(string.IsNullOrEmpty(r.Descripcion) ? r.Entregable : r.Descripcion)} — {nameOf(r.AnalistaId)} ({Reglas.EstadoLabel(r.Estado)})";
        List<Requerimiento> Pend(IEnumerable<Requerimiento> l) => l.Where(r => r.Estado != Estado.Ok).OrderBy(r => r.Fecha).ToList();
        IEnumerable<string> Lines(List<Requerimiento> l) => l.Count > 0 ? l.Select(Line) : ["  - Ninguno"];

        var pc = Pend(cod); var pp = Pend(proc); var ps = Pend(scr);
        var lines = new List<string>
        {
            $"Resumen de carga — {Fechas.FmtD(today)}", "",
            $"PROCESAMIENTO · semana {Fechas.WeekLabel(cur)}", $"• Entregables con OK: {proc.Count - pp.Count} de {proc.Count}", $"• Pendientes de OK ({pp.Count}):",
        };
        lines.AddRange(Lines(pp));
        lines.AddRange(["", $"CODIFICACIÓN · semana {Fechas.WeekLabel(cur)}",
            $"• Entregables con OK: {cod.Count - pc.Count} de {cod.Count} · {Reglas.FmtN(cod.Sum(r => (long)(r.Registros ?? 0)))} registros codificados",
            $"• Pendientes de OK ({pc.Count}):"]);
        lines.AddRange(Lines(pc));
        lines.AddRange(["", "SCRIPTING", $"• Activos: {ps.Count} · Con OK: {scr.Count - ps.Count}", $"• Pendientes de OK ({ps.Count}):"]);
        lines.AddRange(Lines(ps));
        lines.AddRange(["", $"Recordatorio: confirmar con \"Dar OK\" en {AppInfo.Nombre} al terminar cada entregable."]);
        return string.Join('\n', lines);
    }
}
