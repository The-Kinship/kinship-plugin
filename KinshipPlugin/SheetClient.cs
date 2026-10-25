using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace KinshipPlugin;

/// <summary>Reads the published Google Sheet tabs (same data the website shows).</summary>
public static class SheetClient
{
    // The "Faction" tab: two columns, key and value.
    public const string FactionUrl =
        "https://docs.google.com/spreadsheets/d/e/2PACX-1vTAGpMH_xMZvl3V_Pxo-e57S1D9Q2RhjpGxtN2romaYZer0CjAI3qlYzgZa4SUpNZsIbX0Dt3ag6-Jg/pub?gid=0&single=true&output=csv";

    // The "Plots" tab: columns title, summary, status, order. Only status "active" shows.
    public const string PlotsUrl =
        "https://docs.google.com/spreadsheets/d/e/2PACX-1vTAGpMH_xMZvl3V_Pxo-e57S1D9Q2RhjpGxtN2romaYZer0CjAI3qlYzgZa4SUpNZsIbX0Dt3ag6-Jg/pub?gid=1197750534&single=true&output=csv";

    public sealed record Plot(string Title, string Summary, double Order);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<List<List<string>>> FetchCsvAsync(string url)
    {
        var text = await Http.GetStringAsync(url).ConfigureAwait(false);
        return ParseCsv(text);
    }

    /// <summary>Faction tab -> dictionary of lower-case key to value.</summary>
    public static async Task<Dictionary<string, string>> FetchFactionAsync()
    {
        var rows = await FetchCsvAsync(FactionUrl).ConfigureAwait(false);
        var d = new Dictionary<string, string>();
        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Count == 0) continue;
            var key = r[0].Trim().ToLowerInvariant();
            if (key.Length == 0) continue;
            d[key] = r.Count > 1 ? r[1].Trim() : "";
        }
        return d;
    }

    public static async Task<List<Plot>> FetchPlotsAsync()
    {
        var rows = await FetchCsvAsync(PlotsUrl).ConfigureAwait(false);
        var plots = new List<Plot>();
        if (rows.Count == 0) return plots;
        var head = rows[0].ConvertAll(h => h.Trim().ToLowerInvariant());
        int Ix(string n) => head.IndexOf(n);
        string Cell(List<string> r, int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (!Cell(r, Ix("status")).Equals("active", StringComparison.OrdinalIgnoreCase)) continue;
            var title = Cell(r, Ix("title"));
            if (title.Length == 0) continue;
            var order = double.TryParse(Cell(r, Ix("order")), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var o) ? o : 999;
            plots.Add(new Plot(title, Cell(r, Ix("summary")), order));
        }
        plots.Sort((a, b) => a.Order.CompareTo(b.Order));
        return plots;
    }

    // Small CSV reader that handles quotes, commas and line breaks inside cells.
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else cell.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                rows.Add(row); row = new List<string>();
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        rows.RemoveAll(r => r.TrueForAll(x => x.Trim().Length == 0));
        return rows;
    }
}
