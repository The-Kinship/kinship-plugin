using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class StatusView
{
    public bool IsCombined => Id.StartsWith("combat-");   // the Combat Buff / Combat Debuff totals, worked out by the server
    public string Id { get; set; } = ""; public string MemberName { get; set; } = ""; public string Name { get; set; } = "";
    public string Icon { get; set; } = ""; public string Description { get; set; } = ""; public bool IsDebuff { get; set; }
    public bool Carried { get; set; }   // from an item the person is holding: it does not count toward the six
    public int? SecondsLeft { get; set; } public bool Mine { get; set; } public bool CanRemove { get; set; } public bool Self { get; set; } public int AtkBonus { get; set; } public int HealBonus { get; set; }
    public string BonusText() { var p = new List<string>(); if (AtkBonus != 0) p.Add($"{AtkBonus:+#;-#} to attack rolls"); if (HealBonus != 0) p.Add($"{HealBonus:+#;-#} to heal rolls"); return string.Join(", ", p); }
    [JsonIgnore] public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Seconds left counted down on this computer, or null if it lasts until removed.</summary>
    public int? Remaining => SecondsLeft == null ? null : Math.Max(0, SecondsLeft.Value - (int)(DateTime.UtcNow - FetchedAt).TotalSeconds);
    public bool Expired => Remaining is 0;

    public static string Format(int? seconds)
    {
        if (seconds == null) return "until removed";
        var s = seconds.Value;
        if (s >= 86400) return $"{s / 86400}d {s % 86400 / 3600}h";
        if (s >= 3600) return $"{s / 3600}h {s % 3600 / 60}m";
        if (s >= 60) return $"{s / 60}m {s % 60}s";
        return $"{s}s";
    }
}

public sealed class StatusForm
{
    public string Name = "", Description = "", Icon = "";
    public int Minutes, Atk, Heal;
    public bool Debuff;
    public string Type = "", Cure = "";   // Advocate only: "sickness" or "curse", and what a diagnosis reveals
}
public sealed record DiagnoseResult(string Patient, int Roll, bool Success, string Condition, string Cure, string Error);
public sealed record StatusSnap(List<StatusView> All, string Error, string Notice, bool Busy);

/// <summary>Statuses (buffs and debuffs): everyone's are shown, and you can only add your own. Items and Advocates add them to others.</summary>
public sealed class StatusState
{
    public static int Max { get; private set; } = 10;   // the server says how many (it was 6 at first)
    private readonly object gate = new();
    private List<StatusView> all = new();
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastList = DateTime.MinValue;

    public List<StatusView> Mine() { lock (gate) { return all.Where(s => s.Mine && !s.Expired).ToList(); } }
    public StatusSnap Snapshot() { lock (gate) { return new StatusSnap(all.Where(s => !s.Expired).ToList(), error, notice, busy); } }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }

    public void RefreshIfDue(int seconds)
    {
        if (Idle.Asleep) return;
        bool due;
        lock (gate) { due = !busy && Plugin.Config.Me().ItemsToken.Length > 0 && (DateTime.UtcNow - lastList).TotalSeconds > seconds; if (due) lastList = DateTime.UtcNow; }
        if (due) _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/statuses", null).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var list = r.Statuses ?? new();
            foreach (var s in list) s.FetchedAt = now;
            if (r.MaxStatuses is > 0) Max = r.MaxStatuses.Value;
            lock (gate) { all = list; lastList = now; }
        }
        catch (Exception) { /* keep the list we have */ }
    }

    private async Task<bool> Exec(Func<Task<ItemReply>> call, string done)
    {
        lock (gate) { if (busy) return false; busy = true; error = ""; notice = ""; }
        try { await call().ConfigureAwait(false); lock (gate) { notice = done; } }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } return false; }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Status request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } return false; }
        finally { lock (gate) { busy = false; } }
        await RefreshAsync().ConfigureAwait(false);
        return true;
    }

    private static object Body(StatusForm f) => new { name = f.Name.Trim(), description = f.Description.Trim(), icon = f.Icon, minutes = f.Minutes, isDebuff = f.Debuff };

    // ---------- diagnosis (Life Watch and Onyx Blood Guard pages) ----------
    private DiagnoseResult? dx;
    public DiagnoseResult? TakeDiagnosis() { lock (gate) { var r = dx; dx = null; return r; } }
    public async Task DiagnoseAsync(string patient, string kind)
    {
        lock (gate) { if (busy) return; busy = true; error = ""; }
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Post, "/statuses/diagnose", new { patient, kind }).ConfigureAwait(false);
            lock (gate) { dx = new DiagnoseResult(r.Patient ?? patient, r.Roll ?? 0, r.Success == true, r.Condition ?? "", r.Cure ?? "", ""); }
        }
        catch (CombatException ex) { lock (gate) { dx = new DiagnoseResult(patient, 0, false, "", "", ex.Message); } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Diagnosis failed"); lock (gate) { dx = new DiagnoseResult(patient, 0, false, "", "", "Couldn't reach the server. Check your connection and try again."); } }
        finally { lock (gate) { busy = false; } }
    }
    public bool IsBusy() { lock (gate) { return busy; } }

    public Task<bool> AddSelfAsync(StatusForm f) => Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/statuses/self", Body(f)), "Status added.");
    public Task<bool> RemoveAsync(string id) => Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/statuses/{id}/remove", new { }), "Status removed.");

    // ---------- Advocate tools (creation password) ----------
    private Task<bool> Admin(string path, object body, string done)
    {
        var pw = Plugin.Config.CreatePassword;
        if (pw.Length == 0) { lock (gate) { error = "Enter the creation password in the SM Tools tab first."; } return Task.FromResult(false); }
        return Exec(() => ItemsState.SendAsync(HttpMethod.Post, path, body), done);
    }
    public Task<bool> AdminAddAsync(string to, StatusForm f) => Admin("/statuses/admin/add", new { adminPassword = Plugin.Config.CreatePassword, to, name = f.Name.Trim(), description = f.Description.Trim(), icon = f.Icon, minutes = f.Minutes, isDebuff = f.Debuff, statusType = f.Type, cureInfo = f.Cure.Trim(), atkBonus = f.Atk, healBonus = f.Heal }, $"Status added to {to}.");
    public Task<bool> AdminRemoveAsync(string id) => Admin("/statuses/admin/remove", new { adminPassword = Plugin.Config.CreatePassword, statusId = id }, "Status removed.");
}
