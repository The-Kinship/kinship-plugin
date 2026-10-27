using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class ReportView
{
    public long Id { get; set; } public string Member { get; set; } = ""; public string Kind { get; set; } = ""; public string About { get; set; } = "";
    public string Text { get; set; } = ""; public string CreatedAt { get; set; } = ""; public bool Done { get; set; }
}

/// <summary>Reports about bugs or abuse: members send them, SMs (with the creation password) read them.</summary>
public sealed class ReportState
{
    public static readonly ReportState I = new();
    private readonly object gate = new();
    private string error = "", notice = "", adminError = "", adminNotice = "";
    private bool busy, adminBusy, adminLoaded;
    private List<ReportView> list = new();

    public (string Error, string Notice, bool Busy) Snapshot() { lock (gate) { return (error, notice, busy); } }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }
    public (List<ReportView> List, string Error, string Notice, bool Busy, bool Loaded) Admin() { lock (gate) { return (list.ToList(), adminError, adminNotice, adminBusy, adminLoaded); } }

    public async Task<bool> SendAsync(string kind, string about, string text)
    {
        lock (gate) { if (busy) return false; busy = true; error = ""; notice = ""; }
        try
        {
            await ItemsState.SendAsync(HttpMethod.Post, "/reports", new { kind, about = about.Trim(), text = text.Trim() }).ConfigureAwait(false);
            lock (gate) { notice = "Sent. Thank you, the SMs will take a look."; }
            return true;
        }
        catch (CombatException ex) { lock (gate) { error = ex.Status == 401 ? "Sign in on the RP tab first so the SMs know who sent this." : ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Report failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
        return false;
    }

    private async Task AdminCall(string path, object body, string? done = null)
    {
        lock (gate) { if (adminBusy) return; adminBusy = true; adminError = ""; adminNotice = ""; }
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Post, path, body).ConfigureAwait(false);
            if (r.Reports != null) lock (gate) { list = r.Reports; adminLoaded = true; }
            if (done != null) lock (gate) { adminNotice = done; }
        }
        catch (CombatException ex) { lock (gate) { adminError = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Report admin failed"); lock (gate) { adminError = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { adminBusy = false; } }
    }

    private bool HavePassword() { if (Plugin.Config.CreatePassword.Length > 0) return true; lock (gate) { adminError = "Enter the creation password first."; } return false; }
    public async Task AdminLoadAsync() { if (HavePassword()) await AdminCall("/reports/admin/list", new { adminPassword = Plugin.Config.CreatePassword }); }
    public async Task AdminDoneAsync(long id) { if (!HavePassword()) return; await AdminCall("/reports/admin/done", new { adminPassword = Plugin.Config.CreatePassword, id }); await AdminLoadAsync(); }
    public async Task AdminDeleteAsync(long id) { if (!HavePassword()) return; await AdminCall("/reports/admin/delete", new { adminPassword = Plugin.Config.CreatePassword, id }, "Report deleted."); await AdminLoadAsync(); }
}
