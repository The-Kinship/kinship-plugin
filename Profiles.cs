using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class ProfileField { public string Label { get; set; } = ""; public string Value { get; set; } = ""; }
public sealed class ProfileView
{
    public string Name { get; set; } = ""; public string DisplayName { get; set; } = ""; public string Portrait { get; set; } = "";
    public string Age { get; set; } = ""; public string Race { get; set; } = ""; public string Gender { get; set; } = "";
    public string Glance { get; set; } = ""; public string Ooc { get; set; } = "";
    public List<ProfileField> Fields { get; set; } = new();
}
public sealed record ProfileSnap(ProfileView? Mine, string Error, string Notice, bool Busy);

/// <summary>Every registered character's RP profile (shown when you mouse over them), plus your own for editing.</summary>
public sealed class ProfileState
{
    private readonly object gate = new();
    private List<ProfileView> all = new();
    private ProfileView? mine;
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastList = DateTime.MinValue;

    /// <summary>True when two character names are the same person: equal, or one is a longer version of the other ("Sesen" and "Sesen Silverborne").</summary>
    public static bool Same(string a, string b)
    {
        a = a.Trim().ToLowerInvariant(); b = b.Trim().ToLowerInvariant();
        if (a.Length == 0 || b.Length == 0) return false;
        return a == b || a.StartsWith(b + " ") || b.StartsWith(a + " ");
    }

    public ProfileView? Find(string name)
    {
        lock (gate)
        {
            var exact = all.FirstOrDefault(p => string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            var close = all.Where(p => Same(p.Name, name)).ToList();
            return close.Count == 1 ? close[0] : null;
        }
    }

    public ProfileSnap Snapshot() { lock (gate) { return new ProfileSnap(mine, error, notice, busy); } }
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
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/profiles", null).ConfigureAwait(false);
            lock (gate) { all = r.Profiles ?? new(); lastList = DateTime.UtcNow; }
        }
        catch (Exception) { /* keep what we have */ }
    }

    public async Task LoadMineAsync()
    {
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/profile", null).ConfigureAwait(false);
            lock (gate) { mine = r.Profile; }
        }
        catch (Exception) { lock (gate) { error = "Couldn't load your profile. Try again."; } }
    }

    private string adminError = "", adminNotice = "";
    public (string Error, string Notice) AdminMessages() { lock (gate) { return (adminError, adminNotice); } }
    /// <summary>Advocates: removes someone's portrait link (creation password).</summary>
    public async Task AdminClearPortraitAsync(string name)
    {
        if (Plugin.Config.CreatePassword.Length == 0) { lock (gate) { adminError = "Enter the creation password first."; adminNotice = ""; } return; }
        lock (gate) { adminError = ""; adminNotice = ""; }
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Post, "/profile/admin/clear-portrait", new { adminPassword = Plugin.Config.CreatePassword, name = name.Trim() }).ConfigureAwait(false);
            lock (gate) { adminNotice = "Portrait cleared for " + (r.Name ?? name) + ". Their card updates for everyone within a few minutes."; }
        }
        catch (CombatException ex) { lock (gate) { adminError = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Clear portrait failed"); lock (gate) { adminError = "Couldn't reach the server. Check your connection and try again."; } }
    }

    public async Task<bool> SaveAsync(ProfileView p)
    {
        lock (gate) { if (busy) return false; busy = true; error = ""; notice = ""; }
        try
        {
            var body = new
            {
                displayName = p.DisplayName.Trim(), portrait = p.Portrait.Trim(), age = p.Age.Trim(), race = p.Race.Trim(), gender = p.Gender.Trim(), glance = p.Glance.Trim(), ooc = p.Ooc.Trim(),
                fields = p.Fields.Where(f => f.Label.Trim().Length > 0).Select(f => new { label = f.Label.Trim(), value = f.Value.Trim() }).ToList(),
            };
            var r = await ItemsState.SendAsync(HttpMethod.Post, "/profile", body).ConfigureAwait(false);
            lock (gate) { mine = r.Profile; notice = "Profile saved."; }
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } return false; }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Profile save failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } return false; }
        finally { lock (gate) { busy = false; } }
        await RefreshAsync().ConfigureAwait(false);
        return true;
    }
}
