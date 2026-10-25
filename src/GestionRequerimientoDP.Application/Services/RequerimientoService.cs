using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

public sealed class NuevoRequerimiento
{
    public Equipo Tipo { get; set; } = Equipo.Procesamiento;
    public string Cliente { get; set; } = "";
    public string Proyecto { get; set; } = "";
    public string Job { get; set; } = "";
    public string SL { get; set; } = Catalogos.SLS[0];
    public string Tecnica { get; set; } = "";
    public string Entregable { get; set; } = Catalogos.ENT_SOL[0];
    public DateOnly? Fecha { get; set; }
    public string Descripcion { get; set; } = "";
    public string Comercial { get; set; } = "";
    /// <summary>Null = sin asignar (proc/cod) o automático (script).</summary>
    public int? AnalistaId { get; set; }
}

public interface IRequerimientoService
{
    Task<List<Requerimiento>> TableroAsync(UsuarioActual me, CancellationToken ct = default);
    Task<List<Requerimiento>> PropiosAsync(UsuarioActual me, CancellationToken ct = default);
    Task<List<Requerimiento>> PeriodoAsync(UsuarioActual me, DateOnly desde, DateOnly hasta, CancellationToken ct = default);
    Task<List<Requerimiento>> SemanaAsync(UsuarioActual me, DateOnly week, CancellationToken ct = default);
    Task<Requerimiento?> GetAsync(UsuarioActual me, int id, CancellationToken ct = default);
    Task<int> AtrasadosAsync(UsuarioActual me, CancellationToken ct = default);

    Task<(Requerimiento Req, bool Correo)> CrearAsync(UsuarioActual me, NuevoRequerimiento d, CancellationToken ct = default);
    Task<bool> AsignarAsync(UsuarioActual me, int id, int? analistaId, CancellationToken ct = default);
    Task SetFechaAsync(UsuarioActual me, int id, DateOnly fecha, CancellationToken ct = default);
    Task SetNotaAsync(UsuarioActual me, int id, string nota, CancellationToken ct = default);
    Task SetMaterialesAsync(UsuarioActual me, int id, DateOnly? fecha, CancellationToken ct = default);
    Task SetAtrasoAsync(UsuarioActual me, int id, AtrasoResponsable? resp, string motivo, string campo, CancellationToken ct = default);
    Task EliminarAsync(UsuarioActual me, int id, CancellationToken ct = default);
}

public sealed class RequerimientoService(IAppDbContextFactory factory, IClock clock, IChangeNotifier changes, INotificationService notif) : IRequerimientoService
{
    public async Task<List<Requerimiento>> TableroAsync(UsuarioActual me, CancellationToken ct = default)
    {
        var cur = Fechas.MondayOf(clock.Today);
        var end = cur.AddDays(6);
        var curStart = Fechas.FromLocal(cur, 0, 0);
        await using var db = factory.Create();
        return await db.Visibles(me).AsNoTracking()
            .Where(r => (r.Tipo != Equipo.Script && r.Fecha >= cur && r.Fecha <= end)
                        || (r.Tipo == Equipo.Script && (r.Estado != Estado.Ok || (r.Fecha >= cur && r.Fecha <= end) || r.OkAt >= curStart)))
            .OrderBy(r => r.Fecha).ThenBy(r => r.Job).ToListAsync(ct);
    }

    public async Task<List<Requerimiento>> PropiosAsync(UsuarioActual me, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Requerimientos.AsNoTracking().Where(r => r.AnalistaId == me.Id).ToListAsync(ct);
    }

    public async Task<List<Requerimiento>> PeriodoAsync(UsuarioActual me, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Visibles(me).AsNoTracking().Where(r => r.Fecha >= desde && r.Fecha <= hasta).ToListAsync(ct);
    }

    /// <summary>Requerimientos de la semana (todas las unidades). La planificación filtra por unidad.</summary>
    public Task<List<Requerimiento>> SemanaAsync(UsuarioActual me, DateOnly week, CancellationToken ct = default) =>
        PeriodoAsync(me, week, week.AddDays(6), ct);

    public async Task<Requerimiento?> GetAsync(UsuarioActual me, int id, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Visibles(me).AsNoTracking().Include(r => r.Solicitud).FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    /// <summary>Atrasados del alcance "semana actual" (contador rojo del menú).</summary>
    public async Task<int> AtrasadosAsync(UsuarioActual me, CancellationToken ct = default)
    {
        if (me.EsComercial) return 0;
        var today = clock.Today;
        return (await TableroAsync(me, ct)).Count(r => Reglas.Atrasado(r, today));
    }

    public async Task<(Requerimiento Req, bool Correo)> CrearAsync(UsuarioActual me, NuevoRequerimiento d, CancellationToken ct = default)
    {
        Guard.Lider(me);
        if (string.IsNullOrWhiteSpace(d.Cliente) || string.IsNullOrWhiteSpace(d.Proyecto) || string.IsNullOrWhiteSpace(d.Job))
            throw new ReglaException("Cliente, Proyecto y Job son obligatorios.");
        if (d.Fecha is null) throw new ReglaException("Indica la fecha.");
        if (string.IsNullOrWhiteSpace(d.Tecnica)) throw new ReglaException("Selecciona la técnica.");

        await using var db = factory.Create();
        var users = await db.UsuariosMapAsync(ct);
        var analista = d.AnalistaId;
        if (analista is { } a && !(users.TryGetValue(a, out var ua) && ua.Activo && ua.Rol == Rol.Analista && ua.Tiene(d.Tipo)))
            throw new ReglaException("El analista seleccionado no pertenece al equipo o está inactivo.");
        if (d.Tipo == Equipo.Script && analista is null)
        {
            var reqs = await db.Requerimientos.AsNoTracking().Where(r => r.Tipo == Equipo.Script || (r.Tipo == Equipo.Procesamiento && r.Estado != Estado.Ok)).ToListAsync(ct);
            analista = ScriptRanking.Rank(users.Values.OrderBy(u => u.Id), reqs, clock.Today).FirstOrDefault()?.Analista.Id;
        }
        var now = clock.Now;
        var r = new Requerimiento
        {
            Tipo = d.Tipo, Fecha = d.Fecha.Value, Pais = "CL", SL = d.SL, Tecnica = d.Tecnica, Cliente = d.Cliente.Trim(), Proyecto = d.Proyecto.Trim(),
            Job = d.Job.Trim(), Entregable = d.Entregable, Descripcion = d.Descripcion.Trim(), Comercial = d.Comercial.Trim(), AnalistaId = analista,
            Estado = Estado.Pendiente, Creado = now, Actualizado = now,
        };
        db.Requerimientos.Add(r);
        await db.SaveAsync(ct);
        var sent = analista != null && notif.QueueAsignacion(db, [r], users) > 0;
        if (analista != null) db.Log(r, OkAccion.Asignacion, me.Id, now, new { analista });
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
        if (sent) changes.Publish(ChangeKind.Notificaciones);
        return (r, sent);
    }

    public async Task<bool> AsignarAsync(UsuarioActual me, int id, int? analistaId, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var r = await db.Requerimientos.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaException("El requerimiento ya no existe.");
        if (r.AnalistaId == analistaId) return false;
        var users = await db.UsuariosMapAsync(ct);
        if (analistaId is { } a && !(users.TryGetValue(a, out var u) && u.Activo && u.Rol == Rol.Analista && u.Tiene(r.Tipo)))
            throw new ReglaException("El analista seleccionado no pertenece al equipo o está inactivo.");
        var now = clock.Now;
        r.AnalistaId = analistaId;
        r.Actualizado = now;
        db.Log(r, OkAccion.Asignacion, me.Id, now, new { analista = analistaId });
        var sent = analistaId != null && notif.QueueAsignacion(db, [r], users) > 0;
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
        if (r.SolicitudId != null) changes.Publish(ChangeKind.Solicitudes);
        if (sent) changes.Publish(ChangeKind.Notificaciones);
        return sent;
    }

    async Task Edit(UsuarioActual me, int id, bool soloLider, Action<Requerimiento> apply, CancellationToken ct)
    {
        if (soloLider) Guard.Lider(me);
        await using var db = factory.Create();
        var r = await db.Requerimientos.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaException("El requerimiento ya no existe.");
        Guard.Edit(me, r);
        apply(r);
        r.Actualizado = clock.Now;
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
    }

    public Task SetFechaAsync(UsuarioActual me, int id, DateOnly fecha, CancellationToken ct = default) => Edit(me, id, true, r => r.Fecha = fecha, ct);

    public Task SetNotaAsync(UsuarioActual me, int id, string nota, CancellationToken ct = default) => Edit(me, id, false, r => r.Nota = nota ?? "", ct);

    public Task SetMaterialesAsync(UsuarioActual me, int id, DateOnly? fecha, CancellationToken ct = default)
    {
        if (fecha is { } f && f > clock.Today) throw new ReglaException("La fecha de materiales no puede ser posterior a hoy.");
        return Edit(me, id, false, r => r.MaterialesAt = fecha, ct);
    }

    /// <summary>Corrección posterior del motivo de atraso (líder o analista asignado).</summary>
    public Task SetAtrasoAsync(UsuarioActual me, int id, AtrasoResponsable? resp, string motivo, string campo, CancellationToken ct = default) =>
        Edit(me, id, false, r =>
        {
            if (!Reglas.IsLateOk(r)) throw new ReglaException("Solo las entregas atrasadas tienen motivo de atraso.");
            if (r.AtrasoResp != resp)
            {
                r.AtrasoResp = resp;
                r.AtrasoMotivo = "";
                r.AtrasoCampo = resp == AtrasoResponsable.Campo ? (r.AtrasoCampo is { Length: > 0 } c ? c : Reglas.CampoFrom(r.Tecnica)) : "";
                return;
            }
            if (resp != null && motivo.Length > 0 && !Catalogos.MOTIVOS[resp.Value].Contains(motivo)) throw new ReglaException("Motivo no válido.");
            r.AtrasoMotivo = motivo;
            if (resp == AtrasoResponsable.Campo) r.AtrasoCampo = campo;
        }, ct);

    public async Task EliminarAsync(UsuarioActual me, int id, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var r = await db.Requerimientos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return;
        db.Requerimientos.Remove(r);
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
    }
}
