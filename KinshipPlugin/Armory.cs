using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class ArmorySlot { public string Slot { get; set; } = ""; public ItemView? Item { get; set; } }
public sealed class ArmoryBonus { public int Atk { get; set; } public int Heal { get; set; } public bool Capped { get; set; } }
public sealed record ArmorySnap(List<ArmorySlot> Slots, ArmoryBonus Bonus, string Error, string Notice, bool Busy);

/// <summary>Your armory: what you are wearing. Worn gear is out of your bag; its bonuses count in rolls (all gear together is capped at +5).</summary>
public sealed class ArmoryState
{
    private readonly object gate = new();
    private List<ArmorySlot> slots = new();
    private ArmoryBonus bonus = new();
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastList = DateTime.MinValue, lastPoll = DateTime.MinValue;

    public static readonly (string Slot, string Label)[] Layout =
    {
        ("helm", "Head"), ("necklace", "Necklace"), ("cloak", "Cloak"), ("chest", "Chest"), ("weapon", "Weapon"),
        ("legs", "Legs"), ("feet", "Feet"), ("ring1", "Ring"), ("ring2", "Ring"),
    };
    public static string SlotName(string slot) => slot switch { "helm" => "head", "chest" => "chest", "cloak" => "cloak", "legs" => "legs", "feet" => "feet", "ring" => "ring", "necklace" => "necklace", "weapon" => "weapon", _ => slot };

    public ArmorySnap Snapshot() { lock (gate) { return new ArmorySnap(slots, bonus, error, notice, busy); } }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }

    public void RefreshIfDue(int seconds)
    {
        bool due;
        lock (gate) { due = !busy && Plugin.Config.Me().ItemsToken.Length > 0 && (DateTime.UtcNow - lastList).TotalSeconds > seconds; if (due) lastList = DateTime.UtcNow; }
        if (due) _ = RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/armory", null).ConfigureAwait(false);
            lock (gate) { slots = r.Slots ?? new(); bonus = r.Bonus ?? new(); lastList = DateTime.UtcNow; }
        }
        catch (Exception) { /* keep what we have */ }
    }

    /// <summary>About once a minute, asks whether any of your gear broke, and tells you with a popup.</summary>
    public void Poll()
    {
        bool due;
        lock (gate) { due = Plugin.Config.Me().ItemsToken.Length > 0 && (DateTime.UtcNow - lastPoll).TotalSeconds > 60; if (due) lastPoll = DateTime.UtcNow; }
        if (due) _ = PollAsync();
    }
    private async Task PollAsync()
    {
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/armory/check", null).ConfigureAwait(false);
            if (r.Broke is { Count: > 0 })
            {
                foreach (var text in r.Broke) Plugin.Scene.ShowLocal(text);
                _ = RefreshAsync(); _ = Plugin.Items.RefreshAsync(true);
            }
        }
        catch (Exception) { /* try again next minute */ }
    }

    private async Task<ItemReply?> Exec(Func<Task<ItemReply>> call, string? done = null)
    {
        lock (gate) { if (busy) return null; busy = true; error = ""; notice = ""; }
        try
        {
            var r = await call().ConfigureAwait(false);
            lock (gate) { if (r.Slots != null) slots = r.Slots; if (r.Bonus != null) bonus = r.Bonus; if (done != null) notice = done; lastList = DateTime.UtcNow; }
            if (r.Items != null) Plugin.Items.SetInventory(r.Items);
            return r;
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Armory request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
        return null;
    }

    public Task EquipAsync(string itemId, string? slot = null) => Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/armory/equip", new { itemId, slot }));
    public Task UnequipAsync(string slot) => Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/armory/unequip", new { slot }));
}
