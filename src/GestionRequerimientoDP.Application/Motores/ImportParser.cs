using System.Globalization;
using System.Text.RegularExpressions;
using GestionRequerimientoDP.Domain;

namespace GestionRequerimientoDP.Application.Motores;

public sealed record ImportRow(DateOnly Fecha, string Pais, string SL, string Tecnica, string Cliente, string Proyecto, string Job,
    string Entregable, string Descripcion, string Comercial, int? AnalistaId);

public sealed class ImportResult
{
    public List<ImportRow> Rows { get; } = [];
    public int Duplicados { get; set; }
    public int Invalidas { get; set; }
    /// <summary>Nombres de analista no reconocidos (orden de aparición).</summary>
    public List<string> NoReconocidos { get; } = [];
    public SortedSet<DateOnly> Semanas { get; } = [];
}

/// <summary>Interpretación de la planilla (§3.1 "Importar planilla").</summary>
public static partial class ImportParser
{
    static readonly string[] Cols = ["fecha", "pais", "bu", "tecnica", "cliente", "proyecto", "job", "entregable", "descripcion", "comercial", "analista"];
    static readonly Dictionary<string, string> Hdr = new()
    {
        ["dia"] = "fecha", ["fecha"] = "fecha", ["pais"] = "pais", ["bu"] = "bu", ["sl"] = "bu", ["tecnica"] = "tecnica", ["cliente"] = "cliente",
        ["proyecto"] = "proyecto", ["estudio"] = "proyecto", ["job"] = "job", ["entregable"] = "entregable",
        ["descripcion"] = "descripcion", ["comercial"] = "comercial", ["analista"] = "analista",
    };

    /// <summary>Filas pegadas desde Excel: separadas por tabulador o punto y coma.</summary>
    public static List<object?[]> FromText(string text) =>
        text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Split('\t', ';').Cast<object?>().ToArray())
            .ToList();

    public static string DupKey(DateOnly fecha, string job, string entregable, string descripcion, int? analista) =>
        string.Join('|', Fechas.Iso(fecha), Reglas.Norm(job), Reglas.Norm(entregable), Reglas.Norm(descripcion), analista?.ToString() ?? "");

    /// <param name="matrix">Celdas: DateTime, número (serial de Excel) o texto.</param>
    /// <param name="teamAnalysts">Analistas del equipo de la unidad (para reconocer ANALISTA).</param>
    /// <param name="existentes">Requerimientos ya guardados de la unidad (para detectar duplicados).</param>
    public static ImportResult Parse(IReadOnlyList<object?[]>? matrix, IEnumerable<Usuario> teamAnalysts, IEnumerable<Requerimiento> existentes)
    {
        var res = new ImportResult();
        if (matrix is null || matrix.Count == 0) return res;

        string?[] map = Cols;
        var start = 0;
        var h = matrix[0].Select(c => Reglas.Norm(Convert.ToString(c, CultureInfo.InvariantCulture))).ToArray();
        if (h.Contains("job")) { map = h.Select(c => Hdr.GetValueOrDefault(c)).ToArray(); start = 1; }

        var team = teamAnalysts.ToList();
        var seen = existentes.Select(r => DupKey(r.Fecha, r.Job, r.Entregable, r.Descripcion, r.AnalistaId)).ToHashSet();

        for (var i = start; i < matrix.Count; i++)
        {
            var row = matrix[i];
            var o = new Dictionary<string, object?>();
            for (var j = 0; j < map.Length; j++)
                if (map[j] is { } k) o[k] = j < row.Length ? row[j] : null;
            string S(string k) => (Convert.ToString(o.GetValueOrDefault(k), CultureInfo.InvariantCulture) ?? "").Trim();

            var fecha = ToIsoDate(o.GetValueOrDefault("fecha"));
            var job = S("job");
            var proyecto = S("proyecto");
            if (job.Length == 0 && proyecto.Length == 0) continue;
            if (fecha is null || job.Length == 0) { res.Invalidas++; continue; }

            var q = Reglas.Norm(S("analista"));
            int? an = null;
            if (q.Length > 0)
            {
                var u = team.FirstOrDefault(u => Reglas.Norm(u.Nombre) == q)
                        ?? team.FirstOrDefault(u => Reglas.Norm(u.Nombre).Contains(q) || q.Contains(Reglas.Norm(u.Nombre)));
                if (u != null) an = u.Id;
                else if (!res.NoReconocidos.Contains(S("analista"))) res.NoReconocidos.Add(S("analista"));
            }

            var key = DupKey(fecha.Value, job, S("entregable"), S("descripcion"), an);
            if (!seen.Add(key)) { res.Duplicados++; continue; }
            res.Semanas.Add(Fechas.MondayOf(fecha.Value));
            res.Rows.Add(new ImportRow(fecha.Value, S("pais") is { Length: > 0 } p ? p : "CL", S("bu"), S("tecnica"), S("cliente"), proyecto, job,
                S("entregable"), S("descripcion"), S("comercial"), an));
        }
        return res;
    }

    [GeneratedRegex(@"^(\d{4})-(\d{1,2})-(\d{1,2})")] private static partial Regex IsoRx();
    [GeneratedRegex(@"^(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{2,4})$")] private static partial Regex DmyRx();

    /// <summary>DIA: fecha de Excel (DateTime o serial), "yyyy-mm-dd" o "d/m/yyyy" (m/d si el segundo número es mayor que 12).</summary>
    public static DateOnly? ToIsoDate(object? v)
    {
        switch (v)
        {
            case DateTime dt: return DateOnly.FromDateTime(dt);
            case DateOnly d: return d;
            case DateTimeOffset dto: return DateOnly.FromDateTime(dto.DateTime);
            case double or float or decimal or int or long:
                var n = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return n > 20000 ? DateOnly.FromDateTime(DateTime.UnixEpoch.AddMilliseconds(Math.Round((n - 25569) * 86400000))) : null;
        }
        var s = (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Trim();
        if (s.Length == 0) return null;
        var m = IsoRx().Match(s);
        if (m.Success) return Make(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        m = DmyRx().Match(s);
        if (m.Success)
        {
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value), y = int.Parse(m.Groups[3].Value);
            if (y < 100) y += 2000;
            int dd = a, mm = b;
            if (b > 12) { dd = b; mm = a; }
            return Make(y, mm, dd);
        }
        // Serial de Excel recibido como texto (p. ej. CSV)
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return ToIsoDate(num);
        return null;
    }

    static DateOnly? Make(int y, int m, int d) =>
        m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m) && y is > 1900 and < 3000 ? new DateOnly(y, m, d) : null;
}
