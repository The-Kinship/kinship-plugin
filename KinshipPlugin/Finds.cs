using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>World items: a spot in the world that holds copies of an item, like a herb by a tree. Anyone who walks up can pick one up.</summary>
public sealed class FindView
{
    public string Id { get; set; } = ""; public string Label { get; set; } = ""; public string Text { get; set; } = ""; public string Mode { get; set; } = "once"; public int Minutes { get; set; }
    public string Zone { get; set; } = ""; public int Territory { get; set; } public uint Map { get; set; } public string Room { get; set; } = "";
    public float X { get; set; } public float Y { get; set; } public float Z { get; set; } public float MapX { get; set; } public float MapY { get; set; }
    public bool Mine { get; set; } public string State { get; set; } = "ready"; public int SecondsLeft { get; set; }
    public string? ItemName { get; set; } public string? ItemIcon { get; set; }

    public float? Distance()
    {
        var p = WorldInfo.PlayerPos();
        if (p == null || Plugin.ClientState.TerritoryType != Territory || WorldInfo.CurrentRoom != Room) return null;
        return Vector3.Distance(p.Value, new Vector3(X, Y, Z));
    }
}

public sealed class FindState
{
    public const float PopupRange = 3f;    // how close before the pick-up window appears
    public const float PopupStay = 5f;     // once it is up, it stays until you are this far
    private readonly object gate = new();
    private List<FindView> finds = new();
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastList = DateTime.MinValue, noticeAt = DateTime.MinValue;
    private DateTime tabSeen = DateTime.MinValue;
    /// <summary>Called by the tab every frame it is on screen. While it is open the list also includes everything you made, and the first frame refreshes straight away.</summary>
    public void TabOpen()
    {
        lock (gate) { if ((DateTime.UtcNow - tabSeen).TotalSeconds > 5) lastList = DateTime.MinValue; tabSeen = DateTime.UtcNow; }
    }
    private string ListPath(string p) { lock (gate) { return (DateTime.UtcNow - tabSeen).TotalSeconds < 5 ? p + "?full=1" : p; } }
    public int MaxMine { get; private set; } = 20;


    public List<FindView> All { get { lock (gate) { return finds; } } }   // only ever replaced, never edited in place
    public (string Error, string Notice, bool Busy) Messages() { lock (gate) { return (error, notice, busy); } }
    public bool RecentNotice() { lock (gate) { return notice.Length > 0 && (DateTime.UtcNow - noticeAt).TotalSeconds < 5; } }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }

    private async Task<ItemReply?> Exec(Func<Task<ItemReply>> call, string? done = null)
    {
        lock (gate) { if (busy) return null; busy = true; error = ""; if (done != null) notice = ""; }
        try { var r = await call().ConfigureAwait(false); lock (gate) { if (done != null) { notice = done; noticeAt = DateTime.UtcNow; } } return r; }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "World item request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
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
            var r = await ItemsState.SendAsync(HttpMethod.Get, ListPath("/finds"), null).ConfigureAwait(false);
            lock (gate) { finds = r.Finds ?? new(); lastList = DateTime.UtcNow; if (r.MaxMine > 0) MaxMine = r.MaxMine; }
        }
        catch (Exception) { /* keep the list we have */ }
    }

    public async Task PickUpAsync(FindView f)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/finds/{f.Id}/pickup", new { }), "").ConfigureAwait(false);
        if (r == null) return;
        lock (gate) { notice = $"You pick up: {r.Picked}."; noticeAt = DateTime.UtcNow; }
        await RefreshAsync().ConfigureAwait(false);
        _ = Plugin.Items.RefreshAsync(true);
    }

    public async Task CreateAsync(string itemId, string label, string text, string mode, int minutes, Spot s)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/finds", new
        {
            itemId, label = label.Trim(), text = text.Trim(), mode, minutes, territory = (int)s.Territory, map = (int)s.Map, room = s.Room, x = s.Pos.X, y = s.Pos.Y, z = s.Pos.Z, zone = s.Zone, mapX = s.MapX, mapY = s.MapY
        }), "World item placed here.").ConfigureAwait(false);
        if (r != null) await RefreshAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/finds/{id}/delete", new { }), "Deleted.").ConfigureAwait(false);
        if (r != null) await RefreshAsync().ConfigureAwait(false);
    }

    public async Task ResetAsync(string id)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/finds/{id}/reset", new { }), "Back in place.").ConfigureAwait(false);
        if (r != null) await RefreshAsync().ConfigureAwait(false);
    }

    public static void ShowOnMap(FindView f)
    {
        try
        {
            var link = new MapLinkPayload((uint)f.Territory, f.Map, f.MapX, f.MapY);
            _ = Plugin.Framework.RunOnFrameworkThread(() => Plugin.GameGui.OpenMapWithMapLink(link));
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Could not open the map"); }
    }
}

/// <summary>A small window that appears when you stand right next to a world item: what it looks like, and a button to pick it up.</summary>
public sealed class FindWindow : Window, IDisposable
{
    private const float Width = 340f;
    private static readonly Vector4 Parchment = new(0.86f, 0.80f, 0.62f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);

    public FindWindow() : base("Something here##KinshipFind",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        IsOpen = true;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(Width, 40f), MaximumSize = new Vector2(Width, 2000f) };
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var vp = Dalamud.Interface.Utility.ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + vp.Size.X / 2f - Width / 2f, vp.Pos.Y + vp.Size.Y * 0.45f);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    private bool shown;
    private static List<FindView> Near(float range) =>
        Plugin.Finds.All.Where(f => f.State == "ready" && f.Territory == Plugin.ClientState.TerritoryType && f.Room == WorldInfo.CurrentRoom && f.Distance() is { } d && d <= range)
            .OrderBy(f => f.Distance() ?? 0f).Take(5).ToList();

    public override bool DrawConditions()
    {
        if (Plugin.Config.Me().ItemsToken.Length == 0 || Idle.Paused) return false;
        Plugin.Finds.RefreshIfDue(20);
        shown = Near(shown ? FindState.PopupStay : FindState.PopupRange).Count > 0;
        return shown || Plugin.Finds.RecentNotice();
    }

    public override void Draw()
    {
        Alpha.Mark();
        var (error, notice, busy) = Plugin.Finds.Messages();
        var near = Near(FindState.PopupStay);
        if (near.Count > 0) ImGui.TextColored(Parchment, "You notice something");
        foreach (var f in near)
        {
            ImGui.Separator();
            ImGui.TextColored(Parchment, f.Label);
            if (f.Text.Length > 0) { ImGui.PushTextWrapPos(Width - 16f); ImGui.TextUnformatted(f.Text); ImGui.PopTextWrapPos(); }
            if (busy) ImGui.BeginDisabled();
            if (ImGui.SmallButton("Pick up##f" + f.Id)) _ = Plugin.Finds.PickUpAsync(f);
            if (busy) ImGui.EndDisabled();
        }
        if (error.Length > 0) ImGui.TextColored(Red, error);
        else if (notice.Length > 0 && Plugin.Finds.RecentNotice()) ImGui.TextColored(Green, notice);
    }
}
