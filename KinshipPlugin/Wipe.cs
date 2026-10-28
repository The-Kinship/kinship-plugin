using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

public sealed class WipeInfo
{
    public List<string> Keep { get; set; } = new(); public List<string> Remove { get; set; } = new();
    public Dictionary<string, int> Counts { get; set; } = new(); public bool Done { get; set; }
}

/// <summary>"Wipe the system": clears test data so the FC can start fresh, keeping the characters you name. Creation password only.</summary>
public sealed class WipeState
{
    public static readonly WipeState I = new();
    private readonly object gate = new();
    private WipeInfo? preview; private string previewKey = "";
    private string error = "", notice = "";
    private bool busy;

    public (WipeInfo? Preview, string Key, string Error, string Notice, bool Busy) Snapshot() { lock (gate) { return (preview, previewKey, error, notice, busy); } }
    public void Reset() { lock (gate) { preview = null; previewKey = ""; error = ""; notice = ""; } }
    public static string KeyOf(string names, bool quests) => string.Join("|", Names(names).Select(n => n.ToLowerInvariant()).OrderBy(n => n)) + (quests ? "+q" : "");
    public static List<string> Names(string text) => text.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).Where(n => n.Length > 0).ToList();

    private async Task<WipeInfo?> Call(string path, object body)
    {
        if (Plugin.Config.CreatePassword.Length == 0) { lock (gate) { error = "Enter the creation password first."; notice = ""; } return null; }
        lock (gate) { if (busy) return null; busy = true; error = ""; notice = ""; }
        try { var r = await ItemsState.SendAsync(HttpMethod.Post, "/system/admin/" + path, body).ConfigureAwait(false); return r.Wipe; }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Wipe request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
        return null;
    }

    public async Task CheckAsync(string names, bool quests)
    {
        var w = await Call("wipe-check", new { adminPassword = Plugin.Config.CreatePassword, keep = Names(names), quests });
        if (w != null) lock (gate) { preview = w; previewKey = KeyOf(names, quests); }
    }

    public async Task WipeAsync(string names, bool quests, string confirm)
    {
        var w = await Call("wipe", new { adminPassword = Plugin.Config.CreatePassword, keep = Names(names), quests, confirm });
        if (w == null) return;
        lock (gate) { preview = null; previewKey = ""; notice = $"Done. Kept: {string.Join(", ", w.Keep)}. Removed {w.Remove.Count} other character(s) and cleared everything else."; }
        _ = Plugin.Items.RefreshAsync(true);
    }
}

/// <summary>The SM Tools section for the wipe.</summary>
public static class WipeAdmin
{
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private static string keep = "", confirm = "";
    private static bool quests;
    private static readonly (string Table, string Label)[] Labels =
    {
        ("items", "items"), ("stashes", "stashes"), ("signs", "signs"), ("mails", "mails"), ("statuses", "statuses"), ("gold_log", "gold history entries"),
        ("quest_progress", "quest progress records"), ("quests", "quests"), ("reports", "reports"), ("duels", "fights"), ("scenes", "scenes"), ("rooms", "combat encounters"),
    };

    public static void Draw()
    {
        if (!ImGui.CollapsingHeader("Wipe the system (start fresh)")) return;
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(Red, "This cannot be undone.");
        ImGui.TextUnformatted("Use it when testing is over and you are ready to use the system for real. It keeps only the characters you name here (they stay registered with the same sign-in and recovery code, and keep their profiles). It removes every other character, and clears all items, stashes, signs, mail, statuses, gold, quest progress, fights, scenes and reports. Marks and bans are kept. Quests are kept unless you tick the box. Uses the creation password only.");
        ImGui.PopTextWrapPos();
        UiUtil.CreatePasswordInput();
        ImGui.Text("Characters to keep (all of yours, separated by commas)");
        ImGui.SetNextItemWidth(Math.Max(220f, ImGui.GetContentRegionAvail().X - 8f)); ImGui.InputText("##wipekeep", ref keep, 300);
        ImGui.Checkbox("Also delete all quests##wipeq", ref quests);

        var s = WipeState.I.Snapshot();
        if (s.Busy || WipeState.Names(keep).Count == 0) ImGui.BeginDisabled();
        if (ImGui.Button("Check what would be deleted##wipecheck")) { confirm = ""; _ = WipeState.I.CheckAsync(keep, quests); }
        if (s.Busy || WipeState.Names(keep).Count == 0) ImGui.EndDisabled();
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);

        if (s.Preview == null) return;
        if (s.Key != WipeState.KeyOf(keep, quests)) { ImGui.TextColored(Dim, "You changed the list. Check again."); return; }
        ImGui.Spacing(); ImGui.Separator();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted("Will keep: " + string.Join(", ", s.Preview.Keep));
        ImGui.TextColored(s.Preview.Remove.Count > 0 ? Red : Dim, s.Preview.Remove.Count > 0 ? "Will remove these characters: " + string.Join(", ", s.Preview.Remove) : "No other characters to remove.");
        var lines = Labels.Where(l => s.Preview.Counts.TryGetValue(l.Table, out var n) && n > 0).Select(l => $"{s.Preview.Counts[l.Table]} {l.Label}").ToList();
        ImGui.TextUnformatted("Will delete: " + (lines.Count > 0 ? string.Join(", ", lines) : "nothing else"));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Text("Type WIPE EVERYTHING to confirm");
        ImGui.SetNextItemWidth(220f); ImGui.InputText("##wipeconfirm", ref confirm, 30);
        var ok = confirm == "WIPE EVERYTHING" && !s.Busy;
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button("Wipe now##wipego")) { _ = WipeState.I.WipeAsync(keep, quests, confirm); confirm = ""; }
        if (!ok) ImGui.EndDisabled();
    }
}
