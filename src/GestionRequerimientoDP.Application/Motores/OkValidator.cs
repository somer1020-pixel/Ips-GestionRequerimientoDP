using System.Globalization;
using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Application.Motores;

/// <summary>Datos del diálogo "Confirmar OK de término".</summary>
public sealed class OkInput
{
    public string Horas { get; set; } = "";
    public string Registros { get; set; } = "";
    public MetodoCodificacion? Metodo { get; set; }
    public DateOnly? Materiales { get; set; }
    public AtrasoResponsable? Resp { get; set; }
    public string Motivo { get; set; } = "";
    public string Campo { get; set; } = "";
    public string Detalle { get; set; } = "";
    public string Nota { get; set; } = "";

    /// <summary>Precarga del diálogo (§3.3 y §3.3e).</summary>
    public static OkInput Defaults(Requerimiento r, DateOnly today)
    {
        var sugiereComercial = today > r.Fecha && Reglas.MaterialesTarde(r, r.MaterialesAt);
        return new OkInput
        {
            Horas = r.Horas?.ToString(CultureInfo.InvariantCulture) ?? "",
            Registros = r.Registros is > 0 ? r.Registros.Value.ToString(CultureInfo.InvariantCulture) : "",
            Metodo = r.Metodo,
            Materiales = r.MaterialesAt,
            Resp = r.AtrasoResp ?? (sugiereComercial ? AtrasoResponsable.Comercial : null),
            Motivo = r.AtrasoMotivo is { Length: > 0 } m ? m : sugiereComercial ? Catalogos.MotivoMaterialesTarde : "",
            Detalle = r.AtrasoDetalle,
            Campo = r.AtrasoCampo is { Length: > 0 } c ? c : Reglas.CampoFrom(r.Tecnica),
        };
    }
}

public sealed record OkPatch(decimal? Horas, int? Registros, MetodoCodificacion? Metodo, DateOnly? Materiales,
    AtrasoResponsable? Resp, string Motivo, string Campo, string Detalle, string Nota);

public static class OkValidator
{
    /// <summary>Valida el OK. Devuelve (error, null) o (null, patch) listo para aplicar.</summary>
    public static (string? Error, OkPatch? Patch) Validate(Requerimiento r, OkInput i, DateOnly today)
    {
        decimal? horas = null; int? registros = null; MetodoCodificacion? metodo = null;
        if (r.Tipo == Equipo.Codificacion)
        {
            var digits = new string((i.Registros ?? "").Where(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out var n) || n <= 0) return ("Ingresa la cantidad de registros codificados.", null);
            if (i.Metodo is null) return ("Indica si la codificación se realizó por IA o Manual.", null);
            registros = n; metodo = i.Metodo;
        }
        else
        {
            var ok = decimal.TryParse((i.Horas ?? "").Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var hv);
            if (!ok || hv <= 0 || hv > 200) return ("Ingresa las horas dedicadas (mayor a 0, ej. 3,5).", null);
            horas = Math.Round(hv, 2);
        }

        var late = today > r.Fecha;
        if (late && (i.Resp is null || string.IsNullOrWhiteSpace(i.Motivo)))
            return ("La entrega está atrasada: indica el responsable y el motivo del atraso.", null);
        if (late && i.Resp == AtrasoResponsable.Campo && string.IsNullOrWhiteSpace(i.Campo))
            return ("Indica el área de campo responsable del atraso.", null);
        if (late && !Catalogos.MOTIVOS[i.Resp!.Value].Contains(i.Motivo))
            return ("Selecciona un motivo válido para el responsable indicado.", null);
        if (i.Materiales is { } m && m > today) return ("La fecha de materiales no puede ser posterior a hoy.", null);

        return (null, new OkPatch(horas, registros, metodo, i.Materiales,
            late ? i.Resp : null, late ? i.Motivo : "", late && i.Resp == AtrasoResponsable.Campo ? i.Campo : "",
            late ? (i.Detalle ?? "").Trim() : "", (i.Nota ?? "").Trim()));
    }
}
