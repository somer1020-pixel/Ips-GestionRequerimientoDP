using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

public sealed record ExportSheet(string Name, IReadOnlyList<object?[]> Rows, IReadOnlyList<double>? Widths = null, bool HeaderRow = true);
public sealed record ExportFile(string FileName, byte[] Content);

/// <summary>Lectura y escritura de planillas (ClosedXML en Infrastructure).</summary>
public interface IExcelGateway
{
    byte[] Write(IReadOnlyList<ExportSheet> sheets);
    /// <summary>Primera hoja como matriz de celdas (DateTime, double o string).</summary>
    List<object?[]> ReadFirstSheet(Stream xlsx);
    List<object?[]> ReadCsv(Stream csv);
}

public interface IImportService
{
    Task<ImportResult> PreviewAsync(UsuarioActual me, Equipo unidad, IReadOnlyList<object?[]>? matrix, CancellationToken ct = default);
    /// <summary>Importa como pendientes. Devuelve el resultado, la primera semana importada y los correos de asignación.</summary>
    Task<(ImportResult Result, DateOnly? Semana, int Correos)> ImportAsync(UsuarioActual me, Equipo unidad, IReadOnlyList<object?[]>? matrix, CancellationToken ct = default);
}

public interface IExportService
{
    Task<ExportFile> PlanificacionAsync(UsuarioActual me, Equipo unidad, DateOnly week, CancellationToken ct = default);
    Task<ExportFile> MiSemanaAsync(UsuarioActual me, DateOnly week, CancellationToken ct = default);
    Task<ExportFile> HistoricoAsync(UsuarioActual me, Periodo p, int? analista, CancellationToken ct = default);
    Task<ExportFile?> AtrasosAsync(UsuarioActual me, Periodo p, int? analista, Equipo? unidad, CancellationToken ct = default);
    /// <summary>Descarga de solicitudes comerciales. Marca DescargadaAt en las solicitudes incluidas.</summary>
    Task<ExportFile?> SolicitudesAsync(UsuarioActual me, Equipo? unidad, bool soloPorAsignar, CancellationToken ct = default);
    /// <summary>Libro con hojas agenda + resumen (§3.4).</summary>
    byte[] Build(IReadOnlyCollection<Requerimiento> rows, IReadOnlyDictionary<int, Usuario> users, string title);
}

public sealed class ImportExportService(IAppDbContextFactory factory, IClock clock, IChangeNotifier changes, INotificationService notif, IExcelGateway excel)
    : IImportService, IExportService
{
    public async Task<ImportResult> PreviewAsync(UsuarioActual me, Equipo unidad, IReadOnlyList<object?[]>? matrix, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        return await ParseAsync(db, unidad, matrix, ct);
    }

    static async Task<ImportResult> ParseAsync(IAppDbContext db, Equipo unidad, IReadOnlyList<object?[]>? matrix, CancellationToken ct)
    {
        var team = (await db.UsuariosConEquipos().Where(u => u.Rol == Rol.Analista).OrderBy(u => u.Id).ToListAsync(ct)).Where(u => u.Tiene(unidad));
        var existentes = await db.Requerimientos.AsNoTracking().Where(r => r.Tipo == unidad).ToListAsync(ct);
        return ImportParser.Parse(matrix, team, existentes);
    }

    public async Task<(ImportResult Result, DateOnly? Semana, int Correos)> ImportAsync(UsuarioActual me, Equipo unidad, IReadOnlyList<object?[]>? matrix, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var res = await ParseAsync(db, unidad, matrix, ct);
        if (res.Rows.Count == 0) return (res, null, 0);
        var now = clock.Now;
        var nuevos = res.Rows.Select(x => new Requerimiento
        {
            Tipo = unidad, Fecha = x.Fecha, Pais = x.Pais, SL = x.SL, Tecnica = x.Tecnica, Cliente = x.Cliente, Proyecto = x.Proyecto, Job = x.Job,
            Entregable = x.Entregable, Descripcion = x.Descripcion, Comercial = x.Comercial, AnalistaId = x.AnalistaId,
            Estado = Estado.Pendiente, Creado = now, Actualizado = now,
        }).ToList();
        db.Requerimientos.AddRange(nuevos);
        await db.SaveAsync(ct);
        var n = notif.QueueAsignacion(db, nuevos, await db.UsuariosMapAsync(ct));
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
        if (n > 0) changes.Publish(ChangeKind.Notificaciones);
        return (res, res.Semanas.Min, n);
    }

    static readonly string[] Head = ["DIA", "PAIS", "BU", "TECNICA", "CLIENTE", "PROYECTO", "JOB", "ENTREGABLE", "DESCRIPCION", "COMERCIAL", "ANALISTA", "TIPO", "ESTADO",
        "MATERIALES RECIBIDOS", "FECHA OK", "HORAS", "REGISTROS CODIFICADOS", "METODO", "SOLICITUD", "SOLICITADO POR", "FECHA SOLICITUD", "DIAS ATRASO",
        "RESPONSABLE ATRASO", "AREA CAMPO", "MOTIVO ATRASO", "DETALLE ATRASO"];
    static readonly double[] Widths = [11, 5, 22, 18, 18, 34, 17, 22, 30, 30, 20, 13, 10, 14, 16, 8, 12, 9, 10, 22, 16, 10, 24, 10, 30, 30];

    public byte[] Build(IReadOnlyCollection<Requerimiento> rows, IReadOnlyDictionary<int, Usuario> users, string title)
    {
        string Name(int? id) => id is { } i && users.TryGetValue(i, out var u) ? u.Nombre : "Sin asignar";
        var data = rows.OrderBy(r => r.Fecha).ThenBy(r => Name(r.AnalistaId), StringComparer.CurrentCulture).ThenBy(r => r.Job, StringComparer.Ordinal)
            .Select(r => new object?[]
            {
                r.Fecha.ToDateTime(TimeOnly.MinValue), r.Pais, r.SL, r.Tecnica, r.Cliente, r.Proyecto, r.Job, r.Entregable, r.Descripcion, r.Comercial,
                r.AnalistaId is null ? "" : Name(r.AnalistaId), Reglas.Tipo(r.Tipo).Label, Reglas.EstadoLabel(r.Estado),
                r.MaterialesAt is { } m ? Fechas.Iso(m) : "", Fechas.IsoDT(r.OkAt), r.Horas, r.Registros, r.Metodo?.ToString() ?? "",
                r.Solicitud?.Codigo ?? "", r.Solicitud is { } s ? Name(s.SolicitanteId) : "", r.Solicitud is { } s2 ? Fechas.IsoDT(s2.CreadaAt) : "",
                Reglas.IsLateOk(r) ? Reglas.DiasAtraso(r) : null,
                r.AtrasoResp != null ? Reglas.RespLabel(r) : Reglas.IsLateOk(r) ? "Sin clasificar" : "",
                r.AtrasoCampo, r.AtrasoMotivo, r.AtrasoDetalle,
            }).ToList();

        var resumen = new List<object?[]> { new object?[] { title }, Array.Empty<object?>(),
            new object?[] { "ANALISTA", "ENTREGABLES DP", "SCRIPTS", "CODIFICACION", "ABIERTOS", "CON OK", "HORAS", "REGISTROS CODIFICADOS" } };
        resumen.AddRange(rows.GroupBy(r => r.AnalistaId is null ? "Sin asignar" : Name(r.AnalistaId)).Select(g => new object?[]
        {
            g.Key, g.Count(r => r.Tipo == Equipo.Procesamiento), g.Count(r => r.Tipo == Equipo.Script), g.Count(r => r.Tipo == Equipo.Codificacion),
            g.Count(r => r.Estado != Estado.Ok), g.Count(r => r.Estado == Estado.Ok), g.Sum(r => r.Horas ?? 0), g.Sum(r => r.Registros ?? 0),
        }));
        return excel.Write([new ExportSheet("agenda", [Head.Cast<object?>().ToArray(), .. data], Widths), new ExportSheet("resumen", resumen, null, false)]);
    }

    async Task<(List<Requerimiento>, Dictionary<int, Usuario>)> Load(IAppDbContext db, IQueryable<Requerimiento> q, CancellationToken ct) =>
        (await q.AsNoTracking().Include(r => r.Solicitud).ToListAsync(ct), await db.UsuariosMapAsync(ct));

    public async Task<ExportFile> PlanificacionAsync(UsuarioActual me, Equipo unidad, DateOnly week, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var end = week.AddDays(6);
        var (rows, users) = await Load(db, db.Requerimientos.Where(r => r.Fecha >= week && r.Fecha <= end && (r.Tipo == unidad || r.Tipo == Equipo.Script)), ct);
        return new($"Planificacion_{Fechas.Iso(week)}.xlsx", Build(rows, users, $"Planificación semana {Fechas.WeekLabel(week)}"));
    }

    public async Task<ExportFile> MiSemanaAsync(UsuarioActual me, DateOnly week, CancellationToken ct = default)
    {
        if (!me.EsAnalista) throw new PermisoException();
        var cur = Fechas.MondayOf(clock.Today);
        var end = week.AddDays(6);
        await using var db = factory.Create();
        var (rows, users) = await Load(db, db.Requerimientos.Where(r => r.AnalistaId == me.Id
            && ((r.Fecha >= week && r.Fecha <= end) || (week == cur && r.Estado != Estado.Ok && r.Fecha < cur))), ct);
        var first = me.Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "analista";
        return new($"Mi_semana_{first}_{Fechas.Iso(week)}.xlsx", Build(rows, users, $"{me.Nombre} · semana {Fechas.WeekLabel(week)}"));
    }

    public async Task<ExportFile> HistoricoAsync(UsuarioActual me, Periodo p, int? analista, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var (rows, users) = await Load(db, db.Requerimientos.Where(r => r.Fecha >= p.Desde && r.Fecha <= p.Hasta && (analista == null || r.AnalistaId == analista)), ct);
        var title = $"Histórico {p.Label}{(analista is { } a && users.TryGetValue(a, out var u) ? " · " + u.Nombre : "")}";
        return new($"Historico_{Fechas.Iso(p.Desde)}_{Fechas.Iso(p.Hasta)}.xlsx", Build(rows, users, title));
    }

    public async Task<ExportFile?> AtrasosAsync(UsuarioActual me, Periodo p, int? analista, Equipo? unidad, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var (rows, users) = await Load(db, db.Requerimientos.Where(r => r.Fecha >= p.Desde && r.Fecha <= p.Hasta), ct);
        var view = KpiEngine.Atrasos(p, rows, analista, unidad, clock.Today);
        if (view.Total == 0) return null;
        return new($"Atrasos_{Fechas.Iso(p.Desde)}_{Fechas.Iso(p.Hasta)}.xlsx", Build(view.Todos, users, $"Motivos de atraso · {p.Label}"));
    }

    public async Task<ExportFile?> SolicitudesAsync(UsuarioActual me, Equipo? unidad, bool soloPorAsignar, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var sols = await db.Solicitudes.Include(s => s.Requerimientos).ToListAsync(ct);
        var sel = sols.Where(s => s.Requerimientos.Count > 0 && (unidad is null || s.Requerimientos[0].Tipo == unidad)
                                  && (!soloPorAsignar || s.Requerimientos.Any(r => r.AnalistaId == null))).ToList();
        var rows = sel.SelectMany(s => s.Requerimientos).Where(r => !soloPorAsignar || r.AnalistaId == null).ToList();
        if (rows.Count == 0) return null;
        var users = await db.UsuariosMapAsync(ct);
        var today = clock.Today;
        var bytes = Build(rows, users, $"Solicitudes comerciales {(soloPorAsignar ? "por asignar" : "")} · {Fechas.FmtD(today)}");
        var now = clock.Now;
        foreach (var s in sel.Where(s => s.Requerimientos.Any(r => rows.Contains(r)) && s.DescargadaAt == null)) s.DescargadaAt = now;
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Solicitudes);
        return new($"Solicitudes_{(unidad is null ? "todas" : Reglas.TipoKey(unidad.Value))}_{Fechas.Iso(today)}.xlsx", bytes);
    }
}
