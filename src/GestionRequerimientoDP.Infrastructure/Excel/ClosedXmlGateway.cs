using System.Text;
using ClosedXML.Excel;
using GestionRequerimientoDP.Application.Services;

namespace GestionRequerimientoDP.Infrastructure.Excel;

/// <summary>Importación y exportación de planillas con ClosedXML (MIT).</summary>
public sealed class ClosedXmlGateway : IExcelGateway
{
    public byte[] Write(IReadOnlyList<ExportSheet> sheets)
    {
        using var wb = new XLWorkbook();
        foreach (var sheet in sheets)
        {
            var ws = wb.Worksheets.Add(sheet.Name);
            for (var i = 0; i < sheet.Rows.Count; i++)
            {
                var row = sheet.Rows[i];
                for (var j = 0; j < row.Length; j++)
                {
                    var cell = ws.Cell(i + 1, j + 1);
                    switch (row[j])
                    {
                        case null: break;
                        case DateTime d: cell.Value = d; cell.Style.DateFormat.Format = "dd-mm-yyyy"; break;
                        case int n: cell.Value = n; break;
                        case long n: cell.Value = n; break;
                        case decimal n: cell.Value = n; break;
                        case double n: cell.Value = n; break;
                        default: cell.Value = Convert.ToString(row[j]) ?? ""; break;
                    }
                }
            }
            if (sheet.HeaderRow && sheet.Rows.Count > 0)
            {
                var hdr = ws.Row(1);
                hdr.Style.Font.Bold = true;
                ws.SheetView.FreezeRows(1);
            }
            if (sheet.Widths is { } w)
                for (var j = 0; j < w.Count; j++) ws.Column(j + 1).Width = w[j];
            else
                ws.Columns().AdjustToContents();
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public List<object?[]> ReadFirstSheet(Stream xlsx)
    {
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        if (used is null) return [];
        var cols = used.LastColumn().ColumnNumber();
        var result = new List<object?[]>();
        foreach (var row in ws.Rows(1, used.LastRow().RowNumber()))
        {
            var cells = new object?[cols];
            for (var j = 1; j <= cols; j++)
            {
                var v = row.Cell(j).Value;
                cells[j - 1] = v.Type switch
                {
                    XLDataType.DateTime => v.GetDateTime(),
                    XLDataType.Number => v.GetNumber(),
                    XLDataType.Boolean => v.GetBoolean().ToString(),
                    XLDataType.Blank => "",
                    _ => v.ToString(),
                };
            }
            result.Add(cells);
        }
        return result;
    }

    public List<object?[]> ReadCsv(Stream csv)
    {
        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0)
            .Select(l => SplitCsv(l, l.Contains('\t') ? '\t' : l.Contains(';') ? ';' : ',')).ToList();
    }

    static object?[] SplitCsv(string line, char sep)
    {
        var res = new List<object?>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == sep) { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        res.Add(sb.ToString());
        return res.ToArray();
    }
}
