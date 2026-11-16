using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>On-screen pop-ups for the Lounge: the Party Finder refresh reminder, and "time is up" for a service session. They stay until you answer them.</summary>
public sealed class LoungeAlertWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Cream = new(0.98f, 0.95f, 0.88f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);

    public LoungeAlertWindow() : base("Embershade Lounge##KinshipLoungeAlert",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        IsOpen = true;
        BgAlpha = 0.95f;
    }

    public void Dispose() { }

    public override bool DrawConditions()
    {
        if (Plugin.Config.Me().ItemsToken.Length == 0) return false;
        var (pf, ses) = Plugin.Lounge.Alerts();
        return pf || ses != null || Plugin.Lounge.Pings().Count > 0;
    }

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + vp.Size.X / 2f - 230f, vp.Pos.Y + vp.Size.Y * 0.18f);
        PositionCondition = ImGuiCond.Always;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(460f, 40f), MaximumSize = new Vector2(460f, 2000f) };
    }

    private static void Big(string text)
    {
        if (Plugin.PopupFont.Available)
        {
            using (Plugin.PopupFont.Push())
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Cream);
                ImGui.PushTextWrapPos(420f); ImGui.TextUnformatted(text); ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }
        }
        else { ImGui.PushStyleColor(ImGuiCol.Text, Cream); ImGui.PushTextWrapPos(420f); ImGui.TextUnformatted(text); ImGui.PopTextWrapPos(); ImGui.PopStyleColor(); }
    }

    public override void Draw()
    {
        var (pf, ses) = Plugin.Lounge.Alerts();
        var pings = Plugin.Lounge.Pings();
        foreach (var g in pings)
        {
            ImGui.PushID("png" + g.Id);
            ImGui.TextColored(Gold, g.Kind == "security" ? "Embershade Lounge  -  security call" : g.Kind == "ban" ? "Embershade Lounge  -  ban request" : "Embershade Lounge  -  manager call");
            Big(g.Text);
            if (g.Responder.Length > 0) ImGui.TextColored(Dim, $"{g.Responder} is responding.");
            if (g.Responder.Length == 0 && ImGui.Button("I'll go")) _ = Plugin.Lounge.RespondAsync(g.Id);
            if (g.Responder.Length == 0) ImGui.SameLine();
            if (ImGui.Button("Dismiss")) _ = Plugin.Lounge.DismissPingAsync(g.Id);
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.PopID();
        }
        if (ses != null)
        {
            ImGui.TextColored(Gold, "Embershade Lounge  -  service");
            ImGui.Spacing();
            Big("Time is up!");
            ImGui.TextWrapped($"{ses.Name}" + (ses.Customer.Length > 0 ? $" with {ses.Customer}" : "") + $"  ({Dur.Of(ses.Minutes)} booked)");
            ImGui.Spacing();
            if (ImGui.Button("Add 30 minutes##lat1")) _ = Plugin.Lounge.ExtendAsync(30);
            ImGui.SameLine(); if (ImGui.Button("Add 1 hour##lat2")) _ = Plugin.Lounge.ExtendAsync(60);
            ImGui.SameLine(); if (ImGui.Button("Close session##lat3")) _ = Plugin.Lounge.EndSessionAsync();
            ImGui.TextColored(Dim, "Closing makes the receipt. ");
            ImGui.SameLine(); if (ImGui.SmallButton("Remind me in 5 minutes##lat4")) Plugin.Lounge.SnoozeSession();
            if (pf) { ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing(); }
        }
        if (pf)
        {
            ImGui.TextColored(Gold, "Embershade Lounge  -  Party Finder");
            ImGui.Spacing();
            Big("Please refresh the Party Finder post.");
            ImGui.Spacing();
            if (ImGui.Button("Done, dismiss##lapf")) Plugin.Lounge.DismissPf();
            ImGui.SameLine(); ImGui.TextColored(Dim, "Then press I posted it in the Party Finder tab to restart the timer.");
        }
    }
}
