using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>A small window that appears when you stand on a stash. Open it right there, put things in and take things out, without going to the tab.</summary>
public sealed class StashReaderWindow : Window, IDisposable
{
    private const float Width = 420f;
    private const float PopupRange = 2.5f;   // how close before the pop-up appears (standing on it)
    private const float PopupStay = 4f;      // once it is up it stays until you are this far, so it doesn't flicker
    private const float AutoClose = 15f;     // an opened stash closes by itself when you walk this far away
    private static readonly Vector4 Parchment = new(0.86f, 0.80f, 0.62f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private readonly StashesTab tab = new();
    private bool mine;     // true while the stash showing here was opened from this window
    private bool shown;

    public StashReaderWindow() : base("Stash nearby##KinshipStashReader",
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
        Position = new Vector2(vp.Pos.X + vp.Size.X / 2f - Width / 2f, vp.Pos.Y + vp.Size.Y * 0.30f);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    private static bool Close(StashView s, float range) => WorldInfo.DistanceTo(s) is { } d && d <= range;
    private bool AnyNear()
    {
        var range = shown ? PopupStay : PopupRange;
        shown = Plugin.Stashes.All.Any(s => Close(s, range));
        return shown;
    }

    public override bool DrawConditions()
    {
        if (Plugin.Config.Me().ItemsToken.Length == 0 || !Plugin.Config.StashPopup) return false;
        Plugin.Stashes.RefreshIfDue(60);
        var open = Plugin.Stashes.Snapshot().Open;
        if (!mine || open == null)
        {
            mine = false;
            return AnyNear();
        }
        if (WorldInfo.DistanceTo(open) is not { } d || d > AutoClose) { Plugin.Stashes.CloseOpen(); mine = false; return AnyNear(); }
        return true;
    }

    public override void Draw()
    {
        var s = Plugin.Stashes.Snapshot();
        if (mine && s.Open != null)
        {
            tab.DrawOpen(s, Plugin.Items.Snapshot(), true);
            if (s.Open == null) mine = false;
            return;
        }
        ImGui.TextColored(Parchment, "A stash is here");
        var near = Plugin.Stashes.All.Where(x => Close(x, PopupStay)).OrderBy(x => WorldInfo.DistanceTo(x) ?? 0f).Take(5).ToList();
        foreach (var st in near)
        {
            ImGui.Text(st.Name + (st.IsPrivate ? "  (private)" : ""));
            ImGui.SameLine();
            if (!s.Busy && ImGui.SmallButton("Open##sr" + st.Id)) { mine = true; _ = Plugin.Stashes.OpenAsync(st.Id); }
        }
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
    }
}
