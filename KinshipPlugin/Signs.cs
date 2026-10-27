using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace KinshipPlugin;

public sealed class SignView
{
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Zone { get; set; } = "";
    public int Territory { get; set; } public uint Map { get; set; }
    public float X { get; set; } public float Y { get; set; } public float Z { get; set; }
    public float MapX { get; set; } public float MapY { get; set; }
    public string OwnerName { get; set; } = ""; public bool Mine { get; set; }
    public string? Owner { get; set; }          // only filled in by the Advocate list
    public string? Description { get; set; }   // only comes with a single sign, not the list
    public int Gold { get; set; }               // likewise: gold left at the sign shows only once you read it

    public float? Distance()
    {
        var p = WorldInfo.PlayerPos();
        if (p == null || Plugin.ClientState.TerritoryType != Territory) return null;
        return Vector3.Distance(p.Value, new Vector3(X, Y, Z));
    }
    public bool Near() => Distance() is { } d && d <= SignState.ReadRange;
}

public sealed record SignSnap(List<SignView> Signs, List<ZoneCount> Zones, SignView? Reading, string Error, string Notice, bool Busy);

/// <summary>Signs and markers: notes left at a spot in the world. The list lives on your server.</summary>
public sealed class SignState
{
    public const float ReadRange = 10f;    // how close you must be to look from the list in the tab
    public const float PopupRange = 1f;    // how close before the pop-up appears (right next to it)
    public const float PopupStay = 2.5f;    // once it is up, it stays until you are this far, so it doesn't flicker
    public const float LabelRange = 40f;   // how close before its name tag shows

    private readonly object gate = new();
    private List<SignView> signs = new();
    private List<ZoneCount> zones = new();
    private SignView? reading;
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastList = DateTime.MinValue;

    public List<SignView> All { get { lock (gate) { return signs; } } }   // never edited in place, only replaced
    public SignView? Reading { get { lock (gate) { return reading; } } }
    public SignSnap Snapshot() { lock (gate) { return new SignSnap(signs, zones, reading, error, notice, busy); } }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }
    public void CloseReading() { lock (gate) { reading = null; } }

    private async Task<ItemReply?> Exec(Func<Task<ItemReply>> call, string? done = null)
    {
        lock (gate) { if (busy) return null; busy = true; error = ""; notice = ""; }
        try
        {
            var r = await call().ConfigureAwait(false);
            lock (gate) { if (done != null) notice = done; }
            return r;
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Sign request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
        return null;
    }

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
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/signs", null).ConfigureAwait(false);
            lock (gate) { signs = r.Signs ?? new(); zones = r.Zones ?? new(); lastList = DateTime.UtcNow; if (reading != null && signs.All(s => s.Id != reading.Id)) reading = null; }
        }
        catch (Exception) { /* keep the list we have */ }
    }

    /// <summary>Fetches one sign with its full text, without opening the reader.</summary>
    public async Task<SignView?> FetchAsync(string id) => (await Exec(() => ItemsState.SendAsync(HttpMethod.Get, "/signs/" + id, null)).ConfigureAwait(false))?.Sign;

    public async Task ReadAsync(string id)
    {
        var s = await FetchAsync(id).ConfigureAwait(false);
        if (s != null) lock (gate) { reading = s; }
    }

    public async Task CreateAsync(string name, string text, Spot s, int gold = 0)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/signs", new
        {
            name = name.Trim(), description = text.Trim(), territory = (int)s.Territory, map = (int)s.Map, x = s.Pos.X, y = s.Pos.Y, z = s.Pos.Z, zone = s.Zone, mapX = s.MapX, mapY = s.MapY, gold
        }), gold > 0 ? $"Sign made, with {gold:N0} gold left under it." : "Sign made. It stays right here.").ConfigureAwait(false);
        if (r != null) { await RefreshAsync().ConfigureAwait(false); if (gold > 0) _ = Plugin.Bank.RefreshAsync(); }
    }

    /// <summary>Takes the gold left at the sign you are reading. First come, first served.</summary>
    public async Task ClaimAsync(string id)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/signs/{id}/claim", new { }), "").ConfigureAwait(false);
        if (r?.Sign != null) { lock (gate) { reading = r.Sign; notice = $"You take {r.Claimed:N0} gold."; } _ = Plugin.Bank.RefreshAsync(); }
    }

    public async Task UpdateAsync(string id, string name, string text)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/signs/{id}/update", new { name = name.Trim(), description = text.Trim() }), "Saved.").ConfigureAwait(false);
        if (r != null) { CloseReading(); await RefreshAsync().ConfigureAwait(false); }
    }

    public async Task MoveHereAsync(SignView s, Spot where)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/signs/{s.Id}/update", new
        {
            territory = (int)where.Territory, map = (int)where.Map, x = where.Pos.X, y = where.Pos.Y, z = where.Pos.Z, zone = where.Zone, mapX = where.MapX, mapY = where.MapY
        }), "Moved here.").ConfigureAwait(false);
        if (r != null) { CloseReading(); await RefreshAsync().ConfigureAwait(false); }
    }

    public async Task DeleteAsync(string id)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/signs/{id}/delete", new { }), "Sign deleted.").ConfigureAwait(false);
        if (r != null) { CloseReading(); await RefreshAsync().ConfigureAwait(false); _ = Plugin.Bank.RefreshAsync(); }
    }

    /// <summary>Advocates (creation password) can delete any sign.</summary>
    public async Task AdminDeleteAsync(string id)
    {
        var pw = Plugin.Config.CreatePassword;
        if (pw.Length == 0) { lock (gate) { error = "Enter the creation password in the Advocate tools first."; } return; }
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/signs/admin/delete", new { adminPassword = pw, signId = id }), "Sign deleted.").ConfigureAwait(false);
        if (r != null) { CloseReading(); await RefreshAsync().ConfigureAwait(false); }
    }

    public static void ShowOnMap(SignView s)
    {
        try
        {
            var link = new MapLinkPayload((uint)s.Territory, s.Map, s.MapX, s.MapY);
            _ = Plugin.Framework.RunOnFrameworkThread(() => Plugin.GameGui.OpenMapWithMapLink(link));
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Could not open the map"); }
    }
}
