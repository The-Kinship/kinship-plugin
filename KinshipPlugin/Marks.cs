using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

public sealed class MarkView { public long Id { get; set; } public string Reason { get; set; } = ""; public string CreatedAt { get; set; } = ""; }
public sealed class MarkPerson
{
    public string Name { get; set; } = ""; public bool Banned { get; set; } public string BanReason { get; set; } = ""; public bool Registered { get; set; }
    public List<MarkView> Marks { get; set; } = new();
}

/// <summary>Advocate-only infringement tracker: marks against a character, removing someone from the system, and bans. Uses the creation password.</summary>
public sealed class MarksState
{
    public static readonly MarksState I = new();
    private readonly object gate = new();
    private List<MarkPerson> people = new();
    private string error = "", notice = "";
    private bool busy, loaded;

    public (List<MarkPerson> People, string Error, string Notice, bool Busy, bool Loaded) Snapshot() { lock (gate) { return (people.ToList(), error, notice, busy, loaded); } }

    private async Task Call(string path, object body, Func<ItemReply, string>? done = null)
    {
        if (Plugin.Config.CreatePassword.Length == 0) { lock (gate) { error = "Enter the creation password first."; notice = ""; } return; }
        lock (gate) { if (busy) return; busy = true; error = ""; notice = ""; }
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Post, "/marks/admin/" + path, body).ConfigureAwait(false);
            if (r.People != null) lock (gate) { people = r.People; loaded = true; }
            if (done != null) lock (gate) { notice = done(r); }
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } return; }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Marks request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } return; }
        finally { lock (gate) { busy = false; } }
        if (path != "list") await Call("list", new { adminPassword = Plugin.Config.CreatePassword });
    }

    public Task LoadAsync() => Call("list", new { adminPassword = Plugin.Config.CreatePassword });
    public Task AddAsync(string name, string reason) => Call("add", new { adminPassword = Plugin.Config.CreatePassword, name = name.Trim(), reason = reason.Trim() }, r => "Mark added to " + (r.Name ?? name) + ".");
    public Task RemoveMarkAsync(long id) => Call("remove", new { adminPassword = Plugin.Config.CreatePassword, id }, _ => "Mark removed.");
    public Task KickAsync(string name, string reason) => Call("kick", new { adminPassword = Plugin.Config.CreatePassword, name = name.Trim(), reason = reason.Trim() }, r => "Removed from the system: " + string.Join(", ", r.Removed ?? new List<string>()) + ".");
    public Task BanAsync(string name, string reason) => Call("ban", new { adminPassword = Plugin.Config.CreatePassword, name = name.Trim(), reason = reason.Trim() }, r => "Banned. Removed: " + (r.Removed is { Count: > 0 } ? string.Join(", ", r.Removed) : "nobody (not registered)") + ".");
    public Task UnbanAsync(string name) => Call("unban", new { adminPassword = Plugin.Config.CreatePassword, name = name.Trim() }, _ => "Unbanned " + name.Trim() + ".");
}

/// <summary>The SM Tools section for the infringement tracker.</summary>
public static class MarksAdmin
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private static string name = "", reason = "", pending = "";

    public static void Draw()
    {
        if (!ImGui.CollapsingHeader("Infringements and bans")) return;
        ImGui.TextWrapped("For Advocates only. Add a mark against a character when they break the rules, remove them from the system, or ban them. Marks are kept by character name, so the record stays even after they are removed. Uses the creation password.");
        UiUtil.CreatePasswordInput();
        var s = MarksState.I.Snapshot();
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        else if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);
        if (s.Busy) ImGui.TextColored(Dim, "Working...");
        else if (ImGui.Button((s.Loaded ? "Reload" : "Load the record") + "##mkload")) _ = MarksState.I.LoadAsync();

        ImGui.Spacing();
        ImGui.Text("Character name"); ImGui.SetNextItemWidth(240f); ImGui.InputText("##mkname", ref name, 60);
        ImGui.Text("Reason"); ImGui.SetNextItemWidth(Math.Max(200f, ImGui.GetContentRegionAvail().X - 8f)); ImGui.InputText("##mkreason", ref reason, 200);
        var n = name.Trim().Length > 0;
        var both = n && reason.Trim().Length > 0;
        if (!both || s.Busy) ImGui.BeginDisabled();
        if (ImGui.Button("Add a mark##mkadd")) { _ = MarksState.I.AddAsync(name, reason); reason = ""; pending = ""; }
        ImGui.SameLine(); if (ImGui.Button("Remove from system...##mkkick")) pending = "kick";
        ImGui.SameLine(); if (ImGui.Button("Ban...##mkban")) pending = "ban";
        if (!both || s.Busy) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!n || s.Busy) ImGui.BeginDisabled();
        if (ImGui.Button("Unban##mkunban")) { _ = MarksState.I.UnbanAsync(name); pending = ""; }
        if (!n || s.Busy) ImGui.EndDisabled();

        if (pending.Length > 0 && both)
        {
            ImGui.Spacing();
            ImGui.PushTextWrapPos(0f);
            if (pending == "kick") ImGui.TextColored(Red, $"Remove {name.Trim()} and all of their characters, items and stashes from the system? A mark is kept. They can register again.");
            else
            {
                ImGui.TextColored(Red, $"Ban {name.Trim()}? All of their characters are removed and those names can't register again.");
                ImGui.TextColored(Dim, "They could still register a different name with the member password. Change that password too if you want a hard stop.");
            }
            ImGui.PopTextWrapPos();
            if (ImGui.Button("Yes, do it##mkyes"))
            {
                if (pending == "kick") _ = MarksState.I.KickAsync(name, reason); else _ = MarksState.I.BanAsync(name, reason);
                pending = ""; reason = "";
            }
            ImGui.SameLine(); if (ImGui.Button("Cancel##mkno")) pending = "";
        }
        else if (!both) pending = "";

        if (!s.Loaded) return;
        ImGui.Spacing(); ImGui.Separator();
        ImGui.TextColored(Gold, $"On record ({s.People.Count})");
        if (s.People.Count == 0) ImGui.TextColored(Dim, "Nobody has a mark or a ban.");
        foreach (var p in s.People)
        {
            ImGui.PushID(p.Name);
            if (ImGui.Selectable(p.Name + $"  ({p.Marks.Count} mark{(p.Marks.Count == 1 ? "" : "s")})" + (p.Registered ? "" : "  not registered"))) { name = p.Name; pending = ""; }
            if (p.Banned) { ImGui.SameLine(); ImGui.TextColored(Red, "BANNED"); }
            if (p.Banned && p.BanReason.Length > 0) ImGui.TextColored(Dim, "  Ban reason: " + p.BanReason);
            foreach (var m in p.Marks)
            {
                var when = DateTime.TryParse(m.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("MMM d") : "";
                ImGui.PushID((int)m.Id);
                ImGui.TextColored(Dim, "  " + when + ":"); ImGui.SameLine();
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(150f, ImGui.GetContentRegionAvail().X - 40f)); ImGui.TextUnformatted(m.Reason); ImGui.PopTextWrapPos();
                ImGui.SameLine(); if (ImGui.SmallButton("x")) _ = MarksState.I.RemoveMarkAsync(m.Id);
                ImGui.PopID();
            }
            ImGui.PopID();
            ImGui.Separator();
        }
    }
}
