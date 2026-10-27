using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The Fight tab: target another player and pick a fight.</summary>
public sealed class DuelTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);

    public void Draw()
    {
        var (d, error, busy) = Plugin.Duels.Snapshot();
        ImGui.TextColored(Gold, "Pick a fight");
        ImGui.TextWrapped("Target another player in the RP system, then press the button. They get to accept or refuse. If they accept, you take turns pressing attack or heal until one of you wins. The winner gets \"Won a fight\" and the loser \"Lost a fight\" for 10 minutes.");
        ImGui.Spacing();
        string target = "";
        try { target = Plugin.TargetManager.Target?.Name.TextValue ?? ""; } catch (Exception) { target = ""; }
        if (target.Length == 0) ImGui.TextColored(Dim, "Nothing targeted.");
        else ImGui.TextColored(Gold, "Target: " + target);
        var active = d != null && (d.Status == "pending" || d.Status == "active");
        var can = target.Length > 0 && !busy && !active;
        if (!can) ImGui.BeginDisabled();
        if (ImGui.Button("Pick a fight##duel")) _ = Plugin.Duels.ChallengeAsync(target);
        if (!can) ImGui.EndDisabled();
        if (error.Length > 0) ImGui.TextColored(Red, error);
        if (active) { ImGui.Spacing(); ImGui.TextColored(Dim, "You have a fight going."); if (ImGui.SmallButton("Show the fight window")) { Plugin.Duels.ClearError(); Plugin.ShowDuel(true); } }
    }
}
