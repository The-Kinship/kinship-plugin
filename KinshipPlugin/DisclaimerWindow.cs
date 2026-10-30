using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>Shown at every login (until "Don't show this again"): third-party plugin risk, in plain words.</summary>
public sealed class DisclaimerWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private bool understood;

    public DisclaimerWindow() : base("Before you play##KinshipDisclaimer", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings)
    {
        Size = new Vector2(440f, 330f); SizeCondition = ImGuiCond.Always;
    }

    public void Dispose() { }
    public override void OnOpen() { understood = false; }

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + vp.Size.X / 2f - 220f, vp.Pos.Y + vp.Size.Y / 2f - 200f);
        PositionCondition = ImGuiCond.Appearing;
    }

    public override void Draw()
    {
        Alpha.Mark();
        ImGui.TextColored(Gold, "Please read");
        ImGui.Spacing();
        ImGui.TextWrapped("Rafter Cat's RP Toolbox is a third-party add-on. It is not made or supported by Square Enix. Use it at your own risk.");
        ImGui.Spacing();
        ImGui.TextWrapped("It does not cheat, automate the game, or inject code, and it only reads information from the game. But it still carries the same risk as any other third-party add-on, and Square Enix has the final say on what is allowed.");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.Checkbox("I understand and use the plugin at my own risk", ref understood);
        ImGui.Spacing();
        if (!understood) ImGui.BeginDisabled();
        if (ImGui.Button("Continue##disc", new Vector2(120f, 0f))) IsOpen = false;
        ImGui.SameLine();
        if (ImGui.Button("Don't show this again##disc2", new Vector2(190f, 0f))) { Plugin.Config.HideDisclaimer = true; Plugin.Config.Save(); IsOpen = false; }
        if (!understood) ImGui.EndDisabled();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.7f, 1f), "You can read more any time in the About Plugin tab.");
    }
}
