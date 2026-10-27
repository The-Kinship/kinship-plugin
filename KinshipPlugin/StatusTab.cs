using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>Statuses: see everyone's buffs and debuffs, add flavor statuses to yourself, and (Advocates) add or remove any.</summary>
public sealed class StatusTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private static readonly Vector4 BuffColour = new(0.5f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 DebuffColour = new(0.95f, 0.45f, 0.4f, 1f);

    private readonly IconPicker picker = new("statuspick");
    private readonly StatusForm form = new() { Icon = "game:10205" };
    private readonly StatusForm adminForm = new() { Icon = "game:10205" };
    private string adminTo = "";
    private bool pickingAdmin;

    public void Draw(ItemsSnap inv)
    {
        Plugin.Status.RefreshIfDue(15);
        var s = Plugin.Status.Snapshot();
        var mine = s.All.Where(x => x.Mine).ToList();

        ImGui.TextColored(Gold, "Statuses");
        ImGui.TextWrapped("Buffs and debuffs your character has. Everyone can see everyone's. Items can give or remove them, and you can add little ones for yourself.");
        var bar = Plugin.Config.StatusBar;
        if (ImGui.Checkbox("Show my statuses on screen", ref bar)) { Plugin.Config.StatusBar = bar; Plugin.Config.Save(); }
        ImGui.SameLine(); if (ImGui.SmallButton("Refresh##status")) _ = Plugin.Status.RefreshAsync();
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        else if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);
        ImGui.Spacing();

        ImGui.TextColored(Gold, $"Mine ({mine.Count} / {StatusState.Max})");
        if (mine.Count == 0) ImGui.TextColored(Dim, "None right now.");
        foreach (var st in mine) DrawLine(st, s.Busy, false);

        ImGui.Spacing();
        DrawAddSelf(s, mine.Count);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        DrawEveryone(s);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        DrawAdvocate(s, inv);
        picker.Draw();
    }

    private static void DrawIcon(StatusView st, float size)
    {
        var wrap = ItemIcons.Wrap(st.Icon);
        if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(size, size)); else ImGui.Button("?##si" + st.Id, new Vector2(size, size));
    }

    private void DrawLine(StatusView st, bool busy, bool advocate)
    {
        DrawIcon(st, 28f);
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextColored(st.IsDebuff ? DebuffColour : BuffColour, st.Name);
        ImGui.SameLine(); ImGui.TextColored(Dim, StatusView.Format(st.Remaining));
        if (st.Description.Length > 0) { ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f); ImGui.TextUnformatted(st.Description); ImGui.PopTextWrapPos(); }
        if (!busy && st.CanRemove && ImGui.SmallButton("Remove##rm" + st.Id)) _ = Plugin.Status.RemoveAsync(st.Id);
        else if (!busy && advocate && !st.CanRemove && ImGui.SmallButton("Advocate remove##ar" + st.Id)) _ = Plugin.Status.AdminRemoveAsync(st.Id);
        ImGui.EndGroup();
        if (st.Self && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right) && !busy) _ = Plugin.Status.RemoveAsync(st.Id);
        ImGui.Spacing();
    }

    private void IconAndFields(StatusForm f, string id)
    {
        var wrap = f.Icon.Length > 0 ? ItemIcons.Wrap(f.Icon) : null;
        if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(36f, 36f)); else ImGui.Button("?##fi" + id, new Vector2(36f, 36f));
        ImGui.SameLine();
        if (ImGui.Button("Choose icon##" + id)) picker.Open(v => f.Icon = v);
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Name##" + id, ref f.Name, 30);
        ImGui.Text("Description"); ImGui.InputTextMultiline("##fd" + id, ref f.Description, 200, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 52f));
        ImGui.SetNextItemWidth(120f); ImGui.InputInt("Minutes##" + id, ref f.Minutes, 1);
        if (f.Minutes < 0) f.Minutes = 0;
        ImGui.TextColored(Dim, "0 means it lasts until it is removed.");
        ImGui.Checkbox("Debuff (bad)##" + id, ref f.Debuff);
    }

    private void DrawAddSelf(StatusSnap s, int count)
    {
        if (!ImGui.CollapsingHeader("Add a status to myself")) return;
        IconAndFields(form, "self");
        var ok = form.Name.Trim().Length > 0 && form.Icon.Length > 0 && !s.Busy && count < StatusState.Max;
        if (count >= StatusState.Max) ImGui.TextColored(Dim, $"You already have {StatusState.Max}. Remove one first.");
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button("Add status##self")) { _ = Plugin.Status.AddSelfAsync(form); }
        if (!ok) ImGui.EndDisabled();
    }

    private void DrawEveryone(StatusSnap s)
    {
        ImGui.TextColored(Gold, "Everyone");
        var advocate = Plugin.Config.CreatePassword.Length > 0;
        var others = s.All.Where(x => !x.Mine).ToList();
        if (others.Count == 0) { ImGui.TextColored(Dim, "Nobody else has any statuses."); return; }
        foreach (var g in others.GroupBy(x => x.MemberName))
        {
            ImGui.TextColored(Gold, g.Key);
            ImGui.Indent();
            foreach (var st in g) DrawLine(st, s.Busy, advocate);
            ImGui.Unindent();
        }
    }

    private void DrawAdvocate(StatusSnap s, ItemsSnap inv)
    {
        if (!ImGui.CollapsingHeader("Advocate tools##statuses")) return;
        ImGui.TextWrapped("Add a status to any member, or remove any status (the Remove buttons above show for Advocates). Uses the creation password.");
        UiUtil.CreatePasswordInput();
        var members = inv.Members.Append(inv.Name).Where(m => m.Length > 0).Distinct().OrderBy(m => m).ToList();
        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("Member##statusadmin", adminTo.Length > 0 ? adminTo : "Choose a member"))
        {
            foreach (var m in members) if (ImGui.Selectable(m, m == adminTo)) adminTo = m;
            ImGui.EndCombo();
        }
        IconAndFields(adminForm, "adm");
        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo("Kind##admk", adminForm.Type == "sickness" ? "Sickness" : adminForm.Type == "curse" ? "Curse" : "Neither"))
        {
            if (ImGui.Selectable("Neither", adminForm.Type.Length == 0)) adminForm.Type = "";
            if (ImGui.Selectable("Sickness (Life Watch can diagnose)", adminForm.Type == "sickness")) adminForm.Type = "sickness";
            if (ImGui.Selectable("Curse (Onyx Blood Guard can diagnose)", adminForm.Type == "curse")) adminForm.Type = "curse";
            ImGui.EndCombo();
        }
        if (adminForm.Type.Length > 0) { ImGui.Text("Cure information (shown to a successful diagnosis)"); ImGui.InputTextMultiline("##admcure", ref adminForm.Cure, 200, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 52f)); }
        var ok = adminTo.Length > 0 && adminForm.Name.Trim().Length > 0 && adminForm.Icon.Length > 0 && !s.Busy;
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button("Give status##adm")) { _ = Plugin.Status.AdminAddAsync(adminTo, adminForm); }
        if (!ok) ImGui.EndDisabled();
    }
}
