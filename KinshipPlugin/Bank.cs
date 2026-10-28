using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class BankEntry
{
    public string? Name { get; set; } public int Delta { get; set; } public string Note { get; set; } = ""; public string At { get; set; } = "";
}
public sealed class BankMember { public string Name { get; set; } = ""; public int Gold { get; set; } }
public sealed record BankSnap(List<BankEntry> History, string Error, string Notice, bool Busy);
public sealed record BankAdminSnap(List<BankMember> Balances, long Total, List<BankEntry> Recent, string Error, string Notice, bool Busy, bool Loaded);

/// <summary>The Kinship bank: your purse of RP gold. Only people with the bank password can create or take gold.</summary>
public sealed class BankState
{
    private readonly object gate = new();
    private List<BankEntry> history = new();
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastRefresh = DateTime.MinValue;

    private List<BankMember> balances = new();
    private List<BankEntry> recent = new();
    private long total;
    private string adminError = "", adminNotice = "";
    private bool adminBusy, adminLoaded;

    public BankSnap Snapshot() { lock (gate) { return new BankSnap(history.ToList(), error, notice, busy); } }
    public BankAdminSnap AdminSnapshot() { lock (gate) { return new BankAdminSnap(balances.ToList(), total, recent.ToList(), adminError, adminNotice, adminBusy, adminLoaded); } }

    public void RefreshIfDue(int seconds)
    {
        if (Idle.Asleep) return;
        bool due;
        lock (gate) { due = !busy && Plugin.Config.Me().ItemsToken.Length > 0 && (DateTime.UtcNow - lastRefresh).TotalSeconds > seconds; if (due) lastRefresh = DateTime.UtcNow; }
        if (due) _ = RefreshAsync();
    }

    private async Task<ItemReply?> Exec(Func<Task<ItemReply>> call, string? done = null)
    {
        lock (gate) { if (busy) return null; busy = true; error = ""; notice = ""; }
        try
        {
            var r = await call().ConfigureAwait(false);
            if (r.Gold != null) Plugin.Items.SetGold(r.Gold.Value);
            lock (gate) { if (r.History != null) history = r.History; if (done != null) notice = done; lastRefresh = DateTime.UtcNow; }
            return r;
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Bank request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
        return null;
    }

    public Task RefreshAsync() => Exec(() => ItemsState.SendAsync(HttpMethod.Get, "/bank/me", null));

    public async Task PayAsync(string to, int amount)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/bank/pay", new { to, amount }), $"Paid {amount:N0} gold to {to}.").ConfigureAwait(false);
        _ = r;
    }

    // ---------- bank tools (bank password) ----------
    private async Task<ItemReply?> RunAdmin(Func<string, Task<ItemReply>> action, string? done = null)
    {
        var pw = Plugin.Config.BankPassword;
        lock (gate) { if (adminBusy) return null; adminBusy = true; adminError = ""; adminNotice = ""; }
        try
        {
            if (pw.Length == 0) throw new CombatException("Enter the bank password first.", 400);
            var r = await action(pw).ConfigureAwait(false);
            lock (gate) { if (done != null) adminNotice = done; }
            return r;
        }
        catch (CombatException ex) { lock (gate) { adminError = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Bank tools request failed"); lock (gate) { adminError = "Couldn't reach the server. Try again."; } }
        finally { lock (gate) { adminBusy = false; } }
        return null;
    }

    public async Task AdminLoadAsync()
    {
        var r = await RunAdmin(pw => ItemsState.SendAsync(HttpMethod.Post, "/bank/admin/list", new { bankPassword = pw })).ConfigureAwait(false);
        if (r != null) lock (gate) { balances = r.Balances ?? new(); recent = r.Recent ?? new(); total = r.Total ?? 0; adminLoaded = true; }
    }

    public async Task<bool> MintAsync(string to, int amount, string note)
    {
        var r = await RunAdmin(pw => ItemsState.SendAsync(HttpMethod.Post, "/bank/admin/mint", new { bankPassword = pw, to, amount, note }), $"Gave {amount:N0} gold to {to}.").ConfigureAwait(false);
        if (r == null) return false;
        await AdminLoadAsync().ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<bool> TakeAsync(string from, int amount, string note)
    {
        var r = await RunAdmin(pw => ItemsState.SendAsync(HttpMethod.Post, "/bank/admin/take", new { bankPassword = pw, to = from, amount, note }), $"Took {amount:N0} gold from {from}.").ConfigureAwait(false);
        if (r == null) return false;
        await AdminLoadAsync().ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
        return true;
    }
}
