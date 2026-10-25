using System.Globalization;

namespace GestionRequerimientoDP.Domain;

/// <summary>Fechas y formatos en español de Chile (equivalentes a fmtD, weekLabel, nextBiz… del mockup).</summary>
public static class Fechas
{
    public static readonly TimeZoneInfo Chile = ResolveChile();

    static TimeZoneInfo ResolveChile()
    {
        foreach (var id in new[] { "America/Santiago", "Pacific SA Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    static readonly string[] Meses = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];
    public static readonly string[] MesesLargos = ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];
    static readonly string[] Dias = ["Dom", "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb"];

    /// <summary>Fecha local (Chile) de un timestamp.</summary>
    public static DateOnly LocalDate(DateTimeOffset t) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, Chile).DateTime);

    /// <summary>Construye un timestamp a partir de una fecha/hora local de Chile.</summary>
    public static DateTimeOffset FromLocal(DateOnly d, int hour, int minute)
    {
        var local = d.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Chile.GetUtcOffset(local));
    }

    public static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>Próximo día hábil (salta sábado y domingo).</summary>
    public static DateOnly NextBiz(DateOnly d)
    {
        var x = d.AddDays(1);
        while (x.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) x = x.AddDays(1);
        return x;
    }

    public static int DaysBetween(DateOnly a, DateOnly b) => b.DayNumber - a.DayNumber;

    /// <summary>"Lun 19 oct".</summary>
    public static string FmtD(DateOnly d) => $"{Dias[(int)d.DayOfWeek]} {d.Day} {Meses[d.Month - 1]}";
    public static string FmtD(DateOnly? d) => d is { } x ? FmtD(x) : "—";

    /// <summary>"Lun 19 oct 18:10".</summary>
    public static string FmtDT(DateTimeOffset? t)
    {
        if (t is null) return "—";
        var l = TimeZoneInfo.ConvertTime(t.Value, Chile);
        return $"{FmtD(DateOnly.FromDateTime(l.DateTime))} {l:HH:mm}";
    }

    /// <summary>"19 – 23 oct" o "29 sep – 3 oct".</summary>
    public static string WeekLabel(DateOnly monday)
    {
        var b = monday.AddDays(4);
        return monday.Month == b.Month
            ? $"{monday.Day} – {b.Day} {Meses[b.Month - 1]}"
            : $"{monday.Day} {Meses[monday.Month - 1]} – {b.Day} {Meses[b.Month - 1]}";
    }

    public static string WeekName(DateOnly monday, DateOnly today)
    {
        var cur = MondayOf(today);
        if (monday == cur) return "Semana actual";
        if (monday == cur.AddDays(7)) return "Semana siguiente";
        return monday < cur ? "Semana anterior" : "Semana futura";
    }

    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string IsoDT(DateTimeOffset? t) => t is null ? "" : TimeZoneInfo.ConvertTime(t.Value, Chile).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
