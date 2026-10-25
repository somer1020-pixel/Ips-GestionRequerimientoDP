using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

public sealed record ConsultaFila(string Entregable, string Descripcion, string TipoLabel, string Dia, string Asignado, string Estado, string EstadoCss);
public sealed record ConsultaJob(string Job, string Cliente, string Proyecto, string Comercial, string Status, string StatusCss, string NextLine, List<ConsultaFila> Filas);
public sealed record ConsultaResult(List<ConsultaJob> Jobs, int TotalJobs, string Hint);

/// <summary>Consulta pública de solo lectura (§3.5). Nunca expone notas ni comentarios de OK.</summary>
public interface IConsultaService
{
    Task<ConsultaResult> BuscarAsync(string q, CancellationToken ct = default);
}

public sealed class ConsultaService(IAppDbContextFactory factory, IClock clock) : IConsultaService
{
    public async Task<ConsultaResult> BuscarAsync(string q, CancellationToken ct = default)
    {
        var n = Reglas.Norm(q);
        if (n.Length < 3) return new([], 0, "Escribe al menos 3 caracteres.");
        await using var db = factory.Create();
        // Coincidencia sin acentos ni mayúsculas, independiente de la intercalación de la BD.
        var keys = await db.Requerimientos.AsNoTracking().Select(r => new { r.Job, r.Proyecto, r.Cliente }).Distinct().ToListAsync(ct);
        var jobs = keys.Where(k => Reglas.Norm($"{k.Job} {k.Proyecto} {k.Cliente}").Contains(n)).Select(k => k.Job).Distinct().ToList();
        var total = jobs.Count;
        if (total == 0) return new([], 0, "No encontramos requerimientos con ese criterio. Revisa el número de Job o consulta al líder de Operaciones.");

        var hits = await db.Requerimientos.AsNoTracking().Where(r => jobs.Contains(r.Job)).ToListAsync(ct);
        hits = hits.Where(r => Reglas.Norm($"{r.Job} {r.Proyecto} {r.Cliente}").Contains(n)).ToList();
        var names = await db.Usuarios.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Nombre, ct);
        var today = clock.Today;

        var result = hits.GroupBy(r => r.Job).Take(20).Select(g =>
        {
            var l = g.OrderBy(r => r.Fecha).ToList();
            var f = l[0];
            var open = l.Where(r => r.Estado != Estado.Ok).ToList();
            var status = open.Count == 0 ? "Entregado" : l.Any(r => r.AnalistaId == null) ? "Por asignar" : open.Count < l.Count ? "Entregado parcial"
                : l.Any(r => r.Estado == Estado.EnCurso) ? "En proceso" : "Asignado";
            var css = status == "Entregado" ? "be be-success" : status == "Por asignar" ? "be be-warning" : "be be-neutral";
            var lastOk = l.Where(r => r.OkAt != null).Select(r => r.OkAt!.Value).DefaultIfEmpty().Max();
            var next = open.Count > 0 ? $"Próxima entrega: {Fechas.FmtD(open[0].Fecha)}" : $"Completado {Fechas.FmtD(lastOk == default ? null : Fechas.LocalDate(lastOk))}";
            return new ConsultaJob(f.Job, f.Cliente, f.Proyecto, string.IsNullOrEmpty(f.Comercial) ? "—" : f.Comercial, status, css, next,
                l.Select(r =>
                {
                    var (est, ecss) = Reglas.EstadoPublico(r);
                    return new ConsultaFila(r.Entregable, string.IsNullOrEmpty(r.Descripcion) ? "—" : r.Descripcion, Reglas.Tipo(r.Tipo).Label,
                        Fechas.FmtD(r.Fecha) + (Reglas.Atrasado(r, today) ? " · fuera de plazo" : ""),
                        r.AnalistaId is { } a && names.TryGetValue(a, out var nm) ? nm : "Por asignar", est, ecss);
                }).ToList());
        }).ToList();
        var groups = hits.Select(r => r.Job).Distinct().Count();
        return new(result, groups, $"{groups} job(s) encontrados{(groups > 20 ? " · mostrando 20" : "")}.");
    }
}
