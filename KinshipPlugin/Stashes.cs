using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Excel.Sheets;

namespace KinshipPlugin;

public sealed class StashView
{
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Zone { get; set; } = "";
    public int Territory { get; set; } public uint Map { get; set; }
    public float X { get; set; } public float Y { get; set; } public float Z { get; set; }
    public float MapX { get; set; } public float MapY { get; set; }
    public string OwnerName { get; set; } = ""; public bool Mine { get; set; } public bool IsPrivate { get; set; }
    public int Count { get; set; } public int Capacity { get; set; }
    public string? Owner { get; set; }   // only filled in by the Advocate list
}

public sealed record Spot(ushort Territory, uint Map, Vector3 Pos, string Zone, float MapX, float MapY);
public sealed record StashSnap(List<StashView> Stashes, StashView? Open, List<ItemView> OpenItems, string Error, string Notice, bool Busy);

/// <summary>Reads where you are standing. Read-only: nothing here changes anything in the game.</summary>
public static class WorldInfo
{
    public const float UseRange = 10f;       // how close you must be to open a stash
    public const float LabelRange = 60f;     // how close before its name tag shows

    public static bool InInstance =>
        Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56] || Plugin.Condition[ConditionFlag.BoundByDuty95];

    public static Vector3? PlayerPos() { try { return Plugin.ObjectTable.LocalPlayer?.Position; } catch (Exception) { return null; } }

    public static float? DistanceTo(StashView s)
    {
        var p = PlayerPos();
        if (p == null || Plugin.ClientState.TerritoryType != s.Territory) return null;
        return Vector3.Distance(p.Value, new Vector3(s.X, s.Y, s.Z));
    }
    public static bool Near(StashView s) => DistanceTo(s) is { } d && d <= UseRange;

    public static string ZoneName(ushort territory)
    {
        try
        {
            if (Plugin.GameData.GetExcelSheet<TerritoryType>().TryGetRow(territory, out var row))
                return row.PlaceName.ValueNullable?.Name.ToString() ?? "";
        }
        catch (Exception) { /* fall through */ }
        return "";
    }

    public static (float, float) ToMapCoords(Vector3 pos, uint mapId)
    {
        try
        {
            if (Plugin.GameData.GetExcelSheet<Map>().TryGetRow(mapId, out var map) && map.SizeFactor > 0)
            {
                var c = map.SizeFactor / 100.0;
                double Conv(double v, double offset) => 41.0 / c * (((v + offset) * c + 1024.0) / 2048.0) + 1.0;
                return ((float)Conv(pos.X, map.OffsetX), (float)Conv(pos.Z, map.OffsetY));
            }
        }
        catch (Exception) { /* fall through */ }
        return (0f, 0f);
    }

    /// <summary>Where you are right now, or null with the reason you can't make a stash here.</summary>
    public static Spot? Current(out string problem)
    {
        problem = "";
        var pos = PlayerPos();
        if (pos == null) { problem = "Log in to a character first."; return null; }
        if (InInstance) { problem = "Stashes can't be made inside instances."; return null; }
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) { problem = "Wait until the zone has finished loading."; return null; }
        var territory = Plugin.ClientState.TerritoryType;
        var map = Plugin.ClientState.MapId;
        var zone = ZoneName(territory);
        var (mx, my) = ToMapCoords(pos.Value, map);
        return new Spot(territory, map, pos.Value, zone.Length > 0 ? zone : $"Zone {territory}", mx, my);
    }
}

/// <summary>Stashes: places in the world that hold items. The list lives on your server.</summary>
public sealed class StashState
{
    private readonly object gate = new();
    private List<StashView> stashes = new();
    private StashView? open;
    private List<ItemView> openItems = new();
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastList = DateTime.MinValue;

    public List<StashView> All { get { lock (gate) { return stashes; } } }   // never edited in place, only replaced
    public StashSnap Snapshot() { lock (gate) { return new StashSnap(stashes, open, openItems.ToList(), error, notice, busy); } }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }
    public void CloseOpen() { lock (gate) { open = null; openItems = new(); } }

    private async Task<ItemReply?> Exec(Func<Task<ItemReply>> call, string? done = null)
    {
        lock (gate) { if (busy) return null; busy = true; error = ""; notice = ""; }
        try
        {
            var r = await call().ConfigureAwait(false);
            lock (gate)
            {
                if (r.Stash != null) { open = r.Stash; openItems = r.StashItems ?? new(); stashes = stashes.Where(x => x.Id != r.Stash.Id).Append(r.Stash).ToList(); }
                if (done != null) notice = done;
            }
            if (r.Items != null) Plugin.Items.SetInventory(r.Items);
            return r;
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Stash request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
        return null;
    }

    public void RefreshIfDue(int seconds)
    {
        bool due;
        lock (gate) { due = !busy && Plugin.Config.ItemsToken.Length > 0 && (DateTime.UtcNow - lastList).TotalSeconds > seconds; if (due) lastList = DateTime.UtcNow; }
        if (due) _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/stashes", null).ConfigureAwait(false);
            lock (gate) { stashes = r.Stashes ?? new(); lastList = DateTime.UtcNow; if (open != null) open = stashes.FirstOrDefault(s => s.Id == open.Id); }
        }
        catch (Exception) { /* keep the list we have */ }
    }

    private static object Place(Spot s) => new { territory = (int)s.Territory, map = (int)s.Map, x = s.Pos.X, y = s.Pos.Y, z = s.Pos.Z, zone = s.Zone, mapX = s.MapX, mapY = s.MapY };

    public async Task CreateAsync(string name, bool isPrivate, Spot s)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/stashes", new
        {
            name = name.Trim(), isPrivate, territory = (int)s.Territory, map = (int)s.Map, x = s.Pos.X, y = s.Pos.Y, z = s.Pos.Z, zone = s.Zone, mapX = s.MapX, mapY = s.MapY
        }), "Stash made. It stays right here.").ConfigureAwait(false);
        if (r != null) { CloseOpen(); await RefreshAsync().ConfigureAwait(false); }
    }

    public async Task OpenAsync(string id) => await Exec(() => ItemsState.SendAsync(HttpMethod.Get, "/stashes/" + id, null)).ConfigureAwait(false);
    public async Task PutAsync(string id, string itemId) => await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{id}/put", new { itemId })).ConfigureAwait(false);
    public async Task TakeAsync(string id, string itemId) => await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{id}/take", new { itemId })).ConfigureAwait(false);

    public async Task UpdateAsync(string id, string name, bool isPrivate)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{id}/update", new { name = name.Trim(), isPrivate }), "Saved.").ConfigureAwait(false);
        if (r != null) { CloseOpen(); await RefreshAsync().ConfigureAwait(false); }
    }

    public async Task MoveHereAsync(StashView s, Spot where)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{s.Id}/update", new
        {
            name = s.Name, isPrivate = s.IsPrivate, territory = (int)where.Territory, map = (int)where.Map, x = where.Pos.X, y = where.Pos.Y, z = where.Pos.Z, zone = where.Zone, mapX = where.MapX, mapY = where.MapY
        }), "Moved here.").ConfigureAwait(false);
        if (r != null) { CloseOpen(); await RefreshAsync().ConfigureAwait(false); }
    }

    public async Task DeleteAsync(string id)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{id}/delete", new { }), "Stash deleted.").ConfigureAwait(false);
        if (r != null) { CloseOpen(); await RefreshAsync().ConfigureAwait(false); }
    }

    /// <summary>Opens the game map with a flag on the stash.</summary>
    public static void ShowOnMap(StashView s)
    {
        try
        {
            var link = new MapLinkPayload((uint)s.Territory, s.Map, s.MapX, s.MapY);
            _ = Plugin.Framework.RunOnFrameworkThread(() => Plugin.GameGui.OpenMapWithMapLink(link));
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Could not open the map"); }
    }
}
