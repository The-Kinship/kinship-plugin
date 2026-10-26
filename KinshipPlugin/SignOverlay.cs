using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>An invisible full-screen layer that writes each nearby sign's name where it is in the world.</summary>
public sealed class SignOverlay : Window, IDisposable
{
    private static readonly Vector4 Parchment = new(0.86f, 0.80f, 0.62f, 1f);
    private static readonly Vector4 Faint = new(0.75f, 0.75f, 0.8f, 1f);

    public SignOverlay() : base("Sign labels##KinshipSignOverlay",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav)
    {
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        IsOpen = true;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport;
        Position = vp.Pos; PositionCondition = ImGuiCond.Always;
        Size = vp.Size; SizeCondition = ImGuiCond.Always;
    }

    public override bool DrawConditions() => Plugin.Config.SignLabels && Plugin.Config.ItemsToken.Length > 0;

    public override void Draw()
    {
        Plugin.Signs.RefreshIfDue(60);
        var me = WorldInfo.PlayerPos();
        if (me == null) return;
        var territory = Plugin.ClientState.TerritoryType;
        foreach (var s in Plugin.Signs.All)
        {
            if (s.Territory != territory) continue;
            var pos = new Vector3(s.X, s.Y, s.Z);
            var dist = Vector3.Distance(me.Value, pos);
            if (dist > SignState.LabelRange) continue;
            if (!Plugin.GameGui.WorldToScreen(pos + new Vector3(0, 1.0f, 0), out var screen)) continue;
            var sub = dist <= SignState.ReadRange ? "you can read this" : $"sign  {dist:0}y";
            var w = Math.Max(ImGui.CalcTextSize(s.Name).X, ImGui.CalcTextSize(sub).X);
            ImGui.SetCursorScreenPos(new Vector2(screen.X - w / 2f, screen.Y - 18f));
            ImGui.TextColored(Parchment, s.Name);
            ImGui.SetCursorScreenPos(new Vector2(screen.X - w / 2f, screen.Y - 2f));
            ImGui.TextColored(Faint, sub);
        }
    }
}
