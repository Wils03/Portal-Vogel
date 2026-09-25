using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Portal.Services;

/// <summary>Leitura (CSV/XLSX) e geração (XLSX) de planilhas para importar/exportar dados.</summary>
public static class Planilhas
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Lê as linhas usando a primeira linha como cabeçalho. Chaves normalizadas (minúsculas, sem acento/espaço).</summary>
    public static List<Dictionary<string, string>> Ler(Stream arquivo, string nomeArquivo)
    {
        using var memoria = new MemoryStream();
        arquivo.CopyTo(memoria);
        memoria.Position = 0;
        return nomeArquivo.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? LerXlsx(memoria) : LerCsv(memoria);
    }

    private static List<Dictionary<string, string>> LerXlsx(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var usadas = ws.RangeUsed();
        if (usadas is null) return [];

        var cabecalho = usadas.FirstRow().Cells().Select(c => Normalizar(c.GetString())).ToList();
        var linhas = new List<Dictionary<string, string>>();
        foreach (var row in usadas.RowsUsed().Skip(1))
        {
            var d = new Dictionary<string, string>();
            for (var i = 0; i < cabecalho.Count; i++)
            {
                var cell = row.Cell(i + 1);
                d[cabecalho[i]] = cell.DataType switch
                {
                    XLDataType.Number => cell.GetDouble().ToString(PtBr),
                    XLDataType.DateTime => cell.GetDateTime().ToString("dd/MM/yyyy"),
                    _ => cell.GetFormattedString()
                };
            }
            linhas.Add(d);
        }
        return linhas;
    }

    private static List<Dictionary<string, string>> LerCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var primeira = reader.ReadLine();
        if (primeira is null) return [];
        var separador = primeira.Count(ch => ch == ';') >= primeira.Count(ch => ch == ',') ? ';' : ',';
        var cabecalho = DividirCsv(primeira, separador).Select(Normalizar).ToList();

        var linhas = new List<Dictionary<string, string>>();
        while (reader.ReadLine() is { } linha)
        {
            if (string.IsNullOrWhiteSpace(linha)) continue;
            var campos = DividirCsv(linha, separador);
            var d = new Dictionary<string, string>();
            for (var i = 0; i < cabecalho.Count; i++)
                d[cabecalho[i]] = i < campos.Count ? campos[i] : "";
            linhas.Add(d);
        }
        return linhas;
    }

    private static List<string> DividirCsv(string linha, char separador)
    {
        var campos = new List<string>();
        var atual = new StringBuilder();
        var aspas = false;
        for (var i = 0; i < linha.Length; i++)
        {
            var ch = linha[i];
            if (ch == '"')
            {
                if (aspas && i + 1 < linha.Length && linha[i + 1] == '"') { atual.Append('"'); i++; }
                else aspas = !aspas;
            }
            else if (ch == separador && !aspas) { campos.Add(atual.ToString().Trim()); atual.Clear(); }
            else atual.Append(ch);
        }
        campos.Add(atual.ToString().Trim());
        return campos;
    }

    public static string Normalizar(string texto)
    {
        var semAcento = new string(texto.Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
        return new string(semAcento.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    public static string Campo(this Dictionary<string, string> linha, params string[] nomes)
    {
        foreach (var n in nomes)
            if (linha.TryGetValue(Normalizar(n), out var v)) return v;
        return "";
    }

    /// <summary>Aceita "1.234,56", "1234.56", "R$ 10,00", "17%".</summary>
    public static decimal Decimal(string? texto)
    {
        var t = (texto ?? "").Replace("R$", "").Replace("%", "").Trim();
        if (t.Length == 0) return 0m;
        if (t.Contains(',')) return decimal.TryParse(t, NumberStyles.Any, PtBr, out var br) ? br : 0m;
        return decimal.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var inv) ? inv : 0m;
    }

    public static DateOnly? Mes(string? texto)
    {
        var t = (texto ?? "").Trim();
        string[] formatos = ["MM/yyyy", "M/yyyy", "yyyy-MM", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];
        if (DateTime.TryParseExact(t, formatos, PtBr, DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(t, PtBr, DateTimeStyles.None, out dt))
            return new DateOnly(dt.Year, dt.Month, 1);
        return null;
    }

    /// <summary>Gera um XLSX com cabeçalho congelado e filtro.</summary>
    public static byte[] GerarXlsx(string aba, IReadOnlyList<string> cabecalho, IEnumerable<IReadOnlyList<object?>> linhas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(aba);
        for (var c = 0; c < cabecalho.Count; c++)
            ws.Cell(1, c + 1).Value = cabecalho[c];

        var r = 2;
        foreach (var linha in linhas)
        {
            for (var c = 0; c < linha.Count; c++)
                ws.Cell(r, c + 1).Value = linha[c] switch
                {
                    null => Blank.Value,
                    decimal d => d,
                    double d => d,
                    int i => i,
                    long l => l,
                    DateTime dt => dt,
                    DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                    var v => v.ToString()
                };
            r++;
        }

        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        if (r > 2) ws.Range(1, 1, r - 1, cabecalho.Count).SetAutoFilter();
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
