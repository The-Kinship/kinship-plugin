using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>The RP popup. Stays on screen until each message is clicked away, so nobody misses one.</summary>
public sealed class PopupWindow : Window, IDisposable
{
    private const float Width = 640f;
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Cream = new(0.98f, 0.95f, 0.88f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);

    public PopupWindow() : base("RP##KinshipPopup",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(Width, 40f), MaximumSize = new Vector2(Width, 2000f) };
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        BgAlpha = 0.94f;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + (vp.Size.X - Width) / 2f, vp.Pos.Y + vp.Size.Y * 0.10f);
        PositionCondition = ImGuiCond.Always;
    }

    private string joinName = "";

    private void DrawJoin(Popup p)
    {
        ImGui.Spacing();
        if (Plugin.Combat.Connected && string.Equals(Plugin.Combat.Code, p.Combat, StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 0.5f, 1f), "You are in this encounter. It is in the Combat tab.");
            return;
        }
        var saved = Plugin.Config.CombatName.Length > 0 ? Plugin.Config.CombatName : Plugin.Config.SceneName.Length > 0 ? Plugin.Config.SceneName : Plugin.Config.ItemsName;
        var nameToUse = saved.Length > 0 ? saved : joinName.Trim();
        if (saved.Length == 0) { ImGui.SetNextItemWidth(220f); ImGui.InputText("Your name##pjn" + p.Id, ref joinName, 60); }
        var busy = Plugin.Combat.Snapshot().Busy;
        var ok = nameToUse.Length > 0 && !busy;
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button("Join combat##pj" + p.Id)) _ = JoinAsync(p, nameToUse);
        if (!ok) ImGui.EndDisabled();
        var err = Plugin.Combat.Snapshot().Error;
        if (err.Length > 0) ImGui.TextColored(new Vector4(0.95f, 0.4f, 0.35f, 1f), err);
    }

    private static async System.Threading.Tasks.Task JoinAsync(Popup p, string name)
    {
        await Plugin.Combat.JoinAsync(p.Combat, name).ConfigureAwait(false);
        if (Plugin.Combat.Connected && string.Equals(Plugin.Combat.Code, p.Combat, StringComparison.OrdinalIgnoreCase)) { Plugin.Scene.Dismiss(p.Id); Plugin.ShowMain(); }
    }

    public override void Draw()
    {
        var list = Plugin.Scene.ActivePopups();
        if (list.Count == 0) { IsOpen = false; return; }

        for (var i = 0; i < list.Count; i++)
        {
            var p = list[i];
            ImGui.TextColored(Gold, list.Count > 1 ? $"RP  ·  {i + 1} of {list.Count}" : "RP");
            ImGui.Spacing();

            if (Plugin.PopupFont.Available)
            {
                using (Plugin.PopupFont.Push())
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Cream);
                    ImGui.PushTextWrapPos(Width - 40f); ImGui.TextUnformatted(p.Text); ImGui.PopTextWrapPos();
                    ImGui.PopStyleColor();
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Cream);
                ImGui.PushTextWrapPos(Width - 40f); ImGui.TextUnformatted(p.Text); ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }

            if (p.Ooc.Length > 0)
            {
                ImGui.Spacing();
                UiUtil.BoldWrapped(UiUtil.Ooc(p.Ooc), Gold, Width - 40f);
            }

            if (p.Combat.Length > 0) DrawJoin(p);

            ImGui.Spacing();
            if (ImGui.Button("Dismiss##p" + p.Id)) Plugin.Scene.Dismiss(p.Id);
            if (i < list.Count - 1) { ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing(); }
        }
        if (list.Count > 1)
        {
            ImGui.SameLine();
            if (ImGui.Button("Dismiss all")) Plugin.Scene.DismissAll();
        }
    }
}
