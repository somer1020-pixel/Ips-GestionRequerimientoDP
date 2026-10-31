using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GestionRequerimientoDP.Domain;

public sealed record TipoInfo(string Label, string Short, string Css, string EquipoLabel);
public sealed record RespInfo(string Label, string Color);

/// <summary>Reglas de negocio puras (sin I/O). Equivalen a las funciones auxiliares del mockup.</summary>
public static partial class Reglas
{
    public static TipoInfo Tipo(Equipo t) => t switch
    {
        Equipo.Script => new("Script", "Script", "be be-info", "Scripting"),
        Equipo.Codificacion => new("Codificación", "COD", "be be-navy", "Codificación"),
        _ => new("Procesamiento", "DP", "be be-neutral", "Procesamiento"),
    };

    /// <summary>
    /// ¿Puede ser responsable de un requerimiento de este tipo? Analistas activos del equipo y cualquier líder activo
    /// (otro líder o uno mismo). Los líderes no entran en balanceo ni en la autoasignación de scripts: se asignan a mano.
    /// </summary>
    public static bool Asignable(Usuario u, Equipo tipo) => u.Activo && (u.Rol == Rol.Lider || (u.Rol == Rol.Analista && u.Tiene(tipo)));

    /// <summary>Opciones del select de asignación: analistas del equipo y después los líderes.</summary>
    public static List<Usuario> Asignables(IEnumerable<Usuario> users, Equipo tipo) =>
        [.. users.Where(u => u.Rol == Rol.Analista && Asignable(u, tipo)).OrderBy(u => u.Id), .. Lideres(users)];

    /// <summary>Filtro "Todos los analistas": analistas activos y después los líderes activos.</summary>
    public static List<Usuario> Responsables(IEnumerable<Usuario> users) =>
        [.. users.Where(u => u.Rol == Rol.Analista && u.Activo).OrderBy(u => u.Id), .. Lideres(users)];

    static IEnumerable<Usuario> Lideres(IEnumerable<Usuario> users) =>
        users.Where(u => u.Rol == Rol.Lider && u.Activo).OrderBy(u => u.Nombre, StringComparer.CurrentCulture);

    /// <summary>Nombre en los selects: los líderes se marcan "(líder)".</summary>
    public static string NombreResponsable(Usuario u) => u.Rol == Rol.Lider ? $"{u.Nombre} (líder)" : u.Nombre;

    public static string EstadoLabel(Estado e) => e switch { Estado.EnCurso => "En curso", Estado.Ok => "OK", _ => "Pendiente" };
    public static string EstadoColumna(Estado e) => e switch { Estado.EnCurso => "En curso", Estado.Ok => "OK · Terminado", _ => "Pendiente" };
    public static string EstadoCss(Estado e) => e switch { Estado.EnCurso => "be be-info", Estado.Ok => "be be-success", _ => "be be-neutral" };

    public static string RolLabel(Rol r) => r switch { Rol.Lider => "Líder de equipo", Rol.Comercial => "Comercial", _ => "Analista" };

    public static string EquipoLabel(Usuario u)
    {
        if (u.Rol == Rol.Comercial) return "—";
        if (u.Rol == Rol.Lider && u.Equipos.Count == 0) return "Todos";
        var s = string.Join(" + ", u.EquiposOrdenados.Select(e => Tipo(e).EquipoLabel));
        return s.Length > 0 ? s : "—";
    }

    public static string Iniciales(string? nombre) =>
        string.IsNullOrWhiteSpace(nombre) ? "—"
        : string.Concat(nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    /// <summary>Normaliza para comparar: sin acentos, minúsculas, espacios colapsados.</summary>
    public static string Norm(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return Spaces().Replace(sb.ToString().ToLowerInvariant(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"\bms\b")] private static partial Regex MsWord();

    /// <summary>Área de campo a partir de la técnica.</summary>
    public static string CampoFrom(string? tecnica)
    {
        var s = Norm(tecnica);
        if (s.Length == 0) return "";
        if (s.Contains("cati") || s.Contains("telef")) return "CATI";
        if (s.Contains("f2f") || s.Contains("intercep") || s.Contains("presencial") || s.Contains("cara a cara")) return "F2F";
        if (s.Contains("mystery") || MsWord().IsMatch(s)) return "MS";
        if (s.Contains("idp")) return "IDP";
        if (s.Contains("ivr")) return "IVR";
        if (s.Contains("iis")) return "IIS";
        if (s.Contains("whatsapp")) return "WhatsApp";
        if (s.Contains("efm")) return "EFM";
        if (s.Contains("online") || s.Contains("qualtrics") || s.Contains("cawi") || s.Contains("web")) return "Online";
        return "";
    }

    /// <summary>Entrega atrasada: OK registrado después del día comprometido.</summary>
    public static bool IsLateOk(Requerimiento r) => r.Estado == Estado.Ok && r.OkAt is { } ok && Fechas.LocalDate(ok) > r.Fecha;

    public static bool IsOnTime(Requerimiento r) => r.Estado == Estado.Ok && r.OkAt is { } ok && Fechas.LocalDate(ok) <= r.Fecha;

    public static int DiasAtraso(Requerimiento r) => r.OkAt is { } ok ? Fechas.DaysBetween(r.Fecha, Fechas.LocalDate(ok)) : 0;

    /// <summary>Abierto con fecha pasada.</summary>
    public static bool Atrasado(Requerimiento r, DateOnly today) => r.Estado != Estado.Ok && r.Fecha < today;

    /// <summary>Ícono rojo: vence hoy o está vencido.</summary>
    public static bool IsDue(Requerimiento r, DateOnly today) => r.Estado != Estado.Ok && r.Fecha <= today;

    /// <summary>Ícono amarillo: vence el próximo día hábil.</summary>
    public static bool IsWarn(Requerimiento r, DateOnly today) => r.Estado != Estado.Ok && r.Fecha > today && r.Fecha <= Fechas.NextBiz(today);

    public static string AlertTitle(Requerimiento r, DateOnly today)
    {
        if (r.Estado == Estado.Ok) return "";
        if (r.Fecha < today) return "Vencido";
        if (r.Fecha == today) return "Vence hoy";
        return r.Fecha <= Fechas.NextBiz(today) ? "Vence el próximo día hábil" : "";
    }

    /// <summary>Sin gestión: pendiente, o sin actualización en staleDays días o más.</summary>
    public static bool SinGestion(Requerimiento r, DateOnly today, int staleDays) =>
        r.Estado == Estado.Pendiente || Fechas.LocalDate(r.Actualizado) <= today.AddDays(-staleDays);

    /// <summary>Clave efectiva del responsable: op_proc / op_script / op_cod / comercial / campo / sin.</summary>
    public static string EffResp(Requerimiento r) => r.AtrasoResp switch
    {
        AtrasoResponsable.Operaciones => "op_" + TipoKey(r.Tipo),
        AtrasoResponsable.Comercial => "comercial",
        AtrasoResponsable.Campo => "campo",
        _ => "sin",
    };

    public static string TipoKey(Equipo t) => t switch { Equipo.Script => "script", Equipo.Codificacion => "cod", _ => "proc" };

    public static RespInfo RespX(string key) => key switch
    {
        "op_proc" => new("Procesamiento", "var(--teal-text)"),
        "op_script" => new("Script", "var(--teal)"),
        "op_cod" => new("Codificación", "var(--color-accent-300)"),
        "comercial" => new("Área comercial (materiales)", "var(--warning)"),
        "campo" => new("Campo", "var(--ink-muted)"),
        _ => new("Sin clasificar", "var(--border)"),
    };

    /// <summary>"Operaciones" nunca se muestra: se usa la unidad del requerimiento.</summary>
    public static string RespLabel(Requerimiento r) =>
        r.AtrasoResp == AtrasoResponsable.Campo && !string.IsNullOrEmpty(r.AtrasoCampo) ? $"Campo · {r.AtrasoCampo}" : RespX(EffResp(r)).Label;

    public static string RespOptionLabel(AtrasoResponsable resp, Equipo tipo) => resp switch
    {
        AtrasoResponsable.Operaciones => Tipo(tipo).Label,
        AtrasoResponsable.Comercial => "Área comercial (materiales)",
        _ => "Campo",
    };

    /// <summary>Sugerencia automática: materiales recibidos el día de entrega o después → comercial.</summary>
    public static bool MaterialesTarde(Requerimiento r, DateOnly? materiales) => materiales is { } m && m >= r.Fecha;

    // Formatos numéricos es-CL
    static readonly NumberFormatInfo Cl = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NumberGroupSizes = [3] };
    public static string FmtN(long n) => n.ToString("#,##0", Cl);
    public static string FmtN(int? n) => FmtN((long)(n ?? 0));
    public static string FmtH(decimal? h) => Math.Round(h ?? 0, 2).ToString("#,##0.##", Cl) + " h";

    /// <summary>"· 3,5 h" o "· 1.240 reg. · IA".</summary>
    public static string OkSuffix(Requerimiento r) => r.Tipo == Equipo.Codificacion
        ? (r.Registros is > 0 ? $" · {FmtN(r.Registros)} reg. · {r.Metodo?.ToString() ?? "—"}" : "")
        : (r.Horas != null ? " · " + FmtH(r.Horas) : "");

    /// <summary>Estado visible para la consulta pública y para el comercial.</summary>
    public static (string Label, string Css) EstadoPublico(Requerimiento r)
    {
        if (r.AnalistaId == null) return ("Por asignar", "be be-warning");
        if (r.Estado == Estado.Ok) return ($"Entregado {Fechas.FmtD(r.OkAt is { } o ? Fechas.LocalDate(o) : null)}", "be be-success");
        return r.Estado == Estado.EnCurso ? ("En proceso", "be be-info") : ("Asignado · programado", "be be-info");
    }

    /// <summary>Líderes que reciben avisos del equipo t (o todos los líderes si nadie tiene ese equipo a cargo).</summary>
    public static List<Usuario> TeamLeadersOf(IEnumerable<Usuario> users, Equipo t)
    {
        var l = users.Where(u => u.Rol == Rol.Lider && u.Activo).ToList();
        var m = l.Where(u => u.Equipos.Count == 0 || u.Tiene(t)).ToList();
        return m.Count > 0 ? m : l;
    }

    /// <summary>Alcance "semana actual": entregables proc/cod de la semana, scripts abiertos y scripts cerrados en la semana.</summary>
    public static bool EnSemanaActual(Requerimiento r, DateOnly today)
    {
        var cur = Fechas.MondayOf(today);
        if (r.Tipo == Equipo.Script)
            return r.Estado != Estado.Ok || Fechas.MondayOf(r.Fecha) == cur || (r.OkAt is { } ok && Fechas.LocalDate(ok) >= cur);
        return Fechas.MondayOf(r.Fecha) == cur;
    }
}
