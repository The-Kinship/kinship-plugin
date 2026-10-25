using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The RP tab: join a scene to receive popups, or run a scene and send them.</summary>
public sealed class RpTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);

    private string name = Plugin.Config.SceneName.Length > 0 ? Plugin.Config.SceneName : Plugin.Config.CombatName;
    private string codeInput = "";
    private string newTitle = "";
    private string text = "", ooc = "";
    private bool sendAll = true;
    private readonly HashSet<string> picked = new();

    public void Draw()
    {
        var s = Plugin.Scene.Snapshot();
        if (!s.Connected) { DrawConnect(s); return; }
        if (s.Role == "dm") DrawDm(s); else DrawPlayer(s);
    }

    // ---------- not in a scene ----------
    private void DrawConnect(SceneSnap s)
    {
        ImGui.TextColored(Gold, "Join a scene");
        ImGui.TextWrapped("Your DM gives you a scene code. Join it to receive the DM's RP popups. Popups stay off until you join.");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Your name##rp", ref name, 60);
        ImGui.SetNextItemWidth(240f); var enter = ImGui.InputText("Scene code##rp", ref codeInput, 300, ImGuiInputTextFlags.EnterReturnsTrue);
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        if (s.Busy) ImGui.TextColored(Dim, "Working...");
        else if (ImGui.Button("Join scene") || enter) _ = Plugin.Scene.JoinAsync(codeInput, name);

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Run a scene (DM)");
        ImGui.TextWrapped("Start a scene for your RP group. Share the code, then send popups to everyone or to chosen people. Different groups can run different scenes at the same time.");
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Scene name##rp", ref newTitle, 80);
        UiUtil.CreatePasswordInput();
        if (!s.Busy && ImGui.Button("Create scene")) _ = Plugin.Scene.CreateAsync(name, newTitle);
    }

    // ---------- a player in a scene ----------
    private static void DrawPlayer(SceneSnap s)
    {
        ImGui.TextColored(Gold, s.Title.Length > 0 ? s.Title : "RP scene");
        ImGui.Text("Scene " + s.Code);
        ImGui.Spacing();

        if (s.Listening)
        {
            ImGui.TextColored(Green, "RP popups are ON.");
            if (ImGui.Button("Pause popups")) Plugin.Scene.SetListening(false);
        }
        else
        {
            ImGui.TextColored(Dim, "RP popups are OFF. Turn them on when your RP starts.");
            if (ImGui.Button("Turn on RP popups")) Plugin.Scene.SetListening(true);
        }
        ImGui.SameLine();
        if (ImGui.Button("Leave scene")) { Plugin.Scene.Leave(); return; }

        ImGui.Spacing();
        var echo = Plugin.Config.ChatEcho;
        if (ImGui.Checkbox("Also write popups into my chat log", ref echo)) { Plugin.Config.ChatEcho = echo; Plugin.Config.Save(); }
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        RecentList();
    }

    private static void RecentList()
    {
        if (!ImGui.CollapsingHeader("Recent popups")) return;
        var recent = Plugin.Scene.RecentPopups();
        if (recent.Count == 0) { ImGui.TextColored(Dim, "Nothing yet."); return; }
        foreach (var p in recent)
        {
            ImGui.TextWrapped(p.Text);
            if (p.Ooc.Length > 0) ImGui.TextColored(Dim, UiUtil.Ooc(p.Ooc));
            ImGui.Spacing();
        }
    }

    // ---------- the DM's panel ----------
    private void DrawDm(SceneSnap s)
    {
        Plugin.Scene.RefreshMembersIfDue();
        ImGui.TextColored(Gold, s.Title.Length > 0 ? s.Title : "RP scene");
        ImGui.Text("Scene " + s.Code);
        ImGui.SameLine(); if (ImGui.SmallButton("Copy code")) ImGui.SetClipboardText(s.Code);
        ImGui.TextColored(Dim, "Give this code to your RP group. Each person joins it from their RP tab.");
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        ImGui.Separator();

        // who is in the scene
        ImGui.TextColored(Gold, $"In this scene ({s.Members.Count})");
        if (s.Members.Count == 0) ImGui.TextColored(Dim, "Nobody has joined yet.");
        foreach (var m in s.Members)
        {
            var on = picked.Contains(m.Id);
            if (ImGui.Checkbox(m.Name + "##m" + m.Id, ref on)) { if (on) picked.Add(m.Id); else picked.Remove(m.Id); }
            ImGui.SameLine(); if (ImGui.SmallButton("Remove##k" + m.Id)) _ = Plugin.Scene.RemoveMemberAsync(m.Id);
        }
        picked.RemoveWhere(id => s.Members.All(m => m.Id != id));
        if (s.Members.Count > 0)
        {
            if (ImGui.SmallButton("Check everyone")) foreach (var m in s.Members) picked.Add(m.Id);
            ImGui.SameLine(); if (ImGui.SmallButton("Check no one")) picked.Clear();
        }

        // write the popup
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Send a popup");
        ImGui.InputTextMultiline("##rptext", ref text, 300, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 64f));
        ImGui.TextColored(Dim, "What everyone sees, e.g. \"You hear screaming from the house by the water.\"");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 8f);
        ImGui.InputText("##rpooc", ref ooc, 200);
        ImGui.TextColored(Dim, "Optional OOC note, shown in bold inside (( )).");
        if (ImGui.SmallButton("Fill in: encounter started note"))
            ooc = "Encounter started! Stay put and wait for the DM to give you the combat code";

        ImGui.Spacing();
        if (ImGui.RadioButton("Everyone in the scene", sendAll)) sendAll = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("Only the people I checked", !sendAll)) sendAll = false;

        var canSend = text.Trim().Length > 0 && (sendAll || picked.Count > 0) && s.Members.Count > 0;
        if (!canSend) ImGui.BeginDisabled();
        if (!s.Busy && ImGui.Button("Send popup"))
        {
            var message = text; var note = ooc; var to = sendAll ? null : picked.ToList();
            _ = Send(message, note, to);
        }
        if (!canSend) ImGui.EndDisabled();
        if (s.Status.Length > 0) { ImGui.SameLine(); ImGui.TextColored(Green, s.Status); }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        if (ImGui.SmallButton("Close scene")) _ = Plugin.Scene.CloseSceneAsync();
        ImGui.SameLine(); ImGui.TextColored(Dim, "Ends the scene for everyone.");
    }

    private async System.Threading.Tasks.Task Send(string message, string note, List<string>? to)
    {
        if (await Plugin.Scene.SendPopupAsync(message.Trim(), note.Trim(), to)) { text = ""; ooc = ""; }
    }
}
