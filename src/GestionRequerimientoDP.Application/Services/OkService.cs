using GestionRequerimientoDP.Application.Motores;
using GestionRequerimientoDP.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionRequerimientoDP.Application.Services;

public enum MoveResult { Hecho, SinCambio, RequiereOk }

public interface IOkService
{
    /// <summary>Cambia el estado. Mover a OK no cierra: devuelve RequiereOk para abrir el diálogo.</summary>
    Task<MoveResult> MoveToAsync(UsuarioActual me, int id, Estado estado, CancellationToken ct = default);
    /// <summary>Registra el OK. Devuelve el mensaje de error de validación o null si se guardó.</summary>
    Task<string?> ConfirmOkAsync(UsuarioActual me, int id, OkInput input, CancellationToken ct = default);
    Task ReabrirAsync(UsuarioActual me, int id, CancellationToken ct = default);
}

public sealed class OkService(IAppDbContextFactory factory, IClock clock, IChangeNotifier changes) : IOkService
{
    public async Task<MoveResult> MoveToAsync(UsuarioActual me, int id, Estado estado, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        var r = await db.Requerimientos.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaException("El requerimiento ya no existe.");
        Guard.Edit(me, r);
        if (estado == Estado.Ok) return r.Estado == Estado.Ok ? MoveResult.SinCambio : MoveResult.RequiereOk;
        if (r.Estado == estado) return MoveResult.SinCambio;
        if (r.Estado == Estado.Ok && !me.EsLider) throw new PermisoException("Solo el líder puede reabrir un requerimiento con OK");
        var now = clock.Now;
        var from = r.Estado;
        r.Estado = estado;
        r.LimpiarOk();
        r.Actualizado = now;
        db.Log(r, from == Estado.Ok ? OkAccion.Reabrir : OkAccion.CambioEstado, me.Id, now, new { from = from.ToString(), to = estado.ToString() });
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
        return MoveResult.Hecho;
    }

    public async Task<string?> ConfirmOkAsync(UsuarioActual me, int id, OkInput input, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        var r = await db.Requerimientos.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaException("El requerimiento ya no existe.");
        Guard.Edit(me, r);
        if (r.Estado == Estado.Ok) return "El requerimiento ya tiene OK.";
        var (error, p) = OkValidator.Validate(r, input, clock.Today);
        if (error != null) return error;
        var now = clock.Now;
        r.Estado = Estado.Ok;
        r.OkAt = now;
        r.OkById = me.Id;
        r.OkNota = p!.Nota;
        r.MaterialesAt = p.Materiales;
        r.Horas = p.Horas;
        r.Registros = p.Registros;
        r.Metodo = p.Metodo;
        r.AtrasoResp = p.Resp;
        r.AtrasoMotivo = p.Motivo;
        r.AtrasoCampo = p.Campo;
        r.AtrasoDetalle = p.Detalle;
        r.Actualizado = now;
        db.Log(r, OkAccion.Ok, me.Id, now, new { p.Horas, p.Registros, metodo = p.Metodo?.ToString(), resp = p.Resp?.ToString(), p.Motivo, p.Campo, p.Detalle, p.Nota });
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
        if (r.SolicitudId != null) changes.Publish(ChangeKind.Solicitudes);
        return null;
    }

    public async Task ReabrirAsync(UsuarioActual me, int id, CancellationToken ct = default)
    {
        Guard.Lider(me);
        await using var db = factory.Create();
        var r = await db.Requerimientos.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaException("El requerimiento ya no existe.");
        if (r.Estado != Estado.Ok) return;
        var now = clock.Now;
        r.Estado = Estado.EnCurso;
        r.LimpiarOk();
        r.Actualizado = now;
        db.Log(r, OkAccion.Reabrir, me.Id, now, new { to = "EnCurso" });
        await db.SaveAsync(ct);
        changes.Publish(ChangeKind.Requerimientos);
    }
}
