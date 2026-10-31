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
    public int Territory { get; set; } public uint Map { get; set; } public string Room { get; set; } = "";
    public float X { get; set; } public float Y { get; set; } public float Z { get; set; }
    public float MapX { get; set; } public float MapY { get; set; }
    public string OwnerName { get; set; } = ""; public bool Mine { get; set; } public bool IsPrivate { get; set; }
    public int Count { get; set; } public int Capacity { get; set; } public int Gold { get; set; }
    public string? Owner { get; set; }   // only filled in by the Advocate list
}

public sealed record Spot(uint Territory, uint Map, Vector3 Pos, string Zone, float MapX, float MapY, string Room = "");
public sealed record ZoneCount(string Zone, int Count);
public sealed record StashSnap(List<StashView> Stashes, List<ZoneCount> Zones, StashView? Open, List<ItemView> OpenItems, string Error, string Notice, bool Busy);

/// <summary>Reads where you are standing. Read-only: nothing here changes anything in the game.</summary>
public static class WorldInfo
{
    public const float UseRange = 10f;       // how close you must be to open a stash
    public const float LabelRange = 60f;     // how close before its name tag shows

    public static bool InInstance =>
        Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56] || Plugin.Condition[ConditionFlag.BoundByDuty95];

    // Where you stood on the last frame. Read by the network code, which may not touch the game's objects from another thread.
    private static volatile Tuple<int, Vector3, string>? last;
    /// <summary>Which house you are in (world, district, ward, plot, room), or empty. Houses share one map per type, so position alone can't tell them apart.</summary>
    public static string CurrentRoom => last?.Item3 ?? "";
    public static void Track()
    {
        try
        {
            var p = Plugin.ObjectTable.LocalPlayer?.Position;
            last = p == null ? null : Tuple.Create((int)Plugin.ClientState.TerritoryType, p.Value, ReadRoom());
        }
        catch (Exception) { last = null; }
    }
    /// <summary>Your last known position in the form the server expects, or null if unknown.</summary>
    public static object? At() { var l = last; return l == null ? null : new { territory = l.Item1, room = l.Item3, x = l.Item2.X, y = l.Item2.Y, z = l.Item2.Z }; }
    public static string AtQuery() { var l = last; return l == null ? "" : FormattableString.Invariant($"t={l.Item1}&r={l.Item3}&x={l.Item2.X:0.##}&y={l.Item2.Y:0.##}&z={l.Item2.Z:0.##}"); }

    // Read-only: asks the game's housing data which ward/plot/room you are in. Only does so in housing areas.
    private static unsafe string ReadRoom()
    {
        try
        {
            if (!Plugin.GameData.GetExcelSheet<TerritoryType>().TryGetRow(Plugin.ClientState.TerritoryType, out var row)) return "";
            var use = row.TerritoryIntendedUse.RowId;
            if (use != 13 && use != 14) return "";   // 13 = housing district, 14 = inside a house, apartment or room
            var hm = FFXIVClientStructs.FFXIV.Client.Game.HousingManager.Instance();
            if (hm == null) return "";
            int ward = hm->GetCurrentWard();
            if (ward < 0) return "";
            int division = hm->GetCurrentDivision();
            var world = Plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0;
            if (!hm->IsInside()) return $"h{world}-{division}-{ward}";   // out in the ward: the same ward on your world
            return $"h{world}-{division}-{ward}-{(int)hm->GetCurrentPlot()}-{(int)hm->GetCurrentRoom()}";
        }
        catch (Exception) { return ""; }
    }

    public static Vector3? PlayerPos() { try { return Plugin.ObjectTable.LocalPlayer?.Position; } catch (Exception) { return null; } }

    public static float? DistanceTo(StashView s)
    {
        var p = PlayerPos();
        if (p == null || Plugin.ClientState.TerritoryType != s.Territory || CurrentRoom != s.Room) return null;
        return Vector3.Distance(p.Value, new Vector3(s.X, s.Y, s.Z));
    }
    public static bool Near(StashView s) => DistanceTo(s) is { } d && d <= UseRange;

    public static string ZoneName(uint territory)
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
        if (Plugin.Condition[ConditionFlag.BetweenAreas]) { problem = "Wait until the zone has finished loading."; return null; }
        var territory = Plugin.ClientState.TerritoryType;
        var map = Plugin.ClientState.MapId;
        var zone = ZoneName(territory);
        var (mx, my) = ToMapCoords(pos.Value, map);
        return new Spot(territory, map, pos.Value, zone.Length > 0 ? zone : $"Zone {territory}", mx, my, CurrentRoom);
    }
}

/// <summary>Stashes: places in the world that hold items. The list lives on your server.</summary>
public sealed class StashState
{
    private readonly object gate = new();
    private List<StashView> stashes = new();
    private List<ZoneCount> zones = new();
    private StashView? open;
    private List<ItemView> openItems = new();
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastList = DateTime.MinValue;
    private DateTime tabSeen = DateTime.MinValue;
    /// <summary>Called by the tab every frame it is on screen. While it is open the list also includes everything you made, and the first frame refreshes straight away.</summary>
    public void TabOpen()
    {
        lock (gate) { if ((DateTime.UtcNow - tabSeen).TotalSeconds > 5) lastList = DateTime.MinValue; tabSeen = DateTime.UtcNow; }
    }
    private string ListPath(string p) { lock (gate) { return (DateTime.UtcNow - tabSeen).TotalSeconds < 5 ? p + "?full=1" : p; } }
    public int MaxMine { get; private set; } = 3;


    public List<StashView> All { get { lock (gate) { return stashes; } } }   // never edited in place, only replaced
    public StashSnap Snapshot() { lock (gate) { return new StashSnap(stashes, zones, open, openItems.ToList(), error, notice, busy); } }
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
        if (Idle.Asleep) return;
        bool due;
        lock (gate) { due = !busy && Plugin.Config.Me().ItemsToken.Length > 0 && (DateTime.UtcNow - lastList).TotalSeconds > seconds; if (due) lastList = DateTime.UtcNow; }
        if (due) _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Get, ListPath("/stashes"), null).ConfigureAwait(false);
            lock (gate) { stashes = r.Stashes ?? new(); zones = r.Zones ?? new(); lastList = DateTime.UtcNow; if (open != null) open = stashes.FirstOrDefault(s => s.Id == open.Id); }
        }
        catch (Exception) { /* keep the list we have */ }
    }

    private static object Place(Spot s) => new { territory = (int)s.Territory, map = (int)s.Map, room = s.Room, x = s.Pos.X, y = s.Pos.Y, z = s.Pos.Z, zone = s.Zone, mapX = s.MapX, mapY = s.MapY };

    public async Task CreateAsync(string name, bool isPrivate, Spot s)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/stashes", new
        {
            name = name.Trim(), isPrivate, territory = (int)s.Territory, map = (int)s.Map, room = s.Room, x = s.Pos.X, y = s.Pos.Y, z = s.Pos.Z, zone = s.Zone, mapX = s.MapX, mapY = s.MapY
        }), "Stash made. It stays right here.").ConfigureAwait(false);
        if (r != null) { CloseOpen(); await RefreshAsync().ConfigureAwait(false); }
    }

    public async Task OpenAsync(string id) => await Exec(() => ItemsState.SendAsync(HttpMethod.Get, "/stashes/" + id, null)).ConfigureAwait(false);
    public async Task PutGoldAsync(string id, int amount) { var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{id}/put-gold", new { amount })).ConfigureAwait(false); if (r != null) _ = Plugin.Bank.RefreshAsync(); }
    public async Task TakeGoldAsync(string id, int amount) { var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{id}/take-gold", new { amount })).ConfigureAwait(false); if (r != null) _ = Plugin.Bank.RefreshAsync(); }
    public async Task PutAsync(string id, string itemId, int count = 1) => await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{id}/put", new { itemId, count })).ConfigureAwait(false);
    public async Task TakeAsync(string id, string itemId, int count = 1) => await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{id}/take", new { itemId, count })).ConfigureAwait(false);

    public async Task UpdateAsync(string id, string name, bool isPrivate)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{id}/update", new { name = name.Trim(), isPrivate }), "Saved.").ConfigureAwait(false);
        if (r != null) { CloseOpen(); await RefreshAsync().ConfigureAwait(false); }
    }

    public async Task MoveHereAsync(StashView s, Spot where)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/stashes/{s.Id}/update", new
        {
            name = s.Name, isPrivate = s.IsPrivate, territory = (int)where.Territory, map = (int)where.Map, room = where.Room, x = where.Pos.X, y = where.Pos.Y, z = where.Pos.Z, zone = where.Zone, mapX = where.MapX, mapY = where.MapY
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
