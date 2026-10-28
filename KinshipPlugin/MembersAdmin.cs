using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

public sealed class RegisteredPerson { public string Name { get; set; } = ""; public List<string> Tags { get; set; } = new(); public string Joined { get; set; } = ""; }

/// <summary>Little coloured labels like "SM - Onyx - Banker", shown at the bottom of profile cards and roster entries.</summary>
public static class TagBadges
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    public static Vector4 ColorOf(string tag) => tag switch
    {
        "Onyx" => new(0.62f, 0.45f, 0.98f, 1f),
        "Vanguard" => new(0.74f, 0.80f, 0.90f, 1f),
        "Life Watch" => new(0.42f, 0.78f, 0.50f, 1f),
        "Ember" => new(0.95f, 0.45f, 0.35f, 1f),
        "Banker" => new(0.95f, 0.85f, 0.45f, 1f),
        "Kin" => new(0.85f, 0.88f, 0.95f, 1f),
        "Alt" => new(0.60f, 0.65f, 0.72f, 1f),
        _ => Gold,
    };
    public static void Draw(IReadOnlyList<string> tags)
    {
        for (var i = 0; i < tags.Count; i++)
        {
            if (i > 0) { ImGui.SameLine(0f, 4f); ImGui.TextColored(Dim, "-"); ImGui.SameLine(0f, 4f); }
            ImGui.TextColored(ColorOf(tags[i]), tags[i]);
        }
    }
}

/// <summary>SM Tools: everyone registered on the server, with a quick remove button and tags (SM, Banker, a sect) that show on their card.</summary>
public static class MembersAdmin
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);

    private static readonly object gate = new();
    private static List<RegisteredPerson> people = new();
    private static List<string> choices = new();
    private static bool loaded, busy;
    private static string error = "", notice = "";
    private static string filter = "", editing = "", confirmRemove = "";
    private static readonly HashSet<string> editTags = new();

    private static async Task Run(Func<string, Task<ItemReply>> call, string done, bool reload)
    {
        if (Plugin.Config.CreatePassword.Length == 0) { lock (gate) { error = "Enter the creation password first."; notice = ""; } return; }
        lock (gate) { if (busy) return; busy = true; error = ""; notice = ""; }
        try
        {
            var r = await call(Plugin.Config.CreatePassword).ConfigureAwait(false);
            if (r.Registered != null) lock (gate) { people = r.Registered; choices = r.TagChoices ?? choices; loaded = true; }
            lock (gate) { if (done.Length > 0) notice = done; }
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } return; }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Registered characters request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } return; }
        finally { lock (gate) { busy = false; } }
        if (reload) await LoadAsync().ConfigureAwait(false);
    }

    private static Task LoadAsync() => Run(pw => ItemsState.SendAsync(HttpMethod.Post, "/items/admin/registered", new { adminPassword = pw }), "", false);
    private static Task SaveTagsAsync(string name, List<string> tags) =>
        Run(pw => ItemsState.SendAsync(HttpMethod.Post, "/items/admin/set-tags", new { adminPassword = pw, name, tags }), $"Tags saved for {name}.", true);
    private static Task RemoveAsync(string name) =>
        Run(pw => ItemsState.SendAsync(HttpMethod.Post, "/items/admin/remove-member", new { adminPassword = pw, name }), $"{name} was removed.", true);

    public static void Draw()
    {
        if (!ImGui.CollapsingHeader("Registered characters and tags")) return;
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(Dim, "Everyone registered on the server. Tags (like SM, Banker or a sect) show at the bottom of their roster entry and profile card. Remove deletes the character and everything they own. Uses the creation password.");
        ImGui.PopTextWrapPos();
        UiUtil.CreatePasswordInput();

        List<RegisteredPerson> list; List<string> opts; bool isLoaded, isBusy; string err, note;
        lock (gate) { list = people; opts = choices; isLoaded = loaded; isBusy = busy; err = error; note = notice; }
        if (err.Length > 0) ImGui.TextColored(Red, err); else if (note.Length > 0) ImGui.TextColored(Green, note);
        if (isBusy) ImGui.TextColored(Dim, "Working...");
        else if (ImGui.Button(isLoaded ? "Reload##regload" : "Load registered characters##regload")) _ = LoadAsync();
        if (!isLoaded) return;

        ImGui.Spacing();
        ImGui.TextColored(Gold, $"Registered characters ({list.Count})");
        var tagged = new Dictionary<string, int>();
        foreach (var t in opts) tagged[t] = list.Count(p => p.Tags.Contains(t));
        if (opts.Count > 0) ImGui.TextColored(Dim, string.Join("   ", opts.Where(t => tagged[t] > 0).Select(t => $"{t}: {tagged[t]}")));
        ImGui.SetNextItemWidth(200f); ImGui.InputText("Search##regfilter", ref filter, 40);
        ImGui.Spacing();

        foreach (var p in list.Where(p => filter.Trim().Length == 0 || p.Name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            ImGui.TextColored(Gold, p.Name);
            if (p.Tags.Count > 0) { ImGui.SameLine(); TagBadges.Draw(p.Tags); }
            if (editing == p.Name)
            {
                foreach (var t in opts)
                {
                    var on = editTags.Contains(t);
                    if (ImGui.Checkbox(t + "##tag" + p.Name + t, ref on)) { if (on) editTags.Add(t); else editTags.Remove(t); }
                    ImGui.SameLine();
                }
                ImGui.NewLine();
                if (isBusy) ImGui.BeginDisabled();
                if (ImGui.SmallButton("Save tags##tsv" + p.Name)) { _ = SaveTagsAsync(p.Name, opts.Where(editTags.Contains).ToList()); editing = ""; }
                if (isBusy) ImGui.EndDisabled();
                ImGui.SameLine(); if (ImGui.SmallButton("Cancel##tcn" + p.Name)) editing = "";
            }
            else
            {
                if (ImGui.SmallButton("Tags##ted" + p.Name)) { editing = p.Name; editTags.Clear(); foreach (var t in p.Tags) editTags.Add(t); }
                ImGui.SameLine();
                if (confirmRemove == p.Name)
                {
                    ImGui.TextColored(Red, "Remove them and everything they own?"); ImGui.SameLine();
                    if (ImGui.SmallButton("Yes, remove##rmy" + p.Name)) { _ = RemoveAsync(p.Name); confirmRemove = ""; }
                    ImGui.SameLine(); if (ImGui.SmallButton("No##rmn" + p.Name)) confirmRemove = "";
                }
                else if (ImGui.SmallButton("Remove##rmv" + p.Name)) confirmRemove = p.Name;
            }
            ImGui.Separator();
        }
    }
}
