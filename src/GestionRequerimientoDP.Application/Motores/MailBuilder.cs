using System.Text.Json;
using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Application.Motores;

public sealed record MailDraft(NotificacionKind Kind, IReadOnlyList<string> To, IReadOnlyList<string> Cc, string Subject, string Body, IReadOnlyList<int> ReqIds)
{
    public Notificacion ToEntity(DateTimeOffset now) => new()
    {
        Kind = Kind, To = string.Join(';', To), Cc = string.Join(';', Cc), Subject = Subject, Body = Body,
        ReqIds = JsonSerializer.Serialize(ReqIds), CreatedAt = now, Status = NotificacionStatus.Pendiente,
    };
}

/// <summary>Formato de los correos automáticos (§3.3d y §3.0).</summary>
public static class MailBuilder
{
    static readonly string P = $"[{AppInfo.Nombre}]";

    static string Line(Requerimiento r, bool conEstado) =>
        $"• {Fechas.FmtD(r.Fecha)} · {r.Job} · {r.Proyecto} — {r.Entregable}: {(string.IsNullOrEmpty(r.Descripcion) ? "—" : r.Descripcion)} ({Reglas.Tipo(r.Tipo).Label}{(conEstado ? ", " + Reglas.EstadoLabel(r.Estado) : "")})";

    static string First(string nombre) => nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nombre;

    public static MailDraft Asignacion(Usuario analista, IReadOnlyList<Requerimiento> items)
    {
        var one = items.Count == 1;
        var subject = one ? $"{P} Nuevo requerimiento asignado: {items[0].Job} · {items[0].Proyecto}" : $"{P} Tienes {items.Count} requerimientos nuevos asignados";
        var body = string.Join('\n', new[]
        {
            $"Hola {First(analista.Nombre)},", "",
            one ? "Se te asignó el siguiente requerimiento:" : "Se te asignaron los siguientes requerimientos:",
        }.Concat(items.Select(r => Line(r, false))).Concat([
            "", $"Revisa el detalle en {AppInfo.Nombre} → Mi semana y marca \"Iniciar\" al comenzar.", "", $"Mensaje automático de {AppInfo.Nombre}.",
        ]));
        return new(NotificacionKind.Asignacion, [analista.Email], [], subject, body, items.Select(r => r.Id).ToList());
    }

    /// <param name="analista">Null = requerimientos sin analista (van solo a los líderes).</param>
    public static MailDraft Atraso(Usuario? analista, IReadOnlyList<Requerimiento> items, IReadOnlyList<Usuario> lideres)
    {
        var leaders = lideres.Select(l => l.Email).ToList();
        var subject = $"{P} {items.Count} requerimiento(s) atrasado(s) sin gestión{(analista is null ? " y sin analista" : "")}";
        var body = string.Join('\n', new[]
        {
            analista is null ? "Hola equipo líder," : $"Hola {First(analista.Nombre)},", "",
            "Los siguientes requerimientos ya pasaron su día de entrega y no registran gestión (sin iniciar o sin actualización reciente):",
        }.Concat(items.Select(r => Line(r, true))).Concat([
            "", "Actualiza el estado o deja una nota con el motivo. Si ya terminaste, registra el OK de término.", "", $"Mensaje automático de {AppInfo.Nombre}.",
        ]));
        return new(NotificacionKind.Atraso, analista is null ? leaders : [analista.Email], analista is null ? [] : leaders, subject, body, items.Select(r => r.Id).ToList());
    }

    public static MailDraft Solicitud(IReadOnlyList<Usuario> lideres, Usuario? solicitante, string codigo, IReadOnlyList<Requerimiento> items, string nota)
    {
        var f = items[0];
        var unidad = Reglas.Tipo(f.Tipo).Label;
        var lines = new List<string>
        {
            $"Hola {string.Join(", ", lideres.Select(l => First(l.Nombre)))},", "",
            $"{solicitante?.Nombre ?? "Un área comercial"} ingresó una nueva solicitud para {unidad}:",
            $"• Cliente: {f.Cliente} · Job {f.Job}", $"• Proyecto: {f.Proyecto}", $"• Requerida para: {Fechas.FmtD(f.Fecha)}", $"• Entregables ({items.Count}):",
        };
        lines.AddRange(items.Select(r => $"   - {r.Entregable}{(string.IsNullOrEmpty(r.Descripcion) ? "" : ": " + r.Descripcion)}"));
        if (!string.IsNullOrEmpty(nota)) lines.AddRange(["", $"Nota: {nota}"]);
        lines.AddRange(["", $"La solicitud no tiene analista asignado. Revísala en {AppInfo.Nombre} → Solicitudes nuevas y asígnala desde Planificación semanal o Scripts.", "", $"Mensaje automático de {AppInfo.Nombre}."]);
        return new(NotificacionKind.Solicitud, lideres.Select(l => l.Email).ToList(), solicitante is null ? [] : [solicitante.Email],
            $"{P} Nueva solicitud {codigo} · {unidad} · {f.Proyecto}", string.Join('\n', lines), items.Select(r => r.Id).ToList());
    }

    /// <summary>Un correo por analista con todos sus requerimientos (acciones masivas).</summary>
    public static List<MailDraft> AsignacionPorAnalista(IEnumerable<Requerimiento> asignados, IReadOnlyDictionary<int, Usuario> usuarios) =>
        asignados.Where(r => r.AnalistaId != null)
            .GroupBy(r => r.AnalistaId!.Value)
            .Where(g => usuarios.ContainsKey(g.Key))
            .Select(g => Asignacion(usuarios[g.Key], g.ToList()))
            .ToList();
}
