using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

public sealed class SolicitudInput
{
    public Equipo Tipo { get; set; } = Equipo.Procesamiento;
    public string Cliente { get; set; } = "";
    public string Proyecto { get; set; } = "";
    public string Job { get; set; } = "";
    public string SL { get; set; } = Catalogos.SLS[0];
    public string Tecnica { get; set; } = "";
    public DateOnly? Fecha { get; set; }
    public List<string> Entregables { get; set; } = [];
    public string Descripcion { get; set; } = "";
    public string Nota { get; set; } = "";
}

/// <summary>Tarjeta de solicitud (vista del líder y del comercial).</summary>
public sealed record SolicitudCard(Solicitud Solicitud, List<Requerimiento> Items, string SolicitanteNombre, string Status, string StatusCss, int SinAsignar)
{
    public Requerimiento First => Items[0];
    public Equipo Tipo => First.Tipo;
    public bool Abierta => SinAsignar > 0;
}

public interface ISolicitudService
{
    Task<Solicitud> EnviarAsync(UsuarioActual me, SolicitudInput input, CancellationToken ct = default);
    Task<List<SolicitudCard>> TodasAsync(UsuarioActual me, CancellationToken ct = default);
    Task<List<SolicitudCard>> MiasAsync(UsuarioActual me, CancellationToken ct = default);
    /// <summary>Solicitudes sin asignar de los equipos a cargo del líder (banner) y total sin asignar (contador).</summary>
    Task<(int MisEquipos, int Total)> PendientesAsync(UsuarioActual me, CancellationToken ct = default);
    Task<string> DestinoAsync(Equipo tipo, CancellationToken ct = default);
}

public sealed class SolicitudService(IAppDbContextFactory factory, IClock clock, IChangeNotifier changes) : ISolicitudService
{
    public static (string Status, string Css) Estado(IReadOnlyList<Requerimiento> g, bool descargada)
    {
        var un = g.Count(r => r.AnalistaId == null);
        if (g.All(r => r.Estado == Domain.Estado.Ok)) return ("Entregada", "be be-success");
        if (un == g.Count) return descargada ? ("Descargada · por asignar", "be be-warning") : ("Nueva · por asignar", "be be-danger");
        if (un > 0) return ("Asignación parcial", "be be-warning");
        return g.Any(r => r.Estado != Domain.Estado.Pendiente) ? ("En proceso", "be be-info") : ("Asignada", "be be-info");
    }

    static SolicitudCard Card(Solicitud s, IReadOnlyDictionary<int, Usuario> users)
    {
        var items = s.Requerimientos.OrderBy(r => r.Fecha).ThenBy(r => r.Id).ToList();
        var (st, css) = Estado(items, s.DescargadaAt != null);
        return new(s, items, users.TryGetValue(s.SolicitanteId, out var u) ? u.Nombre : "—", st, css, items.Count(r => r.AnalistaId == null));
    }

    public async Task<Solicitud> EnviarAsync(UsuarioActual me, SolicitudInput d, CancellationToken ct = default)
    {
        if (!me.EsComercial) throw new PermisoException("Solo las áreas comerciales ingresan solicitudes.");
        var today = clock.Today;
        if (string.IsNullOrWhiteSpace(d.Cliente) || string.IsNullOrWhiteSpace(d.Proyecto) || string.IsNullOrWhiteSpace(d.Job))
            throw new ReglaException("Cliente, Job y Proyecto son obligatorios.");
        if (d.Fecha is null || d.Fecha < today) throw new ReglaException("Indica una fecha requerida igual o posterior a hoy.");
        if (string.IsNullOrWhiteSpace(d.Tecnica)) throw new ReglaException("Selecciona la técnica.");
        if (string.IsNullOrWhiteSpace(d.SL)) throw new ReglaException("Selecciona la SL.");
        var permitidos = Catalogos.EntregablesDe(d.Tipo);
        var ents = d.Entregables.Where(permitidos.Contains).Distinct().ToList();
        if (ents.Count == 0) throw new ReglaException(d.Tipo == Equipo.Script ? "Selecciona al menos un tipo de requerimiento." : "Selecciona al menos un entregable.");

        await using var db = factory.Create();
        var users = await db.UsuariosMapAsync(ct);
        var codigos = await db.Solicitudes.Select(s => s.Codigo).ToListAsync(ct);
        var num = codigos.Select(c => int.TryParse(new string(c.Where(char.IsDigit).ToArray()), out var n) ? n : 0).Append(1044).Max() + 1;
        var now = clock.Now;
        var sol = new Solicitud { Codigo = $"S-{num}", SolicitanteId = me.Id, CreadaAt = now, Nota = d.Nota.Trim() };
        foreach (var e in ents)
            sol.Requerimientos.Add(new Requerimiento
            {
                Tipo = d.Tipo, Fecha = d.Fecha.Value, Pais = "CL", SL = d.SL, Tecnica = d.Tecnica, Cliente = d.Cliente.Trim(), Proyecto = d.Proyecto.Trim(),
                Job = d.Job.Trim(), Entregable = e, Descripcion = d.Descripcion.Trim(), Comercial = me.Nombre, AnalistaId = null,
                Estado = Domain.Estado.Pendiente, Creado = now, Actualizado = now, Nota = d.Nota.Trim(),
            });
        db.Solicitudes.Add(sol);
        await db.SaveAsync(ct);
        var leaders = Reglas.TeamLeadersOf(users.Values.OrderBy(u => u.Id), d.Tipo);
        db.Notificaciones.Add(MailBuilder.Solicitud(leaders, users.GetValueOrDefault(me.Id), sol.Codigo, sol.Requerimientos, sol.Nota).ToEntity(now));
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Solicitudes);
        changes.Publish(ChangeKind.Requerimientos);
        changes.Publish(ChangeKind.Notificaciones);
        return sol;
    }

    async Task<List<SolicitudCard>> Cards(IQueryable<Solicitud> q, IAppDbContext db, CancellationToken ct)
    {
        var users = await db.UsuariosMapAsync(ct);
        var sols = await q.AsNoTracking().Include(s => s.Requerimientos).ToListAsync(ct);
        return sols.Where(s => s.Requerimientos.Count > 0).Select(s => Card(s, users)).ToList();
    }

    public async Task<List<SolicitudCard>> TodasAsync(UsuarioActual me, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        return (await Cards(db.Solicitudes, db, ct))
            .OrderByDescending(c => c.Abierta).ThenBy(c => c.Items.Min(r => r.Fecha)).ToList();
    }

    public async Task<List<SolicitudCard>> MiasAsync(UsuarioActual me, CancellationToken ct = default)
    {
        if (!me.EsComercial) throw new PermisoException();
        await using var db = factory.Create();
        return (await Cards(db.Solicitudes.Where(s => s.SolicitanteId == me.Id), db, ct)).OrderByDescending(c => c.Solicitud.CreadaAt).ToList();
    }

    public async Task<(int MisEquipos, int Total)> PendientesAsync(UsuarioActual me, CancellationToken ct = default)
    {
        if (!me.EsLider) return (0, 0);
        await using var db = factory.Create();
        var tipos = await db.Requerimientos.Where(r => r.SolicitudId != null && r.AnalistaId == null).Select(r => new { r.SolicitudId, r.Tipo }).Distinct().ToListAsync(ct);
        var total = tipos.Select(t => t.SolicitudId).Distinct().Count();
        var mine = tipos.Where(t => me.Equipos.Count == 0 || me.Equipos.Contains(t.Tipo)).Select(t => t.SolicitudId).Distinct().Count();
        return (mine, total);
    }

    public async Task<string> DestinoAsync(Equipo tipo, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        var users = await db.UsuariosConEquipos().OrderBy(u => u.Id).ToListAsync(ct);
        return string.Join(", ", Reglas.TeamLeadersOf(users, tipo).Select(u => u.Nombre));
    }
}
