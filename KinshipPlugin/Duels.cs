using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class DuelView
{
    public string Id { get; set; } = ""; public string Status { get; set; } = ""; public bool Challenger { get; set; }
    public string Foe { get; set; } = ""; public string You { get; set; } = "";
    public int YouHp { get; set; } public int FoeHp { get; set; } public int Max { get; set; } public int HealsLeft { get; set; }
    public bool YourTurn { get; set; } public List<string> Log { get; set; } = new(); public bool? YouWon { get; set; }
}

/// <summary>Pick a fight: challenge another player, and if they accept, take turns pressing attack or heal. Nothing happens by itself.</summary>
public sealed class DuelState
{
    private readonly object gate = new();
    private DuelView? duel;
    private string error = "", shownKey = "", dismissedKey = "";
    private bool busy;
    private DateTime lastPoll = DateTime.MinValue;

    public (DuelView? Duel, string Error, bool Busy) Snapshot() { lock (gate) { return (duel, error, busy); } }
    /// <summary>True when there is something to show (and the player hasn't closed it).</summary>
    public bool Visible() { lock (gate) { return duel != null && Key(duel) != dismissedKey; } }
    private static string Key(DuelView d) => d.Id + ":" + d.Status;
    public void Dismiss() { lock (gate) { if (duel != null) dismissedKey = Key(duel); } }
    public void ClearError() { lock (gate) { error = ""; } }
    public void ClearDismissed() { lock (gate) { dismissedKey = ""; } }

    /// <summary>Checks for a challenge or the other player's move: every few seconds, faster during a fight.</summary>
    public void Poll()
    {
        bool due;
        lock (gate)
        {
            var fast = duel != null && (duel.Status == "active" || duel.Status == "pending");
            due = !busy && Plugin.Config.Me().ItemsToken.Length > 0 && (DateTime.UtcNow - lastPoll).TotalSeconds > (fast ? 2 : 8);
            if (due) lastPoll = DateTime.UtcNow;
        }
        if (due) _ = PollAsync();
    }
    private async Task PollAsync()
    {
        try { var r = await ItemsState.SendAsync(HttpMethod.Get, "/duels/mine", null).ConfigureAwait(false); Apply(r.Duel); }
        catch (Exception) { /* try again soon */ }
    }

    private void Apply(DuelView? d)
    {
        bool open = false, challenged = false;
        lock (gate)
        {
            duel = d;
            if (d != null && Key(d) != dismissedKey && Key(d) != shownKey)
            {
                shownKey = Key(d); open = true;
                challenged = d.Status == "pending" && !d.Challenger;
            }
        }
        if (open) { Plugin.ShowDuel(); if (challenged) _ = Plugin.Framework.RunOnFrameworkThread(() => Plugin.Chat.Print($"[Kinship] {d!.Foe} wants to pick a fight with you!")); }
        if (d != null && (d.Status == "done")) _ = Plugin.Status.RefreshAsync();   // the won/lost status shows up straight away
    }

    private async Task Exec(Func<Task<ItemReply>> call)
    {
        lock (gate) { if (busy) return; busy = true; error = ""; }
        try { var r = await call().ConfigureAwait(false); Apply(r.Duel); lock (gate) { lastPoll = DateTime.UtcNow; } }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Fight request failed"); lock (gate) { error = "Couldn't reach the server. Try again."; } }
        finally { lock (gate) { busy = false; } }
    }

    public Task ChallengeAsync(string target) { lock (gate) { dismissedKey = ""; } return Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/duels", new { target })); }
    public Task AcceptAsync(string id) => Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/duels/{id}/accept", new { }));
    public Task RefuseAsync(string id) => Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/duels/{id}/refuse", new { }));
    public Task CancelAsync(string id) => Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/duels/{id}/cancel", new { }));
    public Task ForfeitAsync(string id) => Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/duels/{id}/forfeit", new { }));
    public Task ActAsync(string id, string kind) => Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/duels/{id}/act", new { kind }));
}
