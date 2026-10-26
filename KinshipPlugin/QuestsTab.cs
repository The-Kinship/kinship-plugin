using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>Quests: talk to your targeted NPC, see where you are in each quest, and (Advocates) write and hand out quests.</summary>
public sealed class QuestsTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);

    private readonly IconPicker picker = new("questpick");
    private QuestForm form = new();
    private bool editing;
    private string assignTo = "", assignQuest = "";

    private static string NameOf(string t) => t == "give" ? "Hand over an item" : t == "receive" ? "They give an item" : t == "look" ? "Look at a spot" : t == "listen" ? "Listen at a spot" : t == "search" ? "Search a spot" : "They talk";

    internal static string Objective(QuestNow c) =>
        c.Type == "give" ? $"Bring {c.Item} to {c.Npc}."
        : c.Type == "look" ? "Look around" + Where(c) : c.Type == "listen" ? "Listen" + Where(c) : c.Type == "search" ? "Search" + Where(c)
        : $"Speak to {c.Npc}.";
    private static string Where(QuestNow c) => c.Place != null && c.Place.Zone.Length > 0 ? " in " + c.Place.Zone + "." : ".";

    public void Draw(ItemsSnap inv)
    {
        Plugin.Quests.RefreshIfDue(20);
        var s = Plugin.Quests.Snapshot();

        ImGui.TextColored(Gold, "Quests");
        ImGui.TextWrapped("Target a character in the game, then press Talk. If they have something to say about one of your quests, you'll hear it.");
        string target = "";
        try { target = Plugin.TargetManager.Target?.Name.TextValue ?? ""; } catch (Exception) { target = ""; }
        if (target.Length == 0) ImGui.TextColored(Dim, "Nothing targeted.");
        else ImGui.TextColored(Gold, "Target: " + target);
        var can = target.Length > 0 && !s.Busy;
        if (!can) ImGui.BeginDisabled();
        if (ImGui.Button("Talk##quest")) _ = Plugin.Quests.TalkAsync(target);
        if (!can) ImGui.EndDisabled();
        ImGui.SameLine(); if (ImGui.SmallButton("Refresh##quests")) _ = Plugin.Quests.RefreshAsync();
        var win = Plugin.Config.QuestWindow;
        if (ImGui.Checkbox("Show the quest window on screen while I have a quest", ref win)) { Plugin.Config.QuestWindow = win; Plugin.Config.Save(); }
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        else if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var active = s.Mine.Where(q => !q.Done).ToList();
        ImGui.TextColored(Gold, $"My quests ({active.Count})");
        if (active.Count == 0) ImGui.TextColored(Dim, "No quests right now.");
        foreach (var q in active)
        {
            ImGui.TextColored(Gold, q.Title);
            ImGui.SameLine(); ImGui.TextColored(Dim, $"step {q.Step + 1} of {q.Total}");
            ImGui.Indent();
            if (q.Intro.Length > 0) { ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30f); ImGui.TextUnformatted(q.Intro); ImGui.PopTextWrapPos(); }
            if (q.Current != null)
            {
                ImGui.TextUnformatted(Objective(q.Current));
                if (q.Current.Hint.Length > 0) ImGui.TextColored(Dim, q.Current.Hint);
            }
            ImGui.Unindent();
            ImGui.Spacing();
        }
        var finished = s.Mine.Where(q => q.Done).ToList();
        if (finished.Count > 0 && ImGui.CollapsingHeader($"Finished ({finished.Count})##questsdone"))
            foreach (var q in finished) ImGui.TextColored(Green, q.Title);

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        DrawAdvocate(s, inv);
        picker.Draw();
    }

    // ---------- Advocate tools ----------
    private void DrawAdvocate(QuestSnap s, ItemsSnap inv)
    {
        if (!ImGui.CollapsingHeader("Advocate tools##quests")) return;
        ImGui.TextWrapped("Write quests and give them to members. Uses the creation password.");
        UiUtil.CreatePasswordInput();
        if (ImGui.Button("Load quests##qa")) _ = Plugin.Quests.AdminListAsync();
        ImGui.Spacing();

        foreach (var q in s.Admin)
        {
            ImGui.TextColored(Gold, q.Title); ImGui.SameLine(); ImGui.TextColored(Dim, $"{q.Steps?.Count ?? 0} steps");
            if (q.Players != null)
                foreach (var p in q.Players) ImGui.TextColored(p.Done ? Green : Dim, $"   {p.Name}: " + (p.Done ? "finished" : $"on step {p.Step + 1}"));
            if (q.Claims != null)
                foreach (var c in q.Claims) ImGui.TextColored(Dim, $"   Step {c.Step + 1} was taken by {c.Name}");
            if (q.Claims is { Count: > 0 } && !s.Busy) { if (ImGui.SmallButton("Reset taken items##qrc" + q.Id)) _ = Plugin.Quests.AdminResetClaimsAsync(q.Id); ImGui.SameLine(); }
            if (ImGui.SmallButton("Edit##qe" + q.Id)) { form = FormFrom(q); editing = true; }
            ImGui.SameLine();
            if (!s.Busy && ImGui.SmallButton("Delete##qd" + q.Id)) _ = Plugin.Quests.AdminDeleteAsync(q.Id);
            ImGui.Spacing();
        }

        var members = inv.Members.Append(inv.Name).Where(m => m.Length > 0).Distinct().OrderBy(m => m).ToList();
        if (s.Admin.Count > 0)
        {
            ImGui.TextColored(Gold, "Give or take a quest");
            ImGui.SetNextItemWidth(220f);
            if (ImGui.BeginCombo("Quest##qasq", assignQuest.Length > 0 ? s.Admin.FirstOrDefault(x => x.Id == assignQuest)?.Title ?? "Choose a quest" : "Choose a quest"))
            {
                foreach (var q in s.Admin) if (ImGui.Selectable(q.Title + "##qs" + q.Id, q.Id == assignQuest)) assignQuest = q.Id;
                ImGui.EndCombo();
            }
            ImGui.SetNextItemWidth(220f);
            if (ImGui.BeginCombo("Member##qasm", assignTo.Length > 0 ? assignTo : "Choose a member"))
            {
                foreach (var m in members) if (ImGui.Selectable(m, m == assignTo)) assignTo = m;
                ImGui.EndCombo();
            }
            var ok = assignQuest.Length > 0 && assignTo.Length > 0 && !s.Busy;
            if (!ok) ImGui.BeginDisabled();
            if (ImGui.Button("Give quest##qg")) _ = Plugin.Quests.AdminAssignAsync(assignQuest, assignTo);
            ImGui.SameLine();
            if (ImGui.Button("Take quest away##qt")) _ = Plugin.Quests.AdminUnassignAsync(assignQuest, assignTo);
            if (!ok) ImGui.EndDisabled();
            ImGui.TextColored(Dim, "Giving a quest someone already has starts it over.");
            ImGui.Spacing();
        }

        ImGui.Separator();
        ImGui.TextColored(Gold, editing && form.Id.Length > 0 ? "Edit quest" : "New quest");
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Title##qf", ref form.Title, 60);
        ImGui.Text("Introduction (optional)"); ImGui.InputTextMultiline("##qfi", ref form.Intro, 400, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 52f));
        for (var i = 0; i < form.Steps.Count; i++) DrawStep(form.Steps[i], i);
        if (form.Steps.Count < QuestState.MaxSteps && ImGui.Button("Add a step##qas")) form.Steps.Add(new StepForm());
        var valid = form.Title.Trim().Length > 0 && form.Steps.All(StepOk) && !s.Busy;
        if (!valid) ImGui.BeginDisabled();
        if (ImGui.Button((form.Id.Length > 0 ? "Save changes" : "Create quest") + "##qsave")) { _ = Plugin.Quests.AdminSaveAsync(form); form = new QuestForm(); editing = false; }
        if (!valid) ImGui.EndDisabled();
        if (editing) { ImGui.SameLine(); if (ImGui.Button("Cancel edit##qce")) { form = new QuestForm(); editing = false; } }
    }

    private static bool StepOk(StepForm s) =>
        s.Text.Trim().Length > 0 && (s.AtSpot ? s.Place != null : s.Npc.Trim().Length > 0) && (s.Type != "give" || s.Item.Trim().Length > 0) && (s.Type != "receive" || (s.GiftName.Trim().Length > 0 && s.GiftIcon.Length > 0));

    private void DrawStep(StepForm st, int i)
    {
        ImGui.Spacing();
        ImGui.TextColored(Gold, $"Step {i + 1}");
        if (form.Steps.Count > 1) { ImGui.SameLine(); if (ImGui.SmallButton($"Remove##qrs{i}")) { form.Steps.RemoveAt(i); return; } }
        ImGui.Indent();
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo($"Kind##qk{i}", NameOf(st.Type)))
        {
            foreach (var t in new[] { "talk", "receive", "give", "look", "listen", "search" }) if (ImGui.Selectable(NameOf(t), st.Type == t)) st.Type = t;
            ImGui.EndCombo();
        }
        if (st.AtSpot)
        {
            var spot = WorldInfo.Current(out var problem);
            if (st.Place != null) ImGui.TextColored(Green, $"Location set: {st.Place.Zone} ({st.Place.MapX:0.0}, {st.Place.MapY:0.0})"); else ImGui.TextColored(Red, "No location yet.");
            if (spot == null) ImGui.TextColored(Dim, problem);
            else
            {
                if (ImGui.Button($"Use this location##qul{i}")) st.Place = spot;
                ImGui.SameLine(); ImGui.TextColored(Dim, "Stand where it happens, then press this.");
            }
        }
        else
        {
            ImGui.SetNextItemWidth(220f); ImGui.InputText($"Character name##qn{i}", ref st.Npc, 40);
            ImGui.TextColored(Dim, "Exactly as it shows when you target them in the game.");
        }
        if (st.Type == "give") { ImGui.SetNextItemWidth(220f); ImGui.InputText($"Item to hand over##qi{i}", ref st.Item, 40); ImGui.TextColored(Dim, "Must be an item an Advocate gave out. It is used up."); }
        ImGui.Text(st.Type == "give" ? "What they say when given it" : st.AtSpot ? "What the player sees, hears or finds" : "What they say"); ImGui.InputTextMultiline($"##qt{i}", ref st.Text, 600, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 52f));
        ImGui.SetNextItemWidth(300f); ImGui.InputText($"Hint for the player (optional)##qh{i}", ref st.Hint, 150);
        if (st.Type == "receive" || st.Type == "search")
        {
            if (st.Type == "search") ImGui.TextColored(Dim, "Optional: something they find and keep. Leave the name empty to find nothing.");
            var wrap = ItemIcons.Wrap(st.GiftIcon);
            if (wrap != null) { ImGui.Image(wrap.Handle, new Vector2(32f, 32f)); ImGui.SameLine(); }
            if (ImGui.SmallButton($"Choose item icon##qgi{i}")) { var step = st; picker.Open(v => step.GiftIcon = v); }
            ImGui.SetNextItemWidth(220f); ImGui.InputText($"Item they give##qgn{i}", ref st.GiftName, 40);
            ImGui.SetNextItemWidth(300f); ImGui.InputText($"Item description##qgd{i}", ref st.GiftDesc, 300);
            ImGui.Checkbox($"Only the first person to get it##qo{i}", ref st.Once);
        }
        ImGui.Unindent();
    }

    private static QuestForm FormFrom(QuestView q)
    {
        var f = new QuestForm { Id = q.Id, Title = q.Title, Intro = q.Intro, Steps = new() };
        foreach (var s in q.Steps ?? new())
            f.Steps.Add(new StepForm { Type = s.Type, Npc = s.Npc, Text = s.Text, Hint = s.Hint, Item = s.Item, GiftName = s.Gift?.Name ?? "", GiftIcon = s.Gift?.Icon is { Length: > 0 } ic ? ic : "game:20001", GiftDesc = s.Gift?.Description ?? "", Once = s.Once,
                Place = s.Place == null ? null : new Spot((uint)s.Place.Territory, s.Place.Map, new Vector3(s.Place.X, s.Place.Y, s.Place.Z), s.Place.Zone, s.Place.MapX, s.Place.MapY) });
        if (f.Steps.Count == 0) f.Steps.Add(new StepForm());
        return f;
    }
}
