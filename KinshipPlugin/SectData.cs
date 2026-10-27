using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Numerics;

namespace KinshipPlugin;

public sealed record SectInfo(string Key, string Name, string Leader, string Image, Vector4 Color, string Blurb, bool Hidden = false);

public static class SectData
{
    private static Vector4 Hex(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    // The sect passwords and the sect pages live on the server. Nothing secret is kept in the plugin.
    public static readonly Dictionary<string, SectInfo> All = new()
    {
        ["onyx"]  = new("onyx",  "The Onyx Blood Guard", "The Prefect",            "onyx.jpg",       Hex(0x8b, 0x5c, 0xf6), "Magic, research, and the occult."),
        ["iron"]  = new("iron",  "The Iron Vanguard",    "The Vanguard Commander", "vanguard.jpg",   Hex(0x8f, 0xa6, 0xcf), "Military, patrols, and peacekeeping."),
        ["life"]  = new("life",  "The Life Watch",       "The Life Warden",        "lifewatch.jpg",  Hex(0x4f, 0x9d, 0x5d), "Healing of body and mind."),
        ["ember"] = new("ember", "The Ember Court",      "The Baron / Baroness",   "ember-court.jpg",Hex(0xd9, 0x47, 0x2b), "Social RP, events, and welcomes."),
        ["crypt"] = new("crypt", "The Onyx Crypt",       "",                       "onyx.jpg",       Hex(0x8b, 0x5c, 0xf6), "", true),
    };

    public static readonly string[] Order = { "onyx", "iron", "life", "ember" };

    public sealed record EnterResult(bool Ok, string Message);

    private static readonly object gate = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, string> sessionTokens = new();                      // entered without "remember"
    private static readonly Dictionary<string, List<SheetClient.SectRow>> content = new();
    private static readonly Dictionary<string, DateTime> lastLoad = new();
    private static readonly HashSet<string> loading = new();
    private static EnterResult? result;

    private sealed class Reply
    {
        public string? Token { get; set; } public string? Error { get; set; } public List<Row>? Rows { get; set; }
        public sealed class Row { public string Section { get; set; } = ""; public string Title { get; set; } = ""; public string Text { get; set; } = ""; public double Order { get; set; } }
    }

    private static string? Token(string key)
    {
        lock (gate)
        {
            if (sessionTokens.TryGetValue(key, out var t)) return t;
            return Plugin.Config.Me().SectTokens.TryGetValue(key, out var r) && r.Length > 0 ? r : null;
        }
    }

    public static bool IsUnlocked(string key) => Token(key) != null;
    public static bool IsOpenNow(string key) => Token(key) != null;
    public static bool IsBusy() { lock (gate) { return loading.Count > 0; } }

    /// <summary>The page content for a sect once the server has sent it, or null while it is still loading.</summary>
    public static List<SheetClient.SectRow>? Rows(string key) { lock (gate) { return content.TryGetValue(key, out var r) ? r : null; } }

    /// <summary>What happened to the last password try (taken once).</summary>
    public static EnterResult? TakeResult() { lock (gate) { var r = result; result = null; return r; } }

    private static void Forget(string key)
    {
        lock (gate) { sessionTokens.Remove(key); content.Remove(key); }
        if (Plugin.Config.Me().SectTokens.Remove(key)) Plugin.Config.Save();
    }

    /// <summary>Forgets everything unlocked for this game session (used when the character changes).</summary>
    public static void ClearSession() { lock (gate) { sessionTokens.Clear(); content.Clear(); lastLoad.Clear(); result = null; } }

    private static async Task<(Reply? Reply, int Status)> PostAsync(object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, CombatState.BaseUrl + "/api/sects/open") { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        using var res = await Http.SendAsync(req).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        Reply? reply = null;
        try { reply = JsonSerializer.Deserialize<Reply>(text, Json); } catch { /* not JSON */ }
        return (reply, (int)res.StatusCode);
    }

    private static List<SheetClient.SectRow> ToRows(string key, Reply r) =>
        (r.Rows ?? new()).Select(x => new SheetClient.SectRow(key, x.Section.Trim().ToLowerInvariant(), x.Title, x.Text, x.Order)).ToList();

    /// <summary>Asks the server to open a sect with its password. The server sends the pages only if it is right.</summary>
    public static async Task EnterAsync(string key, string password, bool remember)
    {
        lock (gate) { if (!loading.Add(key)) return; }
        try
        {
            var (reply, status) = await PostAsync(new { sect = key, password }).ConfigureAwait(false);
            if (status >= 200 && status < 300 && reply?.Token != null)
            {
                lock (gate)
                {
                    content[key] = ToRows(key, reply); lastLoad[key] = DateTime.UtcNow;
                    if (!remember) sessionTokens[key] = reply.Token;
                    result = new EnterResult(true, "");
                }
                if (remember) { Plugin.Config.Me().SectTokens[key] = reply.Token; Plugin.Config.Save(); }
            }
            else lock (gate) { result = new EnterResult(false, reply?.Error ?? $"The server answered {status}."); }
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Sect entry failed"); lock (gate) { result = new EnterResult(false, "Couldn't reach the server. Check your connection and try again."); } }
        finally { lock (gate) { loading.Remove(key); } }
    }

    /// <summary>Reloads an unlocked sect's pages with the saved entry, at most once per interval.</summary>
    public static void LoadIfDue(string key, int seconds)
    {
        var token = Token(key);
        if (token == null) return;
        lock (gate)
        {
            if (loading.Contains(key) || (lastLoad.TryGetValue(key, out var at) && (DateTime.UtcNow - at).TotalSeconds < seconds)) return;
            loading.Add(key); lastLoad[key] = DateTime.UtcNow;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var (reply, status) = await PostAsync(new { sect = key, token }).ConfigureAwait(false);
                if (status >= 200 && status < 300 && reply != null) lock (gate) { content[key] = ToRows(key, reply); }
                else if (status == 401) Forget(key);   // the password was changed: ask for it again
            }
            catch (Exception ex) { Plugin.Log.Warning(ex, "Could not load the sect pages."); }
            finally { lock (gate) { loading.Remove(key); } }
        });
    }
}
