using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>A small movable bar on your screen showing your own statuses, like the game's buff bar.</summary>
public sealed class StatusBar : Window, IDisposable
{
    private const float Icon = 32f;
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);

    public StatusBar() : base("Kinship status##KinshipStatusBar",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize |
        ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav)
    {
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        IsOpen = true;
        BgAlpha = 0.55f;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + vp.Size.X / 2f - 120f, vp.Pos.Y + 90f);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    public override bool DrawConditions()
    {
        if (Plugin.Config.Me().ItemsToken.Length == 0) return false;
        Plugin.Status.RefreshIfDue(30);   // keeps everyone's statuses fresh even when the tab is closed
        return Plugin.Config.StatusBar && Plugin.Status.Mine().Count > 0;
    }

    public override void Draw()
    {
        var mine = Plugin.Status.Mine();
        for (var i = 0; i < mine.Count; i++)
        {
            var s = mine[i];
            if (i > 0) ImGui.SameLine();
            ImGui.BeginGroup();
            var wrap = ItemIcons.Wrap(s.Icon);
            if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Icon, Icon)); else ImGui.Button("?##st" + s.Id, new Vector2(Icon, Icon));
            var colour = s.IsDebuff ? new Vector4(0.95f, 0.45f, 0.4f, 1f) : new Vector4(0.5f, 0.85f, 0.55f, 1f);
            ImGui.TextColored(colour, s.Remaining == null ? (s.IsDebuff ? "debuff" : "buff") : StatusView.Format(s.Remaining));
            ImGui.EndGroup();
            if (ImGui.IsItemHovered())
            {
                if (s.Self && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) _ = Plugin.Status.RemoveAsync(s.Id);
                ImGui.BeginTooltip();
                ImGui.TextColored(s.IsDebuff ? new Vector4(0.95f, 0.45f, 0.4f, 1f) : Gold, s.Name);
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20f);
                if (s.Description.Length > 0) ImGui.TextUnformatted(s.Description);
                ImGui.PopTextWrapPos();
                if (s.BonusText().Length > 0) ImGui.TextColored(Gold, s.BonusText());
                if (!s.Carried) ImGui.TextColored(Dim, StatusView.Format(s.Remaining));
                if (s.Self) ImGui.TextColored(Dim, "Right-click to remove");
                ImGui.EndTooltip();
            }
        }
    }
}
