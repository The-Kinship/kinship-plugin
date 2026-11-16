using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class LoungeGift { public string Name { get; set; } = ""; public string Icon { get; set; } = "game:20001"; public string Description { get; set; } = ""; public string UseLabel { get; set; } = ""; public string UseText { get; set; } = ""; }
public sealed class LoungeMenuItem { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public int Price { get; set; } public string Description { get; set; } = ""; public LoungeGift? Gift { get; set; } }
public sealed class LoungeWorker { public string Name { get; set; } = ""; public string Since { get; set; } = ""; }
public sealed class LoungeMe { public string Since { get; set; } = ""; public int UnpaidMinutes { get; set; } public int UnpaidGold { get; set; } public int SoldCount { get; set; } public long SoldTotal { get; set; } }
public sealed class LoungeLine { public string Name { get; set; } = ""; public int Price { get; set; } public int Count { get; set; } public long Total { get; set; } }
public sealed class LoungeTonight { public List<LoungeLine> Items { get; set; } = new(); public int Count { get; set; } public long Total { get; set; } }
public sealed class LoungeStaff { public string Name { get; set; } = ""; public int Minutes { get; set; } public int Gold { get; set; } public bool ClockedIn { get; set; } }
public sealed class LoungeView
{
    public string Msg { get; set; } = ""; public bool CanManage { get; set; } public string Hours { get; set; } = ""; public int Rate { get; set; }
    public List<LoungeMenuItem> Menu { get; set; } = new(); public List<LoungeWorker> Working { get; set; } = new();
    public LoungeMe Me { get; set; } = new(); public LoungeTonight? Tonight { get; set; } public List<LoungeStaff>? Staff { get; set; }
}

/// <summary>The Embershade Lounge: hours, menu, clock in and out, receipts. Everything lives on the server; this keeps the last answer.</summary>
public sealed class LoungeState
{
    private readonly object gate = new();
    private LoungeView? view; private string error = "", notice = ""; private bool busy; private DateTime lastLoad = DateTime.MinValue;

    public (LoungeView? View, string Error, string Notice, bool Busy) Snapshot() { lock (gate) { return (view, error, notice, busy); } }

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
    public Task NewNightAsync() => Call(HttpMethod.Post, "/lounge/newnight", new { });

    private async Task Call(HttpMethod method, string path, object? body, bool quiet = false)
    {
        lock (gate) { if (busy) return; busy = true; if (!quiet) { error = ""; notice = ""; } }
        try
        {
            var r = await ItemsState.SendAsync(method, path, body).ConfigureAwait(false);
            lock (gate) { if (r.Lounge != null) { view = r.Lounge; if (!quiet && r.Lounge.Msg.Length > 0) notice = r.Lounge.Msg; } }
        }
        catch (CombatException ex) { if (!quiet) lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Lounge request failed"); if (!quiet) lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
    }
}
