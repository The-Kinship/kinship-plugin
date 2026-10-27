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

    // The "SectContent" tab: columns sect, section, title, text, order.
    public const string SectUrl =
        "https://docs.google.com/spreadsheets/d/e/2PACX-1vTAGpMH_xMZvl3V_Pxo-e57S1D9Q2RhjpGxtN2romaYZer0CjAI3qlYzgZa4SUpNZsIbX0Dt3ag6-Jg/pub?gid=1603698241&single=true&output=csv";

    public sealed record SectRow(string Sect, string Section, string Title, string Text, double Order);

    public sealed record Person(string Name, string Sect, string Rank, string Race, string Age, string Bio, string Portrait, string Carrd, double Order, string Gender = "");

    public sealed record IconEntry(string Icon, string Label, string Category);

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

    public static async Task<List<SectRow>> FetchSectRowsAsync()
    {
        var rows = await FetchCsvAsync(SectUrl).ConfigureAwait(false);
        var result = new List<SectRow>();
        if (rows.Count == 0) return result;
        var head = rows[0].ConvertAll(h => h.Trim().ToLowerInvariant());
        int Ix(string n) => head.IndexOf(n);
        string Cell(List<string> r, int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            var sect = Cell(r, Ix("sect")).ToLowerInvariant();
            var section = Cell(r, Ix("section")).ToLowerInvariant();
            if (sect.Length == 0 || section.Length == 0) continue;
            var order = double.TryParse(Cell(r, Ix("order")), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var o) ? o : 999;
            result.Add(new SectRow(sect, section, Cell(r, Ix("title")), Cell(r, Ix("text")), order));
        }
        return result;
    }

    /// <summary>The Roster tab: name, sect, rank, race, age, bio, portrait, carrd_url, gender, status, order. Rows marked hidden are skipped.</summary>
    public static async Task<List<Person>> FetchRosterAsync(string url)
    {
        var rows = await FetchCsvAsync(url).ConfigureAwait(false);
        var result = new List<Person>();
        if (rows.Count == 0) return result;
        var head = rows[0].ConvertAll(h => h.Trim().ToLowerInvariant());
        int Ix(string n) => head.IndexOf(n);
        string Cell(List<string> r, int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
        for (var i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            var name = Cell(r, Ix("name"));
            if (name.Length == 0) continue;
            if (Cell(r, Ix("status")).Equals("hidden", StringComparison.OrdinalIgnoreCase)) continue;
            var order = double.TryParse(Cell(r, Ix("order")), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var o) ? o : 999;
            result.Add(new Person(name, Cell(r, Ix("sect")), Cell(r, Ix("rank")), Cell(r, Ix("race")), Cell(r, Ix("age")),
                Cell(r, Ix("bio")), Cell(r, Ix("portrait")), Cell(r, Ix("carrd_url")), order, Cell(r, Ix("gender"))));
        }
        return result;
    }

    /// <summary>The Icons tab: icon (a game icon number, or a file name in the website's images/icons folder), label, category.</summary>
    public static async Task<List<IconEntry>> FetchIconsAsync(string url)
    {
        var rows = await FetchCsvAsync(url).ConfigureAwait(false);
        var result = new List<IconEntry>();
        if (rows.Count == 0) return result;
        var head = rows[0].ConvertAll(h => h.Trim().ToLowerInvariant());
        int Ix(string n) => head.IndexOf(n);
        string Cell(List<string> r, int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
        foreach (var r in rows.GetRange(1, rows.Count - 1))
        {
            var raw = Cell(r, Ix("icon"));
            if (raw.Length == 0) continue;
            var icon = uint.TryParse(raw, out _) ? "game:" + raw : "site:" + raw;
            result.Add(new IconEntry(icon, Cell(r, Ix("label")), Cell(r, Ix("category"))));
        }
        return result;
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
