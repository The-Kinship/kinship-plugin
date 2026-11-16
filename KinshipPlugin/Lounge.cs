using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class LoungeGift { public string Name { get; set; } = ""; public string Icon { get; set; } = "game:20001"; public string Description { get; set; } = ""; public string UseLabel { get; set; } = ""; public string UseText { get; set; } = ""; }
public sealed class LoungeMenuItem { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public int Price { get; set; } public string Description { get; set; } = ""; public LoungeGift? Gift { get; set; } }
public sealed class LoungeWorker { public string Name { get; set; } = ""; public string Since { get; set; } = ""; public List<string> Roles { get; set; } = new(); }
public sealed class LoungeStaffRoles { public string Name { get; set; } = ""; public List<string> Roles { get; set; } = new(); }
public sealed class LoungePing { public string Id { get; set; } = ""; public string Kind { get; set; } = ""; public string Text { get; set; } = ""; public string At { get; set; } = ""; public string Responder { get; set; } = ""; }
public sealed class LoungeMe { public string Since { get; set; } = ""; public int UnpaidMinutes { get; set; } public int UnpaidGold { get; set; } public int SoldCount { get; set; } public long SoldTotal { get; set; } }
public sealed class LoungeBy { public string Name { get; set; } = ""; public int Count { get; set; } }
public sealed class LoungeLine { public string Name { get; set; } = ""; public int Price { get; set; } public int Count { get; set; } public long Total { get; set; } public List<LoungeBy> By { get; set; } = new(); }
public sealed class LoungePerson { public string Name { get; set; } = ""; public int Count { get; set; } public long Total { get; set; } public int Minutes { get; set; } public long ServiceTotal { get; set; } }
public sealed class LoungeService { public string Id { get; set; } = ""; public string Who { get; set; } = ""; public string Name { get; set; } = ""; public int Price { get; set; } }
public sealed class LoungeSession { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Customer { get; set; } = ""; public int Rate { get; set; } public int Minutes { get; set; } public string StartedAt { get; set; } = ""; public string EndsAt { get; set; } = ""; }
public sealed class LoungeActive { public string Who { get; set; } = ""; public string Name { get; set; } = ""; public string Customer { get; set; } = ""; public string EndsAt { get; set; } = ""; }
public sealed class LoungeServiceLine { public string Who { get; set; } = ""; public string Name { get; set; } = ""; public string Customer { get; set; } = ""; public int Rate { get; set; } public int Minutes { get; set; } public long Total { get; set; } }
public static class Dur
{
    public static string Of(int m) => m < 60 ? $"{m} minutes" : m % 60 == 0 ? (m / 60 == 1 ? "1 hour" : $"{m / 60} hours") : $"{m / 60} hr {m % 60} min";
}
/// <summary>The little receipt for a finished service session.</summary>
public sealed class LoungeReceipt
{
    public string Staff { get; set; } = ""; public string Service { get; set; } = ""; public string Customer { get; set; } = ""; public int Rate { get; set; } public int Minutes { get; set; } public long Total { get; set; } public string ClosedAt { get; set; } = "";
    public string ToText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Embershade Lounge service receipt");
        sb.AppendLine($"Staff: {Staff}");
        sb.AppendLine($"Service: {Service}");
        if (Customer.Length > 0) sb.AppendLine($"Customer: {Customer}");
        sb.AppendLine($"Rate: {Rate:N0} gil per hour");
        sb.AppendLine($"Time: {Dur.Of(Minutes)}");
        sb.Append($"Total due: {Total:N0} gil");
        return sb.ToString();
    }
    public string OneLine() => $"[Kinship] Receipt: {Staff}, {Service}{(Customer.Length > 0 ? " for " + Customer : "")}, {Rate:N0} gil per hour x {Dur.Of(Minutes)} = {Total:N0} gil due.";
}
public sealed class LoungePf { public string Text { get; set; } = ""; public int Minutes { get; set; } = 55; public string PostedAt { get; set; } = ""; public string PostedBy { get; set; } = ""; }
public sealed class LoungeNightRow { public string Id { get; set; } = ""; public string ClosedAt { get; set; } = ""; public int Count { get; set; } public long Total { get; set; } }
/// <summary>A closed night, as saved on the server. ToText() makes the plain text receipt with dates in the viewer's own time zone.</summary>
public sealed class LoungeNight
{
    public string NightStart { get; set; } = ""; public string ClosedAt { get; set; } = ""; public string ClosedBy { get; set; } = "";
    public List<LoungeLine> Items { get; set; } = new(); public List<LoungePerson> People { get; set; } = new(); public int Count { get; set; } public long Total { get; set; }
    public List<LoungeServiceLine> Services { get; set; } = new(); public long ServiceTotal { get; set; } public long Grand { get; set; }
    private static string Fmt(string iso) => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("ddd MMM d yyyy, h:mm tt") : iso;
    public DateTime ClosedLocal() => DateTime.TryParse(ClosedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime() : DateTime.Now;
    public string ToText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Embershade Lounge receipt book");
        sb.AppendLine($"Night started: {Fmt(NightStart)}");
        sb.AppendLine($"Closed: {Fmt(ClosedAt)} by {ClosedBy}");
        sb.AppendLine();
        sb.AppendLine("SOLD");
        if (Items.Count == 0) sb.AppendLine("  Nothing was sold.");
        foreach (var l in Items)
        {
            sb.AppendLine($"  {l.Count} x {l.Name} = {l.Total:N0} gil");
            if (l.By.Count > 0) sb.AppendLine("      " + string.Join(", ", l.By.Select(b => $"{b.Name} sold {b.Count}")));
        }
        sb.AppendLine();
        sb.AppendLine($"ITEMS TOTAL: {Count} sold, {Total:N0} gil");
        if (Services.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("SERVICES");
            foreach (var x in Services) sb.AppendLine($"  {x.Who}: {x.Name}{(x.Customer.Length > 0 ? " for " + x.Customer : "")}, {Dur.Of(x.Minutes)} at {x.Rate:N0} gil per hour = {x.Total:N0} gil");
            sb.AppendLine($"SERVICES TOTAL: {Services.Count} booked, {ServiceTotal:N0} gil");
        }
        sb.AppendLine();
        sb.AppendLine($"GRAND TOTAL: {(Grand > 0 || Services.Count > 0 ? Grand : Total):N0} gil");
        sb.AppendLine();
        sb.AppendLine("BY PERSON");
        foreach (var p in People) sb.AppendLine($"  {p.Name}: {p.Count} sold, {p.Total:N0} gil" + (p.ServiceTotal > 0 ? $", services {p.ServiceTotal:N0} gil" : "") + $", on shift {p.Minutes} min");
        return sb.ToString();
    }
}
public sealed class LoungeTonight { public List<LoungeLine> Items { get; set; } = new(); public int Count { get; set; } public long Total { get; set; } public List<LoungeServiceLine> Services { get; set; } = new(); public long ServiceTotal { get; set; } public long Grand { get; set; } }
public sealed class LoungeStaff { public string Name { get; set; } = ""; public int Minutes { get; set; } public int Gold { get; set; } public bool ClockedIn { get; set; } }
public sealed class LoungeVenue { public string Room { get; set; } = ""; public float X { get; set; } public float Y { get; set; } public float Z { get; set; } public int Radius { get; set; } = 40; public string Label { get; set; } = ""; }
public sealed class LoungePatron { public string Name { get; set; } = ""; public string ArrivedAt { get; set; } = ""; public string Helper { get; set; } = ""; public bool Mine { get; set; } public string HelpedAt { get; set; } = ""; public string HelpedBy { get; set; } = ""; public bool Manual { get; set; } }
public sealed class LoungeRoster { public string Name { get; set; } = ""; public string Status { get; set; } = ""; }
public sealed class LoungePlan { public string Id { get; set; } = ""; public string StartsAt { get; set; } = ""; public string EndsAt { get; set; } = ""; public string Note { get; set; } = ""; public List<LoungeRoster> Staff { get; set; } = new(); public string Mine { get; set; } = ""; }
public sealed class LoungeView
{
    public List<LoungePing> Calls { get; set; } = new(); public bool CanClearCalls { get; set; }
    public List<LoungeStaffRoles> Roles { get; set; } = new(); public List<LoungePing> Pings { get; set; } = new();
    public bool NightOpen { get; set; } = true; public string AdText { get; set; } = ""; public LoungeVenue? Venue { get; set; } public List<LoungePatron> Patrons { get; set; } = new(); public List<LoungePlan> Schedule { get; set; } = new();
    public string Msg { get; set; } = ""; public bool CanManage { get; set; } public string Hours { get; set; } = ""; public int Rate { get; set; }
    public List<LoungeMenuItem> Menu { get; set; } = new(); public List<LoungeWorker> Working { get; set; } = new();
    public LoungeMe Me { get; set; } = new(); public LoungeTonight? Tonight { get; set; } public List<LoungeStaff>? Staff { get; set; }
    public LoungePf Pf { get; set; } = new(); public List<LoungeNightRow>? Nights { get; set; }
    public List<LoungeService> Services { get; set; } = new(); public LoungeSession? Session { get; set; } public List<LoungeActive> Sessions { get; set; } = new();
}

/// <summary>The Embershade Lounge: hours, menu, clock in and out, receipts. Everything lives on the server; this keeps the last answer.</summary>
public sealed class LoungeState
{
    private readonly object gate = new();
    private LoungeView? view; private string error = "", notice = ""; private bool busy, loud; private DateTime lastLoad = DateTime.MinValue, lastTick = DateTime.MinValue;
    private LoungeNight? openNight; private string announced = "";

    public LoungeNight? OpenNight() { lock (gate) { return openNight; } }
    public void CloseNight() { lock (gate) { openNight = null; } }

    /// <summary>Runs about once a second. If you were the one who posted the Party Finder listing, tells you in chat when it is time to refresh it.</summary>
    public void Tick() { PfTick(); ReportPatrons(); bool on; lock (gate) { on = view?.Me.Since.Length > 0; } if (on) RefreshIfDue(10); }   // clocked in: check for calls every few seconds

    // Staff who are clocked in and standing at the venue tell the server who is nearby. Only names are sent; it is read-only and nothing is sent to the game.
    private DateTime lastReport = DateTime.MinValue;
    private void ReportPatrons()
    {
        try
        {
            if (Idle.Asleep || (DateTime.UtcNow - lastReport).TotalSeconds < 8) return;
            LoungeView? v; bool b;
            lock (gate) { v = view; b = busy; }
            if (b || v == null || v.Venue == null || v.Venue.Room.Length == 0 || v.Me.Since.Length == 0) return;
            if (WorldInfo.CurrentRoom != v.Venue.Room) return;
            var me = Plugin.ObjectTable.LocalPlayer;
            if (me == null) return;
            var centre = new Vector3(v.Venue.X, v.Venue.Y, v.Venue.Z);
            var names = new List<string>();
            foreach (var o in Plugin.ObjectTable)
            {
                if (o is not IPlayerCharacter pc || pc.Address == me.Address) continue;
                if (Vector3.Distance(pc.Position, centre) > v.Venue.Radius) continue;
                var n = pc.Name.ToString();
                if (n.Length > 0) names.Add(n);
                if (names.Count >= 60) break;
            }
            lastReport = DateTime.UtcNow;
            _ = Call(HttpMethod.Post, "/lounge/patrons/report", new { names }, quiet: true);
        }
        catch (Exception) { }
    }

    private void PfTick()
    {
        lock (gate)
        {
            if ((DateTime.UtcNow - lastTick).TotalSeconds < 5) return;
            lastTick = DateTime.UtcNow;
            var pf = view?.Pf;
            if (pf == null || pf.PostedAt.Length == 0 || pf.PostedAt == announced || !string.Equals(pf.PostedBy, Plugin.Config.Me().ItemsName, StringComparison.OrdinalIgnoreCase)) return;
            if (!DateTime.TryParse(pf.PostedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)) return;
            if ((DateTime.UtcNow - at.ToUniversalTime()).TotalMinutes < pf.Minutes) return;
            announced = pf.PostedAt; pfAlert = true;
        }
    }

    // ---- on-screen alerts: the Party Finder refresh reminder and "time is up" for a service session
    private bool pfAlert; private DateTime snoozeUntil = DateTime.MinValue; private LoungeReceipt? lastReceipt;
    public (bool Pf, LoungeSession? Session) Alerts()
    {
        lock (gate)
        {
            LoungeSession? due = null;
            var ses = view?.Session;
            if (ses != null && DateTime.UtcNow >= snoozeUntil && DateTime.TryParse(ses.EndsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var end) && end.ToUniversalTime() <= DateTime.UtcNow) due = ses;
            return (pfAlert, due);
        }
    }
    public void DismissPf() { lock (gate) { pfAlert = false; } }
    public void SnoozeSession() { lock (gate) { snoozeUntil = DateTime.UtcNow.AddMinutes(5); } }
    public LoungeReceipt? LastReceipt() { lock (gate) { return lastReceipt; } }
    public void DismissReceipt() { lock (gate) { lastReceipt = null; } }

    public (LoungeView? View, string Error, string Notice, bool Busy) Snapshot() { lock (gate) { return (view, error, notice, busy && loud); } }   // background checks do not show "Working..."

    public void RefreshIfDue(int seconds)
    {
        if (Idle.Asleep) return;
        bool due;
        lock (gate) { due = !busy && Plugin.Config.Me().ItemsToken.Length > 0 && (DateTime.UtcNow - lastLoad).TotalSeconds > seconds; if (due) lastLoad = DateTime.UtcNow; }
        if (due) _ = Call(HttpMethod.Get, "/lounge", null, quiet: true);
    }
    public Task Refresh() => Call(HttpMethod.Get, "/lounge", null);
    public Task ClockAsync(bool on) => Call(HttpMethod.Post, "/lounge/clock", new { @in = on });
    public Task SellAsync(string menuId, int qty, string to) => Call(HttpMethod.Post, "/lounge/sell", new { menuId, qty, to });
    public Task UndoAsync() => Call(HttpMethod.Post, "/lounge/undo", new { });
    public Task SaveInfoAsync(string hours, int rate) => Call(HttpMethod.Post, "/lounge/info", new { hours, rate });
    public Task SaveMenuAsync(string? id, string name, int price, string description, LoungeGift? gift) =>
        Call(HttpMethod.Post, "/lounge/menu/save", new { id, name, price, description, gift = gift != null && gift.Name.Trim().Length > 0 ? new { name = gift.Name.Trim(), icon = gift.Icon, description = gift.Description.Trim(), useLabel = gift.UseLabel.Trim(), useText = gift.UseText.Trim() } : null });
    public Task DeleteMenuAsync(string id) => Call(HttpMethod.Post, "/lounge/menu/delete", new { id });
    public Task ClockOutAsync(string name) => Call(HttpMethod.Post, "/lounge/clockout", new { name });
    public Task PayAsync(string name) => Call(HttpMethod.Post, "/lounge/pay", new { name });
    public Task CloseNightAsync() => Call(HttpMethod.Post, "/lounge/closenight", new { });
    public Task StartNightAsync() => Call(HttpMethod.Post, "/lounge/startnight", new { });
    public Task SavePfAsync(string text, int minutes) => Call(HttpMethod.Post, "/lounge/pf/save", new { text, minutes });
    public Task PostedAsync() => Call(HttpMethod.Post, "/lounge/pf/posted", new { });
    public Task ViewNightAsync(string id) => Call(HttpMethod.Post, "/lounge/night", new { id });
    public Task SaveServiceAsync(string? id, string name, int price) => Call(HttpMethod.Post, "/lounge/service/save", new { id, name, price });
    public Task DeleteServiceAsync(string id) => Call(HttpMethod.Post, "/lounge/service/delete", new { id });
    public Task StartSessionAsync(string serviceId, int minutes, string customer) => Call(HttpMethod.Post, "/lounge/session/start", new { serviceId, minutes, customer });
    public Task ExtendAsync(int minutes) => Call(HttpMethod.Post, "/lounge/session/extend", new { minutes });
    public static readonly string[] Positions = { "Manager", "Security", "Bartender", "Server", "Host", "Entertainer", "Cook", "Other" };
    public Task SetRolesAsync(string name, IEnumerable<string> roles) => Call(HttpMethod.Post, "/lounge/roles/set", new { name, roles = roles.ToArray() });
    public Task CallManagerAsync(bool ic) => Call(HttpMethod.Post, "/lounge/call/manager", new { ic = ic ? "IC" : "OOC" });
    public Task CallSecurityAsync() => Call(HttpMethod.Post, "/lounge/call/security", new { });
    public Task CallBanAsync(string patron, string reason) => Call(HttpMethod.Post, "/lounge/call/ban", new { patron, reason });
    public Task RespondAsync(string id) { lock (gate) { dismissed.Add(id); } return Call(HttpMethod.Post, "/lounge/call/respond", new { id }); }
    public Task ClearCallAsync(string id) => Call(HttpMethod.Post, "/lounge/call/clear", new { id });
    public Task DismissPingAsync(string id) { lock (gate) { dismissed.Add(id); } return Call(HttpMethod.Post, "/lounge/call/dismiss", new { id }); }
    private readonly HashSet<string> dismissed = new();
    public List<LoungePing> Pings() { lock (gate) { return (view?.Pings ?? new()).Where(x => !dismissed.Contains(x.Id)).ToList(); } }
    public Task PatronHelpAsync(string name) => Call(HttpMethod.Post, "/lounge/patrons/help", new { name });
    public Task PatronUnhelpAsync(string name) => Call(HttpMethod.Post, "/lounge/patrons/unhelp", new { name });
    public Task PatronDoneAsync(string name) => Call(HttpMethod.Post, "/lounge/patrons/done", new { name });
    public Task PatronAddAsync(string name) => Call(HttpMethod.Post, "/lounge/patrons/add", new { name });
    public Task PatronRemoveAsync(string name) => Call(HttpMethod.Post, "/lounge/patrons/remove", new { name });
    public Task SetVenueAsync(string label, int radius)
    {
        var p = WorldInfo.PlayerPos(); var room = WorldInfo.CurrentRoom;
        if (p == null || room.Length == 0) { lock (gate) { error = "You need to be standing in a housing ward or inside a house to set the venue."; } return Task.CompletedTask; }
        return Call(HttpMethod.Post, "/lounge/venue/set", new { room, x = p.Value.X, y = p.Value.Y, z = p.Value.Z, radius, label });
    }
    public Task ClearVenueAsync() => Call(HttpMethod.Post, "/lounge/venue/clear", new { });
    public Task SaveAdAsync(string text) => Call(HttpMethod.Post, "/lounge/ad/save", new { text });
    public Task SaveNightAsync(string? id, string startsAt, string endsAt, string note) => Call(HttpMethod.Post, "/lounge/schedule/save", new { id, startsAt, endsAt, note });
    public Task DeleteNightAsync(string id) => Call(HttpMethod.Post, "/lounge/schedule/delete", new { id });
    public Task RsvpAsync(string nightId, bool on) => Call(HttpMethod.Post, "/lounge/schedule/rsvp", new { nightId, on });
    public Task SetRosterAsync(string nightId, string name, string status) => Call(HttpMethod.Post, "/lounge/roster/set", new { nightId, name, status });
    public Task EndSessionAsync(string? name = null) => Call(HttpMethod.Post, "/lounge/session/end", new { name });

    private async Task Call(HttpMethod method, string path, object? body, bool quiet = false)
    {
        lock (gate) { if (busy) return; busy = true; loud = !quiet; if (!quiet) { error = ""; notice = ""; } }
        try
        {
            var r = await ItemsState.SendAsync(method, path, body).ConfigureAwait(false);
            string saved = "";
            if (r.Night != null && path == "/lounge/closenight") saved = SaveReceipt(r.Night);
            lock (gate)
            {
                if (r.Lounge != null) { view = r.Lounge; if (!quiet && r.Lounge.Msg.Length > 0) notice = r.Lounge.Msg + saved; }
                if (r.Night != null) openNight = r.Night;
                if (r.Receipt != null) { lastReceipt = r.Receipt; snoozeUntil = DateTime.MinValue; }
            }
            if (r.Receipt != null) { var line = r.Receipt.OneLine(); _ = Plugin.Framework.RunOnFrameworkThread(() => Plugin.Chat.Print(line)); }
        }
        catch (CombatException ex) { if (!quiet) lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Lounge request failed"); if (!quiet) lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
    }

    /// <summary>Writes the closed night as a plain text file in the plugin's own folder on this computer. The server also keeps it under Past nights.</summary>
    private static string SaveReceipt(LoungeNight n)
    {
        try
        {
            var dir = System.IO.Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "Receipts");
            System.IO.Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, $"Receipt-{n.ClosedLocal():yyyy-MM-dd-HHmm}.txt");
            System.IO.File.WriteAllText(file, n.ToText());
            return " A text copy was saved to: " + file;
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Could not save the receipt file"); return " (Could not save a text file on this computer.)"; }
    }
}
