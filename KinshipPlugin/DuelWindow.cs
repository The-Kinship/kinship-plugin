using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>The fight window: a challenge to accept or refuse, then the little fight where both players press attack or heal.</summary>
public sealed class DuelWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);

    public DuelWindow() : base("Fight##KinshipDuel", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360f, 120f), MaximumSize = new Vector2(420f, 700f) };
        RespectCloseHotkey = false;
    }
    public void Dispose() { }

    public override void PreDraw() { if (!Plugin.Duels.Visible()) IsOpen = false; }

    public override void Draw()
    {
        var (d, error, busy) = Plugin.Duels.Snapshot();
        if (d == null) { ImGui.Text("No fight right now."); return; }
        if (error.Length > 0) ImGui.TextColored(Red, error);

        switch (d.Status)
        {
            case "pending" when !d.Challenger:
                ImGui.TextColored(Gold, $"{d.Foe} wants to pick a fight with you!");
                ImGui.TextColored(Dim, "Both of you take turns pressing attack or heal until one of you wins.");
                ImGui.Spacing();
                if (!busy && ImGui.Button("Accept##duel")) _ = Plugin.Duels.AcceptAsync(d.Id);
                ImGui.SameLine(); if (!busy && ImGui.Button("Refuse##duel")) _ = Plugin.Duels.RefuseAsync(d.Id);
                break;
            case "pending":
                ImGui.TextColored(Gold, $"Waiting for {d.Foe} to answer...");
                if (!busy && ImGui.Button("Cancel##duel")) _ = Plugin.Duels.CancelAsync(d.Id);
                break;
            case "active": DrawFight(d, busy); break;
            case "done":
                ImGui.TextColored(d.YouWon == true ? Green : Red, d.YouWon == true ? "You won the fight!" : "You lost the fight.");
                DrawLog(d); Close();
                break;
            case "refused": ImGui.TextColored(Dim, d.Challenger ? $"{d.Foe} refused the fight." : "You refused the fight."); Close(); break;
            case "cancelled": ImGui.TextColored(Dim, "The challenge was cancelled."); Close(); break;
            default: ImGui.TextColored(Dim, "The challenge ran out of time."); Close(); break;
        }
    }

    private static void Close() { ImGui.Spacing(); if (ImGui.Button("Close##duel")) { Plugin.Duels.Dismiss(); } }

    private static void DrawFight(DuelView d, bool busy)
    {
        ImGui.TextColored(Gold, $"{d.You}  vs  {d.Foe}");
        ImGui.Text(d.You); ImGui.ProgressBar(d.Max == 0 ? 0f : (float)d.YouHp / d.Max, new Vector2(300f, 18f), $"{d.YouHp}/{d.Max}");
        ImGui.Text(d.Foe); ImGui.ProgressBar(d.Max == 0 ? 0f : (float)d.FoeHp / d.Max, new Vector2(300f, 18f), $"{d.FoeHp}/{d.Max}");
        ImGui.Spacing();
        if (d.YourTurn)
        {
            ImGui.TextColored(Green, "Your turn!");
            if (!busy && ImGui.Button("Attack##duel")) _ = Plugin.Duels.ActAsync(d.Id, "attack");
            ImGui.SameLine();
            var canHeal = !busy && d.HealsLeft > 0;
            if (!canHeal) ImGui.BeginDisabled();
            if (ImGui.Button($"Heal ({d.HealsLeft} left)##duel")) _ = Plugin.Duels.ActAsync(d.Id, "heal");
            if (!canHeal) ImGui.EndDisabled();
        }
        else ImGui.TextColored(Dim, $"Waiting for {d.Foe}...");
        DrawLog(d);
        ImGui.Spacing();
        if (!busy && ImGui.SmallButton("Give up##duel")) _ = Plugin.Duels.ForfeitAsync(d.Id);
    }

    private static void DrawLog(DuelView d)
    {
        ImGui.Separator();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24f);
        foreach (var line in d.Log.AsEnumerable().Reverse().Take(6)) ImGui.TextUnformatted(line);
        ImGui.PopTextWrapPos();
    }
}
